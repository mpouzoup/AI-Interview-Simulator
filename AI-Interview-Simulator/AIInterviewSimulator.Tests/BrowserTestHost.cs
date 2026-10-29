using System.Net;
using AIInterviewSimulator.Api.Security;
using System.Text.Json;
using AIInterviewSimulator.Api.Controllers;
using AIInterviewSimulator.Data.Context;
using AIInterviewSimulator.Data.Entities;
using AIInterviewSimulator.Engine.Engines;
using AIInterviewSimulator.Engine.Feedback;
using AIInterviewSimulator.Engine.Managers;
using AIInterviewSimulator.Engine.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// Explicit test-runner mode only; never loads production settings or contacts Gemini.
internal static class BrowserTestHost
{
    internal static async Task Run()
    {
        var folder = Path.Combine(Path.GetTempPath(), "interview-browser-" + Guid.NewGuid());
        Directory.CreateDirectory(folder);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "BrowserTests", ContentRootPath = folder });
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["GeminiApiKey"] = "stub-only-not-a-real-key", ["Export:Key"] = new string('t', 32),
            ["Study:CollectionEnabled"] = "true", ["Study:Consent:Approved"] = "true",
            ["Study:Consent:Version"] = "synthetic-browser-v1", ["Study:Consent:Text"] = "SYNTHETIC TEST ONLY - not approved for participants." });
        builder.WebHost.UseUrls("http://127.0.0.1:18555");
        builder.Logging.ClearProviders();
        builder.Services.AddDbContext<ApplicationDbContext>(o => o.UseSqlite($"Data Source={Path.Combine(folder, "browser.db")};Default Timeout=10"));
        builder.Services.AddParticipantSecurity();
        builder.Services.AddControllers(o => o.Filters.Add<ParticipantAccessFilter>()).AddApplicationPart(typeof(InterviewController).Assembly);
        builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));
        builder.Services.AddScoped<ScriptBasedEngine>(); builder.Services.AddScoped<InterviewManager>();
        builder.Services.AddScoped<ScriptFeedbackEngine>(); builder.Services.AddScoped<LlmFeedbackEngine>(); builder.Services.AddScoped<FeedbackManager>();
        var stub = new ProviderStub();
        builder.Services.AddScoped(sp => new GeminiService(new HttpClient(stub, false), sp.GetRequiredService<IConfiguration>()));
        var app = builder.Build(); app.UseCors(); app.UseAuthentication(); app.UseAuthorization(); app.MapControllers();
        app.MapPost("/test/prepare/{condition}", async (string condition, ApplicationDbContext db) =>
        {
            if (condition is not ("A" or "B")) return Results.BadRequest();
            var counts = await db.InterviewSessions.GroupBy(s => s.Condition).Select(g => new { Key = g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count);
            counts.TryGetValue(condition, out var desired); counts.TryGetValue(condition == "A" ? "B" : "A", out var opposite);
            for (var n = opposite; n <= desired; n++) db.Add(new InterviewSession { FlowVersion = 2, Condition = condition == "A" ? "B" : "A", User = new User { Nickname = "Test allocation seed" } });
            await db.SaveChangesAsync(); return Results.Ok();
        });
        app.MapPost("/test/provider/{mode}", (string mode) => { stub.FailRevised = mode == "fail-revised"; return Results.Ok(); });
        app.MapGet("/test/status", async (ApplicationDbContext db) =>
        {
            var sessions = await db.InterviewSessions.AsNoTracking().Include(s => s.User).Include(s => s.Answers).Where(s => s.User!.Nickname.StartsWith("Browser E2E")).ToListAsync();
            return Results.Ok(new { providerRequests = stub.Requests, providerFailures = stub.Failures, sessions = sessions.Select(s => new { s.Id, s.Condition, s.FlowVersion, s.FinishedAt,
                answers = s.Answers.OrderBy(a => a.StageNumber).Select(a => new { a.Id, a.StageNumber, a.AnswerText, a.RevisedAnswerText, a.FeedbackText, a.RevisedFeedbackText, a.StageCompletedAt,
                    a.OriginalWritingMilliseconds, a.RevisionWritingMilliseconds, a.FeedbackWaitMilliseconds, a.RevisedFeedbackWaitMilliseconds, a.StageElapsedMilliseconds }) }) });
        });
        await using (var scope = app.Services.CreateAsyncScope()) await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.MigrateAsync();
        Console.WriteLine("Isolated browser host: http://127.0.0.1:18555; SQLite: " + folder);
        await app.RunAsync();
    }
    internal static async Task ExportReadOnly(string databasePath, string outputPath)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(
            new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = Path.GetFullPath(databasePath), Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly }.ToString()).Options;
        await using var db = new ApplicationDbContext(options);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Export:Key"] = new string('v', 32) }).Build();
        var controller = new InterviewController(db, null!, null!, config);
        controller.ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext { HttpContext = new DefaultHttpContext() };
        controller.Request.Headers["X-Export-Key"] = new string('v', 32);
        var result = (Microsoft.AspNetCore.Mvc.FileContentResult)await controller.ExportDataAsCsv();
        await File.WriteAllBytesAsync(outputPath, result.FileContents);
        Console.WriteLine("CSV generated by real export controller using read-only SQLite; no provider calls.");
    }

    private sealed class ProviderStub : HttpMessageHandler
    {
        public volatile bool FailRevised; public int Requests; public int Failures;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Requests);
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            var prompt = JsonDocument.Parse(body).RootElement.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString()!;
            var revised = prompt.Contains("BROWSER_REVISED");
            if (FailRevised && revised) { Interlocked.Increment(ref Failures); return new(HttpStatusCode.BadRequest); }
            var text = revised ? "Η νέα απάντηση περιγράφει την προσέγγισή σας. Μπορείτε να διευκρινίσετε το αποτέλεσμα." : "Η απάντηση δίνει ένα αρχικό πλαίσιο. Μπορείτε να προσθέσετε συγκεκριμένες ενέργειες.";
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { candidates = new[] { new { finishReason = "STOP", content = new { parts = new[] { new { text } } } } } })) };
        }
    }
}
