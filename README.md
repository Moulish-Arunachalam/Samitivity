# Samitivity

A private Windows tray companion that plays local voice recordings when you press a global shortcut. Built as a small, offline gift application; no accounts, network client, telemetry, external player, or keyboard hook.

**Status: implemented engineering candidate, not yet validated for gifting.** The solution cross-compiles on macOS and the portable tests run there. Actual Windows playback, installer UI, recovery, and hardware behavior require the acceptance checks in [TESTING.md](TESTING.md). No private voice recordings were provided or added. The public CI installer is an empty library; add your private bundle locally before gifting.

## Using it

Install on a Windows x64 PC with built-in Media Foundation, then look for Samitivity in the notification area (possibly under the overflow arrow). Right-click → **Configure**. The Start menu shortcut also opens Configure; you can pin it to the taskbar. Double-clicking the tray icon opens it too.

| Action | Default |
| --- | --- |
| Miss Me | Ctrl + Alt + S |
| Motivation | Ctrl + Shift + Win + S |
| Just Because | Alt + Shift + S |
| Stop and reset | Ctrl + Alt + Backspace |

A new category activation interrupts the current recording. Each distinct press selects another note; holding the keys does not repeat. Stop leaves the app idle. Startup, resume, device changes, and crash recovery never start audio.

All shortcuts are editable. Windows registration conflicts appear in Configure; reassign and **Save**. Windows-key combinations are reserved and may fail, while Ctrl+Alt can behave as AltGr on some layouts. `RegisterHotKey` cannot represent S+Backspace or S+Delete as a chord: S is not a modifier. No shortcut can guarantee compatibility with every application's existing shortcuts. Unmodified S and backtick/tilde remain ordinary typing keys.

Import multiple M4A, MP3, or WAV files with the Windows picker. Imports are copied, fully decoded for validation, and held pending until Save. The original recordings are untouched. Preview applies to saved notes; pending notes become previewable after Save. Save confirms pending changes; Close warns about unsaved changes. Delete moves managed files to local Trash; removing a category preserves its folder.

## Build on macOS

Install the exact .NET 10 LTS SDK from `global.json` using Microsoft's official SDK installer. Then:

```sh
./scripts/test-portable.sh
# Optional Windows cross-compilation; does not execute Windows APIs:
dotnet restore Samitivity.slnx --locked-mode
dotnet build Samitivity.slnx --no-restore -c Release
```

The runtime application is entirely C#/.NET and Windows native APIs. No Node, Python, browser, FFmpeg, or separately installed .NET runtime is needed by the recipient. The self-contained app bundles its runtime. Development and NuGet restore require internet; the installed app does not.

The workflow in `.github/workflows/windows.yml` tests on macOS and Windows, publishes `win-x64`, compiles an Inno Setup installer, and uploads it as a CI artifact. **This checkout has no remote configured; no Windows CI run has been performed here.** Push it to a repository you control to run the workflow. Real voice assets must stay out of public repositories and CI artifacts.

## Repository

- `Samitivity.Core`: configuration, atomic stores, managed imports/indexing, shuffle bags, serialized playback controller.
- `Samitivity.Windows`: WinForms, RegisterHotKey, Media Foundation/WASAPI, device notifications, tray and startup integration.
- `Samitivity.Tests`: portable behavior and failure-path tests.
- `Samitivity.Windows.Tests`: native decoder, hotkey registration, and task-definition tests.
- `samples`: synthetic tones and example configuration; no personal audio.
- `scripts`, `installer`: reproducible build commands, per-user installer, recovery probe, idle measurements.

Read [ARCHITECTURE.md](ARCHITECTURE.md) for exact policies, [INSTALLATION.md](INSTALLATION.md) for distribution, [SECURITY.md](SECURITY.md) for boundaries, and [TESTING.md](TESTING.md) for the Windows release gate.

Current verification details: [requirements audit](docs/REQUIREMENTS-AUDIT.md), [local build status](docs/BUILD-STATUS.md), and [exact Windows CI/download steps](docs/WINDOWS-CI.md).

## 0.1.1 performance revision

See [performance changes, diagnostics and laptop checklist](docs/PERFORMANCE-0.1.1.md). This revision preserves version-1 data and adds bounded short-audio preparation, reusable output, fewer save operations, and a refreshed native UI. Physical latency targets require laptop measurements.

## 0.1.2 compatibility test release

The installer now admits Windows 10 x64 builds before 22H2, including 1909. See [confirmed diagnosis, support limits and physical acceptance instructions](docs/COMPATIBILITY-0.1.2.md). CI runs on Windows Server 2025; client Windows and CrossOver results are separate. No audio backend or data schema changes.
