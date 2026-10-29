using System.Net;
using System.Text.Json;
using AIInterviewSimulator.Api.Security;

namespace AIInterviewSimulator.Api;

// Explicit opt-in, isolated fixture mode. Never enables the research database.
public static class LocalTestMode
{
    public static bool Configure(WebApplicationBuilder builder)
    {
        if (!builder.Configuration.GetValue<bool>("LocalTesting:Enabled")) return false;
        if (!builder.Environment.IsDevelopment())
            throw new InvalidOperationException("LocalTesting is permitted only in Development.");
        if (builder.Configuration.GetSection("Kestrel:Endpoints").GetChildren().Any())
            throw new InvalidOperationException("Custom Kestrel endpoints are not allowed in LocalTesting.");
        var folder = Path.Combine(builder.Environment.ContentRootPath, ".local-test");
        Directory.CreateDirectory(folder);
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = $"Data Source={Path.Combine(folder, "LOCAL_TEST_ONLY.db")};Default Timeout=10",
            ["Study:CollectionEnabled"] = "true",
            ["Study:QuestionnairesEnabled"] = "false",
            ["Study:Consent:Approved"] = "true",
            ["Study:Consent:Version"] = "LOCAL-TEST-ONLY-v1",
            ["Study:Consent:Text"] = "ΛΕΙΤΟΥΡΓΙΑ ΤΟΠΙΚΗΣ ΔΟΚΙΜΗΣ — όχι πραγματική έρευνα. Χρησιμοποιήστε αποκλειστικά επινοημένα στοιχεία και απαντήσεις. Οι απαντήσεις και οι χρόνοι αποθηκεύονται σε ξεχωριστή δοκιμαστική βάση. Το Condition A χρησιμοποιεί τους κανονικούς κανόνες. Το Condition B επιστρέφει stub, χωρίς κλήση Gemini ή χρέωση. Η επιλογή συμμετοχής παρακάτω αφορά μόνο αυτή τη δοκιμή και δεν αποτελεί ερευνητική συναίνεση.",
            ["GeminiApiKey"] = "local-stub-only-not-a-real-key",
            ["Export:Key"] = null,
            ["AllowedHosts"] = "localhost;127.0.0.1"
        });
        builder.WebHost.UseUrls("http://127.0.0.1:15055");
        return true;
    }

    public static bool IsLoopback(HttpContext context) => context.Connection.RemoteIpAddress is { } address && IPAddress.IsLoopback(address);

    public sealed class StubHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    candidates = new[] { new { finishReason = "STOP", content = new { parts = new[] { new {
                        text = "Δοκιμαστική απόκριση Condition B χωρίς κλήση Gemini. Το κείμενο αυτό ελέγχει μόνο τη ροή εμφάνισης και αναθεώρησης, όχι την ποιότητα εξατομικευμένου feedback."
                    } } } } }
                }))
            });
    }
}
