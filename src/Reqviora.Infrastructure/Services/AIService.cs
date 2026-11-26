using Reqviora.Application.Common.Interfaces;

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Reqviora.Infrastructure.Services;

public sealed class AIService(HttpClient http, IOptions<AiOptions> options) : IAIService
{
    public async Task<string> CompleteAsync(string prompt, CancellationToken cancellationToken = default)
    {
        var value = options.Value;
        if (string.IsNullOrWhiteSpace(value.ApiKey))
            throw new InvalidOperationException("AI API key is not configured.");

        var body = await ExternalProviderProtocol.SendForTextAsync(http, () =>
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
            {
                Content = JsonContent.Create(new
                {
                    model = value.Model,
                    messages = new[] { new { role = "user", content = prompt } }
                })
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", value.ApiKey);
            return request;
        }, "AI", value.MaxAttempts, value.BaseRetryMilliseconds, value.MaxResponseCharacters,
            cancellationToken);
        try
        {
            using var json = JsonDocument.Parse(body, new JsonDocumentOptions
            {
                AllowDuplicateProperties = false,
                MaxDepth = 32
            });
            var content = json.RootElement.GetProperty("choices")[0].GetProperty("message")
                .GetProperty("content").GetString()?.Trim();
            return !string.IsNullOrWhiteSpace(content)
                ? content
                : throw new ExternalProviderException("AI_INVALID_RESPONSE",
                    "AI provider returned an invalid response.");
        }
        catch (ExternalProviderException) { throw; }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or
                                           KeyNotFoundException or IndexOutOfRangeException)
        {
            throw new ExternalProviderException("AI_INVALID_RESPONSE",
                "AI provider returned an invalid response.", exception);
        }
    }
}
