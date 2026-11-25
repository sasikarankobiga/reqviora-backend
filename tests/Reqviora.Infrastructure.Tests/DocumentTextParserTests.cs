using System.IO.Compression;
using System.Text;
using Reqviora.Application.Features.Documents;
using Reqviora.Infrastructure.Services;

namespace Reqviora.Infrastructure.Tests;

public sealed class DocumentTextParserTests
{
    private static DocumentInputRequest Request(byte[] bytes, string name, string mediaType) =>
        new(Guid.NewGuid(), Guid.NewGuid(), name, mediaType, bytes.LongLength);

    [Fact]
    public async Task Text_preserves_Tamil_and_evidence()
    {
        const string text = "User can login. தமிழ் தேவைகள்.";
        var bytes = Encoding.UTF8.GetBytes(text);
        var input = Request(bytes, "requirements.txt", "text/plain");
        using var stream = new MemoryStream(bytes);
        var result = await new DocumentTextParser().ParseAsync(input, stream);
        var segment = Assert.Single(result.Segments);
        Assert.Equal(text, segment.Text);
        Assert.Equal(text, segment.Evidence.Quote);
        Assert.Equal(input.UploadedFileId.ToString("D"), segment.Evidence.SourceId);
        Assert.Equal(text.Length, segment.Evidence.EndOffset);
    }

    [Fact]
    public async Task Docx_extracts_main_document_paragraphs()
    {
        using var bytes = new MemoryStream();
        using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var writer = new StreamWriter(zip.CreateEntry("word/document.xml").Open());
            writer.Write("<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p><w:r><w:t>First requirement</w:t></w:r></w:p><w:p><w:r><w:t>Second requirement</w:t></w:r></w:p></w:body></w:document>");
        }
        var payload = bytes.ToArray();
        using var inputStream = new MemoryStream(payload);
        var result = await new DocumentTextParser().ParseAsync(Request(payload, "requirements.docx",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document"), inputStream);
        Assert.Collection(result.Segments,
            first => Assert.Equal("First requirement", first.Text),
            second =>
            {
                Assert.Equal("Second requirement", second.Text);
                Assert.Null(second.Evidence.PageNumber);
            });
    }

    [Fact]
    public async Task Pdf_extracts_text_with_page_evidence()
    {
        var payload = MinimalPdf();
        using var stream = new MemoryStream(payload);
        var result = await new DocumentTextParser().ParseAsync(Request(payload, "requirements.pdf", "application/pdf"), stream);
        var segment = Assert.Single(result.Segments);
        Assert.Contains("Login requirement", segment.Text);
        Assert.Equal(1, segment.Evidence.PageNumber);
    }

    [Fact]
    public async Task Invalid_pdf_and_oversized_text_are_rejected()
    {
        var invalid = Encoding.UTF8.GetBytes("not a PDF");
        using var bad = new MemoryStream(invalid);
        await Assert.ThrowsAsync<InvalidDataException>(() => new DocumentTextParser().ParseAsync(
            Request(invalid, "bad.pdf", "application/pdf"), bad));
        var large = Encoding.UTF8.GetBytes(new string('a', 100001));
        using var text = new MemoryStream(large);
        await Assert.ThrowsAsync<InvalidDataException>(() => new DocumentTextParser().ParseAsync(
            Request(large, "large.txt", "text/plain"), text));
    }

    private static byte[] MinimalPdf()
    {
        const string content = "BT /F1 12 Tf 50 750 Td (Login requirement) Tj ET\n";
        string[] objects = ["<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            $"<< /Length {content.Length} >>\nstream\n{content}endstream"];
        var builder = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var i = 0; i < objects.Length; i++)
        {
            offsets.Add(builder.Length);
            builder.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        var xref = builder.Length;
        builder.Append($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets) builder.Append(offset.ToString("D10")).Append(" 00000 n \n");
        builder.Append($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(builder.ToString());
    }
}
