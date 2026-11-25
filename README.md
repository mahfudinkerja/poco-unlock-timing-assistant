# POCO Unlock Timing Assistant

Windows desktop dashboard that prepares and submits the **official Xiaomi Community bootloader-unlock authorization request** at **00:00:00 Asia/Shanghai (23:00 WIB the previous day)** through a connected POCO F7 over USB ADB.

It synchronizes Windows time against NTP, counts down with a monotonic clock, verifies the Xiaomi Community screen through the accessibility hierarchy, sends **one** guarded UI action at the target instant, and classifies what Xiaomi actually answered. A completed ADB command is never reported as an approval.

> **Bootloader unlocking normally erases all user data.** This tool never bypasses CAPTCHA, account verification, waiting periods or quota rules, and never talks to undocumented Xiaomi endpoints.

## Stack

| Layer | Technology |
| --- | --- |
| Desktop UI | C# · .NET 10 · WPF · MVVM (`CommunityToolkit.Mvvm`) |
| Composition | `Microsoft.Extensions.Hosting` + DI + logging |
| Time | Multi-server SNTP client + `Stopwatch`-based monotonic clock |
| Device | Android Platform Tools (`adb`), UIAutomator hierarchy, `screencap` |
| Persistence | SQLite (`Microsoft.Data.Sqlite`) with CSV/JSON export |
| Tests | xUnit (60 tests) |

## Layout

```
src/PocoUnlockAssistant.Core/            domain: time math, state machine, recognition, gate, contracts
src/PocoUnlockAssistant.Infrastructure/  adb, ntp, uiautomator, sqlite, settings, file logging
src/PocoUnlockAssistant.App/             WPF app: theme, dashboard, view models
tests/PocoUnlockAssistant.Tests/         time, adb parsing, automation, scheduler, persistence
installer/PocoUnlockAssistant.iss        Inno Setup script for the installer build
legacy/electron-prototype/               earlier Electron UI prototype (reference only, not used)
ARCHITECTURE.md  IMPLEMENTATION-PLAN.md  QA-REPORT.md  KNOWN-LIMITATIONS.md  CHANGELOG.md
```

## Requirements

- Windows 10 / 11 x64 to **run** the app.
- .NET 10 SDK to build.
- Android Platform Tools (`adb`) on `PATH`, or set `ANDROID_HOME` / the ADB path in Settings.
- POCO F7 with USB debugging authorized (the app never bypasses the authorization prompt).
- Xiaomi Community installed and **signed in manually** by you.

## Build, test, run

```bash
dotnet restore
dotnet build PocoUnlockAssistant.sln
dotnet test tests/PocoUnlockAssistant.Tests/PocoUnlockAssistant.Tests.csproj
dotnet run --project src/PocoUnlockAssistant.App
```

Portable, self-contained single-file build for Windows x64:

```bash
dotnet publish src/PocoUnlockAssistant.App/PocoUnlockAssistant.App.csproj \
  -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -o artifacts/win-x64
# → artifacts/win-x64/PocoUnlockAssistant.exe (no .NET runtime required on the target PC)
```

The WPF project sets `EnableWindowsTargeting`, so it can be **compiled** on macOS/Linux, but the UI only **runs** on Windows.

Installer (run on Windows with [Inno Setup](https://jrsoftware.org/isinfo.php) 6.3+ after the publish above):

```bat
iscc installer\PocoUnlockAssistant.iss
:: → installer\Output\PocoUnlockAssistantSetup-0.1.1.exe
```

The installer creates Start-menu and optional desktop shortcuts and uninstalls cleanly. Your data in
`%APPDATA%\PocoUnlockAssistant` (history database, settings, logs, evidence) is **preserved** on
uninstall and across upgrades, so the duplicate-submission guard keeps working after reinstalling.

Security note: `Microsoft.Data.Sqlite` is pinned to `10.0.15` because earlier 10.0.x releases bundled
SQLite older than 3.50.2, which is affected by CVE-2025-6965. Re-run
`dotnet list package --vulnerable --include-transitive` when updating dependencies.

## Using the dashboard

1. **CONNECT DEVICE** — verifies adb, USB, authorization, HyperOS version and battery.
2. **OPEN XIAOMI COMMUNITY** — launches the official app; sign in manually.
3. Navigate to **Me → Unlock Bootloader** and press **CHECK READINESS** (NTP, device, app, screen).
4. **ARM SCHEDULER** — locks in the next 00:00:00 Asia/Shanghai and starts the monotonic countdown.
5. Keep the PC awake. At T-10s the app re-syncs time and the device, re-verifies the screen, and at T-0 sends a single tap on the verified button, then captures and classifies the response.
6. **EMERGENCY STOP** aborts immediately; **CANCEL SCHEDULER** aborts before the window.

Every attempt is stored in SQLite (Execution history) and can be exported to CSV/JSON from the UI.

## Data and privacy

Settings, logs, evidence screenshots and the audit database live in `%APPDATA%\PocoUnlockAssistant\` (`settings.json`, `logs\`, `evidence\`, `attempts.db`). No Xiaomi password, cookie, token or account credential is ever collected or stored.

## Known limitations

See [KNOWN-LIMITATIONS.md](KNOWN-LIMITATIONS.md) and [QA-REPORT.md](QA-REPORT.md) — notably: the UI has not been run on Windows or against a real POCO F7 yet, and the Xiaomi Community package id / screen markers must be confirmed on your device.
