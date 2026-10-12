# Requirements audit — 2026-10-12

This audits the existing implementation; it does not certify Windows behavior. Source was preserved and corrected incrementally. Git has no initial commit and no remote; all source files are untracked, so there is no earlier committed diff baseline. No partial `.orig`/`.rej` files or empty source files were found. Names are consistently Samitivity except the intentionally retained `%LOCALAPPDATA%\LittleNotes` data directory.

| Original area | Implemented | Evidence and remaining work |
| --- | --- | --- |
| 1. Background gift behavior | Tray process, random category commands, silent startup, local files, no external player | Portable selection/controller tests pass. Actual tray/background/offline experience and idle resources need Windows. No real gift notes supplied. |
| 2. Architecture | .NET 10 LTS; Core/Windows/tests; WinForms, RegisterHotKey, NAudio MF/WASAPI; self-contained x64 | Full solution compiled on macOS. Windows executable and runtime published; no Windows execution here. |
| 3. Audio | M4A/MP3/WAV import, native full-decode validation, bounded prefetch, original bytes preserved | Portable import tests use a fake validator. Ten Windows integration cases are compiled but unrun, including native format checks. Actual iPhone variants and devices remain untested. Mid-stream format changes explicitly rejected. |
| 4. Keyboard and Stop | Configurable modifiers + key, MOD_NOREPEAT, visible conflicts, category switching, tray/UI stop, Ctrl+Alt+Backspace default | Controller Stop/rapid-request tests pass. OS registration/reassignment tests await Windows. S+Backspace/Delete cannot be expressed via RegisterHotKey; no such gesture implemented. No defined double-activation-window behavior was supplied, so each distinct press selects. No universal non-conflicting mapping exists. |
| 5. Fairness | Independent persistent bags, unbiased underlying shuffle, recent-three avoidance where possible, content IDs, additions/removals/renames, consumed interruptions | Portable tests pass, including restart and failed-write rollback. Exact dynamic policy is documented. Backup corruption recovery can roll history back; arbitrary disk corruption cannot preserve absolute fairness guarantees. |
| 6. Playback | Single latest-request worker, cancellation, no new session before old teardown, per-session completion, fresh default output, bounded buffering/fades | Fake-backend concurrency/lifecycle tests pass. WASAPI stop/join, real device changes, native failure behavior, audible click suppression, and physical latency remain untested. Native driver calls may delay stopping. |
| 7. Management | Category navigation, name/shortcut edits, lists/durations, saved-note Preview, Stop, multi-import, delete confirmation, Explorer, status, Save/discard | Compiles. UI layout/DPI/accessibility and user workflows need Windows visual testing. Category navigation uses a native list rather than the requested cards/tabs. Pending imports preview after Save. No import-cancel button; progress identifies current file. |
| 8. Storage | Per-user LocalAppData, GUID category folders, separate binaries, no server, private-by-account local files | File/path tests pass. No app encryption; same-account apps/admins can read notes. Removed categories and Trash intentionally remain on disk. |
| 9. Recovery | Atomic stores/backups, mutex/events, Explorer broadcast, watcher reconciliation, silent resume, scheduled logon task + restart settings, Startup fallback | Portable recovery tests pass. Task XML compiles and has a Windows test; actual scheduling/restart is NOT RUN. Supplied nonzero-exit probe and abrupt-kill checklist are release gates. No watchdog. |
| 10. Security | No app network code/listener, telemetry, updater, arbitrary command/plugin path, privileged hooks, or antivirus changes; exact package/action pins | Source audit completed; runtime network observation, Windows permissions and Defender checks still needed. Packaging rejects unrelated private assets. |
| 11. Packaging | Per-user Inno setup, startup setup, first-run bundle import, graceful update/uninstall, preserved user data | PowerShell syntax and eight orchestration/privacy tests pass with a fake compiler. Real Inno compilation, installation, upgrade/uninstall, signing and SmartScreen behavior are untested. No installer EXE has been produced on this Mac. |
| 12. macOS/CI | Portable solution/tests, cross-build support, Windows CI restore/build/native tests/publish/package/artifacts | Local commands pass. CI file prepared but not run; no remote configured. Public CI forbids private bundles. |
| 13. Tests | 49 portable cases, 10 Windows-only cases, 8 packaging-orchestration cases, detailed manual acceptance matrix | 49 portable + 8 packaging passed locally. 10 Windows cases are NOT RUN. Physical hotkey, media and hardware acceptance remain open. |
| 14. Performance | Event-driven hotkeys/watcher/audio, no idle scans, short bounded prefix, fresh resource ownership | No measured Windows CPU, RAM, handles, acoustic latency, or overnight stability. Scripts/procedures supplied; no numeric performance claims. |
| 15. Deliverables | Source/solutions/tests, workflow, Inno script, build scripts, example config, synthetic fixtures, README/architecture/testing/security/install docs | Present and syntax/build checked as applicable. Recipient-specific voice bundle and signed finished installer are not supplied. |
| 16. Execution discipline | Existing implementation inspected, errors fixed, portable tests executed, publish verified, limits recorded | No claim of complete/production-ready or validated Windows functionality. Follow CI instructions and acceptance gates before gifting. |

## Corrections made during continuation audit

- Confirmed `win-x64` is included in the shared RuntimeIdentifiers and all lock files. Both ordinary locked restore and self-contained publish now succeed without rewriting dependency versions.
- Clean publish staging prevents obsolete files/private bundle copies from carrying into a new package.
- Private packaging allows only the three documented category directories and audio files; rejects root files, unknown categories, nested/reparse entries, and CI bundling; cleans staged private data on compiler failure.
- Regression tests cover failed state-write consumption, external duplicate deletion, and nonblocking library snapshots during slow decoding.
- Management edits are disabled during first bundle initialization, and Preview is disabled during Save so it cannot reopen a file being deleted.
- Shutdown ignores late hotkeys/power events and hides management while disposing playback.
- Watcher refresh requests received during another maintenance operation are retained and reconciled afterward.
- Status layout sizes with its parent, and library refresh updates the visible recording list while preserving the selection.
- Native streams changing format mid-recording are rejected rather than continuing with a stale output format.

## Release blockers

1. Run and fix the first real Windows CI job; especially native codecs, hotkey registration, and Inno compilation.
2. Install the final build as a standard user and complete the manual matrix on a representative Windows laptop, including actual iPhone files and the intended Bluetooth keyboard/headphones.
3. Record actual Task Scheduler nonzero/abrupt-exit recovery, acoustic latency/Stop latency, and long-running resource behavior.
4. Prepare the private gift bundle locally, verify the final installer, and record its signing/SmartScreen/Defender behavior.

See [WINDOWS-CI.md](WINDOWS-CI.md) for exact publishing/download steps and [WINDOWS-VALIDATION.md](WINDOWS-VALIDATION.md) for the evidence record.
