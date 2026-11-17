using Reqviora.Application.Common.Interfaces;

namespace Reqviora.Infrastructure.Services;

public sealed class FileStorageService : IFileStorageService
{
    public Task<string> SaveAsync(Stream content, string fileName, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Configure a file storage provider before saving files.");
}
