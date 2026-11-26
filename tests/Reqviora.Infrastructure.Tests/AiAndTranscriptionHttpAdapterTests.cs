using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using Reqviora.Application.Features.Documents;
using Reqviora.Infrastructure.Services;

namespace Reqviora.Infrastructure.Tests;

public sealed class AiAndTranscriptionHttpAdapterTests
{
    [Fact]
    public async Task Completion_constructs_authenticated_model_request_and_reads_structured_content()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK,
            """{"choices":[{"message":{"content":"{\"result\":true}"}}]}"""));

        var output = await Ai(handler).CompleteAsync("Generate requirements");

        Assert.Equal("{\"result\":true}", output);
        Assert.Equal("https://provider.example.test/v1/chat/completions", handler.Uris.Single());
        Assert.Equal("Bearer protocol-test-key", handler.Authorizations.Single());
        Assert.Contains("\"model\":\"approved-model\"", handler.Bodies.Single(), StringComparison.Ordinal);
        Assert.Contains("Generate requirements", handler.Bodies.Single(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Completion_retries_transient_statuses(HttpStatusCode transient)
    {
        var handler = new RecordingHandler(
            _ => Json(transient, "provider-secret-body"),
            _ => Json(HttpStatusCode.OK, """{"choices":[{"message":{"content":"recovered"}}]}"""));

        Assert.Equal("recovered", await Ai(handler, maxAttempts: 2).CompleteAsync("retry"));
        Assert.Equal(2, handler.Calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Completion_does_not_retry_auth_failures_and_redacts_credentials_and_body(HttpStatusCode status)
    {
        var handler = new RecordingHandler(_ => Json(status, "provider-secret-body"));

        var exception = await Assert.ThrowsAsync<ExternalProviderException>(
            () => Ai(handler).CompleteAsync("private prompt"));

        Assert.Equal(1, handler.Calls);
        Assert.Contains(((int)status).ToString(), exception.Code, StringComparison.Ordinal);
        Assert.DoesNotContain("protocol-test-key", exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("provider-secret-body", exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("private prompt", exception.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("not-json", "AI_INVALID_RESPONSE")]
    [InlineData("{}", "AI_INVALID_RESPONSE")]
    [InlineData("{\"choices\":[{\"message\":{\"content\":\"\"}}]}", "AI_INVALID_RESPONSE")]
    public async Task Completion_rejects_malformed_or_empty_provider_output(string payload, string code)
    {
        var exception = await Assert.ThrowsAsync<ExternalProviderException>(() =>
            Ai(new RecordingHandler(_ => Json(HttpStatusCode.OK, payload))).CompleteAsync("test"));
        Assert.Equal(code, exception.Code);
    }

    [Fact]
    public async Task Completion_rejects_oversized_provider_output()
    {
        var payload = "{\"choices\":[{\"message\":{\"content\":\"" + new string('x', 1200) + "\"}}]}";
        var exception = await Assert.ThrowsAsync<ExternalProviderException>(() =>
            Ai(new RecordingHandler(_ => Json(HttpStatusCode.OK, payload)), maxCharacters: 1000)
                .CompleteAsync("test"));
        Assert.Equal("AI_RESPONSE_TOO_LARGE", exception.Code);
    }

    [Fact]
    public async Task Completion_maps_provider_timeout_and_preserves_caller_cancellation()
    {
        var timeout = new RecordingHandler((_, _) => throw new TaskCanceledException("simulated timeout"));
        var timeoutException = await Assert.ThrowsAsync<ExternalProviderException>(() =>
            Ai(timeout, maxAttempts: 1).CompleteAsync("test"));
        Assert.Equal("AI_TIMEOUT", timeoutException.Code);

        var cancellation = new RecordingHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Json(HttpStatusCode.OK, "{}");
        });
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Ai(cancellation).CompleteAsync("test", cts.Token));
    }

    [Theory]
    [InlineData("ta-LK", "ta", "வணக்கம். பாதுகாப்பான உள்நுழைவு தேவை.")]
    [InlineData("en-LK", "en", "Users need secure login.")]
    public async Task Transcription_posts_multipart_audio_with_language_model_and_traceable_segments(
        string languageCode, string providerLanguage, string transcript)
    {
        var payload = $$"""{"text":"{{transcript}}","segments":[{"start":0.25,"end":1.5,"text":"{{transcript}}"}]}""";
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK, payload));
        var bytes = Encoding.UTF8.GetBytes("audio-fixture");
        var request = VoiceRequest(languageCode, bytes.LongLength);

        var result = await Voice(handler).TranscribeAsync(request, new MemoryStream(bytes));

        Assert.Equal(transcript, result.OriginalText);
        Assert.Equal(250, Assert.Single(result.Segments).StartMs);
        Assert.Equal(1500, result.Segments[0].EndMs);
        Assert.Equal("Bearer protocol-test-key", handler.Authorizations.Single());
        Assert.Contains("audio/transcriptions", handler.Uris.Single(), StringComparison.Ordinal);
        Assert.Contains("name=model", handler.Bodies.Single(), StringComparison.Ordinal);
        Assert.Contains("approved-transcription-model", handler.Bodies.Single(), StringComparison.Ordinal);
        Assert.Contains("name=language", handler.Bodies.Single(), StringComparison.Ordinal);
        Assert.Contains(providerLanguage, handler.Bodies.Single(), StringComparison.Ordinal);
        Assert.Contains("filename=audio.webm", handler.Bodies.Single(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Transcription_retries_5xx_and_recreates_multipart_content()
    {
        var handler = new RecordingHandler(
            _ => Json(HttpStatusCode.InternalServerError, "private failure"),
            _ => Json(HttpStatusCode.OK, """{"text":"Recovered"}"""));
        var bytes = Encoding.UTF8.GetBytes("audio");

        var result = await Voice(handler, maxAttempts: 2).TranscribeAsync(
            VoiceRequest("en", bytes.LongLength), new MemoryStream(bytes));

        Assert.Equal("Recovered", result.OriginalText);
        Assert.Equal(2, handler.Calls);
        Assert.All(handler.Bodies, body => Assert.Contains("filename=audio.webm", body, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{}")]
    [InlineData("{\"text\":\"\"}")]
    [InlineData("{\"text\":\"valid\",\"segments\":{}}")]
    public async Task Transcription_rejects_malformed_provider_output(string payload)
    {
        var bytes = Encoding.UTF8.GetBytes("audio");
        var exception = await Assert.ThrowsAsync<ExternalProviderException>(() =>
            Voice(new RecordingHandler(_ => Json(HttpStatusCode.OK, payload))).TranscribeAsync(
                VoiceRequest("en", bytes.LongLength), new MemoryStream(bytes)));
        Assert.Equal("TRANSCRIPTION_INVALID_RESPONSE", exception.Code);
    }

    [Fact]
    public async Task Transcription_rejects_metadata_mismatch_before_network_call()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK, "{}"));
        var bytes = Encoding.UTF8.GetBytes("audio");
        await Assert.ThrowsAsync<ArgumentException>(() => Voice(handler).TranscribeAsync(
            VoiceRequest("en", bytes.LongLength + 1), new MemoryStream(bytes)));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Transcription_auth_failure_is_sanitized_and_not_retried()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.Forbidden, "private transcript failure"));
        var bytes = Encoding.UTF8.GetBytes("audio");
        var exception = await Assert.ThrowsAsync<ExternalProviderException>(() => Voice(handler).TranscribeAsync(
            VoiceRequest("en", bytes.LongLength), new MemoryStream(bytes)));
        Assert.Equal(1, handler.Calls);
        Assert.Equal("TRANSCRIPTION_HTTP_403", exception.Code);
        Assert.DoesNotContain("protocol-test-key", exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("private transcript failure", exception.ToString(), StringComparison.Ordinal);
    }

    private static AIService Ai(HttpMessageHandler handler, int maxAttempts = 1, int maxCharacters = 2000) => new(
        Client(handler), Microsoft.Extensions.Options.Options.Create(Configuration(maxAttempts, maxCharacters)));

    private static OpenAiCompatibleVoiceTranscriber Voice(HttpMessageHandler handler, int maxAttempts = 1) => new(
        Client(handler), Microsoft.Extensions.Options.Options.Create(Configuration(maxAttempts, 2000)));

    private static AiOptions Configuration(int maxAttempts, int maxCharacters) => new()
    {
        ApiKey = "protocol-test-key",
        Model = "approved-model",
        TranscriptionModel = "approved-transcription-model",
        MaxAttempts = maxAttempts,
        BaseRetryMilliseconds = 10,
        MaxResponseCharacters = maxCharacters
    };

    private static HttpClient Client(HttpMessageHandler handler) => new(handler)
    {
        BaseAddress = new Uri("https://provider.example.test/v1/")
    };

    private static VoiceInputRequest VoiceRequest(string language, long size) => new(
        Guid.NewGuid(), Guid.NewGuid(), language, "audio/webm", size, 2, true);

    private static HttpResponseMessage Json(HttpStatusCode status, string payload) => new(status)
    {
        Content = new StringContent(payload, Encoding.UTF8, "application/json")
    };

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> _responses;
        public int Calls { get; private set; }
        public List<string> Uris { get; } = [];
        public List<string> Authorizations { get; } = [];
        public List<string> Bodies { get; } = [];

        public RecordingHandler(params Func<HttpRequestMessage, HttpResponseMessage>[] responses) : this(
            responses.Select<Func<HttpRequestMessage, HttpResponseMessage>,
                Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>>(response =>
                (request, _) => Task.FromResult(response(request))).ToArray()) { }

        public RecordingHandler(params Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>[] responses) =>
            _responses = new Queue<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>>(responses);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Calls++;
            Uris.Add(request.RequestUri?.ToString() ?? "");
            Authorizations.Add(request.Headers.Authorization?.ToString() ?? "");
            Bodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken));
            return await _responses.Dequeue()(request, cancellationToken);
        }
    }
}
