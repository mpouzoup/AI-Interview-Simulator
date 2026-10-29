using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text;
using System.Text.Json;
using AIInterviewSimulator.Api.Controllers;
using AIInterviewSimulator.Api.DTOs.Requests;
using AIInterviewSimulator.Client.Pages;
using AIInterviewSimulator.Data.Context;
using AIInterviewSimulator.Data.Entities;
using AIInterviewSimulator.Engine.Engines;
using AIInterviewSimulator.Engine.Feedback;
using AIInterviewSimulator.Engine.Managers;
using AIInterviewSimulator.Engine.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using MudBlazor.Services;

internal static class RevisionFlowUiTests
{
    internal static async Task Run(DbContextOptions<ApplicationDbContext> options, Action<bool, string> check)
    {
        foreach (var condition in new[] { "A", "B" })
        foreach (var stage in new[] { 1, 7 })
        foreach (var revise in new[] { false, true })
        {
            var label = $"v2 UI/{condition}/stage={stage}/revise={revise}";
            var session = new InterviewSession { FlowVersion = 2, Condition = condition, User = new User { Nickname = "UI isolated" } };
            for (var n = 1; n <= stage; n++) session.Answers.Add(new UserAnswer
            {
                SubmissionId = Guid.NewGuid(), StageNumber = n, StageName = "Isolated", AnswerText = "Original text",
                QuestionText = await new ScriptBasedEngine().GetNextQuestionAsync(n, new()),
                StageCompletedAt = n < stage ? DateTime.UtcNow : null
            });
            await using (var db = new ApplicationDbContext(options)) { db.Add(session); await db.SaveChangesAsync(); }
            var answerId = session.Answers.Last().Id;
            var failedProvider = condition == "B";
            using var providerHttp = new HttpClient(new Stub(async (request, _) =>
            {
                var prompt = await request.Content!.ReadAsStringAsync();
                if (failedProvider && prompt.Contains("REVISED"))
                    return new HttpResponseMessage(HttpStatusCode.BadRequest);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new { candidates = new[] { new { finishReason = "STOP", content = new { parts = new[] { new { text = "Provider feedback" } } } } } })
                };
            }));
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["GeminiApiKey"] = "isolated" }).Build();
            var manager = new FeedbackManager(new ScriptFeedbackEngine(), new LlmFeedbackEngine(new GeminiService(providerHttp, config)));
            var losses = new HashSet<string>();
            var failNextQuestionOnce = true;
            var payloads = new Dictionary<string, string>();
            using var handler = new Stub(async (request, _) =>
            {
                await using var db = new ApplicationDbContext(options);
                var controller = new InterviewController(db, new InterviewManager(new ScriptBasedEngine()), manager, config);
                var path = request.RequestUri!.AbsolutePath;
                var body = request.Content == null ? "" : await request.Content.ReadAsStringAsync();
                var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                IActionResult result;
                if (request.Method == HttpMethod.Get)
                {
                    if (failNextQuestionOnce) { failNextQuestionOnce = false; throw new HttpRequestException("Next question retrieval failed"); }
                    result = await controller.GetNextQuestion(session.Id);
                }
                else if (path.EndsWith("/feedback/1")) result = await controller.GenerateAnswerFeedback(answerId, 1);
                else if (path.EndsWith("/feedback/2")) result = await controller.GenerateAnswerFeedback(answerId, 2);
                else if (path.EndsWith("/feedback/1/shown")) result = await controller.AcknowledgeFeedback(answerId, 1, JsonSerializer.Deserialize<FlowTimingRequest>(body, jsonOptions)!);
                else if (path.EndsWith("/feedback/2/shown")) result = await controller.AcknowledgeFeedback(answerId, 2, JsonSerializer.Deserialize<FlowTimingRequest>(body, jsonOptions)!);
                else if (path.EndsWith("/revision-editor/shown")) result = await controller.AcknowledgeRevisionEditor(answerId, JsonSerializer.Deserialize<FlowTimingRequest>(body, jsonOptions)!);
                else if (path.EndsWith("/decision")) result = await controller.SaveAnswerDecision(answerId, JsonSerializer.Deserialize<AnswerDecisionRequest>(body, jsonOptions)!);
                else if (path.EndsWith("/revision")) result = await controller.ReviseAnswer(answerId, JsonSerializer.Deserialize<ReviseAnswerRequest>(body, jsonOptions)!);
                else if (path.EndsWith("/continue")) result = await controller.ContinueAfterRevision(answerId, JsonSerializer.Deserialize<FlowTimingRequest>(body, jsonOptions)!);
                else throw new InvalidOperationException("Unexpected path " + path);
                var status = result is ObjectResult obj ? obj.StatusCode ?? 200 : result is StatusCodeResult code ? code.StatusCode : 500;
                if (status == 200 && (path.EndsWith("/decision") || path.EndsWith("/revision") || path.EndsWith("/continue") || path.EndsWith("/feedback/2/shown")))
                {
                    if (losses.Add(path)) { payloads[path] = body; throw new HttpRequestException("Response lost AFTER commit"); }
                    check(payloads[path] == body, label + ": immutable retry payload for " + path.Split('/').Last());
                }
                return new HttpResponseMessage((HttpStatusCode)status)
                {
                    Content = new StringContent(result is ObjectResult value ? JsonSerializer.Serialize(value.Value) : "{}", Encoding.UTF8, "application/json")
                };
            });
            var activator = new CapturingActivator();
            var services = new ServiceCollection();
            services.AddLogging(); services.AddMudServices();
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
                Set(home, "isSessionStarted", true); Set(home, "sessionId", session.Id); Set(home, "flowVersion", 2);
                Set(home, "currentStage", stage); Set(home, "currentAnswerId", answerId);
                SetState(home, "AwaitingFeedback");
                await Invoke(home, "GetNextQuestionFromApi");
                check(State(home) == "WaitingForDecision", label + ": first feedback displayed");
                await Invoke(home, "RecordRenderedPhase");
                check(!(bool)Get(home, "awaitingDisplayAck")!, label + ": unavailable JS still acknowledges display without fake browser time");
                await Invoke(home, revise ? "ChooseRevision" : "ContinueWithoutRevision");
                check(State(home) == "WaitingForDecision", label + ": lost decision response leaves state pending");
                await Invoke(home, "RetryAnswerAction");
                if (!revise)
                {
                    check(State(home) == "LoadingNextQuestion" && !root.ToHtmlString().Contains("original-answer-input"), label + ": failed next retrieval does not reopen previous editor");
                    await Invoke(home, "GetNextQuestionFromApi");
                    check(State(home) == (stage == 7 ? "Finished" : "Answering"), label + ": continue without revision recovers");
                    return;
                }
                check(State(home) == "Revising", label + ": editor available after saved decision");
                await Invoke(home, "RecordRenderedPhase");
                Set(home, "userInput", "REVISED text");
                await Invoke(home, "SubmitRevision");
                check(State(home) == "Revising", label + ": lost revision response stays pending");
                Set(home, "userInput", "Should never be sent");
                await Invoke(home, "RetryAnswerAction");
                if (condition == "B")
                {
                    check(State(home) == "AwaitingRevisedFeedback" && Get(home, "feedbackRetrievalError") != null, label + ": provider failure shows feedback-only retry");
                    failedProvider = false;
                    await Invoke(home, "RetryFeedback");
                }
                check(State(home) == "WaitingForContinue", label + ": second feedback displayed");
                await Invoke(home, "RecordRenderedPhase");
                check((bool)Get(home, "awaitingDisplayAck")!, label + ": lost display acknowledgement remains retryable");
                await Invoke(home, "AcknowledgeDisplayedFeedback");
                var html = root.ToHtmlString();
                check(html.Contains("Συνέχεια") && !html.Contains("Βελτίωση απάντησης") && !html.Contains("Υποβολή βελτιωμένης απάντησης"),
                    label + ": only continue, no third answer editor");
                var requestCount = payloads.Count;
                await Invoke(home, "ChooseRevision"); await Invoke(home, "SubmitRevision");
                check(payloads.Count == requestCount, label + ": editor handlers cannot reopen third answer");
                await Invoke(home, "ContinueAfterRevisedFeedback");
                check(State(home) == "WaitingForContinue", label + ": lost continue response stays retryable");
                await Invoke(home, "RetryAnswerAction");
                check(State(home) == "LoadingNextQuestion" && !root.ToHtmlString().Contains("original-answer-input"), label + ": next retrieval failure keeps previous editor closed");
                await Invoke(home, "GetNextQuestionFromApi");
                check(State(home) == (stage == 7 ? "Finished" : "Answering"), label + ": final continue recovers");
            });
            await using var read = new ApplicationDbContext(options);
            var row = await read.UserAnswers.SingleAsync(a => a.Id == answerId);
            check(row.AnswerText == "Original text" && (!revise || row.RevisedAnswerText == "REVISED text") && row.StageCompletedAt != null,
                label + ": original/revision and completed stage retained");
            check(row.OriginalWritingMilliseconds == null && row.RevisedFeedbackBrowserDisplayedAt == null,
                label + ": unavailable browser observations remain null");
        }
    }
    private static object? Get(Home home, string name) => typeof(Home).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(home);
    private static void Set(Home home, string name, object value) => typeof(Home).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(home, value);
    private static void SetState(Home home, string value) => Set(home, "interviewState", Enum.Parse(typeof(Home).GetNestedType("InterviewUiState", BindingFlags.NonPublic)!, value));
    private static string State(Home home) => Get(home, "interviewState")!.ToString()!;
    private static Task Invoke(Home home, string name) => (Task)typeof(Home).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(home, null)!;
    private sealed class Stub(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request, token);
    }
}
