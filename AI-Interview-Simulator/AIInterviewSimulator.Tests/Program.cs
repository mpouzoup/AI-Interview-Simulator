using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using AIInterviewSimulator.Api.Controllers;
using AIInterviewSimulator.Api.DTOs.Requests;
using AIInterviewSimulator.Client.Pages;
using AIInterviewSimulator.Data.Context;
using AIInterviewSimulator.Data.Entities;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.HtmlRendering.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using MudBlazor.Services;

if (args.Length == 3 && args[0] == "--export-readonly")
{
    await BrowserTestHost.ExportReadOnly(args[1], args[2]);
    return;
}

if (args.Contains("--browser-host"))
{
    await BrowserTestHost.Run();
    return;
}

// No application host, production configuration, or Gemini service is started.
var directory = Path.Combine(Path.GetTempPath(), "interview-submission-tests-" + Guid.NewGuid());
Directory.CreateDirectory(directory);
var connectionString = $"Data Source={Path.Combine(directory, "tests.db")};Pooling=False;Default Timeout=10";
var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connectionString).Options;
var passed = 0;
try
{
    await using (var db = new ApplicationDbContext(options))
        await db.Database.MigrateAsync();

    foreach (var condition in new[] { "A", "B" })
    {
        var sessionId = await CreateSession(condition);
        var first = Request(sessionId, 1);
        var result = await Submit(first);
        Check(result is OkObjectResult, $"{condition}: normal submission");
        var answerId = AnswerId(result);
        Check(AnswerId(await Submit(first)) == answerId, $"{condition}: same SubmissionId returns same answer");
        Check(await Count(sessionId) == 1, $"{condition}: retry does not insert");
        Check(await Submit(Request(sessionId, 1)) is ConflictObjectResult,
            $"{condition}: different SubmissionId for same stage rejected");
        Check(await Count(sessionId) == 1, $"{condition}: duplicate stage remains a single row");
        Check(await Submit(Request(sessionId, 3)) is ConflictObjectResult,
            $"{condition}: skipped stage rejected");
        Check(await Submit(Request(sessionId, 0)) is ConflictObjectResult,
            $"{condition}: stage zero rejected");
        Check(await Submit(Request(sessionId, 8)) is ConflictObjectResult,
            $"{condition}: stage eight rejected");
        var conflicting = Request(sessionId, 1);
        conflicting.SubmissionId = first.SubmissionId;
        Check(await Submit(conflicting) is ConflictObjectResult,
            $"{condition}: reused SubmissionId with changed payload rejected");
        for (var stage = 2; stage <= 7; stage++)
            Check(await Submit(Request(sessionId, stage)) is OkObjectResult,
                $"{condition}: expected stage {stage} accepted");
        Check(AnswerId(await Submit(first)) == answerId,
            $"{condition}: original retry still succeeds after later stages");
        Check(await Count(sessionId) == 7, $"{condition}: seven stages remain seven answers");
    }

    var finishedSession = await CreateSession("A");
    await using (var db = new ApplicationDbContext(options))
    {
        (await db.InterviewSessions.FindAsync(finishedSession))!.FinishedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }
    Check(await Submit(Request(finishedSession, 1)) is ConflictObjectResult, "finished session rejected");

    // Independent contexts/connections contend on the same isolated SQLite file.
    foreach (var sameSubmission in new[] { false, true })
    {
        var sessionId = await CreateSession("B");
        var first = Request(sessionId, 1);
        var second = sameSubmission ? first : Request(sessionId, 1);
        using var gate = new Barrier(2);
        Task<IActionResult> Concurrent(SubmitAnswerRequest request) => Task.Run(async () =>
        {
            gate.SignalAndWait();
            return await Submit(request);
        });
        var results = await Task.WhenAll(Concurrent(first), Concurrent(second));
        Check(results.Count(r => r is OkObjectResult) == (sameSubmission ? 2 : 1),
            $"concurrent submissions (same ID: {sameSubmission}): success count");
        Check(results.Count(r => r is ConflictObjectResult) == (sameSubmission ? 0 : 1),
            $"concurrent submissions (same ID: {sameSubmission}): conflict count");
        Check(await Count(sessionId) == 1, $"concurrent submissions (same ID: {sameSubmission}): one row");
    }

    // Legacy inconsistencies must be rejected, never silently repaired.
    var legacySession = await CreateSession("A");
    await using (var db = new ApplicationDbContext(options))
    {
        db.UserAnswers.AddRange(Enumerable.Range(0, 2).Select(_ => new UserAnswer
        {
            InterviewSessionId = legacySession, StageNumber = 1,
            SubmissionId = Guid.NewGuid(), AnswerText = "Legacy test answer"
        }));
        await db.SaveChangesAsync();
    }
    Check(await Submit(Request(legacySession, 3)) is ConflictObjectResult, "legacy duplicate sequence rejected");
    Check(await Count(legacySession) == 2, "legacy rows untouched");

    foreach (var failure in new[] { "http", "network", "timeout", "empty", "null", "invalid-json", "unavailable", "invalid-result", "sentinel", "wrong-answer" })
        await TestFeedbackRecovery(failure);

    foreach (var condition in new[] { "A", "B" })
    foreach (var stage in new[] { 1, 7 })
    foreach (var revise in new[] { false, true })
    {
        await TestAnswerActions(condition, stage, revise, false);
        await TestAnswerActions(condition, stage, revise, true);
    }

    foreach (var revisionAction in new[] { false, true })
    foreach (var sameAction in new[] { false, true })
    {
        var session = await CreateSession("B");
        var id = AnswerId(await Submit(Request(session, 1)));
        if (revisionAction) await Decision(id, true);
        using var gate = new Barrier(2);
        Task<IActionResult> ConcurrentAction(bool first) => Task.Run(async () =>
        {
            gate.SignalAndWait();
            return revisionAction
                ? await Revision(id, first || sameAction ? "First revision" : "Different revision")
                : await Decision(id, first || sameAction);
        });
        var results = await Task.WhenAll(ConcurrentAction(true), ConcurrentAction(false));
        var label = $"concurrent revision={revisionAction}, identical={sameAction}";
        Check(results.Count(r => r is OkObjectResult) == (sameAction ? 2 : 1), label + ": success count");
        Check(results.Count(r => r is ConflictObjectResult) == (sameAction ? 0 : 1), label + ": conflict count");
        var winner = (OkObjectResult)results.First(r => r is OkObjectResult);
        var state = await Snapshot(id);
        using var value = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(winner.Value));
        var retry = revisionAction
            ? await Revision(id, value.RootElement.GetProperty("RevisedAnswerText").GetString()!)
            : await Decision(id, value.RootElement.GetProperty("ChoseToRevise").GetBoolean());
        Check(retry is OkObjectResult && state == await Snapshot(id), label + ": winner remains unchanged on retry");
    }

    foreach (var condition in new[] { "A", "B" })
    foreach (var revise in new[] { false, true })
        await TestFinalStageCompletion(condition, revise);

    await FeedbackTests.Run(options, Check);
    await ScriptFeedbackTests.Run(Check);
    await RevisionFlowTests.Run(options, Check);
    await RevisionFlowUiTests.Run(options, Check);
    await LocalTestModeTests.Run(Check);
    await SecurityHttpTests.Run(Check);

    Console.WriteLine($"PASS: {passed} assertions. Only temporary SQLite data and stub HTTP responses were used.");
}
finally
{
    Directory.Delete(directory, recursive: true);
}

void Check(bool condition, string description)
{
    if (!condition) throw new InvalidOperationException("FAIL: " + description);
    passed++;
    Console.WriteLine("PASS: " + description);
}

async Task TestFinalStageCompletion(string condition, bool revise)
{
    var label = $"final-stage/{condition}/revise={revise}";
    var sessionId = await CreateSession(condition);
    var answerId = 0;
    for (var stage = 1; stage <= 7; stage++)
        answerId = AnswerId(await Submit(Request(sessionId, stage)));

    // Cached feedback keeps this regression independent of Gemini and network access.
    await using (var db = new ApplicationDbContext(options))
    {
        var answer = (await db.UserAnswers.FindAsync(answerId))!;
        answer.FeedbackText = "Cached final-stage feedback";
        answer.FeedbackShownAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    async Task<bool> FetchFinished()
    {
        await using var db = new ApplicationDbContext(options);
        var controller = new InterviewController(db, null!, null!, new ConfigurationBuilder().Build());
        var result = await controller.GetNextQuestion(sessionId, answerId);
        Check(result is OkObjectResult, label + ": next-question succeeds");
        using var json = System.Text.Json.JsonDocument.Parse(
            System.Text.Json.JsonSerializer.Serialize(((OkObjectResult)result).Value));
        return json.RootElement.GetProperty("isFinished").GetBoolean();
    }

    var undecided = await Snapshot(answerId);
    Check(!await FetchFinished(), label + ": pending decision stays open");
    Check(undecided == await Snapshot(answerId), label + ": pending decision does not change persisted state");
    Check(await Decision(answerId, revise) is OkObjectResult, label + ": final decision saved");

    if (revise)
    {
        var pending = await Snapshot(answerId);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            Check(!await FetchFinished(), label + ": pending revision stays open on repeated GET");
            Check(pending == await Snapshot(answerId), label + ": repeated GET preserves pending state");
        }
        await using (var db = new ApplicationDbContext(options))
            Check((await db.InterviewSessions.FindAsync(sessionId))!.FinishedAt == null,
                label + ": no completion timestamp before revision");
        Check(await Revision(answerId, "Completed final revision") is OkObjectResult, label + ": revision saved");
    }

    await using (var db = new ApplicationDbContext(options))
        Check((await db.InterviewSessions.FindAsync(sessionId))!.FinishedAt.HasValue,
            label + ": normal action persists completion");
    var completed = await Snapshot(answerId);
    for (var attempt = 0; attempt < 2; attempt++)
    {
        Check(await FetchFinished(), label + ": completed session stays finished");
        Check(completed == await Snapshot(answerId), label + ": completion timestamp survives repeated GET");
    }
}

async Task<Guid> CreateSession(string condition)
{
    await using var db = new ApplicationDbContext(options);
    var session = new InterviewSession { User = new User { Nickname = "Isolated test" }, Condition = condition };
    db.InterviewSessions.Add(session);
    await db.SaveChangesAsync();
    return session.Id;
}

async Task<IActionResult> Submit(SubmitAnswerRequest request)
{
    await using var db = new ApplicationDbContext(options);
    // SubmitAnswer does not use either manager; null ensures no feedback can be generated.
    var controller = new InterviewController(db, null!, null!, new ConfigurationBuilder().Build());
    return await controller.SubmitAnswer(request);
}

async Task<int> Count(Guid sessionId)
{
    await using var db = new ApplicationDbContext(options);
    return await db.UserAnswers.CountAsync(a => a.InterviewSessionId == sessionId);
}

static SubmitAnswerRequest Request(Guid sessionId, int stage) => new()
{
    SubmissionId = Guid.NewGuid(), SessionId = sessionId, StageNumber = stage,
    StageName = "Test stage", QuestionText = "Test question", AnswerText = "Test answer",
    QuestionShownAt = DateTime.UtcNow.AddSeconds(-10), OriginalAnswerSubmittedAt = DateTime.UtcNow
};

static int AnswerId(IActionResult result) =>
    (int)((OkObjectResult)result).Value!.GetType().GetProperty("answerId")!.GetValue(((OkObjectResult)result).Value)!;

async Task TestFeedbackRecovery(string failure)
{
    var handler = new FeedbackHandler(failure);
    var activator = new CapturingActivator();
    var services = new ServiceCollection();
    services.AddLogging();
    services.AddMudServices();
    services.AddSingleton<IJSRuntime, NoOpJs>();
    services.AddSingleton<NavigationManager, TestNavigation>();
    services.AddSingleton<IComponentActivator>(activator);
    services.AddSingleton(new HttpClient(handler) { BaseAddress = new Uri("http://isolated.test/") });
    await using var provider = services.BuildServiceProvider();
    await using var renderer = new TestHtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
    await renderer.Dispatcher.InvokeAsync(async () =>
    {
        var popovers = renderer.BeginRenderingComponent(typeof(MudBlazor.MudPopoverProvider), ParameterView.Empty);
        await popovers.QuiescenceTask;
        var root = renderer.BeginRenderingComponent(typeof(Home), ParameterView.Empty);
        await root.QuiescenceTask;
        var home = activator.Home!;
        Set(home, "isSessionStarted", true);
        Set(home, "sessionId", Guid.NewGuid());
        Set(home, "currentQuestionText", "Test question");
        Set(home, "currentQuestionShownAt", DateTime.UtcNow.AddSeconds(-10));
        Set(home, "userInput", "Test answer");
        await Invoke(home, "SendMessage");
        Check(Get(home, "interviewState")!.ToString() == "AwaitingFeedback", $"{failure}: saved answer locked");
        Check(Get(home, "feedbackRetrievalError") is string { Length: > 0 }, $"{failure}: visible error state");
        var html = WebUtility.HtmlDecode(root.ToHtmlString());
        Check(html.Contains("Επανάληψη ανάκτησης ανατροφοδότησης"), $"{failure}: retry rendered");
        Check(!html.Contains("Γράψτε την απάντησή σας"), $"{failure}: original composer absent");
        Check(!(bool)Get(home, "isBotTyping")!, $"{failure}: loading cleared");
        Set(home, "userInput", "Attempted duplicate");
        await Invoke(home, "SendMessage");
        Check(handler.Posts == 1 && handler.Gets == 1, $"{failure}: duplicate submission blocked");
        // Keep the retry pending to verify a second retry cannot issue another GET.
        var retry = Invoke(home, "RetryFeedback");
        await Invoke(home, "RetryFeedback");
        Check(handler.Posts == 1 && handler.Gets == 2, $"{failure}: retry only fetches once");
        handler.ReleaseRetry();
        await retry;
        Check(Get(home, "interviewState")!.ToString() == "WaitingForDecision", $"{failure}: feedback recovered");
        Check(Get(home, "feedbackRetrievalError") == null, $"{failure}: error cleared");
        Check((int)Get(home, "currentStage")! == 1, $"{failure}: recovery does not skip stage");
        Check(WebUtility.HtmlDecode(root.ToHtmlString()).Contains("Recovered test feedback"),
            $"{failure}: recovered feedback displayed");
    });
}

// SQLite reloads DateTime without Kind; compare persisted ticks, not the optional JSON UTC suffix.
static string NormalizeResponseDates(string json)
{
    using var doc = System.Text.Json.JsonDocument.Parse(json);
    return System.Text.Json.JsonSerializer.Serialize(doc.RootElement.EnumerateObject().ToDictionary(
        p => p.Name,
        p => p.Value.ValueKind == System.Text.Json.JsonValueKind.String && p.Value.TryGetDateTime(out var date)
            ? date.Ticks.ToString() : p.Value.GetRawText()));
}

async Task<IActionResult> Decision(int id, bool revise)
{
    await using var db = new ApplicationDbContext(options);
    return await new InterviewController(db, null!, null!, new ConfigurationBuilder().Build())
        .SaveAnswerDecision(id, new AnswerDecisionRequest { ChoseToRevise = revise });
}

async Task<IActionResult> Revision(int id, string text)
{
    await using var db = new ApplicationDbContext(options);
    return await new InterviewController(db, null!, null!, new ConfigurationBuilder().Build())
        .ReviseAnswer(id, new ReviseAnswerRequest { RevisedAnswerText = text });
}

async Task<string> Snapshot(int id)
{
    await using var db = new ApplicationDbContext(options);
    var answer = await db.UserAnswers.Include(a => a.InterviewSession).SingleAsync(a => a.Id == id);
    return System.Text.Json.JsonSerializer.Serialize(new
    {
        answer.ChoseToRevise, answer.RevisedAnswerText, answer.RevisionDecisionAt,
        answer.RevisionSubmittedAt, answer.DecisionLatencyInSeconds, answer.RevisionLatencyInSeconds,
        answer.InterviewSession!.FinishedAt
    });
}

async Task TestAnswerActions(string condition, int stage, bool revise, bool loseResponse)
{
    var label = $"{condition}/stage {stage}/revise {revise}/lost response {loseResponse}";
    var session = await CreateSession(condition);
    var id = 0;
    for (var n = 1; n <= stage; n++) id = AnswerId(await Submit(Request(session, n)));
    Check(await Revision(id, "Premature") is BadRequestObjectResult, label + ": revision before decision rejected");
    var before = await Snapshot(id);
    var requests = new List<string>();
    var firstResponses = new Dictionary<string, string>();
    var storedSnapshots = new Dictionary<string, string>();
    var handler = new ActionHandler(async request =>
    {
        Check(request.Method == HttpMethod.Put, label + ": only PUT used for action");
        var endpoint = request.RequestUri!.AbsolutePath.Split('/').Last();
        var body = await request.Content!.ReadAsStringAsync();
        requests.Add(endpoint + body);
        using var doc = System.Text.Json.JsonDocument.Parse(body);
        var result = endpoint == "decision"
            ? await Decision(id, doc.RootElement.GetProperty("choseToRevise").GetBoolean())
            : await Revision(id, doc.RootElement.GetProperty("revisedAnswerText").GetString()!);
        Check(result is OkObjectResult, label + ": action endpoint success");
        var json = System.Text.Json.JsonSerializer.Serialize(((OkObjectResult)result).Value);
        if (!firstResponses.ContainsKey(endpoint))
        {
            firstResponses[endpoint] = NormalizeResponseDates(json);
            storedSnapshots[endpoint] = await Snapshot(id);
            if (loseResponse) throw new HttpRequestException("Response lost AFTER database commit");
        }
        else
        {
            Check(firstResponses[endpoint] == NormalizeResponseDates(json), label + ": retry returns original state");
            Check(storedSnapshots[endpoint] == await Snapshot(id), label + ": retry preserves times and state");
        }
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };
    });
    var activator = new CapturingActivator();
    var services = new ServiceCollection();
    services.AddLogging();
    services.AddMudServices();
    services.AddSingleton<IJSRuntime, NoOpJs>();
    services.AddSingleton<NavigationManager, TestNavigation>();
    services.AddSingleton<IComponentActivator>(activator);
    services.AddSingleton(new HttpClient(handler) { BaseAddress = new Uri("http://isolated.test/") });
    await using var provider = services.BuildServiceProvider();
    await using var renderer = new TestHtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
    await renderer.Dispatcher.InvokeAsync(async () =>
    {
        await renderer.BeginRenderingComponent(typeof(MudBlazor.MudPopoverProvider), ParameterView.Empty).QuiescenceTask;
        var root = renderer.BeginRenderingComponent(typeof(Home), ParameterView.Empty);
        await root.QuiescenceTask;
        var home = activator.Home!;
        Set(home, "isSessionStarted", true);
        Set(home, "currentAnswerId", id);
        Set(home, "currentStage", stage);
        Set(home, "originalAnswerText", "Original test text");
        Set(home, "pendingQuestion", new AIInterviewSimulator.Client.Models.QuestionResponseDto
        {
            StageNumber = stage == 7 ? 7 : stage + 1,
            QuestionText = stage == 7 ? "" : "Next test question"
        });
        Set(home, "interviewState", Enum.Parse(Get(home, "interviewState")!.GetType(), "WaitingForDecision"));
        await Invoke(home, revise ? "ChooseRevision" : "ContinueWithoutRevision");
        Check(before != await Snapshot(id), label + ": decision persisted");
        if (loseResponse)
        {
            Check(Get(home, "interviewState")!.ToString() == "WaitingForDecision", label + ": remains pending on lost response");
            Check(WebUtility.HtmlDecode(root.ToHtmlString()).Contains("Δεν μπορέσαμε να επιβεβαιώσουμε"), label + ": uncertain outcome explained");
            var count = requests.Count;
            await Invoke(home, revise ? "ContinueWithoutRevision" : "ChooseRevision");
            Check(requests.Count == count, label + ": opposing decision blocked while uncertain");
            await Invoke(home, "RetryAnswerAction");
            Check(requests[0] == requests[1], label + ": exact decision retried");
        }
        var decisionSnapshot = await Snapshot(id);
        Check(await Decision(id, revise) is OkObjectResult, label + ": identical decision accepted");
        Check(await Decision(id, !revise) is ConflictObjectResult, label + ": different decision rejected");
        Check(decisionSnapshot == await Snapshot(id), label + ": decision retries do not mutate data");
        if (revise)
        {
            Check(Get(home, "interviewState")!.ToString() == "Revising", label + ": revision editor opened");
            await using (var db = new ApplicationDbContext(options))
                Check((await db.InterviewSessions.FindAsync(session))!.FinishedAt == null, label + ": choosing revision does not finish session");
            Set(home, "userInput", "  Revised test text  ");
            await Invoke(home, "SubmitRevision");
            if (loseResponse)
            {
                Check(Get(home, "interviewState")!.ToString() == "Revising", label + ": revision remains pending");
                Check(Get(home, "actionError") is string { Length: > 0 }, label + ": revision error visible");
                // A stale input update must not change the payload of the retry.
                Set(home, "userInput", "Different text after uncertain result");
                await Invoke(home, "RetryAnswerAction");
                var revisions = requests.Where(r => r.StartsWith("revision")).ToArray();
                Check(revisions.Length == 2 && revisions[0] == revisions[1], label + ": exact revision retried");
            }
            var snapshot = await Snapshot(id);
            Check(await Revision(id, " Revised test text ") is OkObjectResult, label + ": trimmed identical revision accepted");
            Check(await Revision(id, "Different revision") is ConflictObjectResult, label + ": different revision rejected");
            Check(await Revision(id, " ") is BadRequestObjectResult, label + ": empty revision rejected");
            Check(snapshot == await Snapshot(id), label + ": revision and completion timestamps unchanged");
        }
        else
            Check(await Revision(id, "Not permitted") is BadRequestObjectResult, label + ": revision after continue rejected");
        Check(Get(home, "actionError") == null, label + ": error cleared after confirmation");
        Check(!(bool)Get(home, "isActionLoading")!, label + ": loading cleared");
        Check(Get(home, "interviewState")!.ToString() == (stage == 7 ? "Finished" : "Answering"), label + ": correct continuation");
        await using (var db = new ApplicationDbContext(options))
            Check((await db.InterviewSessions.FindAsync(session))!.FinishedAt.HasValue == (stage == 7), label + ": correct persisted completion");
        Check(await Count(session) == stage, label + ": no extra answer rows");
    });
}

static object? Get(Home home, string name) => typeof(Home).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(home);
static void Set(Home home, string name, object value) => typeof(Home).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(home, value);
static Task Invoke(Home home, string name) => (Task)typeof(Home).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(home, null)!;

sealed class CapturingActivator : IComponentActivator
{
    public Home? Home { get; private set; }
    public IComponent CreateInstance(Type type)
    {
        var component = (IComponent)Activator.CreateInstance(type)!;
        if (component is Home home) Home = home;
        return component;
    }
}

// Render the existing interactive component locally, without starting a server/circuit.
#pragma warning disable BL0006
sealed class TestHtmlRenderer(IServiceProvider services, ILoggerFactory loggerFactory)
    : StaticHtmlRenderer(services, loggerFactory)
{
    protected override IComponent ResolveComponentForRenderMode(Type componentType, int? parentComponentId,
        IComponentActivator componentActivator, IComponentRenderMode renderMode) =>
        componentActivator.CreateInstance(componentType);
}
#pragma warning restore BL0006

sealed class NoOpJs : IJSRuntime
{
    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => ValueTask.FromResult(default(TValue)!);
    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => InvokeAsync<TValue>(identifier, args);
}

sealed class TestNavigation : NavigationManager
{
    public TestNavigation() => Initialize("http://isolated.test/", "http://isolated.test/");
    protected override void NavigateToCore(string uri, bool forceLoad) => throw new NotSupportedException();
}

sealed class FeedbackHandler(string failure) : HttpMessageHandler
{
    public int Posts { get; private set; }
    public int Gets { get; private set; }
    private readonly TaskCompletionSource retry = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public void ReleaseRetry() => retry.TrySetResult();
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        if (request.Method == HttpMethod.Post)
        {
            Posts++;
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { answerId = 42 }) };
        }
        if (request.Method != HttpMethod.Get) throw new InvalidOperationException("Unexpected HTTP request");
        Gets++;
        if (Gets == 1)
        {
            return failure switch
            {
                "http" => new(HttpStatusCode.ServiceUnavailable),
                "network" => throw new HttpRequestException("Simulated connection loss"),
                "timeout" => throw new TaskCanceledException("Simulated timeout"),
                "empty" => new(HttpStatusCode.OK) { Content = JsonContent.Create(new { stageNumber = 2, questionText = "Next", feedback = " ", answerId = 42, feedbackStatus = "Success" }) },
                "unavailable" => new(HttpStatusCode.OK) { Content = JsonContent.Create(new { answerId = 42, feedbackStatus = "Unavailable", feedback = "Η ανατροφοδότηση δεν ήταν διαθέσιμη αυτή τη στιγμή." }) },
                "invalid-result" => new(HttpStatusCode.OK) { Content = JsonContent.Create(new { answerId = 42, feedbackStatus = "InvalidResult", feedback = " " }) },
                "sentinel" => new(HttpStatusCode.OK) { Content = JsonContent.Create(new { answerId = 42, feedbackStatus = "Success", feedback = "Η ανατροφοδότηση δεν ήταν διαθέσιμη αυτή τη στιγμή." }) },
                "wrong-answer" => new(HttpStatusCode.OK) { Content = JsonContent.Create(new { answerId = 99, feedbackStatus = "Success", feedback = "Unrelated feedback" }) },
                "null" => new(HttpStatusCode.OK) { Content = new StringContent("null", System.Text.Encoding.UTF8, "application/json") },
                _ => new(HttpStatusCode.OK) { Content = new StringContent("{", System.Text.Encoding.UTF8, "application/json") }
            };
        }
        await retry.Task.WaitAsync(token);
        return new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { stageNumber = 2, questionText = "Next test question", feedback = "Recovered test feedback", isFinished = false, feedbackStatus = "Success", answerId = 42 })
        };
    }
}

sealed class ActionHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => respond(request);
}
