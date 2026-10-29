using System.Net;
using System.Text;
using System.Text.Json;
using AIInterviewSimulator.Api.Controllers;
using AIInterviewSimulator.Api.DTOs.Requests;
using AIInterviewSimulator.Client.Models;
using AIInterviewSimulator.Data.Context;
using AIInterviewSimulator.Data.Entities;
using AIInterviewSimulator.Engine.Engines;
using AIInterviewSimulator.Engine.Feedback;
using AIInterviewSimulator.Engine.Managers;
using AIInterviewSimulator.Engine.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Configuration;

internal static class RevisionFlowTests
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private const string Original = "Αντιμετώπισα πρόβλημα. Έλεγξα δεδομένα.";
    private const string Revised = "Αντιμετώπισα πρόβλημα. Έλεγξα δεδομένα. Παραδώσαμε εγκαίρως.";

    internal static async Task Run(DbContextOptions<ApplicationDbContext> options, Action<bool, string> check)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["GeminiApiKey"] = "isolated-key", ["Export:Key"] = new string('x', 32),
            ["Study:CollectionEnabled"] = "true", ["Study:Consent:Approved"] = "true",
            ["Study:Consent:Version"] = "synthetic-v1", ["Study:Consent:Text"] = "SYNTHETIC TEST ONLY"
        }).Build();
        var prompts = new List<string>();
        using var http = new HttpClient(new Stub(async (req, _) =>
        {
            var text = await req.Content!.ReadAsStringAsync();
            var prompt = JsonDocument.Parse(text).RootElement.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString()!;
            prompts.Add(prompt);
            return Valid(prompt.Contains(Revised) ? "Feedback on revised answer" : "Feedback on original answer");
        }));
        FeedbackManager Manager(HttpClient client) => new(new ScriptFeedbackEngine(), new LlmFeedbackEngine(new GeminiService(client, config)));
        async Task<IActionResult> Call(Func<InterviewController, Task<IActionResult>> action, HttpClient? client = null)
        {
            await using var db = new ApplicationDbContext(options);
            var controller = new InterviewController(db, new InterviewManager(new ScriptBasedEngine()), Manager(client ?? http), config);
            controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
            controller.Request.Headers["X-Export-Key"] = new string('x', 32);
            return await action(controller);
        }
        async Task<Guid> Session(string condition)
        {
            await using var db = new ApplicationDbContext(options);
            var session = new InterviewSession { FlowVersion = 2, Condition = condition, User = new User { Nickname = "V2 isolated" } };
            db.Add(session); await db.SaveChangesAsync(); return session.Id;
        }
        async Task<UserAnswer> Read(int id)
        {
            await using var db = new ApplicationDbContext(options);
            return await db.UserAnswers.AsNoTracking().SingleAsync(a => a.Id == id);
        }
        static T Value<T>(IActionResult result) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(((OkObjectResult)result).Value), JsonOptions)!;
        async Task<SubmitAnswerRequest> Request(Guid session, int stage) => new()
        {
            SubmissionId = Guid.NewGuid(), SessionId = session, StageNumber = stage, StageName = "Isolated stage",
            QuestionText = await new ScriptBasedEngine().GetNextQuestionAsync(stage, new()), AnswerText = Original,
            QuestionShownAt = DateTime.UtcNow.AddSeconds(-15), OriginalAnswerSubmittedAt = DateTime.UtcNow,
            QuestionBrowserDisplayedAt = DateTime.UtcNow.AddSeconds(-15),
            Timing = new() { BrowserOccurredAt = DateTime.UtcNow, ElapsedMilliseconds = 12000 }
        };
        await using (var registrationDb = new ApplicationDbContext(options))
        {
            var beforeUsers = await registrationDb.Users.CountAsync();
            foreach (var age in new[] { 0, 17, 100 })
                check(await Call(c => c.CreateUser(new() { Nickname = "Isolated registration", Age = age })) is BadRequestObjectResult,
                    $"registration: direct API age {age} rejected");
            check(await Call(c => c.CreateUser(new() { Nickname = " ", Age = 22 })) is BadRequestObjectResult,
                "registration: blank nickname rejected by API");
            check(await registrationDb.Users.CountAsync() == beforeUsers, "registration: rejected submissions do not create rows");
            foreach (var age in new[] { 18, 99 })
            {
                var result = await Call(c => c.CreateUser(new() { Nickname = " Isolated registration ", Age = age, ConsentAccepted = true, ConsentVersion = "synthetic-v1", ConsentTextHash = AIInterviewSimulator.Api.Security.StudyPolicy.Hash("SYNTHETIC TEST ONLY") }));
                check(result is OkObjectResult, $"registration: existing UI boundary age {age} accepted");
                var registered = Value<User>(result);
                check(registered.Nickname == "Isolated registration", "registration: nickname trimmed without changing demographics");
            }
        }

        var shown = new FlowTimingRequest { BrowserOccurredAt = DateTime.UtcNow, WaitMilliseconds = 2500 };
        foreach (var condition in new[] { "A", "B" })
        foreach (var revise in new[] { false, true })
        {
            var session = await Session(condition);
            for (var stage = 1; stage <= 7; stage++)
            {
                var label = $"v2/{condition}/revise={revise}/stage={stage}";
                var request = await Request(session, stage);
                var submitted = await Call(c => c.SubmitAnswer(request));
                check(submitted is OkObjectResult, label + ": initial saved");
                var id = JsonSerializer.SerializeToElement(((OkObjectResult)submitted).Value).GetProperty("answerId").GetInt32();
                check(await Call(c => c.SubmitAnswer(request)) is OkObjectResult, label + ": initial replay accepted");
                check(await Call(c => c.SaveAnswerDecision(id, new() { ChoseToRevise = true })) is ConflictObjectResult, label + ": decision before feedback rejected");
                check(await Call(c => c.GenerateAnswerFeedback(id, 2)) is ConflictObjectResult, label + ": premature second feedback rejected");
                var first = Value<QuestionResponseDto>(await Call(c => c.GenerateAnswerFeedback(id, 1)));
                check(first.FeedbackStatus == "Success" && first.Round == 1 && first.FlowState == "WaitingForDecision", label + ": initial feedback");
                check(await Call(c => c.SaveAnswerDecision(id, new() { ChoseToRevise = true })) is ConflictObjectResult, label + ": acknowledgement required");
                check(await Call(c => c.AcknowledgeFeedback(id, 1, shown)) is OkResult, label + ": initial display acknowledged");
                var before = await Read(id);
                var calls = prompts.Count;
                await Call(c => c.GenerateAnswerFeedback(id, 1));
                await Call(c => c.AcknowledgeFeedback(id, 1, new() { WaitMilliseconds = 99999 }));
                var replay = await Read(id);
                check(prompts.Count == calls && replay.FeedbackDisplayAcknowledgedAt == before.FeedbackDisplayAcknowledgedAt && replay.FeedbackWaitMilliseconds == 2500,
                    label + ": cached feedback and immutable display observation");
                if (stage < 7)
                {
                    var premature = await Request(session, stage + 1);
                    check(await Call(c => c.SubmitAnswer(premature)) is ConflictObjectResult, label + ": cannot skip current stage");
                }
                var decision = new AnswerDecisionRequest { ChoseToRevise = revise, Timing = new() { ElapsedMilliseconds = 4000, StageElapsedMilliseconds = 20000 } };
                check(await Call(c => c.SaveAnswerDecision(id, decision)) is OkObjectResult, label + ": decision saved");
                var decisionAt = (await Read(id)).RevisionDecisionAt;
                await Call(c => c.SaveAnswerDecision(id, decision));
                check((await Read(id)).RevisionDecisionAt == decisionAt, label + ": decision replay preserves time");
                check(await Call(c => c.SaveAnswerDecision(id, new() { ChoseToRevise = !revise })) is ConflictObjectResult, label + ": opposing decision rejected");
                if (revise)
                {
                    check(await Call(c => c.ContinueAfterRevision(id, new())) is ConflictObjectResult, label + ": premature continue rejected");
                    await Call(c => c.AcknowledgeRevisionEditor(id, new() { BrowserOccurredAt = DateTime.UtcNow }));
                    var revision = new ReviseAnswerRequest { RevisedAnswerText = Revised, Timing = new() { BrowserOccurredAt = DateTime.UtcNow, ElapsedMilliseconds = 8000 } };
                    check(await Call(c => c.ReviseAnswer(id, revision)) is OkObjectResult, label + ": revision saved");
                    var revisedAt = (await Read(id)).RevisionSubmittedAt;
                    await Call(c => c.ReviseAnswer(id, revision));
                    check((await Read(id)).RevisionSubmittedAt == revisedAt, label + ": revision replay preserves time");
                    check(await Call(c => c.ReviseAnswer(id, new() { RevisedAnswerText = "Another answer" })) is ConflictObjectResult, label + ": second revision rejected");
                    var second = Value<QuestionResponseDto>(await Call(c => c.GenerateAnswerFeedback(id, 2)));
                    check(second.Round == 2 && second.FlowState == "WaitingForContinue" && second.FeedbackStatus == "Success", label + ": second feedback ready");
                    var row = await Read(id);
                    var expected = condition == "A" ? await new ScriptFeedbackEngine().GenerateFeedbackAsync(stage, new UserAnswer { AnswerText = Revised }) : "Feedback on revised answer";
                    check(row.AnswerText == Original && row.RevisedAnswerText == Revised && row.FeedbackText == first.Feedback && row.RevisedFeedbackText == expected,
                        label + ": both answers and feedback preserved; revised input used");
                    check(row.StageCompletedAt == null, label + ": generation does not complete stage");
                    check(await Call(c => c.ContinueAfterRevision(id, new())) is ConflictObjectResult, label + ": second display acknowledgement required");
                    await Call(c => c.AcknowledgeFeedback(id, 2, shown));
                    var afterAck = await Read(id);
                    var promptCount = prompts.Count;
                    await Call(c => c.GenerateAnswerFeedback(id, 2));
                    await Call(c => c.AcknowledgeFeedback(id, 2, shown));
                    check(prompts.Count == promptCount && (await Read(id)).RevisedFeedbackDisplayAcknowledgedAt == afterAck.RevisedFeedbackDisplayAcknowledgedAt,
                        label + ": second feedback cached and acknowledgement immutable");
                    var complete = new FlowTimingRequest { ElapsedMilliseconds = 3000, StageElapsedMilliseconds = 40000 };
                    check(await Call(c => c.ContinueAfterRevision(id, complete)) is OkResult, label + ": continue accepted");
                    var completedAt = (await Read(id)).StageCompletedAt;
                    await Call(c => c.ContinueAfterRevision(id, new() { ElapsedMilliseconds = 100000 }));
                    row = await Read(id);
                    check(row.StageCompletedAt == completedAt && row.RevisedFeedbackReadingMilliseconds == 3000 && row.StageElapsedMilliseconds == 40000,
                        label + ": continue retry preserves original measurements");
                    check(row.OriginalWritingMilliseconds == 12000 && row.RevisionWritingMilliseconds == 8000 && row.DecisionMilliseconds == 4000 &&
                        row.FeedbackGenerationMilliseconds >= 0 && row.RevisedFeedbackGenerationMilliseconds >= 0 && row.FeedbackWaitMilliseconds == 2500,
                        label + ": writing, generation, decision and wait separate");
                }
                else
                {
                    var row = await Read(id);
                    check(row.StageCompletedAt != null && row.RevisedAnswerText == null && row.RevisedFeedbackText == null, label + ": continue without revision completes stage");
                    check(await Call(c => c.ReviseAnswer(id, new() { RevisedAnswerText = Revised })) is BadRequestObjectResult, label + ": revision after continue prohibited");
                }
                var next = Value<QuestionResponseDto>(await Call(c => c.GetNextQuestion(session)));
                check(stage == 7 ? next.IsFinished : next.StageNumber == stage + 1 && next.FlowState == "Answering", label + ": correct next stage or completion");
            }
            await using var finalDb = new ApplicationDbContext(options);
            check((await finalDb.InterviewSessions.FindAsync(session))!.FinishedAt != null && await finalDb.UserAnswers.CountAsync(a => a.InterviewSessionId == session) == 7,
                $"v2/{condition}/{revise}: seven rows and final completion");
        }

        // Real manager/service chain, with provider failures only; never call a live service.
        foreach (var failure in new[] { "http", "empty", "malformed", "truncated", "cancelled" })
        {
            var session = await Session("B");
            var req = await Request(session, 1);
            var saved = await Call(c => c.SubmitAnswer(req));
            var id = JsonSerializer.SerializeToElement(((OkObjectResult)saved).Value).GetProperty("answerId").GetInt32();
            await Call(c => c.GenerateAnswerFeedback(id, 1));
            await Call(c => c.AcknowledgeFeedback(id, 1, shown));
            await Call(c => c.SaveAnswerDecision(id, new() { ChoseToRevise = true }));
            await Call(c => c.ReviseAnswer(id, new() { RevisedAnswerText = Revised }));
            using var failing = new HttpClient(new Stub((_, token) =>
            {
                if (failure == "cancelled") throw new OperationCanceledException();
                if (failure == "http") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest));
                if (failure == "empty") return Task.FromResult(Valid(" "));
                if (failure == "truncated") return Task.FromResult(Valid("partial", "MAX_TOKENS"));
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{") });
            }));
            var failed = Value<QuestionResponseDto>(await Call(c => c.GenerateAnswerFeedback(id, 2), failing));
            var row = await Read(id);
            check(failed.FeedbackStatus != "Success" && row.RevisedFeedbackText == null && row.RevisedAnswerText == Revised && row.FeedbackText != null && row.FeedbackLeaseId == null,
                $"v2/{failure}: failure preserves saved work, releases lease, no A fallback");
            check(await Call(c => c.ContinueAfterRevision(id, new())) is ConflictObjectResult, $"v2/{failure}: failure blocks progression");
            var recovered = Value<QuestionResponseDto>(await Call(c => c.GenerateAnswerFeedback(id, 2)));
            check(recovered.FeedbackStatus == "Success" && (await Read(id)).RevisedFeedbackGenerationAttempts == 2, $"v2/{failure}: feedback-only retry recovers");
        }

        await ConcurrencyTests(options, config, check);
        await MigrationTest(check);
        var export = (FileContentResult)await Call(c => c.ExportDataAsCsv());
        var csv = Encoding.UTF8.GetString(export.FileContents);
        check(csv.Contains("RevisedFeedbackText") && csv.Contains("FlowVersion") && csv.Contains("OriginalWritingMilliseconds") && csv.Contains("RevisedFeedbackWaitMilliseconds"),
            "v2: CSV includes both feedback, version and separate timing columns");
        check(prompts.Where(p => p.Contains(Revised)).All(p => !p.Contains("Αρχική απάντηση") && !p.Contains("σύγκρινε")), "v2: no comparison instruction added to revised prompts");
    }

    private static async Task ConcurrencyTests(DbContextOptions<ApplicationDbContext> options, IConfiguration config, Action<bool, string> check)
    {
        // A committed revision with an existing first feedback is a valid pending second round.
        async Task<int> Pending(string condition)
        {
            await using var db = new ApplicationDbContext(options);
            var session = new InterviewSession { FlowVersion = 2, Condition = condition, User = new User { Nickname = "Concurrent" } };
            var answer = new UserAnswer { SubmissionId = Guid.NewGuid(), StageNumber = 7, AnswerText = Original, RevisedAnswerText = Revised,
                ChoseToRevise = true, FeedbackText = "First", FeedbackDisplayAcknowledgedAt = DateTime.UtcNow, InterviewSession = session };
            db.Add(answer); await db.SaveChangesAsync(); return answer.Id;
        }
        async Task<IActionResult> Generate(int id, HttpClient client)
        {
            await using var db = new ApplicationDbContext(options);
            return await new InterviewController(db, new InterviewManager(new ScriptBasedEngine()),
                new FeedbackManager(new ScriptFeedbackEngine(), new LlmFeedbackEngine(new GeminiService(client, config))), config).GenerateAnswerFeedback(id, 2);
        }
        foreach (var supersede in new[] { false, true })
        {
            var id = await Pending("B");
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var calls = 0;
            using var client = new HttpClient(new Stub(async (_, _) => { Interlocked.Increment(ref calls); entered.TrySetResult(); await release.Task; return Valid("Leased result"); }));
            var first = Generate(id, client);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var duplicate = await Generate(id, client);
            var json = JsonSerializer.SerializeToElement(((OkObjectResult)duplicate).Value);
            check(json.GetProperty("feedbackStatus").GetString() == "InProgress" && calls == 1, "v2: parallel generation is single-flight");
            if (supersede)
            {
                await using var db = new ApplicationDbContext(options);
                var answer = await db.UserAnswers.FindAsync(id);
                answer!.FeedbackLeaseUntil = DateTime.UtcNow.AddSeconds(-1);
                await db.SaveChangesAsync();
                using var replacement = new HttpClient(new Stub((_, _) => Task.FromResult(Valid("Replacement result"))));
                check(await Generate(id, replacement) is OkObjectResult, "v2: expired lease can be reclaimed");
            }
            release.TrySetResult();
            var result = await first;
            check(supersede ? result is ConflictObjectResult : result is OkObjectResult, "v2: stale lease cannot overwrite newer result");
            await using var read = new ApplicationDbContext(options);
            var row = await read.UserAnswers.SingleAsync(a => a.Id == id);
            check(row.RevisedFeedbackText == (supersede ? "Replacement result" : "Leased result") && row.FeedbackLeaseId == null,
                "v2: atomic lease completion preserves winning result");
        }
        async Task<IActionResult[]> Parallel(Func<bool, Task<IActionResult>> action)
        {
            using var gate = new Barrier(2);
            Task<IActionResult> Run(bool first) => Task.Run(async () => { gate.SignalAndWait(); return await action(first); });
            return await Task.WhenAll(Run(true), Run(false));
        }
        foreach (var condition in new[] { "A", "B" })
        {
            var id = await Pending(condition);
            using var client = new HttpClient(new Stub((_, _) => Task.FromResult(Valid("Ready"))));
            await Generate(id, client);
            async Task<IActionResult> Action(bool acknowledge)
            {
                await using var db = new ApplicationDbContext(options);
                var c = new InterviewController(db, null!, null!, config);
                return acknowledge ? await c.AcknowledgeFeedback(id, 2, new()) : await c.ContinueAfterRevision(id, new() { ElapsedMilliseconds = 1000 });
            }
            check((await Parallel(_ => Action(true))).All(r => r is OkResult), $"v2/{condition}: concurrent acknowledgement retries");
            check((await Parallel(_ => Action(false))).All(r => r is OkResult), $"v2/{condition}: concurrent continue retries");
        }
        foreach (var condition in new[] { "A", "B" })
        foreach (var same in new[] { true, false })
        {
            var id = await Pending(condition);
            await using (var db = new ApplicationDbContext(options))
            {
                var row = await db.UserAnswers.FindAsync(id);
                row!.ChoseToRevise = null; row.RevisedAnswerText = null;
                await db.SaveChangesAsync();
            }
            var decisions = await Parallel(async first =>
            {
                await using var db = new ApplicationDbContext(options);
                return await new InterviewController(db, null!, null!, config).SaveAnswerDecision(id, new() { ChoseToRevise = same || first });
            });
            check(decisions.Count(r => r is OkObjectResult) == (same ? 2 : 1) && decisions.Count(r => r is ConflictObjectResult) == (same ? 0 : 1),
                $"v2/{condition}: concurrent {(same ? "identical" : "opposing")} decisions");
            // A separate valid decision-to-revise state tests competing revisions.
            var revisionId = await Pending(condition);
            await using (var db = new ApplicationDbContext(options))
            {
                var row = await db.UserAnswers.FindAsync(revisionId);
                row!.RevisedAnswerText = null;
                await db.SaveChangesAsync();
            }
            var revisions = await Parallel(async first =>
            {
                await using var db = new ApplicationDbContext(options);
                return await new InterviewController(db, null!, null!, config).ReviseAnswer(revisionId, new() { RevisedAnswerText = same || first ? "Revision one" : "Revision two" });
            });
            check(revisions.Count(r => r is OkObjectResult) == (same ? 2 : 1) && revisions.Count(r => r is ConflictObjectResult) == (same ? 0 : 1),
                $"v2/{condition}: concurrent {(same ? "identical" : "conflicting")} revisions");
            await using var read = new ApplicationDbContext(options);
            check((await read.InterviewSessions.SingleAsync(s => s.Answers.Any(a => a.Id == revisionId))).FinishedAt == null,
                $"v2/{condition}: concurrent stage-seven revision does not finish session");
        }
    }

    private static async Task MigrationTest(Action<bool, string> check)
    {
        var path = Path.Combine(Path.GetTempPath(), "revision-migration-" + Guid.NewGuid() + ".db");
        try
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite($"Data Source={path};Pooling=False").Options;
            var id = Guid.NewGuid();
            await using (var db = new ApplicationDbContext(options))
            {
                await db.GetService<IMigrator>().MigrateAsync("20260826144351_AddAnswerSubmissionIdempotency");
                await db.Database.ExecuteSqlRawAsync("INSERT INTO Users (Id, Nickname, Age, Gender, AiFamiliarityLevel, CreatedAt) VALUES (1, 'Historical', 22, '', '', '2026-01-01 00:00:00')");
                await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO InterviewSessions (Id, UserId, Condition, StartedAt, FinishedAt) VALUES ({id}, 1, 'A', '2026-01-01 00:00:00', '2026-01-01 00:05:00')");
                await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO UserAnswers (SubmissionId, InterviewSessionId, StageNumber, StageName, QuestionText, AnswerText, QuestionShownAt, AnswerSubmittedAt, FeedbackText, RevisedAnswerText) VALUES ({Guid.NewGuid()}, {id}, 7, 'Historical', 'Question', 'Original', '2026-01-01 00:00:00', '2026-01-01 00:01:00', 'First feedback', 'Revision')");
                await db.Database.MigrateAsync();
            }
            await using (var db = new ApplicationDbContext(options))
            {
                var snapshot = db.GetService<IMigrationsAssembly>().ModelSnapshot!;
                var snapshotModel = db.GetService<IModelRuntimeInitializer>().Initialize(snapshot.Model, designTime: true);
                check(!db.GetService<IMigrationsModelDiffer>().HasDifferences(snapshotModel.GetRelationalModel(), db.GetService<IDesignTimeModel>().Model.GetRelationalModel()),
                    "v2 migration: EF snapshot matches the current model");
                var session = await db.InterviewSessions.SingleAsync();
                var row = await db.UserAnswers.SingleAsync();
                check(session.FlowVersion == 1 && session.FinishedAt != null && row.AnswerText == "Original" && row.RevisedAnswerText == "Revision" && row.FeedbackText == "First feedback",
                    "v2 migration: historical rows retain v1 meaning and text");
                check(row.RevisedFeedbackText == null && row.StageCompletedAt == null && row.OriginalWritingMilliseconds == null && row.FeedbackReadyAt == null,
                    "v2 migration: historical nullable data are not fabricated");
            }
        }
        finally { File.Delete(path); }
    }

    private static HttpResponseMessage Valid(string text, string reason = "STOP") => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(new { candidates = new[] { new { finishReason = reason, content = new { parts = new[] { new { text } } } } } }), Encoding.UTF8, "application/json")
    };
    private sealed class Stub(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request, token);
    }
}
