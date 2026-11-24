# Changelog

All notable changes to POCO Unlock Timing Assistant.

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

- UI not yet run on Windows; real-device automation unverified; no installer; transitive SQLite
  advisory NU1903. See KNOWN-LIMITATIONS.md.
