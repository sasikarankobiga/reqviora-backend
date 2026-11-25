using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Reqviora.Application.Common.Interfaces;
using Reqviora.Infrastructure.Services;

namespace Reqviora.Infrastructure.Tests;

public sealed class FileStorageServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"reqviora-storage-{Guid.NewGuid():N}");

    [Fact]
    public async Task Clean_file_is_written_to_private_relative_path()
    {
        var scanner = new StubScanner(FileThreatScanStatus.Clean);
        var service = Service(scanner, requireAntivirus: true);
        var bytes = Encoding.UTF8.GetBytes("safe requirement");

        await using var upload = new MemoryStream(bytes);
        var path = await service.SaveAsync(upload, "requirements.txt");

        Assert.False(Path.IsPathRooted(path));
        Assert.DoesNotContain("requirements", path, StringComparison.OrdinalIgnoreCase);
        await using var stored = await service.OpenReadAsync(path);
        using var reader = new MemoryStream();
        await stored.CopyToAsync(reader);
        Assert.Equal(bytes, reader.ToArray());
        Assert.Equal(bytes, scanner.Bytes);
    }

    [Fact]
    public async Task Infected_file_is_always_rejected_before_write()
    {
        var service = Service(new StubScanner(FileThreatScanStatus.Infected), requireAntivirus: false);
        await using var upload = new MemoryStream([1, 2, 3]);

        await Assert.ThrowsAsync<InvalidDataException>(() => service.SaveAsync(upload, "infected.txt"));

        Assert.Empty(Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Unavailable_scanner_fails_closed_when_required()
    {
        var service = Service(new StubScanner(FileThreatScanStatus.Unavailable), requireAntivirus: true);
        await using var upload = new MemoryStream([1, 2, 3]);

        await Assert.ThrowsAsync<IOException>(() => service.SaveAsync(upload, "pending.txt"));

        Assert.Empty(Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Unavailable_scanner_can_be_explicitly_allowed_outside_production()
    {
        var service = Service(new StubScanner(FileThreatScanStatus.Unavailable), requireAntivirus: false);
        await using var upload = new MemoryStream([1, 2, 3]);

        var path = await service.SaveAsync(upload, "local.txt");

        Assert.True(File.Exists(Path.Combine(_root, path.Replace('/', Path.DirectorySeparatorChar))));
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("/absolute.txt")]
    public async Task Invalid_read_path_cannot_escape_private_root(string path)
    {
        var service = Service(new StubScanner(FileThreatScanStatus.Clean), requireAntivirus: true);
        await Assert.ThrowsAsync<InvalidDataException>(() => service.OpenReadAsync(path));
    }

    [Fact]
    public async Task Delete_removes_only_a_private_relative_file()
    {
        var service = Service(new StubScanner(FileThreatScanStatus.Clean), requireAntivirus: true);
        await using var upload = new MemoryStream([1, 2, 3]);
        var path = await service.SaveAsync(upload, "temporary.txt");

        await service.DeleteAsync(path);

        Assert.Empty(Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories));
        await Assert.ThrowsAsync<InvalidDataException>(() => service.DeleteAsync("../outside.txt"));
    }

    private FileStorageService Service(IFileThreatScanner scanner, bool requireAntivirus) => new(
        Options.Create(new StorageOptions
        {
            RootPath = _root,
            MaxFileBytes = 1024,
            RequireAntivirus = requireAntivirus
        }),
        scanner,
        NullLogger<FileStorageService>.Instance);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private sealed class StubScanner(FileThreatScanStatus status) : IFileThreatScanner
    {
        public byte[] Bytes { get; private set; } = [];

        public async Task<FileThreatScanResult> ScanAsync(
            Stream content,
            CancellationToken cancellationToken = default)
        {
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, cancellationToken);
            Bytes = buffer.ToArray();
            return new(status);
        }
    }
}
