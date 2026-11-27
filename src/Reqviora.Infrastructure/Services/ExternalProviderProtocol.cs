using System.Net;
using System.Text;

namespace Reqviora.Infrastructure.Services;

public sealed class ExternalProviderException(string code, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public string Code { get; } = code;
}

internal static class ExternalProviderProtocol
{
    public static async Task<string> SendForTextAsync(
        HttpClient http,
        Func<HttpRequestMessage> createRequest,
        string providerName,
        int maxAttempts,
        int baseRetryMilliseconds,
        int maxResponseCharacters,
        CancellationToken cancellationToken)
    {
        maxAttempts = Math.Clamp(maxAttempts, 1, 5);
        baseRetryMilliseconds = Math.Clamp(baseRetryMilliseconds, 10, 5000);
        maxResponseCharacters = Math.Clamp(maxResponseCharacters, 1_000, 4_000_000);
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                using var request = createRequest();
                using var response = await http.SendAsync(
                    request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (IsTransient(response.StatusCode) && attempt < maxAttempts)
                {
                    await DelayAsync(attempt, baseRetryMilliseconds, cancellationToken);
                    continue;
                }
                if (!response.IsSuccessStatusCode)
                    throw new ExternalProviderException(
                        $"{providerName.ToUpperInvariant()}_HTTP_{(int)response.StatusCode}",
                        $"{providerName} provider returned HTTP {(int)response.StatusCode}.");
                return await ReadBoundedAsync(response.Content, providerName, maxResponseCharacters,
                    cancellationToken);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                if (attempt < maxAttempts)
                {
                    await DelayAsync(attempt, baseRetryMilliseconds, cancellationToken);
                    continue;
                }
                throw new ExternalProviderException($"{providerName.ToUpperInvariant()}_TIMEOUT",
                    $"{providerName} provider timed out.");
            }
            catch (HttpRequestException) when (attempt < maxAttempts)
            {
                await DelayAsync(attempt, baseRetryMilliseconds, cancellationToken);
            }
            catch (HttpRequestException exception)
            {
                throw new ExternalProviderException($"{providerName.ToUpperInvariant()}_UNAVAILABLE",
                    $"{providerName} provider is unavailable.", exception);
            }
        }
        throw new ExternalProviderException($"{providerName.ToUpperInvariant()}_RETRY_LIMIT",
            $"{providerName} provider retry limit was reached.");
    }

    private static async Task<string> ReadBoundedAsync(HttpContent content, string providerName,
        int maxCharacters, CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream, Encoding.UTF8, true, 4096, leaveOpen: false);
        var result = new StringBuilder(Math.Min(maxCharacters, 16_384));
        var buffer = new char[Math.Min(4096, maxCharacters + 1)];
        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0) break;
            if (result.Length + read > maxCharacters)
                throw new ExternalProviderException($"{providerName.ToUpperInvariant()}_RESPONSE_TOO_LARGE",
                    $"{providerName} provider response exceeded the configured limit.");
            result.Append(buffer, 0, read);
        }
        if (string.IsNullOrWhiteSpace(result.ToString()))
            throw new ExternalProviderException($"{providerName.ToUpperInvariant()}_EMPTY_RESPONSE",
                $"{providerName} provider returned an empty response.");
        return result.ToString();
    }

    private static Task DelayAsync(int attempt, int baseMilliseconds, CancellationToken cancellationToken) =>
        Task.Delay(TimeSpan.FromMilliseconds(baseMilliseconds * attempt), cancellationToken);

    private static bool IsTransient(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests ||
        (int)statusCode >= 500;
}
