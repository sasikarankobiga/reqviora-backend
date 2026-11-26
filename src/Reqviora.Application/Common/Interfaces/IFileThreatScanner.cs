namespace Reqviora.Application.Common.Interfaces;

public enum FileThreatScanStatus
{
    Clean,
    Infected,
    Unavailable
}

public sealed record FileThreatScanResult(FileThreatScanStatus Status);

public interface IFileThreatScanner
{
    Task<FileThreatScanResult> ScanAsync(
        Stream content,
        CancellationToken cancellationToken = default);
}
