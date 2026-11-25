# Changelog

All notable changes to POCO Unlock Timing Assistant.

## [0.1.1] — 2026-11-25 (Phase 2: audit, dependency security, Windows readiness)

### Security

- **NU1903 resolved, not suppressed.** `Microsoft.Data.Sqlite` raised from `10.0.0` to `10.0.15`,
  which is the first 10.0.x patch that references `SQLitePCLRaw` `2.1.13` instead of `2.1.11`.
  That bundle ships SQLite **3.53.3**, clearing GHSA-2m69-gcr7-jv3q / CVE-2025-6965 (affected:
  SQLitePCLRaw.lib.e_sqlite3 <= 2.1.11, i.e. SQLite < 3.50.2). `Microsoft.Extensions.Logging.Abstractions`
  moved to `10.0.15` to keep the Microsoft.Extensions set aligned.
- Verified at runtime, not just in metadata: `select sqlite_version()` through the upgraded
  `Microsoft.Data.Sqlite` returns `3.53.3`.
- `dotnet list package --vulnerable --include-transitive` reports no vulnerable packages for any
  of the four projects.

### Changed

- Windows sizing made DPI-safe: `MinHeight` lowered from 760 to 560 and `MinWidth` from 1120 to
  960, and the window now clamps itself to `SystemParameters.WorkArea` on construction. At 150%
  scaling a 1920x1080 display exposes only 1280x720 DIPs, so the previous minimum made the
  custom title bar and close button unreachable. Content columns already scroll.
- Added an Inno Setup script (`installer/PocoUnlockAssistant.iss`) for the installer deliverable:
  shortcuts, uninstall, and deliberate preservation of `%APPDATA%\PocoUnlockAssistant` across
  upgrades and uninstall. **The script has not been compiled or installed** — that needs a Windows
  host (see KNOWN-LIMITATIONS).
- Version bumped to 0.1.1.

### Fixed

- README claimed 57 tests while the suite contains 60.

### Verified in this phase

- `dotnet build` clean with **0 warnings** (NU1903 no longer appears), 60/60 tests pass.
- Live SNTP re-run: 3/3 responders, offset **+79.3 ms**, uncertainty **8.4 ms**, max disagreement
  2.1 ms, `Healthy`, trustworthy at the 150 ms limit. Host was macOS, not Windows.

## [0.1.0] — 2026-11-25

### Added

- C# / .NET 10 / WPF desktop application with MVVM (`CommunityToolkit.Mvvm`), dependency injection
  and structured file logging.
- Precision time engine: multi-server SNTP client (Google, Cloudflare, pool.ntp.org), median-offset
  consensus, latency-derived uncertainty, disagreement detection and a `Stopwatch`-based monotonic
  clock that ignores Windows clock changes.
- Target-time math for 00:00:00 Asia/Shanghai with Jakarta (23:00 WIB previous day), WITA and WIT
  conversions, strict-future target calculation and a bounded late window.
- ADB layer: executable resolution, device list parsing (authorized/unauthorized/offline/none),
  property, battery and screen-state probing, app launch, package/version detection and a
  UIAutomator hierarchy + PNG screenshot inspector.
- Xiaomi Community recognition: configurable profile of screen markers and submit-button patterns,
  conservative result classifier (`Approved`, `Submitted`, `Rejected`, `QuotaExhausted`,
  `AccountIneligible`, `NetworkError`, `SecurityChallenge`, `Unknown`).
- Deterministic 22-state submission state machine with illegal-transition rejection and a
  single-action lock per attempt.
- Final pre-execution gate with ten conditions (time trust, uncertainty, target reached, window not
  missed, device ready, screen verified, button enabled, no security challenge, no duplicate,
  emergency stop clear).
- SQLite audit trail with all specified columns, restart-safe duplicate protection, CSV and JSON
  export.
- Windows dashboard: AMOLED black theme with POCO red accent, device card, Beijing/Jakarta/local
  clocks, NTP strip, millisecond countdown, execution status, readiness checks, the seven required
  control buttons, plus execution history, settings, diagnostics, Mi Unlock Tool and About sections.
- Portable self-contained `win-x64` single-file build; 60 xUnit tests.

### Changed

- Replaced the earlier Electron prototype; it is preserved under `legacy/electron-prototype/` and is
  no longer part of the build.

### Fixed

- SNTP client read the receive timestamp from the originate field, which is zero because the request
  never sets it, so every valid reply was discarded. Field offsets (receive 32–39, transmit 40–47)
  are now correct and covered by `NtpPacketTests`.
- Final safety gate was evaluated at T-10s, which made `target-reached` always fail; it now runs at
  T-0 after the fine-grained wait.
- Preparation could not reach `READY` because the intermediate states were skipped; the coordinator
  now walks the deterministic chain.
- `APPROVED`/`REJECTED` now transition through `SUBMITTED` as the state machine requires.

### Known issues

- UI not yet run on Windows; real-device automation unverified; installer script written but not
  built. The transitive SQLite advisory (NU1903) listed here was **fixed in 0.1.1**. See
  KNOWN-LIMITATIONS.md.
