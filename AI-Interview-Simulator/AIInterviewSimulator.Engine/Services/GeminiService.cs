using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace AIInterviewSimulator.Engine.Services;

public class GeminiService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;

    public GeminiService(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _apiKey = configuration["GeminiApiKey"] ?? string.Empty;
    }

    public async Task<string> GenerateResponseAsync(string prompt)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            Console.WriteLine("Gemini API Key is missing.");

            return "Η ανατροφοδότηση δεν ήταν διαθέσιμη αυτή τη στιγμή.";
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
            },
            generationConfig = new
            {
                temperature = 0.4,
                maxOutputTokens = 2048
            }
        };

        string[] candidateModels =
        {
            "gemini-3.6-flash",
            "gemini-3.5-flash",
            "gemini-3.7-flash",
            "gemini-flash-latest"
        };

        foreach (var model in candidateModels)
        {
            int attemptsForThisModel =
                model == "gemini-3.6-flash"
                    ? 2
                    : 1;

            for (int attempt = 1; attempt <= attemptsForThisModel; attempt++)
            {
                var url =
                    $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent";

                using var request =
                    new HttpRequestMessage(HttpMethod.Post, url);

                request.Headers.Add("x-goog-api-key", _apiKey);

                request.Content = new StringContent(
                    JsonSerializer.Serialize(requestBody),
                    Encoding.UTF8,
                    "application/json");

                try
                {
                    using var response =
                        await _httpClient.SendAsync(request);

                    var responseJson =
                        await response.Content.ReadAsStringAsync();

                    if (response.IsSuccessStatusCode)
                    {
                        using var doc =
                            JsonDocument.Parse(responseJson);

                        var generatedText =
                            doc.RootElement
                               .GetProperty("candidates")[0]
                               .GetProperty("content")
                               .GetProperty("parts")[0]
                               .GetProperty("text")
                               .GetString();

                        Console.WriteLine(
                            $"Gemini response generated successfully using {model}.");

                        return generatedText
                               ?? "Δεν παράχθηκε απάντηση.";
                    }

                    Console.WriteLine(
                        $"Gemini API error ({model}) " +
                        $"Attempt {attempt}/{attemptsForThisModel}: " +
                        $"{(int)response.StatusCode} {response.StatusCode}\n" +
                        responseJson);

                    bool shouldRetry =
                        response.StatusCode == HttpStatusCode.RequestTimeout ||
                        response.StatusCode == HttpStatusCode.TooManyRequests ||
                        response.StatusCode == HttpStatusCode.InternalServerError ||
                        response.StatusCode == HttpStatusCode.BadGateway ||
                        response.StatusCode == HttpStatusCode.ServiceUnavailable ||
                        response.StatusCode == HttpStatusCode.GatewayTimeout;

                    if (!shouldRetry)
                    {
                        break;
                    }

                    if (attempt < attemptsForThisModel)
                    {
                        int delaySeconds =
                            (int)Math.Pow(2, attempt);

                        Console.WriteLine(
                            $"Retrying {model} request in {delaySeconds} second(s)...");

                        await Task.Delay(
                            TimeSpan.FromSeconds(delaySeconds));
                    }
                }
                catch (HttpRequestException ex)
                {
                    Console.WriteLine(
                        $"Gemini communication error ({model}) " +
                        $"Attempt {attempt}/{attemptsForThisModel}: " +
                        $"{ex.Message}");

                    if (attempt < attemptsForThisModel)
                    {
                        int delaySeconds =
                            (int)Math.Pow(2, attempt);

                        Console.WriteLine(
                            $"Retrying {model} request in {delaySeconds} second(s)...");

                        await Task.Delay(
                            TimeSpan.FromSeconds(delaySeconds));
                    }
                }
                catch (TaskCanceledException ex)
                {
                    Console.WriteLine(
                        $"Gemini timeout ({model}) " +
                        $"Attempt {attempt}/{attemptsForThisModel}: " +
                        $"{ex.Message}");

                    if (attempt < attemptsForThisModel)
                    {
                        int delaySeconds =
                            (int)Math.Pow(2, attempt);

                        Console.WriteLine(
                            $"Retrying {model} request in {delaySeconds} second(s)...");

                        await Task.Delay(
                            TimeSpan.FromSeconds(delaySeconds));
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(
                        $"Unexpected Gemini error ({model}): {ex.Message}");

                    break;
                }
            }

            Console.WriteLine(
                $"Switching from {model} to the next candidate model.");
        }

        Console.WriteLine(
            "All Gemini candidate models failed.");

        return "Η ανατροφοδότηση δεν ήταν διαθέσιμη αυτή τη στιγμή.";
    }
}