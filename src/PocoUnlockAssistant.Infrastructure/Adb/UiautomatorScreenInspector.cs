using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PocoUnlockAssistant.Core.Abstractions;
using PocoUnlockAssistant.Core.Automation;

namespace PocoUnlockAssistant.Infrastructure.Adb;

/// <summary>
/// Captures the visible accessibility hierarchy through UIAutomator and, when evidence
/// capture is enabled, a PNG screenshot. The hierarchy — not old coordinates — is what
/// the recognizer works from.
/// </summary>
public sealed partial class UiautomatorScreenInspector : IScreenInspector
{
    private const string DumpPath = "/sdcard/window_dump.xml";

    private readonly IAdbClient _adb;
    private readonly string _evidenceDirectory;
    private readonly ILogger<UiautomatorScreenInspector> _logger;

    public UiautomatorScreenInspector(
        IAdbClient adb,
        string evidenceDirectory,
        ILogger<UiautomatorScreenInspector>? logger = null)
    {
        _adb = adb;
        _evidenceDirectory = evidenceDirectory;
        _logger = logger ?? NullLogger<UiautomatorScreenInspector>.Instance;
    }

    public async Task<ScreenCapture> CaptureAsync(
        string? serial,
        bool saveScreenshot,
        CancellationToken cancellationToken = default)
    {
        var prefix = string.IsNullOrWhiteSpace(serial) ? string.Empty : $"-s {serial} ";

        var dump = await _adb.ExecuteAsync($"{prefix}shell uiautomator dump {DumpPath}", cancellationToken)
            .ConfigureAwait(false);
        if (!dump.Ok)
        {
            return ScreenCapture.Failure($"uiautomator dump failed: {dump.StandardError.Trim()}", DateTimeOffset.UtcNow);
        }

        var xml = await _adb.ExecuteAsync($"{prefix}exec-out cat {DumpPath}", cancellationToken)
            .ConfigureAwait(false);
        if (!xml.Ok)
        {
            return ScreenCapture.Failure($"Reading {DumpPath} failed: {xml.StandardError.Trim()}", DateTimeOffset.UtcNow);
        }

        var nodes = ParseHierarchy(xml.StandardOutput);
        if (nodes.Count == 0)
        {
            return ScreenCapture.Failure("UI hierarchy was empty or unparsable.", DateTimeOffset.UtcNow);
        }

        string? screenshotPath = null;
        if (saveScreenshot)
        {
            screenshotPath = await CaptureScreenshotAsync(prefix, cancellationToken).ConfigureAwait(false);
        }

        return new ScreenCapture(true, nodes, screenshotPath, DateTimeOffset.UtcNow, null);
    }

    private async Task<string?> CaptureScreenshotAsync(string prefix, CancellationToken cancellationToken)
    {
        var png = await _adb.ExecuteBinaryAsync($"{prefix}exec-out screencap -p", cancellationToken)
            .ConfigureAwait(false);
        if (png is null || png.Length < 8)
        {
            _logger.LogWarning("Screenshot capture returned no data.");
            return null;
        }

        try
        {
            Directory.CreateDirectory(_evidenceDirectory);
            var path = Path.Combine(
                _evidenceDirectory,
                $"screen-{DateTimeOffset.Now:yyyyMMdd-HHmmss-fff}.png");
            await File.WriteAllBytesAsync(path, png, cancellationToken).ConfigureAwait(false);
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not save the screenshot evidence file.");
            return null;
        }
    }

    /// <summary>Parses a UIAutomator XML dump into normalized nodes.</summary>
    public static IReadOnlyList<UiNode> ParseHierarchy(string xml)
    {
        var nodes = new List<UiNode>();
        if (string.IsNullOrWhiteSpace(xml))
        {
            return nodes;
        }

        var start = xml.IndexOf('<');
        var end = xml.LastIndexOf('>');
        if (start < 0 || end <= start)
        {
            return nodes;
        }

        XDocument document;
        try
        {
            document = XDocument.Parse(xml[start..(end + 1)]);
        }
        catch (System.Xml.XmlException)
        {
            return nodes;
        }

        foreach (var element in document.Descendants().Where(e => e.Name.LocalName == "node"))
        {
            var bounds = ParseBounds((string?)element.Attribute("bounds"));
            nodes.Add(new UiNode(
                Text: ((string?)element.Attribute("text")) ?? string.Empty,
                ResourceId: ((string?)element.Attribute("resource-id")) ?? string.Empty,
                ContentDescription: ((string?)element.Attribute("content-desc")) ?? string.Empty,
                ClassName: ((string?)element.Attribute("class")) ?? string.Empty,
                Enabled: string.Equals((string?)element.Attribute("enabled"), "true", StringComparison.OrdinalIgnoreCase),
                Clickable: string.Equals((string?)element.Attribute("clickable"), "true", StringComparison.OrdinalIgnoreCase),
                Bounds: bounds));
        }

        return nodes;
    }

    public static UiBounds ParseBounds(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return default;
        }

        var match = BoundsRegex().Match(raw);
        if (!match.Success)
        {
            return default;
        }

        var values = match.Groups.Cast<Group>().Skip(1)
            .Select(g => int.TryParse(g.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0)
            .ToArray();

        return new UiBounds(values[0], values[1], Math.Max(0, values[2] - values[0]), Math.Max(0, values[3] - values[1]));
    }

    [GeneratedRegex(@"\[(-?\d+),(-?\d+)\]\[(-?\d+),(-?\d+)\]")]
    private static partial Regex BoundsRegex();
}
