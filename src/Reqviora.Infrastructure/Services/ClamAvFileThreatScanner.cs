using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Options;
using Reqviora.Application.Common.Interfaces;

namespace Reqviora.Infrastructure.Services;

public sealed class ClamAvFileThreatScanner(IOptions<StorageOptions> options) : IFileThreatScanner
{
    public async Task<FileThreatScanResult> ScanAsync(
        Stream content,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (!content.CanSeek)
            throw new ArgumentException("Antivirus scanning requires a seekable stream.", nameof(content));

        var value = options.Value;
        var attempts = Math.Clamp(value.AntivirusMaxAttempts, 1, 5);
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            content.Position = 0;
            var result = await ScanOnceAsync(content, value, cancellationToken);
            if (result.Status != FileThreatScanStatus.Unavailable || attempt == attempts)
                return result;
            await Task.Delay(Math.Clamp(value.AntivirusRetryDelayMilliseconds, 0, 5000), cancellationToken);
        }

        return new(FileThreatScanStatus.Unavailable);
    }

    private static async Task<FileThreatScanResult> ScanOnceAsync(
        Stream content,
        StorageOptions options,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(options.AntivirusTimeoutSeconds, 1, 60)));
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(options.ClamAvHost, options.ClamAvPort, timeout.Token);
            await using var network = client.GetStream();
            await network.WriteAsync("zINSTREAM\0"u8.ToArray(), timeout.Token);
            var chunk = new byte[8192];
            int read;
            while ((read = await content.ReadAsync(chunk, timeout.Token)) > 0)
            {
                var size = new byte[4];
                BinaryPrimitives.WriteInt32BigEndian(size, read);
                await network.WriteAsync(size, timeout.Token);
                await network.WriteAsync(chunk.AsMemory(0, read), timeout.Token);
            }
            await network.WriteAsync(new byte[4], timeout.Token);
            var response = new byte[1024];
            var length = await network.ReadAsync(response, timeout.Token);
            var text = Encoding.UTF8.GetString(response, 0, length);
            if (text.Contains("FOUND", StringComparison.Ordinal))
                return new(FileThreatScanStatus.Infected);
            return text.Contains("OK", StringComparison.Ordinal)
                ? new(FileThreatScanStatus.Clean)
                : new(FileThreatScanStatus.Unavailable);
        }
        catch (Exception exception) when (exception is SocketException or IOException)
        {
            return new(FileThreatScanStatus.Unavailable);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(FileThreatScanStatus.Unavailable);
        }
    }
}
