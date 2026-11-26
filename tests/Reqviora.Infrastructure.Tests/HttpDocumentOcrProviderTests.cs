using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using Reqviora.Application.Features.DocumentIntelligence;
using Reqviora.Application.Features.Documents;
using Reqviora.Infrastructure.Services;

namespace Reqviora.Infrastructure.Tests;

public sealed class HttpDocumentOcrProviderTests
{
    [Fact]
    public async Task Recognize_maps_pages_to_traceable_segments_without_exposing_key()
    {
        var handler = new SequenceHandler(HttpStatusCode.OK,
            """{"pages":[{"pageNumber":1,"text":"Scanned requirement text"}],"warnings":["low contrast"]}""");
        var input = Request("scanned-pdf");

        var result = await Service(handler).RecognizeAsync(
            input, new MemoryStream(Encoding.UTF8.GetBytes("scanned-pdf")));

        var segment = Assert.Single(result.Segments);
        Assert.Equal(input.UploadedFileId.ToString("D"), segment.Evidence.SourceId);
        Assert.Equal("page:1:ocr:offset:0", segment.Evidence.Locator);
        Assert.Equal("Scanned requirement text", segment.Evidence.Quote);
        Assert.Contains("low contrast", result.Warnings);
        Assert.Equal("Bearer test-ocr-key", handler.Authorization);
        Assert.DoesNotContain("test-ocr-key", handler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Transient_provider_failure_is_retried()
    {
        var handler = new SequenceHandler(HttpStatusCode.ServiceUnavailable, "{}",
            HttpStatusCode.OK, """{"pages":[{"pageNumber":2,"text":"Recovered"}]}""");

        var result = await Service(handler).RecognizeAsync(
            Request("pdf"), new MemoryStream(Encoding.UTF8.GetBytes("pdf")));

        Assert.Equal(2, handler.Calls);
        Assert.Equal("Recovered", Assert.Single(result.Segments).Text);
    }

    [Theory]
    [InlineData("{\"pages\":[{\"pageNumber\":0,\"text\":\"bad\"}]}")]
    [InlineData("{\"pages\":[{\"pageNumber\":1,\"text\":\"\"}]}")]
    [InlineData("{\"unexpected\":[]}")]
    public async Task Invalid_provider_payload_is_rejected(string payload)
    {
        var handler = new SequenceHandler(HttpStatusCode.OK, payload);
        await Assert.ThrowsAsync<DocumentOcrException>(() => Service(handler).RecognizeAsync(
            Request("pdf"), new MemoryStream(Encoding.UTF8.GetBytes("pdf"))));
    }

    private static DocumentInputRequest Request(string content) => new(
        Guid.NewGuid(), Guid.NewGuid(), "scan.pdf", "application/pdf", Encoding.UTF8.GetByteCount(content));

    private static HttpDocumentOcrProvider Service(HttpMessageHandler handler) => new(
        new HttpClient(handler) { BaseAddress = new Uri("https://ocr.example.test/v1/recognize") },
        Options.Create(new OcrOptions
        {
            Enabled = true,
            ApiKey = "test-ocr-key",
            Provider = "TestOcr",
            Model = "test-model",
            MaxAttempts = 2,
            BaseRetryMilliseconds = 10
        }));

    private sealed class SequenceHandler(params object[] values) : HttpMessageHandler
    {
        private int _index;
        public int Calls { get; private set; }
        public string? Authorization { get; private set; }
        public string Body { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Calls++;
            Authorization = request.Headers.Authorization?.ToString();
            Body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            var status = (HttpStatusCode)values[_index++];
            var payload = (string)values[_index++];
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
        }
    }
}
