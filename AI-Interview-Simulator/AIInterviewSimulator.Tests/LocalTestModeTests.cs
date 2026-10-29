using System.Net;
using AIInterviewSimulator.Api;
using AIInterviewSimulator.Client;
using AIInterviewSimulator.Engine.Services;
using AIInterviewSimulator.Engine.Feedback;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

internal static class LocalTestModeTests
{
    internal static async Task Run(Action<bool, string> check)
    {
        var folder = Path.Combine(Path.GetTempPath(), "local-mode-" + Guid.NewGuid());
        Directory.CreateDirectory(folder);
        WebApplicationBuilder Builder(string environment, bool enabled)
        {
            var b = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment, ContentRootPath = folder });
            b.Configuration.Sources.Clear();
            b.Configuration.AddInMemoryCollection(new Dictionary<string, string?> {
                ["LocalTesting:Enabled"] = enabled.ToString(), ["Study:CollectionEnabled"] = "false",
                ["Study:Consent:Approved"] = "false", ["ConnectionStrings:DefaultConnection"] = "Data Source=untouched.db" });
            return b;
        }
        foreach (var environment in new[] { "Production", "Staging" })
        {
            var rejected = false;
            try { LocalTestMode.Configure(Builder(environment, true)); } catch (InvalidOperationException) { rejected = true; }
            check(rejected, "local mode: API rejects " + environment);
            rejected = false;
            try { ParticipantClientSecurity.ConfigureLocalTestClient(Builder(environment, true)); } catch (InvalidOperationException) { rejected = true; }
            check(rejected, "local mode: client rejects " + environment);
        }
        var normal = Builder("Development", false);
        check(!LocalTestMode.Configure(normal) && normal.Configuration["Study:CollectionEnabled"] == "false" &&
            normal.Configuration.GetConnectionString("DefaultConnection") == "Data Source=untouched.db", "local mode: ordinary development never enables collection or changes DB");
        var api = Builder("Development", true); LocalTestMode.Configure(api);
        check(api.Configuration.GetConnectionString("DefaultConnection")!.Contains(".local-test") &&
            api.Configuration.GetConnectionString("DefaultConnection")!.Contains("LOCAL_TEST_ONLY.db"), "local mode: forced isolated database path");
        check(api.WebHost.GetSetting("urls") == "http://127.0.0.1:15055", "local mode: API forced loopback binding");
        check(api.Configuration["Study:Consent:Version"] == "LOCAL-TEST-ONLY-v1" &&
            api.Configuration["Study:Consent:Text"]!.Contains("όχι πραγματική έρευνα"), "local mode: consent fixture explicitly marked");
        var custom = Builder("Development", true); custom.Configuration["Kestrel:Endpoints:Public:Url"] = "http://0.0.0.0:8000";
        var invalid = false; try { LocalTestMode.Configure(custom); } catch (InvalidOperationException) { invalid = true; }
        check(invalid, "local mode: custom public Kestrel listener rejected");
        var context = new DefaultHttpContext();context.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.1");
        check(!LocalTestMode.IsLoopback(context), "local mode: remote address rejected");
        context.Connection.RemoteIpAddress = IPAddress.Loopback;
        check(LocalTestMode.IsLoopback(context), "local mode: local address allowed");
        using var http = new HttpClient(new LocalTestMode.StubHandler());
        var result = await new GeminiService(http, api.Configuration).GenerateResponseAsync("Synthetic test");
        check(result.Status == FeedbackStatus.Success && result.Text!.Contains("χωρίς κλήση Gemini"), "local mode: Gemini pipeline uses deterministic in-process stub");
        var client = Builder("Development", true); ParticipantClientSecurity.ConfigureLocalTestClient(client);
        check(client.WebHost.GetSetting("urls") == "http://127.0.0.1:15265" && client.Configuration["InterviewApi:BaseUrl"] == "http://127.0.0.1:15055/", "local mode: matching isolated client/API endpoints");
        client.Services.AddParticipantClientSecurity(client.Configuration, client.Environment);
        await using var app = client.Build();
        var cookie = app.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(ParticipantClientSecurity.CookieScheme);
        check(cookie.Cookie.Name == "Interview.LocalTest" && cookie.Cookie.HttpOnly && cookie.Cookie.SameSite == SameSiteMode.Strict,
            "local mode: separate protected cookie, no authentication bypass");
    }
}
