using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Reqviora.Application.Features.DocumentIntelligence;
using Reqviora.Application.Features.Documents;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace Reqviora.Infrastructure.Services;

public sealed class DocumentTextParser : IDocumentTextParser
{
    private const int MaxTextCharacters = 100_000;
    private const int MaxUnits = 5000;
    private const int SegmentSize = 2000;
    private const long MaxDocxXmlBytes = 8_000_000;

    public async Task<DocumentParseResult> ParseAsync(DocumentInputRequest document, Stream fileContent,
        CancellationToken cancellationToken = default)
    {
        InputStudioContractValidator.Validate(document);
        ArgumentNullException.ThrowIfNull(fileContent);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await fileContent.ReadAsync(chunk.AsMemory(), cancellationToken)) > 0)
        {
            if (buffer.Length + read > document.SizeBytes)
                throw new InvalidDataException("Document size does not match its metadata.");
            buffer.Write(chunk, 0, read);
        }
        if (buffer.Length != document.SizeBytes)
            throw new InvalidDataException("Document size does not match its metadata.");
        buffer.Position = 0;
        var segments = new List<ParsedDocumentSegment>();
        var warnings = new List<string>();
        var total = 0;
        var unitCount = 0;

        void Add(string text, string locator, int? page)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++unitCount > MaxUnits || text.Length > MaxTextCharacters - total)
                throw new InvalidDataException("Document exceeds the supported text limit.");
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
                        $"{locator}:offset:{start}", page, start, start + length, part);
                    segments.Add(new ParsedDocumentSegment(segments.Count, part, evidence));
                }
                start += length;
            }
        }

        try
        {
            switch (document.MediaType)
            {
                case "text/plain":
                    using (var reader = new StreamReader(buffer, new UTF8Encoding(false, true),
                               detectEncodingFromByteOrderMarks: false, leaveOpen: true))
                    {
                        var text = await reader.ReadToEndAsync(cancellationToken);
                        if (text.StartsWith('\uFEFF')) text = text[1..];
                        if (text.Contains('\0')) throw new InvalidDataException("Invalid text document.");
                        Add(text, "text", null);
                    }
                    break;
                case "application/vnd.openxmlformats-officedocument.wordprocessingml.document":
                    using (var archive = new ZipArchive(buffer, ZipArchiveMode.Read, leaveOpen: true))
                    {
                        var entry = archive.GetEntry("word/document.xml")
                            ?? throw new InvalidDataException("DOCX main document part is missing.");
                        if (entry.Length > MaxDocxXmlBytes)
                            throw new InvalidDataException("DOCX main document part exceeds the parsing limit.");
                        using var xmlStream = entry.Open();
                        using var xmlReader = XmlReader.Create(xmlStream, new XmlReaderSettings
                        {
                            DtdProcessing = DtdProcessing.Prohibit,
                            XmlResolver = null,
                            MaxCharactersInDocument = MaxDocxXmlBytes
                        });
                        var xml = XDocument.Load(xmlReader);
                        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
                        if (xml.Root?.Name != w + "document") throw new InvalidDataException("Invalid DOCX document.");
                        var number = 0;
                        foreach (var paragraph in xml.Descendants(w + "p"))
                        {
                            var text = string.Concat(paragraph.Descendants(w + "t").Select(node => node.Value));
                            Add(text, $"paragraph:{++number}", null);
                        }
                    }
                    break;
                case "application/pdf":
                    try
                    {
                        using var pdf = PdfDocument.Open(buffer.ToArray());
                        if (pdf.NumberOfPages > 500) throw new InvalidDataException("PDF exceeds the page limit.");
                        for (var number = 1; number <= pdf.NumberOfPages; number++)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            var text = ContentOrderTextExtractor.GetText(pdf.GetPage(number));
                            if (string.IsNullOrWhiteSpace(text)) warnings.Add($"Page {number} has no extractable text; OCR may be required.");
                            Add(text, $"page:{number}", number);
                        }
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException and not InvalidDataException)
                    {
                        throw new InvalidDataException("Invalid or unsupported PDF document.", ex);
                    }
                    break;
            }
        }
        catch (Exception ex) when (ex is XmlException or DecoderFallbackException)
        {
            throw new InvalidDataException("Invalid document content.", ex);
        }
        if (segments.Count == 0) warnings.Add("No extractable text was found.");
        var result = new DocumentParseResult(document.UploadedFileId, segments, warnings);
        DocumentParsingContractValidator.Validate(result);
        return result;
    }
}
