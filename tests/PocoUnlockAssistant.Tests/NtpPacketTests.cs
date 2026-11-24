using System.Buffers.Binary;
using PocoUnlockAssistant.Infrastructure.Time;
using Xunit;

namespace PocoUnlockAssistant.Tests;

/// <summary>
/// Locks the SNTP wire format. A regression here previously made every valid reply
/// look unusable because the wrong timestamp field was read.
/// </summary>
public class NtpPacketTests
{
    private static byte[] BuildReply(DateTimeOffset receive, DateTimeOffset transmit, byte stratum = 2)
    {
        var buffer = new byte[48];
        buffer[0] = 0x1C; // LI = 0, VN = 3, Mode = 4 (server)
        buffer[1] = stratum;
        WriteTimestamp(buffer, 32, receive);
        WriteTimestamp(buffer, 40, transmit);
        return buffer;
    }

    private static void WriteTimestamp(byte[] buffer, int offset, DateTimeOffset value)
    {
        var sinceEpoch = value.ToUniversalTime() - NtpClient.NtpEpoch;
        var seconds = (uint)sinceEpoch.TotalSeconds;
        var fraction = (uint)((sinceEpoch.TotalSeconds - seconds) * 4294967296.0);
        BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(offset, 4), seconds);
        BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(offset + 4, 4), fraction);
    }

    [Fact]
    public void ParsesAValidReplyAndComputesOffset()
    {
        var sentAt = new DateTimeOffset(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);
        var receivedAt = sentAt.AddMilliseconds(40);
        var serverNow = sentAt.AddMilliseconds(500); // server is 500 ms ahead

        var buffer = BuildReply(
            receive: serverNow.AddMilliseconds(-5),
            transmit: serverNow.AddMilliseconds(5));

        var parsed = NtpClient.ParseResponse(buffer, sentAt, receivedAt);

        Assert.NotNull(parsed);
        var (offset, roundTrip, stratum) = parsed!.Value;
        Assert.Equal(2, stratum);
        // ((t2 - t1) + (t3 - t4)) / 2 = ((495) + (505 - 40)) / 2 = 480 ms
        Assert.InRange(offset.TotalMilliseconds, 475, 485);
        // (t4 - t1) - (t3 - t2) = 40 - 10 = 30 ms
        Assert.InRange(roundTrip.TotalMilliseconds, 28, 32);
    }

    [Fact]
    public void RejectsRepliesWithoutTimestamps()
    {
        var buffer = new byte[48];
        buffer[0] = 0x1C;
        buffer[1] = 2;

        Assert.Null(NtpClient.ParseResponse(buffer, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void RejectsKissOfDeathAndShortPackets()
    {
        var kiss = new byte[48];
        kiss[0] = 0x1C;
        kiss[1] = 0; // stratum 0 = kiss-o'-death

        Assert.Null(NtpClient.ParseResponse(kiss, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        Assert.Null(NtpClient.ParseResponse(new byte[20], DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        Assert.Null(NtpClient.ParseResponse([], DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
    }
}
