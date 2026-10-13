# Samitivity 0.1.2 compatibility audit and acceptance

## Confirmed findings

The physical Windows 10 Pro PC reports **1909 / 18363.592**. Both earlier installers specified `MinVersion=10.0.19045`. Inno rejected the installer before the bundled application or .NET host ran. This directly explains that PC's exact error. The user reports v0.1.0 did run on another Windows 10 Home PC (build not captured); this is anecdotal evidence, not coverage for every Windows 10 version.

Following the request to include older Windows 10 releases, 0.1.2 changes the installer minimum to `10.0`, retaining x64-compatible architecture, per-user installation and the same AppId. Tests check the declared policy against builds 10240, 14393, 18363, 19045 and Windows 11. These are declaration tests, not executions of those operating systems. There is no new application-level 22H2 or Windows 11 restriction. No OS upgrade is required merely to pass this installer gate.

The CrossOver Windows 10 bottle message matches Inno's `WindowsVersionNotSupported` message. Its actual reported build remains unknown. A Windows 11 bottle getting past this gate establishes only installation under that compatibility layer. It does not establish native Windows import or playback behavior.

### Runtime and API audit

- `net10.0-windows`, WinForms, SDK 10.0.401 and locked runtime assets; publishing explicitly uses `win-x64 --self-contained true`, with trimming disabled. No separate .NET installation is needed. Existing NAudio.Wasapi/Core 2.2.1 packages remain pinned. No runtime package or native decoder was added.
- `net10.0-windows` is a Windows target, not a Windows 11 target. The manifest declares the Windows 10 compatibility GUID and `asInvoker`. RegisterHotKey, WASAPI, Media Foundation, Task Scheduler COM, mutex/events and tray APIs are not Windows 11-only APIs. No Windows 11-only call was identified in the application source.
- **Removing an installer gate is not proof that every historical Windows build runs the bundled runtime.** Microsoft's current .NET 10 supported-OS list is lifecycle-based, lists selected Windows 10 LTSC versions, and does not certify ordinary Windows 10 Pro 1909 or 22H2. Compatibility testing on those builds is our responsibility. We are not downgrading runtimes or claiming Microsoft support for obsolete releases. Native x64 Windows 10 and 11 are the intended application targets; older builds are allowed but remain unvalidated until tested. x64-compatible ARM emulation remains unvalidated.
- Standard Windows includes native AAC, MP3 and WAV support. Windows N editions without Media Foundation and compatibility layers may lack required components. This release does not install codecs or silently download prerequisites, and cannot promise those environments work.

### Audio and error audit

The UI calls `Task.Run(() => Library.Stage(...))`. Stage checks local source paths, extension and size, rejects linked/reparse paths, copies to private staging, decodes through EOF using WindowsAudioValidator, and hashes the preserved original bytes. Save commits that staged copy. Playback uses MediaFoundationReader and WASAPI, with the v0.1.1 bounded cache. MediaFoundationReader initializes Media Foundation internally; initialization is not omitted. The v0.1.1 SingleReaderObject change keeps native lifetime on MTA worker threads and removes duplicate source-reader creation. There is no evidence yet that this change caused a native Windows compatibility defect.

**Confirmed diagnostic defect:** the UI discarded exception messages, reasons and native error codes, presenting everything as unsupported/damaged/inaccessible. InvalidDataException can also mean a network-mapped source, empty file, unsupported extension, empty decoder output, duration limit, sample-rate/channel restriction or mid-stream format notification. Thus the previous error does not prove codec failure or corrupt audio.

0.1.2 adds allowlisted rejection reasons, native HRESULT/inner-exception types, decoded format metadata and reported OS/runtime architecture. It logs no audio, category names, source paths or exception messages. It also catches managed startup failures that previously occurred before the tray-level catch. Errors before .NET enters Main still require host tracing or Windows Event Viewer.

**Hypotheses requiring evidence:** CrossOver may report a mapped macOS drive as a network source; its Media Foundation implementation may reject the codec or produce no PCM; a file may contain an unsupported M4A codec such as a variant not handled by the installed media stack. None is established. A `.m4a` extension identifies a container, not an AAC guarantee. The backend is retained because the native Windows environment has not shown a confirmed decoder defect. A bundled alternative would add decoder licensing/security maintenance, native architecture distribution and size costs without an established native failure to fix.

## What CI establishes

The `windows-2025` image is **Windows Server 2025**, not Windows 11. GitHub's standard x64 runner list does not provide a native Windows 10 22H2 or Windows 11 x64 image. No licensed client VM or self-hosted client runner has been provided for this task. We do not create a fictitious client-OS job or relabel Server results. GitHub's Windows 11 ARM runner would still be a different architecture/environment.

Previous MP3/M4A tests already exercised Library.Stage and Commit with the real native validator, but generated files with Microsoft's own encoder. New production-path tests explicitly run Stage on a worker, commit, reopen the index, compare exact original bytes and prepare decoded PCM for mono/stereo AAC/M4A, MP3 and PCM WAV. Corrupt inputs, portable playback lifecycle, cancellation, cache behavior and version-1 data tests remain enabled. These synthetic fixtures do not substitute for iPhone exports or third-party MP3 encoders.

CI now runs the **real installer**, checks bundled runtime files and the installed managed exit-code probe, checks entry into the Windows message loop, reinstalls over valid version-1 configuration/history/notes, and verifies byte preservation after reinstall and uninstall. This is an install-over-existing smoke test of the current installer, not a historical 0.1.1 executable upgrade or standard-user/client-Windows test. Hosted CI accounts are privileged; native standard-user installation and Task Scheduler behavior still need physical validation. Hardware WASAPI output is not exercised by the decoder tests.

## Physical acceptance: Windows 10 1909 first, then 22H2 and Windows 11

1. Record `winver` and whether System type is x64. Use a normal account. Back up `%LOCALAPPDATA%\LittleNotes` if it exists; do not uninstall/delete data before testing an upgrade.
2. Download and extract **Samitivity-win-x64-installer** from the successful 0.1.2 Actions run. From Command Prompt in the extracted folder run:

   ```bat
   Samitivity-0.1.2-win-x64-setup.exe /LOG="%TEMP%\Samitivity-setup.log"
   ```

   Pass: no obsolete 22H2 rejection on 1909, no elevation request, no separate .NET/codec installer, and startup reaches the tray silently. Capture exact error text and the setup log if installation fails. Do not use Windows compatibility mode to change the reported version.
3. Open Configure. Copy test files into a **local** folder first. Import an ordinary iPhone Voice Memos AAC M4A, an ordinary MP3 and a PCM WAV. Pass: each validates and saves, originals remain byte-identical, and preview/category playback is audible. Try a deliberately corrupt copy; pass: rejected with a useful reason, no partial library entry. A lossless/other M4A variant is a separate codec test, not an AAC test.
4. Press category shortcuts twenty times, switch categories, stop during preparation/playback, change the default audio device, sleep/resume. Pass: no overlap, stale starts, unrecoverable output failure or multi-second unexplained stalls. Check logs if timing remains poor.
5. Install over an existing 0.1.0/0.1.1 installation and verify category names, shortcuts, notes and shuffle history persist; restart and repeat playback. Test uninstall on a backed-up test library: notes/settings/history must remain. Repeat sign-in and crash recovery under TESTING.md; task registration alone is not recovery verification.
6. Run the included collection script from PowerShell (read-only, no elevation):

   ```powershell
   & "$env:LOCALAPPDATA\Programs\Samitivity\collect-compatibility.ps1"
   ```

   If script policy blocks it, do not change policy: manually copy `%LOCALAPPDATA%\LittleNotes\logs\performance.tsv`, `.previous`, `last-error.txt` if present, and `startup-status.txt`, plus the winver details. The script otherwise creates `Samitivity-diagnostics` on the desktop, containing selected OS/build/architecture metadata and those logs only. Review before sharing. The separately captured setup log can contain account/install paths; redact them if needed. Never attach personal audio by default.
7. If the installed EXE fails before app logs exist, run in Command Prompt:

   ```bat
   set DOTNET_HOST_TRACE=1
   set DOTNET_HOST_TRACEFILE=%TEMP%\Samitivity-host.txt
   "%LOCALAPPDATA%\Programs\Samitivity\Samitivity.exe" --configure
   ```

   Host traces may contain local paths; review before sharing. Capture the exact dialog and relevant Application event from Event Viewer if no host trace appears.

Run this checklist separately on 1909, 22H2, and Windows 11; record pass/fail, OS build, app version and real file format. No physical acceptance result is claimed in this document. CrossOver results belong in a separate, unsupported-compatibility-layer column.

## Primary references

- [Inno minimum version contract](https://jrsoftware.org/ishelp/topic_setup_minversion.htm)
- [Pinned Inno message source](https://github.com/jrsoftware/issrc/blob/is-6_7_3/Files/Default.isl)
- [.NET 10 current supported OS policy](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md)
- [Microsoft native Media Foundation formats](https://learn.microsoft.com/en-us/windows/win32/medfound/supported-media-formats-in-media-foundation)
- [GitHub runner availability](https://docs.github.com/en/actions/reference/runners/github-hosted-runners)
- [Windows Server 2025 runner image](https://github.com/actions/runner-images/blob/main/images/windows/Windows2025-Readme.md)
