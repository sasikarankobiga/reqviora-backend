using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Options;
using Reqviora.Application.Common.Interfaces;
using Reqviora.Infrastructure.Services;

namespace Reqviora.Infrastructure.Tests;

public sealed class ClamAvFileThreatScannerTests
{
    [Theory]
    [InlineData("stream: OK\0", FileThreatScanStatus.Clean)]
    [InlineData("stream: Test.Signature FOUND\0", FileThreatScanStatus.Infected)]
    public async Task ClamAv_protocol_result_is_mapped_without_exposing_signature(
        string response,
        FileThreatScanStatus expected)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var server = RespondAsync(listener, response);
        var scanner = Scanner(((IPEndPoint)listener.LocalEndpoint).Port);
        var bytes = Encoding.UTF8.GetBytes("uploaded bytes");
        using var content = new MemoryStream(bytes);

        var result = await scanner.ScanAsync(content);

        Assert.Equal(expected, result.Status);
        Assert.Equal(bytes, await server);
    }

    [Fact]
    public async Task Connection_failure_returns_unavailable_after_bounded_attempts()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        using var content = new MemoryStream([1]);

        var result = await Scanner(port, maxAttempts: 2).ScanAsync(content);

        Assert.Equal(FileThreatScanStatus.Unavailable, result.Status);
    }

    [Fact]
    public async Task Caller_cancellation_is_not_converted_to_scanner_unavailability()
    {
        using var content = new MemoryStream([1]);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Scanner(1).ScanAsync(content, cancellation.Token));
    }

    private static ClamAvFileThreatScanner Scanner(int port, int maxAttempts = 1) => new(
        Options.Create(new StorageOptions
        {
            ClamAvHost = IPAddress.Loopback.ToString(),
            ClamAvPort = port,
            AntivirusMaxAttempts = maxAttempts,
            AntivirusRetryDelayMilliseconds = 0,
            AntivirusTimeoutSeconds = 2
        }));

    private static async Task<byte[]> RespondAsync(TcpListener listener, string response)
    {
        using var client = await listener.AcceptTcpClientAsync();
        await using var stream = client.GetStream();
        var command = new byte[10];
        await ReadExactlyAsync(stream, command);
        Assert.Equal("zINSTREAM\0", Encoding.ASCII.GetString(command));
        using var content = new MemoryStream();
        var sizeBytes = new byte[4];
        while (true)
        {
            await ReadExactlyAsync(stream, sizeBytes);
            var size = BinaryPrimitives.ReadInt32BigEndian(sizeBytes);
            if (size == 0) break;
            var chunk = new byte[size];
            await ReadExactlyAsync(stream, chunk);
            content.Write(chunk);
        }
        await stream.WriteAsync(Encoding.UTF8.GetBytes(response));
        return content.ToArray();
    }

    private static async Task ReadExactlyAsync(Stream stream, byte[] buffer)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset));
            if (read == 0) throw new EndOfStreamException();
            offset += read;
        }
    }
}
