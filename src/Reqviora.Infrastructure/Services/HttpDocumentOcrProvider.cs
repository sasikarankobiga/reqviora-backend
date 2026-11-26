using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Reqviora.Application.Features.DocumentIntelligence;
using Reqviora.Application.Features.Documents;

namespace Reqviora.Infrastructure.Services;

public sealed class HttpDocumentOcrProvider(HttpClient http, IOptions<OcrOptions> options)
    : IDocumentOcrProvider
{
    private const int SegmentSize = 2000;
    private const int MaxTextCharacters = 100_000;
    private readonly OcrOptions _options = options.Value;

    public bool IsEnabled => _options.Enabled;

    public async Task<DocumentParseResult> RecognizeAsync(
        DocumentInputRequest document,
        Stream fileContent,
        CancellationToken cancellationToken = default)
    {
        InputStudioContractValidator.Validate(document);
        ArgumentNullException.ThrowIfNull(fileContent);
        if (!IsEnabled)
            throw new InvalidOperationException("OCR is not enabled.");
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
            throw new InvalidOperationException("OCR API key is not configured.");

        using var buffer = new MemoryStream();
        await fileContent.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length != document.SizeBytes)
            throw new DocumentOcrException("Document size does not match its metadata.");

        var maxAttempts = Math.Clamp(_options.MaxAttempts, 1, 5);
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                using var form = new MultipartFormDataContent();
                using var content = new ByteArrayContent(buffer.ToArray());
                content.Headers.ContentType = MediaTypeHeaderValue.Parse(document.MediaType);
                form.Add(content, "file", document.FileName);
                form.Add(new StringContent(_options.Provider), "provider");
                form.Add(new StringContent(_options.Model), "model");
                using var request = new HttpRequestMessage(HttpMethod.Post, "") { Content = form };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
                using var response = await http.SendAsync(
                    request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (IsTransient(response.StatusCode) && attempt < maxAttempts)
                {
                    await DelayAsync(attempt, cancellationToken);
                    continue;
                }
                if (!response.IsSuccessStatusCode)
                    throw new DocumentOcrException($"OCR provider returned HTTP {(int)response.StatusCode}.");
                return await ReadResultAsync(document, response, cancellationToken);
            }
            catch (HttpRequestException) when (attempt < maxAttempts)
            {
                await DelayAsync(attempt, cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                throw new DocumentOcrException("OCR provider is unavailable.", ex);
            }
        }

        throw new DocumentOcrException("OCR provider retry limit was reached.");
    }

    private async Task<DocumentParseResult> ReadResultAsync(
        DocumentInputRequest document,
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (!json.RootElement.TryGetProperty("pages", out var pages) ||
                pages.ValueKind != JsonValueKind.Array)
                throw new DocumentOcrException("OCR response does not contain a pages array.");

            var segments = new List<ParsedDocumentSegment>();
            var warnings = new List<string>();
            var pageNumbers = new HashSet<int>();
            var total = 0;
            foreach (var page in pages.EnumerateArray())
            {
                var pageNumber = page.GetProperty("pageNumber").GetInt32();
                var text = page.GetProperty("text").GetString()?.Trim();
                if (pageNumber <= 0 || !pageNumbers.Add(pageNumber) || string.IsNullOrWhiteSpace(text))
                    throw new DocumentOcrException("OCR response contains an invalid page.");
                if (text.Length > MaxTextCharacters - total)
                    throw new DocumentOcrException("OCR response exceeds the text limit.");
                total += text.Length;
                for (var start = 0; start < text.Length;)
                {
                    var length = Math.Min(SegmentSize, text.Length - start);
                    if (start + length < text.Length && char.IsHighSurrogate(text[start + length - 1]))
                        length--;
                    var part = text.Substring(start, length);
                    if (!string.IsNullOrWhiteSpace(part))
                    {
                        var evidence = new SourceEvidenceRef(document.UploadedFileId.ToString("D"),
                            $"page:{pageNumber}:ocr:offset:{start}", pageNumber, start, start + length, part);
                        segments.Add(new ParsedDocumentSegment(segments.Count, part, evidence));
                    }
                    start += length;
                }
            }
            if (json.RootElement.TryGetProperty("warnings", out var warningItems) &&
                warningItems.ValueKind == JsonValueKind.Array)
            {
                warnings.AddRange(warningItems.EnumerateArray()
                    .Select(item => item.GetString()?.Trim())
                    .Where(item => !string.IsNullOrWhiteSpace(item))!);
            }
            if (segments.Count == 0)
                warnings.Add("OCR found no text.");
            var result = new DocumentParseResult(document.UploadedFileId, segments, warnings);
            DocumentParsingContractValidator.Validate(result);
            return result;
        }
        catch (DocumentOcrException)
        {
            throw;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            throw new DocumentOcrException("OCR provider returned an invalid response.", ex);
        }
    }

    private Task DelayAsync(int attempt, CancellationToken cancellationToken) => Task.Delay(
        TimeSpan.FromMilliseconds(Math.Clamp(_options.BaseRetryMilliseconds, 10, 5000) * attempt),
        cancellationToken);

    private static bool IsTransient(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests ||
        (int)statusCode >= 500;
}
