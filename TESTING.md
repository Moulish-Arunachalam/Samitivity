# Tests and release acceptance

## Evidence recorded in this development session

Development host: Apple Silicon macOS. SDK: 10.0.401. No Windows computer/VM or GitHub remote is attached to this checkout.

- **Executed:** cross-compilation of all four projects, zero warnings/errors (Release).
- **Executed:** 49 portable xUnit tests, all passing; results in `tests/Samitivity.Tests/TestResults/portable.trx`.
- **Executed:** self-contained `win-x64` publish, producing `artifacts/publish/Samitivity.exe` and bundled runtime/dependencies. This is not execution of that EXE.
- **Executed:** eight isolated packaging-orchestration/guard checks using a fake compiler, plus PowerShell syntax parsing. These do not compile an installer.
- **Not executed here:** Windows-only integration tests, Inno compiler, installed-app UI, Task Scheduler probe, real audio/hardware tests, signing/security checks, physical latency or idle-memory measurements.
- **CI prepared, not dispatched:** no GitHub remote is configured. A successful source build does not satisfy the Windows release gate.

Do not give the app as production-ready until the exact final installer passes the recipient-representative Windows checks below.

## Automated checks

macOS/Linux portable checks:

```sh
./scripts/test-portable.sh
```

Packaging orchestration checks (PowerShell on macOS or Windows; fake compiler, temporary files only):

```powershell
./scripts/test-packaging.ps1
```

Windows checks and publish:

```powershell
./scripts/build-windows.ps1 -Publish
```

Portable tests cover full shuffle cycles, boundary history, category independence, zero/one/two notes, restart persistence, dynamic additions/removals, corruption/default/backup handling, atomic temp recovery, configuration validation, creation/rename, import staging/cancellation/duplicates/unsafe paths, indexing and content identity across rename, rapid and concurrent playback requests, deterministic previews, stop/completion/cancel/error transitions, stale completion, and resource lifecycle contracts with an instrumented fake backend. These tests do **not** prove a Windows audio driver satisfies that backend contract.

Windows integration tests use synthetic PCM WAV and native Media Foundation encoders to produce M4A/AAC and MP3, then decode/import them. Corrupt fixtures are rejected; sample rates/channel counts are exercised. They test actual hotkey registration conflicts/reassignment for Stop and inspect the task XML's security/lifecycle settings. No physical keyboard or audio endpoint is needed for this CI suite. These tests must fail rather than silently skip a missing required decoder. Real WASAPI output and iPhone exports remain hardware acceptance tests.

## Windows acceptance matrix

Record Windows build/edition, PC model, architecture, audio driver, output endpoint, Bluetooth keyboard model, keyboard layout, app commit/version, installer SHA-256, and PASS/FAIL plus evidence for each row. An unrun check is **NOT RUN**, not PASS.

| Check | Procedure | Pass criterion |
| --- | --- | --- |
| Standard-user installation | Run the final installer from Downloads as a standard user, online first and then offline. | No UAC/admin requirement, external runtime/codec download, or unexpected permission prompt; tray appears and startup status is visible. |
| Bundled gift | Install a private test bundle with distinct files per category. Repeat launch. | Validated notes appear once, originals match SHA-256, no audio at install/login, no duplicate re-import. |
| Empty library | Use an empty test account or empty category. Press its shortcut. | Silence and informative status; app remains responsive. |
| Hotkeys across apps | In Notepad, browser text fields, Office, fullscreen software, and an elevated app, press each configured shortcut. | Works on supported interactive desktop; ordinary typing stays normal. Record apps/contexts that intercept or reserve it; reassign if needed. Secure desktops are excluded. |
| Physical accent key/layout | Type S and backtick/tilde normally. Test shortcuts on recipient layout; test AltGr characters if applicable. | Ordinary keys unchanged; category shortcuts map to intended virtual S. Any layout/AltGr conflict is disclosed and reassigned. Physical-position invariance is not promised. |
| Registration conflicts | Run another program reserving a test shortcut, then Save that assignment. | Failure shown for the affected binding, other bindings/Stop still usable; reassignment plus Save works. |
| Held key | Hold a category chord for several seconds. Release, press again. | One selection while held; a new selection only on the next distinct activation. |
| iPhone M4A | Import actual Voice Memos exports, including recipient's usual export method; test at least one unsupported/invalid variant. | Supported AAC plays without conversion; unsupported variants are rejected with a readable UI error, originals intact. |
| MP3 and WAV | Import mono/stereo examples at 22.05/44.1/48 kHz and listen through completion. | Correct duration, pitch, channel balance, complete ending; no media player/window launched. |
| Corrupt imports | Import truncated and random-data M4A/MP3/WAV, a zero-byte file, and an oversized file. | Decoder-rejected/invalid files never publish; errors shown; subsequent valid import works. Native decoders may tolerate some damage: record those cases. |
| Save/discard | Edit category/name/shortcuts, stage imports and deletions, then Close → No; Save → Cancel; finally Save → OK. | No staged edit applies before confirmed Save. Close warns; Cancel retains pending changes; confirmed Save reports saved state. |
| Preview | Preview saved note A while B plays, then use a category shortcut. | One voice at a time; preview plays chosen A; preview does not consume the random bag. |
| Twenty rapid presses | Activate same category 20 times, then alternate categories 20 times. | No overlapping voices, crash, abandoned output sessions, or continuing playback after final Stop; final accepted request wins. |
| Emergency Stop, separate gate | While playing, press default Ctrl+Alt+Backspace. Repeat with Ctrl+Shift+Delete assigned in UI; test tray Stop and UI Stop too. | Output stops/reset within measured device behavior; no restart until a fresh activation. Repeated Stop stays silent. No bare S/Backspace/Delete interception. |
| Fairness/restart | Use 7 distinct identifiable notes, record two cycles, exit mid-cycle and reopen. | All notes once per cycle; consumed interrupted note stays consumed; remaining cycle/history persists; categories independent. |
| Live file changes | Rename, copy, remove, and modify managed files through Explorer; let debounce settle. | Rename retains identity; identical bytes count once; missing/corrupt files are not repeatedly selected; Refresh recovers after temporary file locks. |
| Partial import/Save | Terminate the process while staging a large import, then during a multi-import Save. Relaunch. | Originals intact, no partial file plays, abandoned stage ignored/removed; any committed subset is discoverable and retry does not overwrite files. |
| Corrupt config/state | Exit; back up data; damage the primary JSON; relaunch; repeat with damaged backup. | Backup or defaults plus warning, all voice files preserved, no spontaneous playback. Restore test backup afterward. |
| Sign-out/sign-in | Sign out and back in, with lid/battery conditions representative of use. | One tray process, silent, shortcuts ready. |
| Reboot | Fully restart Windows and sign in. | Same silent ready state and persisted playlist. |
| Nonzero exit recovery | Run the probe below interactively as a standard user. | Report records a new LastRunTime after exit code 23, LastResult 23, and at least two runs. Otherwise recovery is unavailable/unverified. |
| Abrupt termination | Launch through installed scheduled task, note PID, use Task Manager → End task. Observe up to 90 seconds. | New PID/tray appears silently, shortcuts work, selected note remains consumed. Record actual result; do not infer from XML or from the probe alone. |
| Intentional exit | Tray → Exit, wait longer than restart interval. | No restart. Start menu launch works later. |
| Sleep/resume | Sleep during playback and while idle; resume after several minutes. | No unsolicited resumed audio; fresh shortcut plays through current default endpoint. |
| Explorer restart | Use Task Manager → Windows Explorer → Restart. | Tray icon returns without a second Samitivity process; hotkeys still work. |
| Bluetooth keyboard | Disconnect/reconnect keyboard and sleep/wake it; test all shortcuts after pairing reconnects. | No held-key replay, ordinary typing intact, hotkeys usable after Windows sees keyboard. Firmware-specific delays recorded. |
| Output devices | Switch default between speakers, wired and Bluetooth headphones; unplug/disconnect while playing; start with no device. | Playback stops safely on notifications/error; clear status and no notification storm; reconnect then fresh activation uses current multimedia default. |
| Offline | Disable network, launch, import local files, use all categories for a session. | All intended local functions work; no network connection/listener from Samitivity. |
| Upgrade | Install over an existing library mid-cycle. Attempt with unsaved UI changes first. | Unsaved changes block clean shutdown until resolved; after upgrade notes/config/history preserved; only one process. |
| Uninstall | Uninstall with notes present, then sign in again. | No startup task/shortcut/process remains; personal data preserved; reinstall recovers it. |
| SmartScreen/Defender | Download the exact final installer; run/scan with defaults enabled. | Record warnings/publisher/scan result; investigate blocks without disabling protections. No guarantee that an unsigned build avoids warnings. |
| Long-running stability | Leave idle overnight, do 100 play/switch/stop cycles, then repeat idle measurement. | No sustained idle work or unbounded memory/handle growth, no hung sessions. Record actual resource measurements. |

## Task Scheduler probe

Run from an interactive standard-user PowerShell on the target/test laptop:

```powershell
./scripts/test-task-recovery.ps1 -Executable "$env:LOCALAPPDATA/Programs/Samitivity/Samitivity.exe"
```

The script creates a uniquely named temporary **diagnostic** task whose action is the app's explicit `--diagnostic-exit-23` mode. This mode exits 23 without a tray or audio. The script observes restart timestamps for 145 seconds, writes `artifacts/task-recovery.json`, and removes its task in `finally`. If the shell is forcibly killed, remove only that `Samitivity-RecoveryProbe-*` task manually. It is bounded to two retries. It does not alter the installed production task.

A successful probe verifies one failure class on that PC. Separately test abrupt process termination of the installed task, sign-in, batteries, service restrictions, and the three-attempt limit. Startup-folder fallback cannot restart a crashed process. Record that reduced guarantee rather than treating fallback as a failure of ordinary sign-in startup.

## Performance and acoustic latency

No latency, CPU, or memory numbers have been measured on Windows in this session.

1. Use the same synthetic tone for repeatability. Warm the app once; measure both cold and warm starts across at least 30 category activations. Include built-in speakers, wired headphones, and Bluetooth separately.
2. Use a high-frame-rate camera/independent audio capture to observe the key action and the first **audible** output, or instrument a Windows test harness with a timestamped input event and WASAPI loopback capture. Do not label `Play()` return, decoder completion, or a UI status change as acoustic onset. Loopback excludes downstream Bluetooth/acoustic delay; document that distinction and measurement uncertainty.
3. Report median, p95, max, sample count, measurement method/resolution, output type, cold/warm state, and any dropped activations. Measure Stop-to-silence similarly. Agree the recipient's acceptable responsiveness after hearing the measured build; no invented target is used here.
4. With no playback or open management window, run `scripts/measure-idle.ps1 -Seconds 60`. Record CPU milliseconds, working/private memory, handles, and threads. Repeat after the stress/overnight test. Use Resource Monitor/ETW if sustained disk or CPU activity appears.

Record results in [docs/WINDOWS-VALIDATION.md](docs/WINDOWS-VALIDATION.md). Release remains blocked on this evidence, even if CI is green.
