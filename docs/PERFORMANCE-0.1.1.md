# Samitivity 0.1.1: performance and physical validation

This is an instrumented performance revision, not a claim that hardware latency targets have been met. The reported 3–25 second playback, 10–20 second import and roughly 20 second save times are v0.1.0 laptop observations. There are no equivalent v0.1.1 physical measurements yet.

## Evidence and changes

- `WindowsAudio.cs`: v0.1.0 opened Media Foundation and WASAPI for every selection. NAudio 2.2.1's default `MediaFoundationReader` creates/releases a source reader in its constructor and creates it again on first read. We now use `SingleReaderObject` with decoder lifetime entirely on MTA workers (validation can also run entirely on STA). [Pinned upstream implementation](https://github.com/naudio/NAudio/blob/v2.2.1/NAudio.Wasapi/MediaFoundationReader.cs).
- Short recordings (at most 30 seconds and 8 MiB decoded each) use a 48 MiB LRU PCM cache. Source path, size and modification time identify a revision. Old revisions are not reused and are evicted normally. No PCM is stored on disk. Up to twelve recordings are warmed after readiness; a shortcut cancels warming. A miss prepares asynchronously and shows Preparing. Oversized/long recordings stream with bounded native buffering. This is intentionally optimized for the reported 2–5 second recordings.
- A compatible WASAPI shared-mode output is reused between sessions and remains stopped while idle, without a continuously rendering silent stream. A format change, device notification, resume or playback error rebuilds it. Each request retains its own source and completion handler. Cancelled native preparation/initialization owns and eventually releases its resources without starting stale playback; only one decoder and one output initialization can be in progress. Native calls themselves cannot be forcibly interrupted safely. A short 5 ms interruption fade has an 8 ms allowance before Stop joins the render thread.
- `LibraryService.Stage` already copied and decoded through EOF before returning. “Import on Save” did not mean validation was postponed. The UI now says “Validated · ready to save.” It reports copy/validation and supports cancellation between copy/decode chunks. No playback device or external process is used for validation. Original bytes are retained; there is no transcoding.
- `TrayApplication.SaveAsync` previously stopped playback, waited for maintenance, and requested a full library refresh even for a name/shortcut edit. Metadata-only saves now avoid those waits; media saves commit already-validated staging, preserving the existing recoverable individual operations. No post-save scan is required. Unchanged refreshes no longer write the index.
- `AtomicJsonStore` previously serialized and durably flushed both a backup and primary on every save, including every shuffle selection. It now durably writes one temporary file and uses atomic replacement to retain the previous valid primary as backup. A corrupt primary still cannot replace a valid backup. Shuffle consumption still commits before playback: durability has not been traded away. Storage latency can therefore still affect selection; diagnostics expose it.
- No recording playback, import or configuration save launches an external process. Explorer opens only when explicitly requested; startup setup remains separate.

These code paths demonstrate redundant work and waits. They do **not** establish which native call or storage operation accounted for each observed multi-second stall; the laptop log is needed to attribute that.

## Diagnostics

Choose **Configure → Diagnostics**. Logs are in `%LOCALAPPDATA%\Samitivity\logs\performance.tsv` and `performance.tsv.previous`. They contain monotonic elapsed milliseconds, numeric request IDs, fixed stage names and durations, never filenames, category names or audio. Each session is capped at 20,000 lines and retains one previous log. Events enter a bounded nonblocking queue; disk writes occur on a background worker. A busy logger may drop events. Close the duplicate instance before reproducing; only the primary application writes performance logs.

Stages include process entry, hotkeys registered, startup ready, reconciliation, shortcut received, selection, preparation, decoder initialization, WASAPI initialization, output submission, first PCM read, import copy/validation/total, atomic persistence and configuration save.

`request_to_output_submission` ends at `WasapiOut.Play()` returning. `request_to_first_pcm_read` ends when the render provider supplies its first nonempty PCM buffer. This is closer to rendering but **not audible output**: Windows buffering, the device and Bluetooth add latency. Neither metric substitutes for an acoustic measurement. Request IDs correlate selection, submission and first-read events. Decoder/output stages have session timestamps for correlation.

To summarize request latency in PowerShell (no installation needed):

```powershell
$rows = Import-Csv "$env:LOCALAPPDATA\Samitivity\logs\performance.tsv" -Delimiter "`t"
$rows | Where-Object stage -eq 'request_to_first_pcm_read' |
    Select-Object request, duration_ms
```

Synthetic Windows tests print cold and cached **preparation-only** timings into the test report, with no brittle timing assertion. They do not open a playback device. Native WASAPI reuse, output errors and acoustic latency require hardware testing.

## Compatibility and remaining limits

Configuration, index and shuffle schemas remain version 1. Installer AppId, per-user data paths, shortcuts and startup policy remain unchanged. Install 0.1.1 over 0.1.0; notes/settings/history remain outside the binary directory. The CI installer contains no private notes. Scheduled Task registration means configured, not verified recovery. The existing startup message deliberately remains unverified until the physical recovery tests pass; fallback has no automatic crash recovery. See `scripts/test-task-recovery.ps1` and TESTING.md for nonzero-exit and termination distinctions.

The WinForms refresh uses warm off-white, lavender controls, rounded buttons, a spacious category list, modern typography and a prominent status/progress footer. Stop stays available during management operations. Save remains non-cancellable once committing; Close protects pending changes. No rendering framework, network feature or dependency was added. Visual layout, keyboard navigation and scaling still require Windows inspection at 100%, 125%, 150% and 200% DPI.

A cache hit performs file metadata checks so modifications/removals are detected. Unchanged-size files with a deliberately preserved timestamp are outside this cache's revision model (also outside the existing index model). Cached PCM may remain in process memory until eviction/exit. Long-file streaming can still wait on a native read during Stop. Extremely slow native initialization is bounded in concurrency, not in operating-system call duration; it can delay a later uncached request. Capture diagnostics rather than assuming all latency has been solved.

## Laptop acceptance checklist

1. Back up the Samitivity folder; install the 0.1.1 installer over 0.1.0. Confirm existing categories, recordings and shortcuts survive. Startup must remain silent.
2. Cold launch: press a category shortcut promptly, then repeat at least twenty times, including after full completion. Record perceived delay and first-PCM-read timings. Targets: warm <150 ms; cold <500 ms where practical. Any unexplained multi-second delay fails acceptance.
3. Switch categories rapidly and press Emergency Stop (default Ctrl+Alt+Backspace) during preparation and playback. Pass: no overlap, stale playback or unintended restart; silence promptly after buffered audio clears.
4. Import two real short iPhone M4As, synthetic WAV and MP3; also a corrupt file. Pass: progress is visible, UI and Stop respond, valid files stage safely, corrupt files show errors, cancellation leaves no partial note. Target small-file import <2 seconds; retain exact log results.
5. Save imports; edit only a name and save again. Pass: no repeated validation, settings persist after relaunch; target metadata save <500 ms. Test Save/Close warnings, preview, deletion and shortcut conflict messages.
6. Sign out/in, sleep/resume and switch/unplug audio/Bluetooth devices. Pass: silent readiness and playback through the current default device after the next shortcut.
7. Run the documented scheduled-task recovery probe, then test actual app termination via the documented task-launched procedure. Record configured/verified/failed separately. Registration alone is not a pass.
8. If anything remains slow, copy both performance TSV files before relaunching again and record Windows version, output device, recording format/duration and whether the UI stayed responsive. Do not send personal recordings by default.
