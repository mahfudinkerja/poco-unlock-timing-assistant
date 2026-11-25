# Architecture

## Shape

Clean-architecture split; the domain never references WPF, adb or SQLite.

```
PocoUnlockAssistant.App (net10.0-windows, WPF, MVVM)
        │  MainViewModel · MainWindow · Theme
        ▼
PocoUnlockAssistant.Infrastructure (net10.0)
        │  NtpClient · AdbClient · UiautomatorScreenInspector
        │  SqliteAttemptStore · SettingsStore · FileLoggerProvider · FastbootClient
        ▼
PocoUnlockAssistant.Core (net10.0)
           Time   ZoneCatalog · TargetTimeCalculator · NtpConsensus · MonotonicClock
           Device AdbDeviceInfo · AdbOutputParser
           Automation  SubmissionStateMachine · ScreenRecognizer · ResultClassifier
                       PreExecutionValidator
           Scheduling  SubmissionCoordinator
           Persistence AttemptRecord
           Abstractions INtpClient · IAdbClient · IScreenInspector · IAttemptStore
           Configuration AssistantSettings
```

Core exposes interfaces (`Abstractions/Contracts.cs`); Infrastructure implements them; the
composition root (`App.xaml.cs`) wires them through `Microsoft.Extensions.Hosting` DI.

## Precision time

`NtpClient` sends RFC 4330 SNTP requests to `time.google.com`, `time.cloudflare.com` and
`pool.ntp.org` in parallel and computes per server:

```
offset = ((t2 - t1) + (t3 - t4)) / 2
delay  =  (t4 - t1) - (t3 - t2)
```

`NtpConsensus.Build` takes the **median** offset (robust against one bad server), estimates
uncertainty as `bestRTT/2 + spread/2`, and flags disagreement when the maximum deviation exceeds
the tolerance. Health: `Healthy` / `Degraded` / `Unreliable` / `Failed`.

`MonotonicClock` anchors a trusted UTC instant to `Stopwatch.GetTimestamp()` and advances it with
`Stopwatch.GetElapsedTime`, so the countdown ignores manual clock changes and time-service jumps.
It re-anchors after every NTP sync. `SystemClockDrift()` exposes tamper detection for diagnostics.

The target always comes from `TargetTimeCalculator.NextOccurrence(now, +08:00, 00:00)`, which is
strictly in the future; Jakarta and WITA/WIT are pure offset conversions (no DST in these zones).

## Attempt flow

```
IDLE → INITIALIZING → TIME_SYNC → DEVICE_CHECK → COMMUNITY_CHECK → PREPARING → READY
     → ARMED → WAITING_FOR_TARGET → PRE_EXECUTION_VALIDATION → EXECUTING
     → WAITING_FOR_RESPONSE → SUBMITTED → APPROVED | REJECTED | QUOTA | ACCOUNT | UNKNOWN
```

`SubmissionStateMachine` owns the transition table: illegal transitions are rejected and logged,
and a machine that already entered `EXECUTING` refuses a second entry, so one attempt can send at
most one action. Terminal states require a new attempt id.

`SubmissionCoordinator` drives it:

| Moment | Work |
| --- | --- |
| T-10 min | NTP sync, connectivity, ADB, device readiness, Xiaomi Community, screen recognition |
| T-5 min | Launch the app (user signs in manually) and navigate to Me → Unlock Bootloader |
| T-1 min | Re-check time, connection and screen |
| T-10 s | Re-sync, re-probe, capture hierarchy, confirm duplicate protection |
| T-0 | Evaluate `PreExecutionValidator`, tap the **verified** button bounds once |
| T+ | Wait, capture the response screen, classify it, store evidence |

`alreadyExecuted` is read from SQLite (`HasExecutedAtAsync`) and cross-checked with
`SubmissionCount`, so a restart cannot replay a submission for the same target.

## Screen recognition

`ScreenRecognitionProfile` (shipped as `screen-profile.json`) lists marker strings per screen kind
and the submit-button text/id patterns. `ScreenRecognizer` scores the normalized accessibility
nodes, and only reports `SubmissionReady` when an unlock-screen marker matched **and** a clickable
button node exists. `ResultClassifier` maps the observed screen to `Approved`, `Submitted`,
`Rejected`, `QuotaExhausted`, `AccountIneligible`, `NetworkError`, `SecurityChallenge` or
`Unknown`; `Unknown`/low-confidence always means "do not retry automatically".

Coordinates are never replayed: the tap target is the bounds of the clickable node found in the
hierarchy captured seconds earlier for the same attempt. If no such node exists, automation aborts.

## Safety gate

`PreExecutionValidator` is a pure function over ten conditions (time synced, uncertainty within
limit, target reached, window not missed, device ready, screen verified, button enabled, no
security challenge, no duplicate, no emergency stop). Any failure aborts and offers the manual
fallback; late submission after the grace window is impossible by construction.

## Persistence

SQLite table `attempts` stores: attempt id, scheduled Beijing/Jakarta timestamps, NTP server, offset,
uncertainty, latency, device model, HyperOS version, ADB status, Community version, execution mode,
UI verification, trigger / ADB completion / response timestamps, result, confidence, state, error,
screenshot path and created/updated times. CSV and JSON exports write next to the database.
No credential is stored because none is ever requested.

## Threading and lifecycle

The view model owns a `DispatcherTimer` (50 ms tick, clocks refreshed once per second) and marshals
background coordinator events back to the UI thread through `Dispatcher.BeginInvoke`. The scheduled
run lives in a `Task` guarded by a `CancellationTokenSource`; closing the window disposes it, and
`EmergencyStop` is checked inside both wait loops.

The window is per-monitor DPI aware (`app.manifest`, `permonitorv2`). Because it draws its own title
bar (`WindowStyle="None"` + `WindowChrome`), a window larger than the work area would put the
caption buttons out of reach; the constructor therefore clamps width/height to
`SystemParameters.WorkArea`, and each section body scrolls independently.

## Distribution

Two artifacts: the portable self-contained single-file exe (`artifacts/win-x64`) and an Inno Setup
installer built from `installer/PocoUnlockAssistant.iss`. The installer writes to
`%ProgramFiles%\PocoUnlockAssistant`, creates Start-menu and optional desktop shortcuts, and
**never deletes `%APPDATA%\PocoUnlockAssistant`** on uninstall or upgrade — that directory holds the
attempt database, settings, logs and evidence, and carries the duplicate-submission guard across
reinstalls. Dependencies are kept current for security: `Microsoft.Data.Sqlite 10.0.15` supplies
`SQLitePCLRaw 2.1.13` / SQLite 3.53.3, which clears CVE-2025-6965.
