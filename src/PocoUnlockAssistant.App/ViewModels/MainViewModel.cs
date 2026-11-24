using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using PocoUnlockAssistant.Core.Abstractions;
using PocoUnlockAssistant.Core.Automation;
using PocoUnlockAssistant.Core.Configuration;
using PocoUnlockAssistant.Core.Device;
using PocoUnlockAssistant.Core.Persistence;
using PocoUnlockAssistant.Core.Scheduling;
using PocoUnlockAssistant.Core.Time;
using PocoUnlockAssistant.Infrastructure.Adb;
using PocoUnlockAssistant.Infrastructure.Configuration;

namespace PocoUnlockAssistant.App.ViewModels;

public enum StatusKind
{
    Idle,
    Ok,
    Warn,
    Error,
    Accent,
}

public sealed record ReadyCheckRow(string Name, string Detail, bool Ok, string Marker);

public sealed record AttemptRow(
    string Scheduled,
    string Jakarta,
    string Result,
    string State,
    string Confidence,
    string Mode,
    string Offset,
    string Uncertainty,
    string Evidence,
    string Note);

public partial class MainViewModel : ObservableObject, IDisposable
{
    private const int MaxLogLines = 400;

    private readonly SubmissionCoordinator _coordinator;
    private readonly SettingsStore _settingsStore;
    private readonly IAttemptStore _store;
    private readonly IAdbClient _adb;
    private readonly FastbootClient _fastboot = new();
    private readonly ILogger<MainViewModel> _logger;
    private readonly DispatcherTimer _timer;

    private CancellationTokenSource? _scheduleCts;
    private DateTimeOffset? _armedTarget;
    private DateTimeOffset _cachedTarget;
    private int _lastSecond = -1;
    private bool _disposed;

    public MainViewModel(
        SubmissionCoordinator coordinator,
        AssistantSettings settings,
        SettingsStore settingsStore,
        IAttemptStore store,
        IAdbClient adb,
        ILogger<MainViewModel> logger)
    {
        _coordinator = coordinator;
        Settings = settings;
        _settingsStore = settingsStore;
        _store = store;
        _adb = adb;
        _logger = logger;

        NavItems = ["Dashboard", "Execution history", "Settings", "Diagnostics", "Mi Unlock Tool", "About"];

        _coordinator.StatusChanged += OnStatusChanged;

        _timer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(50),
        };
        _timer.Tick += OnTick;
        _timer.Start();

        OnTick(this, EventArgs.Empty);
        _ = InitializeAsync();
    }

    public AssistantSettings Settings { get; }

    public IReadOnlyList<string> NavItems { get; }

    public ObservableCollection<ReadyCheckRow> ReadinessChecks { get; } = [];

    public ObservableCollection<AttemptRow> History { get; } = [];

    public ObservableCollection<string> LogLines { get; } = [];

    // ---- header / clocks -------------------------------------------------
    [ObservableProperty] private string _beijingTime = "--:--:--";
    [ObservableProperty] private string _beijingDate = "—";
    [ObservableProperty] private string _jakartaTime = "--:--:--";
    [ObservableProperty] private string _jakartaDate = "—";
    [ObservableProperty] private string _localTime = "--:--:--";
    [ObservableProperty] private string _localDate = "—";

    // ---- countdown -------------------------------------------------------
    [ObservableProperty] private string _countdownDays = "00";
    [ObservableProperty] private string _countdownHours = "00";
    [ObservableProperty] private string _countdownMinutes = "00";
    [ObservableProperty] private string _countdownSeconds = "00";
    [ObservableProperty] private string _countdownMillis = "000";
    [ObservableProperty] private string _targetLabel = "Next 00:00:00 Asia/Shanghai";
    [ObservableProperty] private string _targetWibLabel = "—";

    // ---- execution status ------------------------------------------------
    [ObservableProperty] private string _stateLabel = "IDLE";
    [ObservableProperty] private StatusKind _stateKind = StatusKind.Idle;
    [ObservableProperty] private string _modeLabel = "Controlled ADB automation";
    [ObservableProperty] private string _communityStatus = "Not checked";
    [ObservableProperty] private StatusKind _communityKind = StatusKind.Idle;
    [ObservableProperty] private string _readinessLabel = "Not checked";
    [ObservableProperty] private StatusKind _readinessKind = StatusKind.Idle;
    [ObservableProperty] private string _syncLabel = "Not synchronized";
    [ObservableProperty] private StatusKind _syncKind = StatusKind.Idle;

    [ObservableProperty] private string _ntpServer = "—";
    [ObservableProperty] private string _offsetLabel = "—";
    [ObservableProperty] private string _uncertaintyLabel = "—";
    [ObservableProperty] private string _latencyLabel = "—";
    [ObservableProperty] private string _lastSyncLabel = "Never";
    [ObservableProperty] private string _evidenceDirectory = AppPaths.EvidenceDirectory;

    // ---- device ----------------------------------------------------------
    [ObservableProperty] private string _deviceModel = "POCO F7";
    [ObservableProperty] private string _deviceDetail = "Not connected";
    [ObservableProperty] private string _hyperOsVersion = "—";
    [ObservableProperty] private string _androidVersion = "—";
    [ObservableProperty] private string _adbStatus = "Not checked";
    [ObservableProperty] private StatusKind _adbKind = StatusKind.Idle;
    [ObservableProperty] private string _usbStatus = "—";
    [ObservableProperty] private string _batteryStatus = "—";
    [ObservableProperty] private string _screenStatus = "—";
    [ObservableProperty] private string _serialNumber = "—";

    // ---- operational -----------------------------------------------------
    [ObservableProperty] private string _statusMessage = "Starting…";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _schedulerActive;
    [ObservableProperty] private bool _emergencyStopActive;
    [ObservableProperty] private int _selectedSection;

    // ---- diagnostics -----------------------------------------------------
    [ObservableProperty] private string _diagnosticScreen = "—";
    [ObservableProperty] private string _diagnosticButton = "—";
    [ObservableProperty] private string _diagnosticNodeCount = "0";
    [ObservableProperty] private string _diagnosticScreenshot = "—";
    [ObservableProperty] private string _fastbootStatus = "Not checked";

    // ---- Mi Unlock -------------------------------------------------------
    [ObservableProperty] private string _miUnlockToolPath = string.Empty;

    public bool IsDashboard => SelectedSection == 0;
    public bool IsHistory => SelectedSection == 1;
    public bool IsSettings => SelectedSection == 2;
    public bool IsDiagnostics => SelectedSection == 3;
    public bool IsMiUnlock => SelectedSection == 4;
    public bool IsAbout => SelectedSection == 5;

    public string AppVersion => "0.1.0";

    public string AboutText =>
        "POCO Unlock Timing Assistant prepares and submits the official Xiaomi Community " +
        "bootloader-unlock authorization request at 00:00:00 Asia/Shanghai (23:00 WIB the previous day). " +
        "It never bypasses Xiaomi security, CAPTCHA, account verification or quota rules, and it never " +
        "reports authorization as granted without an explicit Xiaomi response.";

    partial void OnSelectedSectionChanged(int value)
    {
        OnPropertyChanged(nameof(IsDashboard));
        OnPropertyChanged(nameof(IsHistory));
        OnPropertyChanged(nameof(IsSettings));
        OnPropertyChanged(nameof(IsDiagnostics));
        OnPropertyChanged(nameof(IsMiUnlock));
        OnPropertyChanged(nameof(IsAbout));
    }

    partial void OnEmergencyStopActiveChanged(bool value) =>
        StatusMessage = value
            ? "EMERGENCY STOP active. No submission will be attempted until it is cleared."
            : "Emergency stop cleared.";

    // ---- timer -----------------------------------------------------------
    private void OnTick(object? sender, EventArgs e)
    {
        var now = _coordinator.Now;

        if (now.Second != _lastSecond)
        {
            _lastSecond = now.Second;
            UpdateClocks(now);
        }

        if (_cachedTarget == default || now > _cachedTarget)
        {
            _cachedTarget = _coordinator.Target;
            TargetLabel = $"Next {Settings.TargetHour:00}:{Settings.TargetMinute:00}:00 Asia/Shanghai";
        }

        var target = _armedTarget ?? _cachedTarget;
        var remaining = target - now;
        if (remaining < TimeSpan.Zero)
        {
            remaining = TimeSpan.Zero;
        }

        CountdownDays = ((int)remaining.TotalDays).ToString("00");
        CountdownHours = remaining.Hours.ToString("00");
        CountdownMinutes = remaining.Minutes.ToString("00");
        CountdownSeconds = remaining.Seconds.ToString("00");
        CountdownMillis = remaining.Milliseconds.ToString("000");

        TargetWibLabel = $"{TargetTimeCalculator.FormatWib(target)} · Beijing {TargetTimeCalculator.FormatBeijing(target)}";
    }

    private void UpdateClocks(DateTimeOffset now)
    {
        var beijing = TargetTimeCalculator.ToBeijing(now);
        BeijingTime = beijing.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        BeijingDate = beijing.ToString("dddd, dd MMM yyyy", CultureInfo.InvariantCulture) + " · CST (UTC+8)";

        var jakarta = TargetTimeCalculator.ToJakarta(now);
        JakartaTime = jakarta.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        JakartaDate = jakarta.ToString("dddd, dd MMM yyyy", CultureInfo.InvariantCulture) + " · WIB (UTC+7)";

        var local = now.ToLocalTime();
        LocalTime = local.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        LocalDate = $"{local:dddd, dd MMM yyyy} · {TimeZoneInfo.Local.Id}";
    }

    // ---- initialization --------------------------------------------------
    private async Task InitializeAsync()
    {
        try
        {
            StateLabel = _coordinator.Machine.Current.ToString().ToUpperInvariant();
            ModeLabel = Settings.ExecutionMode switch
            {
                ExecutionMode.ControlledAdb => "Controlled ADB automation",
                ExecutionMode.ManualAssisted => "Manual assisted fallback",
                _ => "Monitoring only",
            };

            await RefreshHistoryAsync();
            await SyncTimeAsync();
            await ConnectDeviceAsync();
            StatusMessage = "Ready. Connect the POCO F7, verify readiness, then arm the scheduler.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Initialization failed.");
            StatusMessage = $"Initialization problem: {ex.Message}";
        }
    }

    // ---- device ----------------------------------------------------------
    [RelayCommand]
    private async Task ConnectDeviceAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = "Probing the device over ADB…";
        try
        {
            var info = await _coordinator.ProbeDeviceAsync();
            ApplyDevice(info);
            StatusMessage = info.IsReady
                ? $"{info.DisplayModel} connected and authorized."
                : $"Device not ready — {info.StateLabel}. {info.Error ?? string.Empty}".Trim();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Device probe failed.");
            StatusMessage = $"Device probe failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyDevice(AdbDeviceInfo info)
    {
        DeviceModel = info.IsReady ? info.DisplayModel : "POCO F7";
        DeviceDetail = info.IsReady
            ? $"{info.DisplayModel} · {info.SerialNumber}"
            : $"{info.StateLabel} — connect over USB with USB debugging authorized";
        HyperOsVersion = info.HyperOsVersion ?? "—";
        AndroidVersion = info.AndroidVersion ?? "—";
        AdbStatus = info.StateLabel;
        AdbKind = info.State switch
        {
            AdbConnectionState.Ready => StatusKind.Ok,
            AdbConnectionState.AdbUnavailable => StatusKind.Error,
            AdbConnectionState.NotAttached => StatusKind.Idle,
            _ => StatusKind.Warn,
        };
        UsbStatus = info.UsbState ?? (info.Devices.Any(d => d.IsUsb) ? "USB attached" : "Not detected");
        BatteryStatus = info.BatteryLevel is { } level ? $"{level}%" : "—";
        ScreenStatus = info.ScreenOn switch
        {
            true => "Awake",
            false => "Off",
            null => "—",
        };
        SerialNumber = info.SerialNumber ?? "—";
    }

    // ---- time ------------------------------------------------------------
    [RelayCommand]
    private async Task SyncTimeAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = "Synchronizing time against NTP…";
        try
        {
            var report = await _coordinator.SynchronizeAsync();
            ApplySync(report);
            StatusMessage = report.Ok
                ? $"NTP synchronized with {report.Server}."
                : $"NTP failed: {report.FailureReason}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "NTP synchronization failed.");
            StatusMessage = $"NTP failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplySync(TimeSyncReport report)
    {
        if (!report.Ok)
        {
            SyncLabel = "NTP unavailable";
            SyncKind = StatusKind.Error;
            NtpServer = "—";
            return;
        }

        SyncLabel = report.Health switch
        {
            SyncHealth.Healthy => "Healthy",
            SyncHealth.Degraded => "Degraded",
            _ => "Unreliable",
        };
        SyncKind = report.Health switch
        {
            SyncHealth.Healthy => StatusKind.Ok,
            SyncHealth.Degraded => StatusKind.Warn,
            _ => StatusKind.Error,
        };

        NtpServer = $"{report.Server} · stratum {report.Stratum}";
        OffsetLabel = $"{report.Offset.TotalMilliseconds:+0;-0;0} ms";
        UncertaintyLabel = $"±{report.Uncertainty.TotalMilliseconds:0} ms";
        LatencyLabel = $"{report.BestRoundTrip.TotalMilliseconds:0} ms";
        LastSyncLabel = report.MeasuredAtUtc.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);
    }

    // ---- readiness -------------------------------------------------------
    [RelayCommand]
    private async Task CheckReadinessAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = "Running readiness checks…";
        try
        {
            var report = await _coordinator.PrepareAsync();
            ApplyReadiness(report);
            ApplySync(_coordinator.LastSync);
            ApplyDevice(_coordinator.LastDevice);
            ReadinessLabel = report.Ready ? "Ready" : "Not ready";
            ReadinessKind = report.Ready ? StatusKind.Ok : StatusKind.Warn;
            StatusMessage = report.Summary;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Readiness checks failed.");
            StatusMessage = $"Readiness failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyReadiness(PreparationReport report)
    {
        ReadinessChecks.Clear();
        foreach (var step in report.Steps)
        {
            ReadinessChecks.Add(new ReadyCheckRow(step.Name, step.Detail, step.Ok, step.Ok ? "PASS" : "FAIL"));
        }
    }

    [RelayCommand]
    private async Task OpenCommunityAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var launched = await _adb.LaunchAppAsync(Settings.CommunityPackageId);
            CommunityStatus = launched ? "Launched — sign in manually" : "Launch failed";
            CommunityKind = launched ? StatusKind.Ok : StatusKind.Warn;
            StatusMessage = launched
                ? "Xiaomi Community launched. Sign in manually, then open Me → Unlock Bootloader."
                : $"Could not launch {Settings.CommunityPackageId}. Install Xiaomi Community and connect the device.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Launching Xiaomi Community failed.");
            StatusMessage = $"Could not launch Xiaomi Community: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task PrepareAutomationAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = "Preparing automation (T-10 readiness pass)…";
        try
        {
            var report = await _coordinator.PrepareAsync();
            ApplyReadiness(report);
            ApplySync(_coordinator.LastSync);
            ApplyDevice(_coordinator.LastDevice);
            CommunityStatus = _coordinator.CommunityVersion is { } version
                ? $"Ready · {version}"
                : "Not detected";
            CommunityKind = _coordinator.CommunityVersion is null ? StatusKind.Warn : StatusKind.Ok;
            ReadinessLabel = report.Ready ? "Ready" : "Not ready";
            ReadinessKind = report.Ready ? StatusKind.Ok : StatusKind.Warn;

            if (report.Ready && _coordinator.LastDevice.IsReady)
            {
                await _adb.LaunchAppAsync(Settings.CommunityPackageId);
                StatusMessage = "Xiaomi Community opened. Navigate to Me → Unlock Bootloader, then check readiness again.";
            }
            else
            {
                StatusMessage = report.Summary;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Automation preparation failed.");
            StatusMessage = $"Preparation failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ---- scheduler -------------------------------------------------------
    [RelayCommand]
    private async Task ArmSchedulerAsync()
    {
        if (IsBusy || SchedulerActive)
        {
            return;
        }

        IsBusy = true;
        try
        {
            if (EmergencyStopActive)
            {
                StatusMessage = "Clear the emergency stop before arming.";
                return;
            }

            var record = await _coordinator.ArmAsync();
            _armedTarget = record.ScheduledBeijing;
            SchedulerActive = true;
            StateLabel = _coordinator.Machine.Current.ToString().ToUpperInvariant();
            StateKind = StatusKind.Accent;

            _scheduleCts = new CancellationTokenSource();
            StatusMessage =
                $"Armed for {TargetTimeCalculator.FormatBeijing(record.ScheduledBeijing)} " +
                $"({TargetTimeCalculator.FormatWib(record.ScheduledBeijing)}). Keep the PC awake.";

            _ = RunScheduleAsync(_scheduleCts.Token);
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            _logger.LogWarning(ex, "Arming failed.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RunScheduleAsync(CancellationToken cancellationToken)
    {
        try
        {
            var outcome = await _coordinator.WaitAndExecuteAsync(cancellationToken);
            Dispatch(() =>
            {
                StateLabel = _coordinator.Machine.Current.ToString().ToUpperInvariant();
                StateKind = outcome.ActionSent ? StatusKind.Accent : StatusKind.Warn;
                StatusMessage = $"{outcome.Classification.Result} ({outcome.Classification.Confidence}) — " +
                                outcome.Classification.Evidence;
            });
        }
        catch (OperationCanceledException)
        {
            Dispatch(() => StatusMessage = "Scheduler cancelled by operator.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Scheduled execution failed.");
            Dispatch(() =>
            {
                StateLabel = SubmissionState.Failed.ToString().ToUpperInvariant();
                StateKind = StatusKind.Error;
                StatusMessage = $"Scheduled execution failed: {ex.Message}";
            });
        }
        finally
        {
            _armedTarget = null;
            Dispatch(() =>
            {
                SchedulerActive = false;
                StateLabel = _coordinator.Machine.Current.ToString().ToUpperInvariant();
            });
            await RefreshHistoryAsync();
        }
    }

    [RelayCommand]
    private async Task CancelSchedulerAsync()
    {
        try
        {
            _scheduleCts?.Cancel();
            await _coordinator.CancelAsync();
            StateLabel = _coordinator.Machine.Current.ToString().ToUpperInvariant();
            StateKind = StatusKind.Warn;
            StatusMessage = "Scheduler cancelled. No submission will be sent.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Cancel failed: {ex.Message}";
        }
        finally
        {
            SchedulerActive = false;
            _armedTarget = null;
        }
    }

    [RelayCommand]
    private void TriggerEmergencyStop()
    {
        _coordinator.EmergencyStop();
        EmergencyStopActive = true;
        _scheduleCts?.Cancel();
        StateKind = StatusKind.Error;
        StateLabel = "EMERGENCY STOP";
        SchedulerActive = false;
    }

    [RelayCommand]
    private void ClearEmergencyStop()
    {
        _coordinator.ClearEmergencyStop();
        EmergencyStopActive = false;
        StateLabel = _coordinator.Machine.Current.ToString().ToUpperInvariant();
        StateKind = StatusKind.Idle;
    }

    // ---- history / export ------------------------------------------------
    private async Task RefreshHistoryAsync()
    {
        try
        {
            var records = await _store.GetRecentAsync(100);
            Dispatch(() =>
            {
                History.Clear();
                foreach (var record in records)
                {
                    History.Add(new AttemptRow(
                        TargetTimeCalculator.FormatBeijing(record.ScheduledBeijing),
                        TargetTimeCalculator.FormatWib(record.ScheduledJakarta),
                        record.Result?.ToString() ?? "—",
                        record.State.ToString(),
                        record.Confidence?.ToString() ?? "—",
                        record.Mode.ToString(),
                        record.NtpOffsetMs is { } offset ? $"{offset:+0;-0;0} ms" : "—",
                        record.TimeUncertaintyMs is { } unc ? $"±{unc:0} ms" : "—",
                        string.IsNullOrWhiteSpace(record.ScreenshotPath) ? "—" : "Saved",
                        record.ErrorMessage ?? "—"));
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load execution history.");
        }
    }

    [RelayCommand]
    private async Task ExportCsvAsync()
    {
        try
        {
            var path = await _store.ExportCsvAsync();
            StatusMessage = $"CSV exported to {path}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"CSV export failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ExportJsonAsync()
    {
        try
        {
            var path = await _store.ExportJsonAsync();
            StatusMessage = $"JSON exported to {path}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"JSON export failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void OpenEvidenceFolder() => OpenFolder(AppPaths.EvidenceDirectory);

    [RelayCommand]
    private void OpenLogFolder() => OpenFolder(AppPaths.LogsDirectory);

    // ---- diagnostics -----------------------------------------------------
    [RelayCommand]
    private async Task InspectScreenAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = "Inspecting the current device screen…";
        try
        {
            var device = _coordinator.LastDevice;
            var capture = await _coordinator.InspectScreenAsync(device.SerialNumber);
            DiagnosticScreen = capture.Match.Kind.ToString();
            DiagnosticNodeCount = capture.NodeCount.ToString(CultureInfo.InvariantCulture);
            DiagnosticButton = capture.ButtonText ?? "Not found";
            DiagnosticScreenshot = capture.ScreenshotPath ?? "Not saved";
            StatusMessage = $"Screen recognition: {capture.Match.Kind} ({capture.Match.Confidence}).";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Screen inspection failed.");
            StatusMessage = $"Screen inspection failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void CheckFastboot()
    {
        var ok = _fastboot.TryListDevices(out var output, out var error);
        var hasDevice = ok && output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Any(l => l.Contains("fastboot", StringComparison.OrdinalIgnoreCase));
        FastbootStatus = hasDevice
            ? "Fastboot device detected"
            : ok
                ? "No fastboot device (normal while Android is running)"
                : error ?? "fastboot not available";
        StatusMessage = FastbootStatus;
    }

    // ---- settings --------------------------------------------------------
    [RelayCommand]
    private void SaveSettings()
    {
        Settings.TargetHour = Math.Clamp(Settings.TargetHour, 0, 23);
        Settings.TargetMinute = Math.Clamp(Settings.TargetMinute, 0, 59);
        Settings.MaxUncertaintyMs = Math.Max(10, Settings.MaxUncertaintyMs);
        _coordinator.Settings = Settings;
        _settingsStore.Save(Settings);
        _cachedTarget = default;
        StatusMessage = "Settings saved. Target time is applied to the next arm.";
    }

    // ---- Mi Unlock Tool --------------------------------------------------
    [RelayCommand]
    private void BrowseMiUnlockTool()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select the official Mi Unlock Tool",
            Filter = "Executables (*.exe)|*.exe|All files (*.*)|*.*",
            CheckFileExists = true,
        };

        if (dialog.ShowDialog() == true)
        {
            MiUnlockToolPath = dialog.FileName;
        }
    }

    [RelayCommand]
    private void LaunchMiUnlockTool()
    {
        if (string.IsNullOrWhiteSpace(MiUnlockToolPath) || !File.Exists(MiUnlockToolPath))
        {
            StatusMessage = "Select the official Mi Unlock Tool executable first.";
            return;
        }

        var confirm = MessageBox.Show(
            "Bootloader unlocking normally ERASES ALL USER DATA on the device.\n\n" +
            "The waiting period cannot be bypassed, and the unlock must be confirmed on the phone.\n\n" +
            "Launch the official Mi Unlock Tool now?",
            "Destructive action confirmation",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes)
        {
            StatusMessage = "Mi Unlock Tool launch cancelled.";
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(MiUnlockToolPath) { UseShellExecute = true });
            StatusMessage = "Mi Unlock Tool launched. Follow the official binding and waiting-period steps.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not launch Mi Unlock Tool: {ex.Message}";
        }
    }

    [RelayCommand]
    private void OpenXiaomiDownloadPage() =>
        OpenExternal("https://en.miui.com/unlock/");

    // ---- helpers ---------------------------------------------------------
    private void OnStatusChanged(string message)
    {
        Dispatch(() =>
        {
            StatusMessage = message;
            AppendLog(message);
            StateLabel = _coordinator.Machine.Current.ToString().ToUpperInvariant();
        });
    }

    private void AppendLog(string message)
    {
        LogLines.Insert(0, $"{DateTimeOffset.Now:HH:mm:ss.fff}  {message}");
        while (LogLines.Count > MaxLogLines)
        {
            LogLines.RemoveAt(LogLines.Count - 1);
        }
    }

    private static void Dispatch(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.BeginInvoke(action);
        }
    }

    private static void OpenExternal(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // Opening a browser is best effort.
        }
    }

    private static void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch
        {
            // Best effort.
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Stop();
        _timer.Tick -= OnTick;
        _coordinator.StatusChanged -= OnStatusChanged;
        _scheduleCts?.Cancel();
        _scheduleCts?.Dispose();
    }
}
