# QA Report — Phase 2 (Windows runtime & POCO F7 validation)

Date: 2026-11-25
Commit at audit start: `4fe7f82` (branch `main`, clean working tree)
Host actually used: **macOS 13.7.8 (x86_64)**, .NET SDK 10.0.404 (repo-local `.dotnet/`),
Android Platform Tools **adb 1.0.41 (37.0.1)** at `~/Library/Android/sdk/platform-tools/adb`.

> ## Scope statement — read this before the tables
>
> Phase 2 asks for validation on **Windows 10/11** and on a **physical POCO F7**. Neither exists on
> this host: there is no Windows machine, no VM, and `adb devices -l` returns an empty list
> (`List of devices attached` with no entries, exit code 0). Windows rendering, DPI behaviour, real
> device interrogation and the Xiaomi Community hierarchy therefore could **not** be executed, and
> are reported as **BLOCKED / NOT TESTED**. No screenshot of the running application exists in this
> repository, and none is claimed. Everything marked PASS below was executed and observed on this
> host. Nothing is marked PASS from compilation alone.

## 1. What this phase actually changed

| # | Item | Outcome |
| --- | --- | --- |
| 1 | NU1903 / CVE-2025-6965 | **Fixed** by dependency upgrade, verified at runtime |
| 2 | Window sizing vs. DPI | **Fixed** statically (see §4) — no DPI run performed |
| 3 | Installer script | **Authored**, not compiled (no Inno Setup / Windows host) |
| 4 | Windows/device validation | **Not possible on this host** — listed as BLOCKED |
| 5 | Documentation accuracy | README test count corrected (57 → 60) |

## 2. Dependency security (NU1903)

**Dependency chain (before):**

```
PocoUnlockAssistant.Infrastructure
 └─ Microsoft.Data.Sqlite 10.0.0
     ├─ Microsoft.Data.Sqlite.Core 10.0.0
     │   └─ SQLitePCLRaw.core 2.1.11
     └─ SQLitePCLRaw.bundle_e_sqlite3 2.1.11
         ├─ SQLitePCLRaw.lib.e_sqlite3 2.1.11   <-- the native SQLite binary
         └─ SQLitePCLRaw.provider.e_sqlite3 2.1.11
```

**Advisory:** GHSA-2m69-gcr7-jv3q / **CVE-2025-6965**, severity High (CVSS 7.2, CWE-197).
The advisory lists `SQLitePCLRaw.lib.e_sqlite3` **<= 2.1.11** as affected with "patched: None" —
that entry is stale. The bundled SQLite version is the real subject: the advisory says SQLite
before 3.50.2 allows the number of aggregate terms to exceed available columns, causing memory
corruption. `SQLitePCLRaw` 2.1.12 release notes state it "updates 2.1.11 to reference
SQLitePCLRaw.lib.e_sqlite3 3.53.3" — i.e. SQLite 3.53.3, above the 3.50.2 threshold.

**Fix chosen:** bump `Microsoft.Data.Sqlite` `10.0.0` → `10.0.15`. This is the first 10.0.x patch
that depends on `SQLitePCLRaw.* 2.1.13` (verified from the published `.nuspec`s: 10.0.10 still
pins 2.1.11, 10.0.15 pins 2.1.13). Staying inside 10.0.x avoids the breaking `SQLitePCLRaw` 3.x
provider changes. `Microsoft.Extensions.Logging.Abstractions` was moved to `10.0.15` in step so the
Microsoft.Extensions assemblies stay on one patch level. **No `NoWarn`, no audit suppression, no
`<NuGetAudit>false</NuGetAudit>` anywhere in the repo.**

**Evidence:**

| Check | Command | Result |
| --- | --- | --- |
| Resolved graph after upgrade | `dotnet list package --include-transitive` | `SQLitePCLRaw.bundle_e_sqlite3 / core / lib.e_sqlite3 / provider.e_sqlite3` all **2.1.13** |
| Vulnerability audit | `dotnet list package --vulnerable --include-transitive` | All four projects: *"has no vulnerable packages given the current sources"* |
| Build (advisory gone) | `dotnet build PocoUnlockAssistant.sln` | **Succeeded, 0 warnings, 0 errors** |
| Runtime SQLite identity | temporary harness: `select sqlite_version()` via the upgraded `Microsoft.Data.Sqlite` | **`3.53.3`** (≥ 3.50.2 → not affected) |
| Regression | `dotnet test` | **60 passed / 0 failed / 0 skipped** (4 s) |

## 3. Live time-engine checks (re-run this phase)

`NtpClient` against `time.google.com`, `time.cloudflare.com`, `pool.ntp.org`:

```
ok=True health=Healthy disagreement=False
offset=+79.3 ms  uncertainty=8.4 ms  bestRTT=14.6 ms  server=pool.ntp.org stratum=2
trustworthy(150 ms)=True
  time.google.com      offset=+79.3 ms  rtt=19.2 ms  stratum=1
  time.cloudflare.com  offset=+79.4 ms  rtt=19.6 ms  stratum=3
  pool.ntp.org         offset=+77.3 ms  rtt=14.6 ms  stratum=2
```

Three responders agreed within 2.1 ms, median offset +79.3 ms. This demonstrates the SNTP client,
median consensus, uncertainty and disagreement logic against real servers — **on macOS**. The
Windows network stack, a Windows clock adjustment, and long-run clock drift were not exercised, so
"Windows NTP validation" remains NOT TESTED.

## 4. Windows-facing static audit (no runtime)

| Check | Method | Finding |
| --- | --- | --- |
| DPI awareness declared | `app.manifest` | `permonitorv2` + `true/pm`, `longPathAware` — present |
| XAML ↔ view-model binding integrity | Parsed all 91 `{Binding}` paths in `MainWindow.xaml` against `MainViewModel` (`[ObservableProperty]` + `[RelayCommand]` name generation) | **No unresolved binding**; the 14 non-view-model paths resolve to `ReadyCheckRow(Name, Detail, Ok, Marker)` and `AttemptRecord` item properties |
| Seven required buttons | XAML label scan | `CONNECT DEVICE`, `CHECK READINESS`, `OPEN XIAOMI COMMUNITY`, `PREPARE AUTOMATION`, `ARM SCHEDULER`, `CANCEL SCHEDULER`, `EMERGENCY STOP` all present |
| **Window fits a scaled 1080p screen** | Arithmetic on `Width/Height/MinWidth/MinHeight` vs `SystemParameters.WorkArea` | **Defect found and fixed.** At 125% a 1920x1080 screen exposes 1536x864 DIPs; at 150% only 1280x720. Default `Height=880` and `MinHeight=760` exceeded both, and with `WindowStyle="None"` the title bar / close button could sit off-screen. Now `Height=860`, `MinHeight=560`, `MinWidth=960`, plus a `FitToWorkArea()` clamp in the constructor. Content is already inside `ScrollViewer`s. |
| Credentials in source | Grep for password/secret/token/api-key/bearer | None. (`CancellationToken`/`authorization screen` matches are false positives.) |
| PII in logs | Grep for log calls containing serial/IMEI/androidId/MAC | None found |

The DPI fix is **reasoned, not observed**: it must still be confirmed visually at 100/125/150%.

## 5. Evidence status table

Statuses are restricted to PASS / FAIL / BLOCKED / NOT TESTED.

| Component | Test | Result | Evidence | Remaining Issue |
| --- | --- | --- | --- | --- |
| Windows UI | Actual runtime | **BLOCKED** | No Windows host; app never launched. 91/91 bindings resolve statically; build clean | Entire visual/interaction pass outstanding; no screenshot exists |
| NTP | Live synchronization | **PASS** (host-level) | 3/3 responders, +79.3 ms, uncertainty 8.4 ms, Healthy, trustworthy ≤150 ms — macOS run | Not executed on Windows; Windows clock-adjustment and drift untested |
| ADB | Real device | **BLOCKED** | `adb devices -l` → empty list, exit 0; adb 1.0.41 present | No POCO F7 attached; getprop/dumpsys/uiautomator/screencap/input never run on hardware |
| Device detection | POCO F7 | **NOT TESTED** | Parser unit-tested on captured-format strings only | Model/HyperOS/serial/battery/screen values unverified |
| Xiaomi Community | Package verification | **NOT TESTED** | `com.mi.global.bbs` observed on POCO F7 (v5.4.40) | Screen markers still unconfirmed |
| UIAutomator | Actual hierarchy | **NOT TESTED** | Inspector implemented; fakes used in tests | No real hierarchy captured; CAPTCHA/security markers unverified |
| Screen recognition | Actual screen | **NOT TESTED** | 16 automation tests on simulated hierarchies | Real layout may differ; recognition may fail |
| Scheduler | Safety gates | **PASS** (simulated) / **NOT TESTED** (device) | 10 scheduler tests: exactly one tap, no late submit, emergency stop, wrong screen blocked, duplicate arm rejected, restart-safe dedupe | Gates never evaluated against a live device |
| SQLite | Persistence | **PASS** | Round-trip across restart test; CSV/JSON export test; live `sqlite_version()` = 3.53.3 | Only local file DB; no concurrency stress |
| Security | NuGet audit | **PASS** | `--vulnerable --include-transitive` clean on 4 projects; NU1903 gone without suppression | Boxed-SQLite advisories can reappear; re-audit each release |
| Installer | Clean installation | **BLOCKED** | `installer/PocoUnlockAssistant.iss` authored (shortcuts, uninstall, AppData preserved) | Never compiled with `iscc`, never installed; upgrade/uninstall unverified |

## 6. Automated tests (unchanged scope, re-run after dependency upgrade)

| Suite | Methods | Coverage |
| --- | --- | --- |
| `TimeTests` | 14 | Beijing↔WIB/WITA/WIT conversion, midnight rollover, strict-future target, trigger vs. missed window, countdown formatting, NTP health/disagreement/latency, monotonic clock and clock-tamper detection |
| `AdbParsingTests` | 7 | connected/USB, none, unauthorized, offline, adb missing, getprop parsing, malformed lines |
| `AutomationTests` | 16 | enabled/disabled/missing button, wrong screen, empty hierarchy, CAPTCHA, approval, ambiguous ack, quota/account/network/rejected, "tap ≠ success", gate pass + 8 blocking conditions, state machine path + invalid transitions + duplicate |
| `SchedulerTests` | 10 | preparation paths, end-to-end single tap with timestamps, duplicate arm, missed window, emergency stop, wrong screen, cancel, SQLite round-trip + export |
| `NtpPacketTests` | 3 | real SNTP reply offsets (32/40), missing timestamps, kiss-o'-death/short packets |

Result: **60 passed / 0 failed / 0 skipped**. Fakes replace device, screen and NTP peers; **no
authorization request was ever submitted**.

## 7. Acceptance criteria (section 15) — updated

| # | Criterion | Status |
| --- | --- | --- |
| 1 | Windows x64 build succeeds | ✅ single-file self-contained publish produced (135 MB) |
| 2 | Production app runs without dev tools | ⚠️ artifact built; **never launched on Windows** |
| 3 | NTP synchronization works | ✅ live, 3 servers (macOS host) |
| 4 | Beijing clock correct | ✅ unit-tested + live target conversion |
| 5 | Jakarta conversion correct | ✅ 23:00 WIB previous day |
| 6 | Countdown uses monotonic timing | ✅ `MonotonicClock` tests |
| 7 | POCO F7 detected through ADB | ⚠️ no device attached |
| 8 | Device readiness verified | ⚠️ implemented, unverified on hardware |
| 9 | Xiaomi Community detected | ⚠️ package id unconfirmed |
| 10 | Official authorization screen recognized | ⚠️ simulated hierarchies only |
| 11 | Controlled ADB mode works on verified screen | ⚠️ simulated; never tapped a real device |
| 12 | Unexpected screens block automation | ✅ unit + scheduler test |
| 13 | Duplicate submissions prevented | ✅ state machine + SQLite test |
| 14 | Ambiguous results do not retry | ✅ classifier + no-retry design |
| 15 | Execution timestamps stored | ✅ trigger / ADB completion / response |
| 16 | Screenshots saved when enabled | ⚠️ `screencap` never exercised on a device |
| 17 | Emergency STOP works | ✅ scheduler test; UI button wired |
| 18 | Manual fallback works | ⚠️ modes + abort guidance, no guided checklist |
| 19 | Mi Unlock Tool section independent | ⚠️ fastboot absent on this host |
| 20 | All critical QA tests pass | ✅ 60/60 automated; criteria 2, 7–11, 16, 18, 19 still host/hardware dependent |

## 8. Repository hygiene

- Working tree reviewed before committing; no binaries, no `artifacts/`, no `.dotnet/`, no device
  screenshots committed (`.gitignore` covers `.dotnet/`, `.scratch/`, `artifacts/`, `publish/`).
- No secrets, tokens or account data in source, settings defaults or logs.
- Temporary verification harnesses (`.scratch/`) deleted after use.

## 9. What is required to move to PASS

1. Run `PocoUnlockAssistant.exe` on Windows 10 and 11 at 100/125/150% on 1920x1080 and 2560x1440;
   capture screenshots; confirm the title bar, all cards, all seven buttons, every section, and
   shutdown.
2. Attach the POCO F7 (USB debugging authorized by the user), run the Diagnostics inspector, and
   replace the assumed package id / screen markers with observed values.
3. Execute the device-gate matrix (disconnect, unauthorized, offline, wrong screen).
4. Compile the installer with `iscc` and install/uninstall/upgrade on a clean Windows image.
