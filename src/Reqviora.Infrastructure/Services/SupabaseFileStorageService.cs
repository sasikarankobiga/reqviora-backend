using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Reqviora.Application.Common.Interfaces;

namespace Reqviora.Infrastructure.Services;

public sealed class SupabaseFileStorageService(
    HttpClient http,
    IOptions<StorageOptions> options,
    IFileThreatScanner threatScanner) : IFileStorageService
{
    public async Task<string> SaveAsync(Stream content, string fileName, CancellationToken cancellationToken = default)
    {
        var value = ValidateOptions();
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length is 0 || buffer.Length > value.MaxFileBytes)
            throw new InvalidDataException("File is empty or exceeds the configured size limit.");
        buffer.Position = 0;
        var scan = await threatScanner.ScanAsync(buffer, cancellationToken);
        if (scan.Status == FileThreatScanStatus.Infected)
            throw new InvalidDataException("File was rejected by antivirus scanning.");
        if (scan.Status == FileThreatScanStatus.Unavailable && value.RequireAntivirus)
            throw new IOException("The antivirus scanner is unavailable.");

        var extension = Path.GetExtension(Path.GetFileName(fileName));
        var path = $"{DateTime.UtcNow:yyyy/MM/dd}/{Guid.NewGuid():N}{extension}";
        buffer.Position = 0;
        using var request = Authorized(HttpMethod.Post, ObjectRoute(value, path), value);
        request.Headers.TryAddWithoutValidation("x-upsert", "false");
        request.Content = new StreamContent(buffer);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new IOException($"Private object upload failed with status {(int)response.StatusCode}.");
        return path;
    }

    public async Task<Stream> OpenReadAsync(string storagePath, CancellationToken cancellationToken = default)
    {
        var value = ValidateOptions();
        using var request = Authorized(HttpMethod.Get, ObjectRoute(value, ValidatePath(storagePath)), value);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new IOException($"Private object download failed with status {(int)response.StatusCode}.");
        await using var remote = await response.Content.ReadAsStreamAsync(cancellationToken);
        var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await remote.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > value.MaxFileBytes)
            {
                await buffer.DisposeAsync();
                throw new InvalidDataException("Stored object exceeds the configured size limit.");
            }
            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }
        buffer.Position = 0;
        return buffer;
    }

    public async Task DeleteAsync(string storagePath, CancellationToken cancellationToken = default)
    {
        var value = ValidateOptions();
        using var request = Authorized(HttpMethod.Delete,
            $"storage/v1/object/{Escape(value.SupabaseBucket)}", value);
        request.Content = JsonContent.Create(new { prefixes = new[] { ValidatePath(storagePath) } });
        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.NotFound)
            throw new IOException($"Private object deletion failed with status {(int)response.StatusCode}.");
    }

    public async Task<Uri?> CreateSignedReadUrlAsync(
        string storagePath,
        TimeSpan lifetime,
        CancellationToken cancellationToken = default)
    {
        var value = ValidateOptions();
        var seconds = (int)Math.Clamp(lifetime.TotalSeconds, 30, value.SignedUrlSeconds);
        using var request = Authorized(HttpMethod.Post,
            $"storage/v1/object/sign/{Escape(value.SupabaseBucket)}/{EscapePath(ValidatePath(storagePath))}", value);
        request.Content = JsonContent.Create(new { expiresIn = seconds });
        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new IOException($"Signed URL creation failed with status {(int)response.StatusCode}.");
        using var json = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        var signed = json.RootElement.GetProperty("signedURL").GetString();
        if (string.IsNullOrWhiteSpace(signed)) throw new InvalidDataException("Storage returned no signed URL.");
        return new Uri(http.BaseAddress!, signed.StartsWith("/storage/v1/", StringComparison.Ordinal)
            ? signed : "/storage/v1" + signed);
    }

    private StorageOptions ValidateOptions()
    {
        var value = options.Value;
        if (http.BaseAddress is null || http.BaseAddress.Scheme != Uri.UriSchemeHttps ||
            string.IsNullOrWhiteSpace(value.SupabaseBucket) || string.IsNullOrWhiteSpace(value.SupabaseServiceKey) ||
            value.SignedUrlSeconds is < 30 or > 900)
            throw new InvalidOperationException("Supabase private storage is not securely configured.");
        return value;
    }

    private HttpRequestMessage Authorized(HttpMethod method, string route, StorageOptions value)
    {
        var request = new HttpRequestMessage(method, route);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", value.SupabaseServiceKey);
        request.Headers.TryAddWithoutValidation("apikey", value.SupabaseServiceKey);
        return request;
    }

    private static string ObjectRoute(StorageOptions value, string path) =>
        $"storage/v1/object/{Escape(value.SupabaseBucket)}/{EscapePath(path)}";
    private static string Escape(string value) => Uri.EscapeDataString(value);
    private static string EscapePath(string path) => string.Join('/', path.Split('/').Select(Escape));
    private static string ValidatePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains('\\') || Path.IsPathRooted(path) ||
            path.Split('/').Any(part => part is "" or "." or ".."))
            throw new InvalidDataException("Invalid storage path.");
        return path;
    }
}
