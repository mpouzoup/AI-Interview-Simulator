using System.Net;
using System.Text;
using System.Text.Json;
using AIInterviewSimulator.Api.Controllers;
using AIInterviewSimulator.Client.Models;
using AIInterviewSimulator.Data.Context;
using AIInterviewSimulator.Data.Entities;
using AIInterviewSimulator.Engine.Engines;
using AIInterviewSimulator.Engine.Feedback;
using AIInterviewSimulator.Engine.Managers;
using AIInterviewSimulator.Engine.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

static class FeedbackTests
{
    public static async Task Run(DbContextOptions<ApplicationDbContext> options, Action<bool, string> check)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["GeminiApiKey"] = "isolated-test-key"
        }).Build();
        var cases = new Dictionary<string, (string? Json, FeedbackStatus Status)>
        {
            ["success"] = (Valid("Test feedback"), FeedbackStatus.Success),
            ["http"] = (null, FeedbackStatus.Unavailable),
            ["http-retry"] = (null, FeedbackStatus.Unavailable),
            ["budget-during-retry"] = (null, FeedbackStatus.Unavailable),
            ["network"] = (null, FeedbackStatus.Unavailable),
            ["timeout"] = (null, FeedbackStatus.Unavailable),
            ["null"] = ("null", FeedbackStatus.InvalidResult),
            ["null-text"] = (Valid(null), FeedbackStatus.InvalidResult),
            ["empty"] = (Valid(""), FeedbackStatus.InvalidResult),
            ["whitespace"] = (Valid(" \n "), FeedbackStatus.InvalidResult),
            ["malformed"] = ("{", FeedbackStatus.InvalidResult),
            ["missing-candidates"] = ("{}", FeedbackStatus.InvalidResult),
            ["empty-candidates"] = ("{\"candidates\":[]}", FeedbackStatus.InvalidResult),
            ["truncated"] = (Valid("Partial text", "MAX_TOKENS"), FeedbackStatus.InvalidResult),
            ["sentinel"] = (Valid("Η ανατροφοδότηση δεν ήταν διαθέσιμη αυτή τη στιγμή."), FeedbackStatus.InvalidResult)
        };

        foreach (var stage in new[] { 1, 7 })
        foreach (var (name, example) in cases)
        {
            var label = $"feedback/{name}/stage {stage}";
            var calls = 0;
            var recover = false;
            var clock = new ManualBudgetClock();
            using var client = new HttpClient(new Stub(async (_, token) =>
            {
                calls++;
                if (recover) return Json(Valid("Recovered feedback"));
                if (name == "http") return new(HttpStatusCode.BadRequest);
                if (name == "http-retry") return new(HttpStatusCode.ServiceUnavailable);
                if (name == "budget-during-retry")
                {
                    if (calls == 2) clock.ExpireBudget();
                    return new(HttpStatusCode.ServiceUnavailable);
                }
                if (name == "network")
                {
                    // Cancel the overall budget on transport retry delay; no real waiting/network.
                    clock.ExpireBudget();
                    throw new HttpRequestException("Stub connection failure");
                }
                if (name == "timeout")
                {
                    clock.ExpireBudget();
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                }
                return Json(example.Json!);
            }));
            var service = new GeminiService(client, config, clock);
            var manager = new FeedbackManager(new ScriptFeedbackEngine(), new LlmFeedbackEngine(service));
            var logger = new CaptureLogger();
            var session = new InterviewSession { Condition = "B", User = new User { Nickname = "Feedback test" } };
            var questionTime = DateTime.UtcNow.AddMinutes(-2);
            var answerTime = questionTime.AddSeconds(10);
            await using (var db = new ApplicationDbContext(options))
            {
                for (var n = 1; n <= stage; n++) session.Answers.Add(new UserAnswer
                {
                    SubmissionId = Guid.NewGuid(), StageNumber = n, AnswerText = "Original test answer",
                    QuestionText = "Test question", QuestionShownAt = questionTime, AnswerSubmittedAt = answerTime
                });
                db.InterviewSessions.Add(session);
                await db.SaveChangesAsync();
            }
            var answerId = session.Answers.Last().Id;
            async Task<QuestionResponseDto> Fetch(int? requestedId = null)
            {
                await using var db = new ApplicationDbContext(options);
                var controller = new InterviewController(db, new InterviewManager(new ScriptBasedEngine()), manager, config, logger);
                var result = await controller.GetNextQuestion(session.Id, requestedId ?? answerId);
                check(result is OkObjectResult, label + ": typed API response");
                return JsonSerializer.Deserialize<QuestionResponseDto>(JsonSerializer.Serialize(((OkObjectResult)result).Value),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
            }
            var first = await Fetch();
            check(first.FeedbackStatus == example.Status.ToString(), label + ": correct status");
            check(first.AnswerId == answerId, label + ": linked saved answer");
            check(logger.Messages.Any(m => m.Contains(example.Status.ToString()) && m.Contains(answerId.ToString())), label + ": logged outcome and answer ID");
            await using (var db = new ApplicationDbContext(options))
            {
                var answer = await db.UserAnswers.FindAsync(answerId);
                if (example.Status != FeedbackStatus.Success)
                {
                    check(first.Feedback == null && first.QuestionText == null && !first.IsFinished, label + ": no feedback or next question on failure");
                    check(answer!.FeedbackText == null && answer.FeedbackShownAt == null, label + ": failure not persisted as feedback");
                    check((await db.InterviewSessions.FindAsync(session.Id))!.FinishedAt == null, label + ": failure does not finish session");
                }
                check(answer!.AnswerSubmittedAt == answerTime && answer.QuestionShownAt == questionTime, label + ": original timestamps preserved");
                check(await db.UserAnswers.CountAsync(a => a.InterviewSessionId == session.Id) == stage, label + ": no second initial answer");
            }
            if (name == "timeout")
                check(clock.BudgetDuration == TimeSpan.FromSeconds(30) && calls == 1, label + ": total deadline cancels without another attempt");
            if (name == "http-retry")
                check(calls == 5 && clock.BudgetTimers == 1, label + ": bounded retries share one budget");
            if (name == "budget-during-retry")
                check(calls == 2 && clock.BudgetTimers == 1, label + ": retry does not reset the deadline");
            recover = true;
            var second = await Fetch();
            check(second.FeedbackStatus == "Success" && !string.IsNullOrWhiteSpace(second.Feedback), label + ": successful retry or cached success");
            var callsAfterSuccess = calls;
            var third = await Fetch();
            check(calls == callsAfterSuccess && third.Feedback == second.Feedback, label + ": successful feedback cached");
            await using (var db = new ApplicationDbContext(options))
            {
                var answer = await db.UserAnswers.FindAsync(answerId);
                check(answer!.AnswerSubmittedAt == answerTime && answer.QuestionShownAt == questionTime, label + ": retry preserves original times");
                check(await db.UserAnswers.CountAsync(a => a.InterviewSessionId == session.Id) == stage, label + ": retry preserves answer count");
                var controller = new InterviewController(db, null!, manager, config, logger);
                check(await controller.GetNextQuestion(session.Id, answerId + 100000) is ConflictObjectResult, label + ": wrong answer retry rejected");
            }
        }

        using var neverCall = new HttpClient(new Stub((_, _) => throw new Exception("Unexpected external call")));
        var missingKey = await new GeminiService(neverCall, new ConfigurationBuilder().Build()).GenerateResponseAsync("Test prompt");
        check(missingKey.Status == FeedbackStatus.Unavailable && missingKey.Text == null, "missing key is unavailable, without HTTP");
        var scripted = await new FeedbackManager(new ScriptFeedbackEngine(), new LlmFeedbackEngine(new GeminiService(neverCall, config)))
            .GenerateFeedbackAsync("A", 1, new UserAnswer());
        check(scripted.Status == FeedbackStatus.Success && FeedbackResult.IsUsable(scripted.Text), "Condition A stays scripted without Gemini");
    }

    private static string Valid(string? text, string reason = "STOP") => JsonSerializer.Serialize(new
    {
        candidates = new[] { new { finishReason = reason, content = new { parts = new[] { new { text } } } } }
    });
    private static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(value, Encoding.UTF8, "application/json")
    };

    private sealed class Stub(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => respond(request, token);
    }

    private sealed class CaptureLogger : ILogger<InterviewController>
    {
        public List<string> Messages { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }

    // Trigger the actual 30-second budget instantly; no real 30-second sleeps in tests.
    private sealed class ManualBudgetClock : TimeProvider
    {
        private TimerCallback? callback;
        private object? state;
        public TimeSpan BudgetDuration { get; private set; }
        public int BudgetTimers { get; private set; }
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            if (dueTime == GeminiService.TotalTimeout)
            {
                this.callback = callback;
                this.state = state;
                BudgetDuration = dueTime;
                BudgetTimers++;
            }
            else
            {
                // Advance the retry delay without sleeping, preserving the independent total budget.
                ThreadPool.QueueUserWorkItem(_ => callback(state));
            }
            return new NoOpTimer();
        }
        public void ExpireBudget() => callback!(state);
        private sealed class NoOpTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
