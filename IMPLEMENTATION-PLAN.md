# Implementation plan and status

Stack the specification mandates but the environment cannot run: **C# 13 / .NET 10 / WPF / MVVM**.
Build host used here is macOS, so the WPF project sets `EnableWindowsTargeting` (compile) while the
UI itself remains Windows-only (run). Suites that do not need Windows run on this host.

Phases are listed in the order they were executed. Items marked ⚠️ need a Windows host and/or the
physical POCO F7 — see KNOWN-LIMITATIONS.

| Phase | Scope | Status |
| --- | --- | --- |
| 1 | Research current Xiaomi requirements, validate POCO F7 compatibility | ⚠️ Design follows the official Xiaomi Community flow; current Xiaomi policy, the app package id and the real screen hierarchy were **not** verified against a device |
| 2 | .NET solution and architecture | ✅ `PocoUnlockAssistant.sln`: Core / Infrastructure / App / Tests, DI composition root, Clean-Architecture boundaries |
| 3 | NTP synchronization and precision countdown | ✅ multi-server SNTP, median consensus, uncertainty, disagreement detection, monotonic clock, adaptive countdown; live-verified and unit-tested |
| 4 | ADB device detection and monitoring | ✅ `AdbClient` (5 s/20 s timeouts, structured results), 15 s UI polling, model/HyperOS/battery/screen parsing; ⚠️ connected path unverified |
| 5 | Xiaomi Community screen detection | ✅ configurable `ScreenRecognitionProfile` + recognizer + diagnostics inspector; ⚠️ real hierarchy unverified |
| 6 | Controlled UI automation and state machine | ✅ 22-state deterministic machine, single-action lock, final gate with 10 conditions, response classification, SQLite duplicate protection |
| 7 | Windows dashboard | ✅ AMOLED theme, header, device card, time card, ms countdown, execution status, 7 required buttons + Sync NTP + Clear stop, history/settings/diagnostics/Mi Unlock/about sections; ⚠️ never rendered |
| 8 | Database and audit history | ✅ SQLite schema with all specified columns, CSV/JSON export, restart-safe duplicate check |
| 9 | Mi Unlock Tool assistance | ✅ section with mandatory data-loss warning, fastboot check, browse/launch with confirmation, official download link, binding and waiting-period instructions; ⚠️ fastboot absent on this host |
| 10 | Comprehensive QA and real-device verification | ⚠️ 60 automated tests pass; real-device verification impossible here |
| 11 | Production installer and portable executable | ⚠️ portable self-contained `win-x64` single-file exe published; installer not produced (needs WiX/Inno Setup on Windows) |

## Files created

```
PocoUnlockAssistant.sln, Directory.Build.props, .gitignore, README.md
ARCHITECTURE.md, IMPLEMENTATION-PLAN.md, QA-REPORT.md, KNOWN-LIMITATIONS.md, CHANGELOG.md

src/PocoUnlockAssistant.Core/
  Time/ZoneCatalog.cs  Time/TargetTimeCalculator.cs  Time/TimeSyncReport.cs  Time/MonotonicClock.cs
  Device/AdbModels.cs
  Automation/SubmissionStateMachine.cs  Automation/ScreenRecognition.cs
  Automation/ResultClassifier.cs  Automation/PreExecutionValidator.cs
  Scheduling/SubmissionCoordinator.cs
  Persistence/AttemptRecord.cs
  Abstractions/Contracts.cs
  Configuration/AssistantSettings.cs

src/PocoUnlockAssistant.Infrastructure/
  Time/NtpClient.cs
  Adb/AdbClient.cs  Adb/UiautomatorScreenInspector.cs  Adb/FastbootClient.cs
  Persistence/SqliteAttemptStore.cs
  Configuration/AppPaths.cs  Configuration/SettingsStore.cs
  Logging/FileLoggerProvider.cs

src/PocoUnlockAssistant.App/
  App.xaml(.cs)  MainWindow.xaml(.cs)  app.manifest
  Theme/Theme.xaml
  Converters/Converters.cs  Converters/EnumToBoolConverter.cs
  ViewModels/MainViewModel.cs

tests/PocoUnlockAssistant.Tests/
  TimeTests.cs  AdbParsingTests.cs  AutomationTests.cs  SchedulerTests.cs  NtpPacketTests.cs  Fakes.cs
```

The earlier Electron prototype was moved to `legacy/electron-prototype/` (kept for reference, not
built, not part of the solution).

## Build and test results

- `dotnet build PocoUnlockAssistant.sln` → succeeded, 0 C# warnings, 0 errors.
- `dotnet test` → 60 passed, 0 failed (4 s).
- `dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true` → succeeded.
- Live: NTP `Healthy` (3 responders, ±2.7–8.3 ms uncertainty); ADB detected, no device (expected).

## Remaining work (in order)

1. Run the app on Windows 10/11: verify layout, DPI, fonts, countdown smoothness, all buttons.
2. Attach the POCO F7: verify model/HyperOS/serial/battery/screen, then
   `Diagnostics → Inspect current screen` and correct `screen-profile.json`.
3. Do a monitored dry run (Monitoring mode) at a non-target time to confirm recognition and that
   no tap happens.
4. Ship an installer (WiX MSI or Inno Setup) from a Windows build host.
5. Optional: scrcpy monitoring pane, Windows startup/sleep recovery helpers, Chinese locale markers.

## Risks

- Xiaomi may change the Community package id, screen layout or quota behaviour at any time; the
  profile is configurable, but recognition is inherently brittle.
- `adb shell input tap` latency is device-dependent and can push the action past the reset instant.
- Xiaomi's server-side quota/eligibility rules are out of this application's control.
- The transitive SQLite advisory (NU1903) remains until an updated bundle is released.
