using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PocoUnlockAssistant.Core.Abstractions;
using PocoUnlockAssistant.Core.Time;

namespace PocoUnlockAssistant.Infrastructure.Time;

/// <summary>
/// Minimal SNTP (RFC 4330) client. Queries several servers in parallel and feeds the
/// samples to <see cref="NtpConsensus"/>, which decides whether the measurement is
/// trustworthy enough to allow automated execution.
/// </summary>
public sealed class NtpClient : INtpClient
{
    public static readonly DateTimeOffset NtpEpoch = new(1900, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly string[] _servers;
    private readonly TimeSpan _timeout;
    private readonly ILogger<NtpClient> _logger;

    public NtpClient(IEnumerable<string> servers, TimeSpan? timeout = null, ILogger<NtpClient>? logger = null)
    {
        _servers = servers.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToArray();
        _timeout = timeout ?? TimeSpan.FromMilliseconds(1800);
        _logger = logger ?? NullLogger<NtpClient>.Instance;
    }

    public async Task<TimeSyncReport> SynchronizeAsync(CancellationToken cancellationToken = default)
    {
        if (_servers.Length == 0)
        {
            return TimeSyncReport.Failure("No NTP servers configured.", DateTimeOffset.UtcNow);
        }

        var queries = _servers.Select(server => QueryAsync(server, cancellationToken)).ToArray();

        try
        {
            var results = await Task.WhenAll(queries).ConfigureAwait(false);
            var samples = results.Where(r => r is not null).Select(r => r!).ToList();
            var report = NtpConsensus.Build(samples, DateTimeOffset.UtcNow);

            _logger.LogInformation(
                "NTP sync: ok={Ok} server={Server} offset={Offset}ms uncertainty={Uncertainty}ms health={Health}",
                report.Ok,
                report.Server ?? "-",
                report.Offset.TotalMilliseconds.ToString("0.0"),
                report.Uncertainty.TotalMilliseconds.ToString("0.0"),
                report.Health);

            return report;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "NTP synchronization failed.");
            return TimeSyncReport.Failure(ex.Message, DateTimeOffset.UtcNow);
        }
    }

    private async Task<NtpSample?> QueryAsync(string server, CancellationToken cancellationToken)
    {
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(server, cancellationToken).ConfigureAwait(false);
            var address = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
                          ?? addresses.FirstOrDefault();
            if (address is null)
            {
                return null;
            }

            using var udp = new UdpClient(address.AddressFamily);
            udp.Connect(new IPEndPoint(address, 123));

            var request = new byte[48];
            request[0] = 0x1B; // LI = 0, VN = 3, Mode = 3 (client)

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_timeout);

            var sentAt = DateTimeOffset.UtcNow;
            var sw = Stopwatch.StartNew();
            await udp.SendAsync(request, cts.Token).ConfigureAwait(false);
            var response = await udp.ReceiveAsync(cts.Token).ConfigureAwait(false);
            sw.Stop();
            var receivedAt = DateTimeOffset.UtcNow;

            var parsed = ParseResponse(response.Buffer, sentAt, receivedAt);
            if (parsed is null)
            {
                return null;
            }

            var (offset, roundTrip, stratum) = parsed.Value;
            var rtt = (receivedAt - sentAt).Duration();

            _logger.LogDebug("{Server}: rtt={Rtt}ms offset={Offset}ms", server,
                rtt.TotalMilliseconds.ToString("0.0"), offset.TotalMilliseconds.ToString("0.0"));

            return new NtpSample(server, offset, roundTrip > TimeSpan.Zero ? roundTrip : rtt, stratum);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug(ex, "NTP query to {Server} failed.", server);
            return null;
        }
    }

    /// <summary>
    /// Parses a 48-byte SNTP reply and applies the standard offset/delay formulas.
    /// Field layout matters: receive = bytes 32-39, transmit = bytes 40-47.
    /// </summary>
    public static (TimeSpan Offset, TimeSpan RoundTrip, int Stratum)? ParseResponse(
        byte[] buffer,
        DateTimeOffset sentAt,
        DateTimeOffset receivedAt)
    {
        if (buffer.Length < 48 || buffer[0] == 0)
        {
            return null;
        }

        var stratum = buffer[1];
        if (stratum is 0 or > 15)
        {
            return null;
        }

        var receive = ReadTimestamp(buffer, 32);
        var transmit = ReadTimestamp(buffer, 40);
        if (receive is null || transmit is null)
        {
            return null;
        }

        var t1 = sentAt;
        var t2 = receive.Value;
        var t3 = transmit.Value;
        var t4 = receivedAt;

        var offset = ((t2 - t1) + (t3 - t4)) / 2;
        var roundTrip = (t4 - t1) - (t3 - t2);
        return (offset, roundTrip, stratum);
    }

    private static DateTimeOffset? ReadTimestamp(byte[] buffer, int offset)
    {
        var seconds = BinaryPrimitives.ReadUInt32BigEndian(new ReadOnlySpan<byte>(buffer, offset, 4));
        var fraction = BinaryPrimitives.ReadUInt32BigEndian(new ReadOnlySpan<byte>(buffer, offset + 4, 4));
        if (seconds == 0 && fraction == 0)
        {
            return null;
        }

        var milliseconds = fraction * 1000.0 / 4294967296.0;
        return NtpEpoch.AddSeconds(seconds).AddMilliseconds(milliseconds);
    }
}
