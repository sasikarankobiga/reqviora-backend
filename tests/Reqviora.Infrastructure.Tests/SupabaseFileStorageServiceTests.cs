using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using Reqviora.Application.Common.Interfaces;
using Reqviora.Infrastructure.Services;

namespace Reqviora.Infrastructure.Tests;

public sealed class SupabaseFileStorageServiceTests
{
    [Fact]
    public async Task Upload_sign_and_delete_use_private_authenticated_routes()
    {
        var handler = new RecordingHandler();
        var service = Service(handler);
        await using var content = new MemoryStream(Encoding.UTF8.GetBytes("private document"));

        var path = await service.SaveAsync(content, "requirements.txt");
        var signed = await service.CreateSignedReadUrlAsync(path, TimeSpan.FromMinutes(5));
        await service.DeleteAsync(path);

        Assert.EndsWith(".txt", path, StringComparison.Ordinal);
        Assert.Equal("https://project.supabase.co/storage/v1/object/sign/private-documents/file.txt?token=test", signed!.ToString());
        Assert.Equal(3, handler.Requests.Count);
        Assert.All(handler.Requests, request =>
        {
            Assert.Equal("Bearer test-service-key", request.Authorization);
            Assert.Equal("test-service-key", request.ApiKey);
        });
        Assert.Equal("false", handler.Requests[0].Upsert);
        Assert.DoesNotContain("requirements", handler.Requests[0].Path, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("../secret")]
    [InlineData("folder\\..\\secret")]
    [InlineData("/absolute")]
    public async Task Unsafe_object_paths_are_rejected_before_http(string path)
    {
        var handler = new RecordingHandler();
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            Service(handler).CreateSignedReadUrlAsync(path, TimeSpan.FromMinutes(5)));
        Assert.Empty(handler.Requests);
    }

    private static SupabaseFileStorageService Service(HttpMessageHandler handler) => new(
        new HttpClient(handler) { BaseAddress = new Uri("https://project.supabase.co/") },
        Options.Create(new StorageOptions
        {
            Provider = "Supabase",
            SupabaseBucket = "private-documents",
            SupabaseServiceKey = "test-service-key",
            MaxFileBytes = 1024,
            SignedUrlSeconds = 300
        }),
        new CleanScanner());

    private sealed class CleanScanner : IFileThreatScanner
    {
        public Task<FileThreatScanResult> ScanAsync(Stream content, CancellationToken cancellationToken = default) =>
            Task.FromResult(new FileThreatScanResult(FileThreatScanStatus.Clean));
    }

    private sealed record Captured(string Method, string Path, string? Authorization, string? ApiKey, string? Upsert);

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<Captured> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(new(request.Method.Method, request.RequestUri!.AbsolutePath,
                request.Headers.Authorization?.ToString(),
                request.Headers.TryGetValues("apikey", out var apiKeys) ? apiKeys.Single() : null,
                request.Headers.TryGetValues("x-upsert", out var values) ? values.Single() : null));
            if (request.RequestUri.AbsolutePath.Contains("/object/sign/", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{\"signedURL\":\"/object/sign/private-documents/file.txt?token=test\"}",
                        Encoding.UTF8, "application/json")
                });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
