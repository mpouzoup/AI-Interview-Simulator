using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace AIInterviewSimulator.Engine.Services;

public class GeminiService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;

    public GeminiService(IConfiguration configuration)
    {
        _httpClient = new HttpClient();
        _apiKey = configuration["GeminiApiKey"] ?? string.Empty;
    }

    public async Task<string> GenerateResponseAsync(string prompt)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            return "[Gemini AI Error]: Δεν έχει οριστεί Gemini API Key.";
        }

        var requestBody = new
        {
            contents = new[]
            {
                new
                {
                    parts = new[]
                    {
                        new { text = prompt }
                    }
                }
            }
        };

        var jsonContent = new StringContent(
            JsonSerializer.Serialize(requestBody),
            Encoding.UTF8,
            "application/json");

        string[] candidateModels =
        {
            "gemini-2.5-flash",
            "gemini-2.0-flash"
        };

        string lastError = "";

        foreach (var model in candidateModels)
        {
            var url =
                $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={_apiKey}";

            try
            {
                var response = await _httpClient.PostAsync(url, jsonContent);
                var responseJson = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    using var doc = JsonDocument.Parse(responseJson);

                    return doc.RootElement
                               .GetProperty("candidates")[0]
                               .GetProperty("content")
                               .GetProperty("parts")[0]
                               .GetProperty("text")
                               .GetString()
                           ?? "Δεν παράχθηκε απάντηση.";
                }

                lastError =
                    $"[{model} Error - {response.StatusCode}]: {responseJson}";
            }
            catch (Exception ex)
            {
                lastError =
                    $"[{model} Exception]: {ex.Message}";
            }
        }

        return lastError;
    }
}