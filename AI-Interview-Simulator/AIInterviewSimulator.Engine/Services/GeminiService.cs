using System.Net;
using System.Text;
using System.Text.Json;
using AIInterviewSimulator.Engine.Feedback;
using Microsoft.Extensions.Configuration;

namespace AIInterviewSimulator.Engine.Services;

public class GeminiService
{
    public static readonly TimeSpan TotalTimeout = TimeSpan.FromSeconds(30);
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly TimeProvider _timeProvider;

    public GeminiService(HttpClient httpClient, IConfiguration configuration, TimeProvider? timeProvider = null)
    {
        _httpClient = httpClient;
        _apiKey = configuration["GeminiApiKey"] ?? string.Empty;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<FeedbackResult> GenerateResponseAsync(string prompt, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
            return new(FeedbackStatus.Unavailable, ErrorCode: "missing_api_key");

        using var budget = new CancellationTokenSource(TotalTimeout, _timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, budget.Token);
        var token = linked.Token;
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

        var attempts = 0;
        string? currentModel = null;
        var lastFailure = new FeedbackResult(FeedbackStatus.Unavailable, ErrorCode: "api_failure");
        try
        {
            foreach (var model in candidateModels)
            {
                currentModel = model;
                var maxAttempts = model == "gemini-3.6-flash" ? 2 : 1;
                for (var attempt = 1; attempt <= maxAttempts; attempt++)
                {
                    token.ThrowIfCancellationRequested();
                    attempts++;
                    using var request = new HttpRequestMessage(HttpMethod.Post,
                        $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent");
                    request.Headers.Add("x-goog-api-key", _apiKey);
                    request.Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
                    var retryable = true;
                    try
                    {
                        using var response = await _httpClient.SendAsync(request, token);
                        if (response.IsSuccessStatusCode)
                        {
                            var json = await response.Content.ReadAsStringAsync(token);
                            var parsed = ParseResponse(json) with { Model = model, Attempts = attempts };
                            // A structurally invalid/empty response is not a transport retry.
                            return parsed;
                        }
                        lastFailure = new(FeedbackStatus.Unavailable, ErrorCode: $"http_{(int)response.StatusCode}");
                        retryable = response.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
                            or HttpStatusCode.InternalServerError or HttpStatusCode.BadGateway
                            or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;
                    }
                    catch (HttpRequestException)
                    {
                        lastFailure = new(FeedbackStatus.Unavailable, ErrorCode: "communication_failure");
                    }
                    catch (OperationCanceledException) when (!token.IsCancellationRequested)
                    {
                        // A shorter HttpClient timeout must not start another long request.
                        return new(FeedbackStatus.Unavailable, ErrorCode: "timeout", Model: model, Attempts: attempts);
                    }
                    if (!retryable) break;
                    if (attempt < maxAttempts)
                        await Task.Delay(TimeSpan.FromSeconds(2), _timeProvider, token);
                }
            }
        }
        catch (OperationCanceledException)
        {
            return new(FeedbackStatus.Unavailable,
                ErrorCode: cancellationToken.IsCancellationRequested ? "request_cancelled" : "timeout",
                Model: currentModel, Attempts: attempts);
        }
        return lastFailure with { Model = currentModel, Attempts = attempts };
    }

    private static FeedbackResult ParseResponse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var candidate = doc.RootElement.GetProperty("candidates")[0];
            if (candidate.GetProperty("finishReason").GetString() != "STOP")
                return new(FeedbackStatus.InvalidResult, ErrorCode: "incomplete_or_blocked_response");
            var parts = candidate.GetProperty("content").GetProperty("parts");
            var text = new StringBuilder();
            foreach (var part in parts.EnumerateArray())
            {
                if (part.TryGetProperty("thought", out var thought) && thought.ValueKind == JsonValueKind.True)
                    continue;
                text.Append(part.GetProperty("text").GetString());
            }
            return FeedbackResult.FromText(text.ToString());
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException or ArgumentOutOfRangeException)
        {
            return new(FeedbackStatus.InvalidResult, ErrorCode: "malformed_response");
        }
    }
}
