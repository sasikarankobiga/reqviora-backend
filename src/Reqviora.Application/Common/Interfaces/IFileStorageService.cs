namespace Reqviora.Application.Common.Interfaces;

public interface IFileStorageService
{
    Task<string> SaveAsync(Stream content, string fileName, CancellationToken cancellationToken = default);
    Task<Stream> OpenReadAsync(string storagePath, CancellationToken cancellationToken = default);
    Task DeleteAsync(string storagePath, CancellationToken cancellationToken = default);
    Task<Uri?> CreateSignedReadUrlAsync(
        string storagePath,
        TimeSpan lifetime,
        CancellationToken cancellationToken = default);
}
