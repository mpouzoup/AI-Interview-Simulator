using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;

namespace AIInterviewSimulator.Client;

// Reads the encrypted HttpOnly cookie through the circuit authentication state.
// The API credential never enters JavaScript, sessionStorage, logs or CSV.
public sealed class ParticipantApiHandler(AuthenticationStateProvider authentication) : DelegatingHandler(new HttpClientHandler())
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        var state = await authentication.GetAuthenticationStateAsync();
        var credential = state.User.FindFirstValue("api_access_token");
        if (credential != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential);
        return await base.SendAsync(request, token);
    }
}

public sealed class ParticipantEnrollment
{
    public string Nickname { get; set; } = "";
    public int Age { get; set; }
    public string Gender { get; set; } = "";
    public string AiFamiliarityLevel { get; set; } = "";
    public bool ConsentAccepted { get; set; }
    public string ConsentVersion { get; set; } = "";
    public string ConsentTextHash { get; set; } = "";
}

public sealed class EnrollmentReceipt
{
    public int Id { get; set; }
    public string AccessToken { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
}

public static class ParticipantClientSecurity
{
    public static bool ConfigureLocalTestClient(WebApplicationBuilder builder)
    {
        if (!builder.Configuration.GetValue<bool>("LocalTesting:Enabled")) return false;
        if (!builder.Environment.IsDevelopment()) throw new InvalidOperationException("LocalTesting is permitted only in Development.");
        if (builder.Configuration.GetSection("Kestrel:Endpoints").GetChildren().Any()) throw new InvalidOperationException("Custom Kestrel endpoints are not allowed in LocalTesting.");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["InterviewApi:BaseUrl"] = "http://127.0.0.1:15055/",
            ["Security:DataProtectionKeysPath"] = Path.Combine(builder.Environment.ContentRootPath, ".local-test", "keys"),
            ["AllowedHosts"] = "localhost;127.0.0.1"
        });
        builder.WebHost.UseUrls("http://127.0.0.1:15265");
        return true;
    }

    public const string CookieScheme = "ParticipantCookie";
    public static IServiceCollection AddParticipantClientSecurity(this IServiceCollection services, IConfiguration config, IHostEnvironment environment)
    {
        var localTest = config.GetValue<bool>("LocalTesting:Enabled");
        if (localTest && !environment.IsDevelopment()) throw new InvalidOperationException("LocalTesting is permitted only in Development.");
        var protection = services.AddDataProtection().SetApplicationName(localTest ? "AIInterviewSimulator.LocalTest" : "AIInterviewSimulator.Participant");
        var keyPath = config["Security:DataProtectionKeysPath"];
        if (!string.IsNullOrWhiteSpace(keyPath)) protection.PersistKeysToFileSystem(new DirectoryInfo(keyPath));
        else if (environment.IsProduction()) throw new InvalidOperationException("Configure Security:DataProtectionKeysPath on protected persistent storage before Production startup.");
        services.AddAuthentication(CookieScheme).AddCookie(CookieScheme, o =>
        {
            o.Cookie.Name = localTest ? "Interview.LocalTest" : environment.IsDevelopment() || environment.IsEnvironment("BrowserTests") ? "Interview.Local" : "__Host-Interview";
            o.Cookie.HttpOnly = true; o.Cookie.SameSite = SameSiteMode.Strict; o.Cookie.Path = "/";
            o.Cookie.SecurePolicy = environment.IsDevelopment() || environment.IsEnvironment("BrowserTests") ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            o.SlidingExpiration = false; o.ExpireTimeSpan = TimeSpan.FromHours(24);
            o.Events.OnRedirectToLogin = c => { c.Response.StatusCode = 401; return Task.CompletedTask; };
            o.Events.OnRedirectToAccessDenied = c => { c.Response.StatusCode = 403; return Task.CompletedTask; };
        });
        services.AddAuthorization(); services.AddCascadingAuthenticationState();
        services.AddAntiforgery(o => { o.HeaderName = "X-Interview-CSRF"; o.Cookie.SameSite = SameSiteMode.Strict; });
        services.AddHttpClient("InterviewGateway", c => c.BaseAddress = new Uri(config["InterviewApi:BaseUrl"] ?? "http://localhost:5055/"));
        services.AddScoped(sp => new HttpClient(new ParticipantApiHandler(sp.GetRequiredService<AuthenticationStateProvider>()))
            { BaseAddress = new Uri(config["InterviewApi:BaseUrl"] ?? "http://localhost:5055/") });
        return services;
    }

    public static void MapParticipantGateway(this WebApplication app)
    {
        app.MapGet("/participant/bootstrap", async (HttpContext context, IAntiforgery csrf, IHttpClientFactory clients) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            using var response = await clients.CreateClient("InterviewGateway").GetAsync("api/Interview/study");
            if (!response.IsSuccessStatusCode) return Results.StatusCode(503);
            var study = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            return Results.Ok(new { study, signedIn = context.User.Identity?.IsAuthenticated == true, csrfToken = csrf.GetAndStoreTokens(context).RequestToken });
        });
        app.MapPost("/participant/register", async (HttpContext context, ParticipantEnrollment request, IAntiforgery csrf, IHttpClientFactory clients) =>
        {
            try { await csrf.ValidateRequestAsync(context); }
            catch (AntiforgeryValidationException) { return Results.BadRequest(new { error = "Invalid request verification." }); }
            if (context.User.Identity?.IsAuthenticated == true) return Results.Conflict(new { error = "A participant is already signed in." });
            using var response = await clients.CreateClient("InterviewGateway").PostAsJsonAsync("api/Interview/users", request);
            if (!response.IsSuccessStatusCode) return Results.Json(new { error = "Η εγγραφή δεν ολοκληρώθηκε. Ελέγξτε τη συναίνεση και τα στοιχεία σας." }, statusCode: (int)response.StatusCode);
            var enrolled = await response.Content.ReadFromJsonAsync<EnrollmentReceipt>();
            if (enrolled == null || enrolled.AccessToken.Length != 64) return Results.StatusCode(502);
            var claims = new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, enrolled.Id.ToString()), new Claim("api_access_token", enrolled.AccessToken) }, CookieScheme);
            await context.SignInAsync(CookieScheme, new ClaimsPrincipal(claims), new AuthenticationProperties
                { IsPersistent = false, AllowRefresh = false, ExpiresUtc = new DateTimeOffset(enrolled.ExpiresAt.ToUniversalTime()) });
            context.Response.Headers.CacheControl = "no-store";
            return Results.Ok(new { registered = true });
        });
        app.MapPost("/participant/logout", async (HttpContext context, IAntiforgery csrf, IHttpClientFactory clients) =>
        {
            try { await csrf.ValidateRequestAsync(context); }
            catch (AntiforgeryValidationException) { return Results.BadRequest(); }
            var client = clients.CreateClient("InterviewGateway");
            using var request = new HttpRequestMessage(HttpMethod.Post, "api/Interview/participant/revoke");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", context.User.FindFirstValue("api_access_token"));
            using var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.Unauthorized) return Results.StatusCode(503);
            await context.SignOutAsync(CookieScheme);
            context.Response.Headers.CacheControl = "no-store";
            return Results.Ok();
        }).RequireAuthorization();
    }
}
