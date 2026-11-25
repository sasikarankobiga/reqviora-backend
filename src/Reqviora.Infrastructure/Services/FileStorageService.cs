using Reqviora.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Reqviora.Infrastructure.Services;

public sealed class FileStorageService(
    IOptions<StorageOptions> options,
    IFileThreatScanner threatScanner,
    ILogger<FileStorageService> logger) : IFileStorageService
{
    public async Task<string> SaveAsync(Stream content, string fileName, CancellationToken cancellationToken = default)
    {
        var value = options.Value;
        var root = Path.GetFullPath(value.RootPath);
        Directory.CreateDirectory(root);
        var extension = Path.GetExtension(Path.GetFileName(fileName));
        var relative = Path.Combine(DateTime.UtcNow.ToString("yyyy/MM/dd"), $"{Guid.NewGuid():N}{extension}");
        var destination = Path.GetFullPath(Path.Combine(root, relative));
        if (!destination.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Invalid storage path.");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

        await using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length == 0 || buffer.Length > value.MaxFileBytes)
            throw new InvalidDataException("File is empty or exceeds the configured size limit.");
        buffer.Position = 0;
        var scan = await threatScanner.ScanAsync(buffer, cancellationToken);
        if (scan.Status == FileThreatScanStatus.Infected)
            throw new InvalidDataException("File was rejected by antivirus scanning.");
        if (scan.Status == FileThreatScanStatus.Unavailable && value.RequireAntivirus)
            throw new IOException("The antivirus scanner is unavailable.");
        if (scan.Status == FileThreatScanStatus.Unavailable)
            logger.LogWarning("File storage is proceeding because antivirus is explicitly optional in this environment.");
        buffer.Position = 0;
        await using (var file = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                         81920, FileOptions.Asynchronous | FileOptions.WriteThrough))
            await buffer.CopyToAsync(file, cancellationToken);
        return relative.Replace('\\', '/');
    }

    public Task<Stream> OpenReadAsync(string storagePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = ResolvePrivatePath(storagePath);
        Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string storagePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = ResolvePrivatePath(storagePath);
        if (File.Exists(path))
            File.Delete(path);
        return Task.CompletedTask;
    }

    private string ResolvePrivatePath(string storagePath)
    {
        var root = Path.GetFullPath(options.Value.RootPath);
        if (string.IsNullOrWhiteSpace(storagePath) || Path.IsPathRooted(storagePath))
            throw new InvalidDataException("Invalid storage path.");
        var path = Path.GetFullPath(Path.Combine(root, storagePath));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, comparison))
            throw new InvalidDataException("Invalid storage path.");
        return path;
    }

}
