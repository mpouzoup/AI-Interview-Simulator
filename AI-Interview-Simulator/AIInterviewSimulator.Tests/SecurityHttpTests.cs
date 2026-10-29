using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AIInterviewSimulator.Api.Controllers;
using AIInterviewSimulator.Api.Security;
using AIInterviewSimulator.Client;
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

internal static class SecurityHttpTests
{
    internal static async Task Run(Action<bool, string> check)
    {
        var folder = Path.Combine(Path.GetTempPath(), "interview-security-" + Guid.NewGuid()); Directory.CreateDirectory(folder);
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite($"Data Source={Path.Combine(folder, "security.db")};Pooling=False;Default Timeout=10").Options;
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "SecurityTests", ContentRootPath = folder });
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Study:CollectionEnabled"] = "false", ["Study:Consent:Approved"] = "false",
            ["Study:Consent:Version"] = "fixture-v1", ["Study:Consent:Text"] = "SYNTHETIC TEST ONLY", ["Study:QuestionnairesEnabled"] = "true",
            ["GeminiApiKey"] = "synthetic-key", ["Export:Key"] = new string('e',32) });
        builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddDbContext<ApplicationDbContext>(o => o.UseSqlite($"Data Source={Path.Combine(folder, "security.db")};Pooling=False;Default Timeout=10"));
        builder.Services.AddParticipantSecurity();
        builder.Services.AddControllers(o => o.Filters.Add<ParticipantAccessFilter>()).AddApplicationPart(typeof(InterviewController).Assembly);
        builder.Services.AddScoped<ScriptBasedEngine>(); builder.Services.AddScoped<InterviewManager>(); builder.Services.AddScoped<ScriptFeedbackEngine>();
        builder.Services.AddScoped<LlmFeedbackEngine>(); builder.Services.AddScoped<FeedbackManager>();
        var providerCalls = 0;
        using var provider = new HttpClient(new Stub((_, _) => { Interlocked.Increment(ref providerCalls); return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = JsonContent.Create(new { candidates = new[] { new { finishReason = "STOP", content = new { parts = new[] { new { text = "Synthetic feedback" } } } } } }) }); }));
        builder.Services.AddScoped(sp => new GeminiService(provider, sp.GetRequiredService<IConfiguration>()));
        await using var app = builder.Build(); app.UseAuthentication(); app.UseAuthorization(); app.MapControllers();
        await using (var db = new ApplicationDbContext(dbOptions)) await db.Database.MigrateAsync();
        await app.StartAsync();
        var origin = app.Urls.Single();
        using var anonymous = new HttpClient { BaseAddress = new Uri(origin) };
        const string prefix = "api/Interview/";
        object Enrollment(bool accepted = true, string version = "fixture-v1") => new { nickname = "Synthetic", age = 22, gender = "Other", aiFamiliarityLevel = "Low",
            consentAccepted = accepted, consentVersion = version, consentTextHash = StudyPolicy.Hash("SYNTHETIC TEST ONLY") };
        async Task<JsonElement> Json(HttpResponseMessage r) { r.EnsureSuccessStatusCode(); return await r.Content.ReadFromJsonAsync<JsonElement>(); }
        async Task<(HttpClient Client, string Token, int Id)> Enroll()
        {
            var receipt = await Json(await anonymous.PostAsJsonAsync(prefix+"users", Enrollment()));
            var credential = receipt.GetProperty("accessToken").GetString()!;
            var client = new HttpClient { BaseAddress = new Uri(origin) }; client.DefaultRequestHeaders.Authorization = new("Bearer", credential);
            return (client, credential, receipt.GetProperty("id").GetInt32());
        }
        check((await anonymous.PostAsJsonAsync(prefix+"users", Enrollment())).StatusCode == HttpStatusCode.ServiceUnavailable, "security: collection disabled by default");
        app.Configuration["Study:CollectionEnabled"] = "true";
        check((await anonymous.PostAsJsonAsync(prefix+"users", Enrollment())).StatusCode == HttpStatusCode.ServiceUnavailable, "security: enabling collection does not approve draft consent");
        app.Configuration["Study:Consent:Approved"] = "true";
        check((await anonymous.PostAsJsonAsync(prefix+"users", Enrollment(false))).StatusCode == HttpStatusCode.BadRequest, "security: explicit consent required");
        check((await anonymous.PostAsJsonAsync(prefix+"users", Enrollment(true,"wrong-version"))).StatusCode == HttpStatusCode.BadRequest, "security: consent version mismatch rejected");
        await using (var db = new ApplicationDbContext(dbOptions))
        {
            check(await db.Users.CountAsync()==0 && await db.ConsentRecords.CountAsync()==0, "security: no participant or consent rows before accepted enrollment");
            foreach (var phase in new[] { "pre", "post" }) db.QuestionnaireDefinitions.Add(new QuestionnaireDefinition
            { Code = "synthetic-"+phase, Version = "1", Phase = phase, Approved = true, Required = true, Title = "Synthetic fixture only",
                ItemsJson = JsonSerializer.Serialize(new[] { new QuestionnaireItemSpec("fixture", "Synthetic numeric item", 0, 4, 1) }) });
            db.QuestionnaireDefinitions.Add(new QuestionnaireDefinition { Code="unapproved",Version="1",Phase="pre",Approved=false,ItemsJson="[]" });
            await db.SaveChangesAsync();
        }
        var a = await Enroll(); var b = await Enroll();
        using var clientA=a.Client; using var clientB=b.Client;
        check(a.Token.Length==64 && b.Token.Length==64 && a.Token!=b.Token, "security: independent 256-bit random credentials");
        await using (var db = new ApplicationDbContext(dbOptions))
        {
            check(await db.ParticipantCredentials.AllAsync(c=>c.TokenHash!=a.Token && c.TokenHash!=b.Token && c.ExpiresAt>c.IssuedAt), "security: only credential hashes and fixed expiry stored");
            check(await db.ConsentRecords.CountAsync()==2 && await db.ConsentRecords.AllAsync(c=>c.Version=="fixture-v1" && c.AcceptedAt!=default), "security: versioned consent timestamp stored before sessions");
        }
        check((await anonymous.GetAsync(prefix+"participant")).StatusCode==HttpStatusCode.Unauthorized,"security: anonymous profile denied");
        check((await clientA.PostAsJsonAsync(prefix+"sessions",new { userId=b.Id })).StatusCode==HttpStatusCode.NotFound,"security: participant cannot create another participant session");
        var sessions = await Task.WhenAll(clientA.PostAsJsonAsync(prefix+"sessions",new { userId=a.Id }),clientA.PostAsJsonAsync(prefix+"sessions",new { userId=a.Id }));
        var sessionA=(await Json(sessions[0])).GetProperty("id").GetGuid();
        check((await Json(sessions[1])).GetProperty("id").GetGuid()==sessionA,"security: concurrent session creation is idempotent");
        var sessionB=(await Json(await clientB.PostAsJsonAsync(prefix+"sessions",new { userId=b.Id }))).GetProperty("id").GetGuid();
        check((await anonymous.GetAsync(prefix+$"sessions/{sessionA}/next-question")).StatusCode==HttpStatusCode.Unauthorized,"security: session ID alone grants no access");
        check((await clientB.GetAsync(prefix+$"sessions/{sessionA}/next-question")).StatusCode==HttpStatusCode.NotFound,"security: foreign session read denied");
        check((await clientA.GetAsync(prefix+$"sessions/{sessionA}/next-question")).Content.Headers.ContentType != null,"security: owner can retrieve state");
        var preA=await Json(await clientA.GetAsync(prefix+$"sessions/{sessionA}/questionnaires/pre"));
        var preId=preA[0].GetProperty("id").GetGuid();
        check(preA.GetArrayLength()==1,"questionnaires: unapproved definition not assigned");
        var preState=await Json(await clientA.GetAsync(prefix+$"sessions/{sessionA}/next-question"));
        check(preState.GetProperty("flowState").GetString()=="PreQuestionnaires","questionnaires: required pre administration gates interview");
        check((await clientB.PutAsJsonAsync(prefix+$"questionnaires/{preId}",new { submissionId=Guid.NewGuid(), answers=new[]{new{itemCode="fixture",value=2}} })).StatusCode==HttpStatusCode.NotFound,"security: foreign questionnaire write denied");
        check((await clientA.PutAsJsonAsync(prefix+$"questionnaires/{preId}",new { submissionId=Guid.NewGuid(), answers=new[]{new{itemCode="fixture",value=99}} })).StatusCode==HttpStatusCode.BadRequest,"questionnaires: invalid scale rejected");
        var protectedAnswerId = 0;
        foreach (var participant in new[]{(Client:clientA,Session:sessionA),(Client:clientB,Session:sessionB)})
        {
            var pre=await Json(await participant.Client.GetAsync(prefix+$"sessions/{participant.Session}/questionnaires/pre"));
            var administration=pre[0].GetProperty("id").GetGuid();
            var batch=new { submissionId=Guid.NewGuid(), answers=new[]{new{itemCode="fixture",value=2}} };
            check((await participant.Client.PutAsJsonAsync(prefix+$"questionnaires/{administration}",batch)).IsSuccessStatusCode,"questionnaires: owner pre submission accepted");
            check((await participant.Client.PutAsJsonAsync(prefix+$"questionnaires/{administration}",batch)).IsSuccessStatusCode,"questionnaires: identical replay accepted");
            check((await participant.Client.PutAsJsonAsync(prefix+$"questionnaires/{administration}",new {submissionId=Guid.NewGuid(),answers=batch.answers})).StatusCode==HttpStatusCode.Conflict,"questionnaires: altered replay rejected");
            for(var stage=1;stage<=7;stage++)
            {
                var next=await Json(await participant.Client.GetAsync(prefix+$"sessions/{participant.Session}/next-question"));
                check(next.GetProperty("stageNumber").GetInt32()==stage,"security regression: authorized next question");
                var text=await new ScriptBasedEngine().GetNextQuestionAsync(stage,new());
                var submission=new {submissionId=Guid.NewGuid(),sessionId=participant.Session,stageNumber=stage,stageName="Synthetic",questionText=text,
                    answerText="Αντιμετώπισα πρόβλημα. Έλεγξα δεδομένα.",questionShownAt=DateTime.UtcNow.AddSeconds(-5),originalAnswerSubmittedAt=DateTime.UtcNow};
                var saved=await Json(await participant.Client.PostAsJsonAsync(prefix+"answers",submission)); var id=saved.GetProperty("answerId").GetInt32();
                protectedAnswerId=id;
                await Json(await participant.Client.PostAsync(prefix+$"answers/{id}/feedback/1",null));
                check((await participant.Client.PutAsJsonAsync(prefix+$"answers/{id}/feedback/1/shown",new{})).IsSuccessStatusCode,"security regression: acknowledge own feedback");
                var revise=stage%2==1;
                check((await participant.Client.PutAsJsonAsync(prefix+$"answers/{id}/decision",new{choseToRevise=revise})).IsSuccessStatusCode,"security regression: own decision");
                if(revise)
                {
                    check((await participant.Client.PutAsJsonAsync(prefix+$"answers/{id}/revision",new{revisedAnswerText="Αντιμετώπισα πρόβλημα. Έλεγξα δεδομένα. Παραδώσαμε εγκαίρως."})).IsSuccessStatusCode,"security regression: own single revision");
                    await Json(await participant.Client.PostAsync(prefix+$"answers/{id}/feedback/2",null));
                    await participant.Client.PutAsJsonAsync(prefix+$"answers/{id}/feedback/2/shown",new{});
                    check((await participant.Client.PutAsJsonAsync(prefix+$"answers/{id}/continue",new{})).IsSuccessStatusCode,"security regression: final continuation");
                }
            }
            var state=await Json(await participant.Client.GetAsync(prefix+$"sessions/{participant.Session}/next-question"));
            check(state.GetProperty("flowState").GetString()=="PostQuestionnaires","questionnaires: post phase separate from interview completion");
            var post=await Json(await participant.Client.GetAsync(prefix+$"sessions/{participant.Session}/questionnaires/post"));
            var postId=post[0].GetProperty("id").GetGuid();
            check((await participant.Client.PutAsJsonAsync(prefix+$"questionnaires/{postId}",new {submissionId=Guid.NewGuid(),answers=new[]{new{itemCode="fixture",value=3}}})).IsSuccessStatusCode,"questionnaires: post response saved");
            var finished=await Json(await participant.Client.GetAsync(prefix+$"sessions/{participant.Session}/next-question"));
            check(finished.GetProperty("isFinished").GetBoolean(),"security regression: interview and assigned surveys finish");
        }
        // The last answer belongs to B. Every answer endpoint must deny A, including cached feedback.
        foreach(var path in new[]{"feedback/1","feedback/2"})
            check((await clientA.PostAsync(prefix+$"answers/{protectedAnswerId}/{path}",null)).StatusCode==HttpStatusCode.NotFound,"security: foreign cached feedback never disclosed");
        foreach(var path in new[]{"revision","decision","continue","feedback/1/shown","feedback/2/shown","revision-editor/shown"})
            check((await clientA.PutAsJsonAsync(prefix+$"answers/{protectedAnswerId}/{path}",new{revisedAnswerText="forbidden",choseToRevise=true})).StatusCode==HttpStatusCode.NotFound,"security: foreign action denied on "+path);
        check((await clientA.GetAsync(prefix+"export/csv")).StatusCode==HttpStatusCode.Unauthorized,"security: participant token cannot export all data");
        using var exportRequest=new HttpRequestMessage(HttpMethod.Get,prefix+"export/csv");exportRequest.Headers.Add("X-Export-Key",new string('e',32));
        var csv=await(await anonymous.SendAsync(exportRequest)).Content.ReadAsStringAsync();
        check(csv.Contains("ConsentVersion") && !csv.Contains(a.Token) && !csv.Contains(b.Token) && !csv.Contains("TokenHash") && !csv.Contains("AccessToken"),"security: interview export contains consent metadata, never credentials");
        using var surveyRequest=new HttpRequestMessage(HttpMethod.Get,prefix+"export/questionnaires/csv");surveyRequest.Headers.Add("X-Export-Key",new string('e',32));
        var surveyCsv=await(await anonymous.SendAsync(surveyRequest)).Content.ReadAsStringAsync();
        check(surveyCsv.Contains("synthetic-pre") && surveyCsv.Contains("synthetic-post") && !surveyCsv.Contains(a.Token),"questionnaires: separate phase/condition raw export without credentials");
        var profile=await Json(await clientA.GetAsync(prefix+"participant"));
        check(profile.GetProperty("sessionId").GetGuid()==sessionA && !profile.TryGetProperty("accessToken",out _),"security: refresh profile authoritative and credential-free");
        await clientA.PostAsync(prefix+"participant/revoke",null);
        check((await clientA.GetAsync(prefix+$"sessions/{sessionA}/next-question")).StatusCode==HttpStatusCode.Unauthorized,"security: revoked token immediately rejected");
        await using(var db=new ApplicationDbContext(dbOptions)) {var cred=await db.ParticipantCredentials.SingleAsync(c=>c.UserId==b.Id);cred.ExpiresAt=DateTime.UtcNow.AddSeconds(-1);await db.SaveChangesAsync();}
        check((await clientB.GetAsync(prefix+$"sessions/{sessionB}/next-question")).StatusCode==HttpStatusCode.Unauthorized,"security: expired token rejected");
        await using(var db=new ApplicationDbContext(dbOptions))
        {
            check(await db.QuestionnaireResponses.CountAsync()==4,"questionnaires: four raw responses, no retry duplicates");
            check(await db.InterviewSessions.AllAsync(s=>s.StudyCompletedAt!=null),"questionnaires: study completion distinct and stored");
            check(await db.InterviewSessions.Select(s=>s.Condition).Distinct().CountAsync()==2,"security regression: both original conditions exercised without allocation changes");
        }
        await GatewayTests(origin,folder,check);
        check(providerCalls>0,"security regression: B uses only local provider stub");
        await app.StopAsync();
    }

    private static async Task GatewayTests(string api,string folder,Action<bool,string> check)
    {
        var builder=WebApplication.CreateBuilder(new WebApplicationOptions{EnvironmentName="BrowserTests",ContentRootPath=folder});builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string,string?>{["InterviewApi:BaseUrl"]=api+"/",["Security:DataProtectionKeysPath"]=Path.Combine(folder,"cookie-keys")});
        builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddRazorComponents().AddInteractiveServerComponents();builder.Services.AddParticipantClientSecurity(builder.Configuration,builder.Environment);
        await using var gateway=builder.Build();gateway.UseAuthentication();gateway.UseAuthorization();gateway.UseAntiforgery();gateway.MapParticipantGateway();
        await gateway.StartAsync();
        using var client=new HttpClient(new HttpClientHandler{CookieContainer=new CookieContainer()}){BaseAddress=new Uri(gateway.Urls.Single())};
        var info=await(await client.GetAsync("participant/bootstrap")).Content.ReadFromJsonAsync<JsonElement>();
        var body=new{nickname="Gateway fixture",age=22,gender="Other",aiFamiliarityLevel="Low",consentAccepted=true,consentVersion="fixture-v1",consentTextHash=StudyPolicy.Hash("SYNTHETIC TEST ONLY")};
        check((await client.PostAsJsonAsync("participant/register",body)).StatusCode==HttpStatusCode.BadRequest,"gateway: registration requires anti-CSRF token");
        using var request=new HttpRequestMessage(HttpMethod.Post,"participant/register"){Content=JsonContent.Create(body)};request.Headers.Add("X-Interview-CSRF",info.GetProperty("csrfToken").GetString());
        var result=await client.SendAsync(request);var json=await result.Content.ReadAsStringAsync();
        check(result.IsSuccessStatusCode && !json.Contains("accessToken",StringComparison.OrdinalIgnoreCase),"gateway: browser enrollment response contains no participant credential");
        var cookies=result.Headers.GetValues("Set-Cookie").ToArray();
        check(cookies.Any(c=>c.Contains("httponly",StringComparison.OrdinalIgnoreCase)&&c.Contains("samesite=strict",StringComparison.OrdinalIgnoreCase)),"gateway: credential kept in HttpOnly SameSite cookie");
        var refreshed=await(await client.GetAsync("participant/bootstrap")).Content.ReadFromJsonAsync<JsonElement>();
        using var logout=new HttpRequestMessage(HttpMethod.Post,"participant/logout");logout.Headers.Add("X-Interview-CSRF",refreshed.GetProperty("csrfToken").GetString());
        check((await client.SendAsync(logout)).IsSuccessStatusCode,"gateway: authenticated cookie revocation succeeds");
        check((await client.PostAsync("participant/logout",null)).StatusCode==HttpStatusCode.Unauthorized,"gateway: cleared cookie no longer authorizes access");
        await gateway.StopAsync();
    }
    private sealed class Stub(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> send):HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>send(request,token);}
}
