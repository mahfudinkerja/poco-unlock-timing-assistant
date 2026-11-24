# QA Report

Date: 2026-11-25
Environment: macOS 13.7.8 (x86_64), .NET SDK 10.0.404, adb 1.0.41, no Windows host, no POCO F7 attached.
Command root: repository root. SDK installed locally in `.dotnet/` (git-ignored).

## Build

| Command | Result |
| --- | --- |
| `dotnet build PocoUnlockAssistant.sln` | **Succeeded**, 0 warnings (C#), 0 errors |
| `dotnet build src/PocoUnlockAssistant.App` (WPF, `EnableWindowsTargeting`) | **Succeeded** — XAML and code-behind compiled |
| `dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true` | **Succeeded** → `artifacts/win-x64/PocoUnlockAssistant.exe` (135 MB, single file) |
| `dotnet test tests/PocoUnlockAssistant.Tests` | **60 passed / 0 failed / 0 skipped** (4 s) |

The only build-time warning is NuGet advisory **NU1903** on transitive
`SQLitePCLRaw.lib.e_sqlite3 2.1.11` (see KNOWN-LIMITATIONS).

## Automated tests (60 cases / 50 methods)

| Suite | Methods | What is covered |
| --- | --- | --- |
| `TimeTests` | 14 | 00:00 Beijing = 23:00 WIB previous day, 12:00 Beijing = 11:00 WIB, WITA/WIT offsets, midnight rollover, strict-future target, trigger window vs missed window, countdown formatting, NTP healthy/no-responder/disagreement/latency, monotonic clock independence and clock-tamper detection |
| `AdbParsingTests` | 7 | connected + USB device, no device, unauthorized, offline, adb unavailable, getprop parsing (model/marketname/HyperOS/Android/serial/usb state), malformed lines |
| `AutomationTests` | 16 | expected screen with enabled button, disabled button, missing button, changed/wrong screen, empty hierarchy, CAPTCHA, explicit approval, ambiguous acknowledgement, unknown response, quota/account/network/rejected categories, "tap alone is not success", gate pass + 8 blocking conditions, state machine happy path, invalid transitions, duplicate submission |
| `SchedulerTests` | 10 | preparation success/NTP failure/no device, full scheduled run sends **exactly one** tap and records trigger/ADB/response timestamps with classification, duplicate arm for the same target rejected across a fresh coordinator, missed window never submits late, emergency stop prevents the tap, wrong screen blocks the tap, cancellation aborts, SQLite round-trip across restart + CSV/JSON export |
| `NtpPacketTests` | 3 | real SNTP reply parsing (field offsets 32/40), replies without timestamps, kiss-o'-death/short packets |

Fakes are used for the device, screen and NTP layers; **no real authorization request was ever sent** during testing.

## Live checks performed on this host (not mocked)

| Check | Result |
| --- | --- |
| `NtpClient.SynchronizeAsync()` against time.google.com / time.cloudflare.com / pool.ntp.org | `ok=True`, `Health=Healthy`, 3/3 responders, best server `pool.ntp.org` (stratum 2), offset **+158.0…+160.1 ms**, uncertainty **2.7…8.3 ms**, best RTT 4.6–21.0 ms, `IsTrustworthy(150 ms)=True` |
| `AdbClient.ProbeAsync()` | `AdbAvailable=True`, `State=NotAttached`, `adb 1.0.41`, error empty (expected: no phone attached) |
| `TargetTimeCalculator.NextBeijingReset` for the current instant | `2026-11-26T00:00:00+08:00` → `2026-11-25 23:00:00 WIB` |

The first live NTP run failed with "No NTP server responded." That was a genuine **client bug** (receive timestamp read from the originate field, which is zero when the request does not set it). It was fixed, covered by `NtpPacketTests`, and the live run then succeeded — see CHANGELOG.

## Acceptance criteria (section 15)

| # | Criterion | Status |
| --- | --- | --- |
| 1 | Windows x64 build succeeds | ✅ single-file self-contained publish produced |
| 2 | Production app runs without dev tools | ⚠️ self-contained artifact built; **not launched on Windows** |
| 3 | NTP synchronization works | ✅ live, 3 servers |
| 4 | Beijing clock is correct | ✅ offset math unit-tested + live target conversion |
| 5 | Jakarta conversion is correct | ✅ 23:00 WIB previous day verified |
| 6 | Countdown uses monotonic timing | ✅ `MonotonicClock` tests |
| 7 | POCO F7 detected through ADB | ⚠️ adb path verified; no device attached on this host |
| 8 | Device readiness verified | ⚠️ implemented (model/HyperOS/battery/screen), unverified on hardware |
| 9 | Xiaomi Community detected | ⚠️ `pm list packages`/`dumpsys` implemented, package id unconfirmed on device |
| 10 | Official authorization screen recognized | ⚠️ recognizer unit-tested with simulated hierarchies; real hierarchy not captured |
| 11 | Controlled ADB mode works on the verified screen | ⚠️ simulated end-to-end test sends one tap; real tap not performed |
| 12 | Unexpected screens block automation | ✅ unit + scheduler test |
| 13 | Duplicate submissions prevented | ✅ state machine + SQLite `HasExecutedAtAsync` test |
| 14 | Ambiguous results do not trigger retries | ✅ classifier + no-retry design, unit-tested |
| 15 | Execution timestamps stored | ✅ trigger / ADB completion / response observed |
| 16 | Screenshots saved when enabled | ⚠️ path and setting implemented; `screencap` not exercised on a device |
| 17 | Emergency STOP works | ✅ scheduler test; UI button wired |
| 18 | Manual fallback works | ⚠️ modes exist and abort messages direct the operator, but there is no guided manual checklist flow |
| 19 | Mi Unlock Tool section works independently | ⚠️ section, detection, browse/launch with confirmation, download link, instructions; fastboot not present on this host |
| 20 | All critical QA tests pass | ✅ for the 60 tests written; criteria 2, 7–11, 16, 18, 19 remain hardware/host dependent |

## Not verified (do not treat as done)

- Any WPF rendering, layout, DPI behaviour or interaction (never run on Windows).
- The connected-device branch of the dashboard (HyperOS/model/serial/battery/screen values).
- `uiautomator dump`/`screencap`/`input tap` against a real POCO F7.
- Xiaomi Community package id, version detection and real screen markers.
- Timing accuracy of the actual submission (ADB command latency on the device).
- Installer creation and Windows startup recovery.
