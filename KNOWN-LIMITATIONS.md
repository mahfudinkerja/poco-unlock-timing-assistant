# Known limitations

## Not verified on hardware or Windows

1. **The WPF UI has never been run.** It compiles (including XAML) and was cross-published for
   `win-x64`, but this project was built on macOS without a Windows host. Layout, DPI scaling,
   fonts, the live 50 ms countdown and every button still need a first run on Windows 10/11.
2. **No POCO F7 was attached.** The ADB client itself was exercised live (`adb 1.0.41` detected,
   `NotAttached` reported correctly), but `getprop`, `dumpsys`, `uiautomator dump`, `screencap` and
   `input tap` have not been executed against a real HyperOS 3 device.
3. **Xiaomi Community package id and screen markers are assumptions.** The default profile uses
   `com.miui.community` with English/Indonesian marker strings. Confirm the package id and the
   hierarchy on your device (Diagnostics → Inspect current screen) and edit
   `%APPDATA%\PocoUnlockAssistant\screen-profile.json` if they differ.
4. **Chinese-locale UI strings are not covered** by the shipped recognition profile.

## Functional gaps

5. **No installer.** Only a portable, self-contained single-file executable is produced.
   An MSI/NSIS package needs a Windows build host (WiX/Inno Setup) and was not created.
6. **No Windows sleep/restart recovery or auto-start.** The app is a foreground desktop app:
   it cannot execute while the PC is asleep or powered off, it does not replay a missed window,
   and the startup-recovery option is not implemented (no fake setting is exposed for it).
7. **Submission timing is best effort.** The tap is issued ~220 ms before the target to compensate
   for `adb shell input tap` latency (typically 50–300 ms, unmeasured here). Millisecond-accurate
   server arrival cannot be guaranteed and is not claimed.
8. **Manual assisted mode is a mode plus instructions**, not a guided step-by-step checklist.
9. **`scrcpy` monitoring integration is not implemented.**
10. **Fastboot/Mi Unlock Tool detection requires `fastboot` on PATH** and the official Mi Unlock
    Tool executable chosen by you; neither is bundled. Nothing destructive is executed by the app.
11. **Android Platform Tools are not bundled.** `adb` is resolved from the configured path,
    `ANDROID_HOME`/`ANDROID_SDK_ROOT`, or `PATH`.
12. **NTP server list changes require a restart** (the client is constructed once at startup).

## Safety properties that are intentional (not bugs)

13. A completed tap is never reported as approval; unresolved screens stay `UnknownResult` and are
    never retried automatically.
14. Any CAPTCHA, verification challenge, or changed screen aborts the attempt instead of guessing.
15. Coordinates are never replayed from a previous run: the tap uses the bounds of a clickable node
    found in a hierarchy captured seconds before the action for that same attempt.
16. If no verified clickable button exists, or NTP uncertainty exceeds the configured limit, the
    app refuses to submit and tells you to fall back to manual submission.

## Dependency advisory

17. `dotnet` reports **NU1903** for transitive `SQLitePCLRaw.lib.e_sqlite3 2.1.11`
    (GHSA-2m69-gcr7-jv3q), pulled in by `Microsoft.Data.Sqlite 10.0.0`. No patched version is
    available through that package line yet; re-evaluate when Microsoft ships an updated bundle or
    pin a newer `SQLitePCLRaw.bundle_e_sqlite3` once released.
