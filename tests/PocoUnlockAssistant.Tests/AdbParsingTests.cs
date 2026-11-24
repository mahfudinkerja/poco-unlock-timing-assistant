using PocoUnlockAssistant.Core.Device;
using Xunit;

namespace PocoUnlockAssistant.Tests;

public class AdbParsingTests
{
    private const string ReadyList = """
        List of devices attached
        4f8c9e1b	device usb:336592896X product:peridot model:2406APNCAA device:peridot transport_id:3
        """;

    [Fact]
    public void ParsesConnectedUsbDevice()
    {
        var devices = AdbOutputParser.ParseDeviceList(ReadyList);

        var device = Assert.Single(devices);
        Assert.Equal("4f8c9e1b", device.Serial);
        Assert.True(device.IsUsb);
        Assert.True(device.IsAuthorized);
        Assert.Equal("2406APNCAA", device.Model);
    }

    [Fact]
    public void NoDevice_IsReportedAsNotAttached()
    {
        var devices = AdbOutputParser.ParseDeviceList("List of devices attached\n");
        var info = AdbOutputParser.BuildDeviceInfo(devices, new Dictionary<string, string>(), null, null, "1.0.41");

        Assert.Empty(devices);
        Assert.Equal(AdbConnectionState.NotAttached, info.State);
        Assert.True(info.AdbAvailable);
        Assert.False(info.IsReady);
    }

    [Fact]
    public void UnauthorizedDevice_IsNotReady()
    {
        var devices = AdbOutputParser.ParseDeviceList("List of devices attached\nabc123\tunauthorized usb:1-1\n");
        var info = AdbOutputParser.BuildDeviceInfo(devices, new Dictionary<string, string>(), null, null, "1.0.41");

        Assert.Equal(AdbConnectionState.Unauthorized, info.State);
        Assert.Equal("Unauthorized", info.StateLabel);
        Assert.False(info.IsReady);
    }

    [Fact]
    public void OfflineDevice_IsNotReady()
    {
        var devices = AdbOutputParser.ParseDeviceList("List of devices attached\nabc123\toffline usb:1-1\n");
        var info = AdbOutputParser.BuildDeviceInfo(devices, new Dictionary<string, string>(), null, null, "1.0.41");

        Assert.Equal(AdbConnectionState.Offline, info.State);
        Assert.False(info.IsReady);
    }

    [Fact]
    public void AdbUnavailable_IsDistinctFromNoDevice()
    {
        var info = AdbDeviceInfo.Unavailable("adb not found");

        Assert.False(info.AdbAvailable);
        Assert.Equal(AdbConnectionState.AdbUnavailable, info.State);
        Assert.Contains("adb not found", info.Error);
    }

    [Fact]
    public void ParsesPropertiesAndBuildsDeviceInfo()
    {
        const string props = """
            [ro.product.model]: [2406APNCAA]
            [ro.product.marketname]: [POCO F7]
            [ro.build.version.release]: [15]
            [ro.mi.os.version.incremental]: [OS3.0.2.0.VOLIDXM]
            [ro.serialno]: [4f8c9e1b]
            [sys.usb.state]: [mtp,adb]
            """;

        var properties = AdbOutputParser.ParseProperties(props);
        var info = AdbOutputParser.BuildDeviceInfo(
            AdbOutputParser.ParseDeviceList(ReadyList), properties, 87, true, "1.0.41");

        Assert.Equal(AdbConnectionState.Ready, info.State);
        Assert.Equal("POCO F7", info.DisplayModel);
        Assert.Equal("2406APNCAA", info.Model);
        Assert.Equal("15", info.AndroidVersion);
        Assert.Equal("OS3.0.2.0.VOLIDXM", info.HyperOsVersion);
        Assert.Equal("mtp,adb", info.UsbState);
        Assert.Equal(87, info.BatteryLevel);
        Assert.True(info.ScreenOn);
        Assert.True(info.IsReady);
    }

    [Fact]
    public void MalformedPropertyLines_AreIgnored()
    {
        var properties = AdbOutputParser.ParseProperties("garbage\n[only-key]\n[key]: [value]\n");

        Assert.Single(properties);
        Assert.Equal("value", properties["key"]);
    }
}
