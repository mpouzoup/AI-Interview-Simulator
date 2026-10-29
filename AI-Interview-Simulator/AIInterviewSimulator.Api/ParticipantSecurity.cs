using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using AIInterviewSimulator.Api.DTOs.Requests;
using AIInterviewSimulator.Data.Context;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AIInterviewSimulator.Api.Security;

public sealed class StudyPolicy(IConfiguration configuration)
{
    public bool Enabled => configuration.GetValue<bool>("Study:CollectionEnabled") && configuration.GetValue<bool>("Study:Consent:Approved") &&
        !string.IsNullOrWhiteSpace(Version) && !string.IsNullOrWhiteSpace(Text);
    public bool IsLocalTest => configuration.GetValue<bool>("LocalTesting:Enabled");
    public bool QuestionnairesEnabled => configuration.GetValue<bool>("Study:QuestionnairesEnabled");
    public string Version => configuration["Study:Consent:Version"] ?? "";
    public string Text => configuration["Study:Consent:Text"] ?? "";
    public string TextHash => Hash(Text);
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static (string Token, string Hash) NewCredential()
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        return (token, Hash(token));
    }
}

public sealed class ParticipantAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger, UrlEncoder encoder, ApplicationDbContext db)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Participant";
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(header)) return AuthenticateResult.NoResult();
        if (!header.StartsWith("Bearer ", StringComparison.Ordinal) || header.Length != 71)
            return AuthenticateResult.Fail("Invalid credential.");
        var token = header[7..];
        if (!token.All(Uri.IsHexDigit)) return AuthenticateResult.Fail("Invalid credential.");
        var hash = StudyPolicy.Hash(token);
        var credential = await db.ParticipantCredentials.AsNoTracking().SingleOrDefaultAsync(c => c.TokenHash == hash);
        if (credential == null || credential.RevokedAt != null || credential.ExpiresAt <= DateTime.UtcNow)
            return AuthenticateResult.Fail("Expired or revoked credential.");
        var identity = new ClaimsIdentity(new[] {
            new Claim(ClaimTypes.NameIdentifier, credential.UserId.ToString(CultureInfo.InvariantCulture)),
            new Claim("credential_id", credential.Id.ToString()) }, SchemeName);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }
}

// Global and fail-closed. The export endpoints retain a separate administrator key.
public sealed class ParticipantAccessFilter(ApplicationDbContext db) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        context.HttpContext.Response.Headers.CacheControl = "no-store";
        var action = context.ActionDescriptor.RouteValues["action"];
        if (action is "CreateUser" or "StudyInformation" or "ExportDataAsCsv" or "ExportQuestionnaires") { await next(); return; }
        if (context.HttpContext.User.Identity?.IsAuthenticated != true ||
            !int.TryParse(context.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier), out var owner))
        { context.Result = new UnauthorizedResult(); return; }
        int? requiredOwner = null;
        if (action is "ParticipantProfile" or "RevokeParticipant") requiredOwner = owner;
        else if (context.ActionArguments.TryGetValue("answerId", out var answer))
            requiredOwner = await db.UserAnswers.Where(a => a.Id == (int)answer!).Select(a => (int?)a.InterviewSession!.UserId).SingleOrDefaultAsync();
        else if (context.ActionArguments.TryGetValue("sessionId", out var session))
            requiredOwner = await db.InterviewSessions.Where(s => s.Id == (Guid)session!).Select(s => (int?)s.UserId).SingleOrDefaultAsync();
        else if (context.ActionArguments.TryGetValue("administrationId", out var administration))
            requiredOwner = await db.QuestionnaireAdministrations.Where(a => a.Id == (Guid)administration!).Select(a => (int?)a.UserId).SingleOrDefaultAsync();
        else if (context.ActionArguments.Values.OfType<SubmitAnswerRequest>().FirstOrDefault() is { } submission)
            requiredOwner = await db.InterviewSessions.Where(s => s.Id == submission.SessionId).Select(s => (int?)s.UserId).SingleOrDefaultAsync();
        else if (context.ActionArguments.Values.OfType<StartSessionRequest>().FirstOrDefault() is { } start)
            requiredOwner = start.UserId;
        if (requiredOwner != owner) { context.Result = new NotFoundResult(); return; }
        await next();
    }
}

public static class ParticipantSecurityRegistration
{
    public static IServiceCollection AddParticipantSecurity(this IServiceCollection services)
    {
        services.AddAuthentication(ParticipantAuthentication.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, ParticipantAuthentication>(ParticipantAuthentication.SchemeName, _ => { });
        services.AddAuthorization();
        services.AddScoped<ParticipantAccessFilter>();
        return services;
    }
}
