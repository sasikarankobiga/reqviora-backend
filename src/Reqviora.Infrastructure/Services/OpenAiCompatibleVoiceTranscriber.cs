using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Reqviora.Application.Features.Documents;
using Reqviora.Application.Features.VoiceProcessing;

namespace Reqviora.Infrastructure.Services;

public sealed class OpenAiCompatibleVoiceTranscriber(
    HttpClient http,
    IOptions<AiOptions> options) : IVoiceTranscriber
{
    public async Task<TranscriptDraft> TranscribeAsync(
        VoiceInputRequest request,
        Stream audioContent,
        CancellationToken cancellationToken = default)
    {
        InputStudioContractValidator.Validate(request);
        var value = options.Value;
        if (string.IsNullOrWhiteSpace(value.ApiKey))
            throw new InvalidOperationException("AI API key is not configured.");
        using var buffer = new MemoryStream();
        await audioContent.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length != request.SizeBytes)
            throw new ArgumentException("Audio size does not match its metadata.", nameof(audioContent));
        var bytes = buffer.ToArray();
        var body = await ExternalProviderProtocol.SendForTextAsync(http, () =>
        {
            var form = new MultipartFormDataContent();
            var audio = new ByteArrayContent(bytes);
            audio.Headers.ContentType = MediaTypeHeaderValue.Parse(request.MediaType);
            form.Add(audio, "file", "audio" + Extension(request.MediaType));
            form.Add(new StringContent(value.TranscriptionModel), "model");
            form.Add(new StringContent(request.LanguageCode.StartsWith("ta", StringComparison.Ordinal) ? "ta" : "en"), "language");
            form.Add(new StringContent("verbose_json"), "response_format");
            var message = new HttpRequestMessage(HttpMethod.Post, "audio/transcriptions") { Content = form };
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", value.ApiKey);
            return message;
        }, "TRANSCRIPTION", value.MaxAttempts, value.BaseRetryMilliseconds,
            value.MaxResponseCharacters, cancellationToken);
        try
        {
            using var json = JsonDocument.Parse(body, new JsonDocumentOptions
            {
                AllowDuplicateProperties = false,
                MaxDepth = 32
            });
            var text = json.RootElement.GetProperty("text").GetString()?.Trim();
            if (string.IsNullOrWhiteSpace(text))
                throw new ExternalProviderException("TRANSCRIPTION_INVALID_RESPONSE",
                    "Transcription provider returned an invalid response.");
        var segments = new List<TranscriptSegment>();
        if (json.RootElement.TryGetProperty("segments", out var items))
        {
            foreach (var item in items.EnumerateArray())
            {
                var start = item.GetProperty("start").GetDouble();
                var end = item.GetProperty("end").GetDouble();
                var segmentText = item.GetProperty("text").GetString()?.Trim();
                if (!string.IsNullOrWhiteSpace(segmentText) && end > start)
                    segments.Add(new((long)(start * 1000), (long)(end * 1000), segmentText));
            }
        }
        if (segments.Count == 0)
            segments.Add(new(0, Math.Max(1, request.DurationSeconds) * 1000L, text));
        var draft = new TranscriptDraft(
            Guid.NewGuid(), request.AudioFileId, request.LanguageCode, text, text, 1, segments);
        VoiceTranscriptionContractValidator.Validate(draft);
        return draft;
        }
        catch (ExternalProviderException) { throw; }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or
                                           KeyNotFoundException)
        {
            throw new ExternalProviderException("TRANSCRIPTION_INVALID_RESPONSE",
                "Transcription provider returned an invalid response.", exception);
        }
    }

    private static string Extension(string mediaType) => mediaType switch
    {
        "audio/wav" => ".wav",
        "audio/mpeg" => ".mp3",
        "audio/mp4" => ".m4a",
        "audio/webm" => ".webm",
        _ => throw new ArgumentException("Unsupported audio media type.")
    };
}
