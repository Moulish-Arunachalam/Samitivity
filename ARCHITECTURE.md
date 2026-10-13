# Architecture and decisions

## Scope and platform boundary

Names use **Samitivity** consistently; `LittleNotes` is retained only as the requested data-directory name. .NET 10 is the current LTS choice. Native WinForms avoids a web runtime. Windows-specific code is isolated behind `IAudioBackend`, `IAudioValidator`, and `IRecordingSource`. `IRandomSource` allows deterministic tests; production uses bounded cryptographic `GetInt32` calls.

Runtime dependency: NAudio.Wasapi 2.2.1 and its NAudio.Core 2.2.1 dependency. These provide a small, established Media Foundation/WASAPI surface. Versions and transitive hashes are locked. No codec download or transcoding fallback exists. Newer NAudio major releases need explicit regression testing rather than an automatic upgrade.

Target: native Windows x64 laptops with an audio endpoint and Media Foundation. Windows 10 and Windows 11 x64 are the intended targets. The installer admits all Windows 10 builds as of 0.1.2, including 1909; actual runtime/audio compatibility on historical builds requires separate testing. See docs/COMPATIBILITY-0.1.2.md for the distinction from Microsoft lifecycle support. Windows 7/8, 32-bit Windows, S mode restrictions, Windows N without media components, and arbitrary managed corporate policies are outside the guarantee. ARM64 x64 emulation is not validated. PC brands do not change the design; firmware, keyboard layouts, audio drivers, and Windows editions still affect it.

## Hotkey and playback flow

1. A hidden top-level native window receives WM_HOTKEY. RegisterHotKey includes MOD_NOREPEAT. No hook, raw-input logger, polling, or scan-code interception is installed.
2. The handler enqueues an intent and returns without opening an audio file. A single worker owns session sequencing. There is at most one pending intent; newer requests replace unstarted requests and cancel the active token. Superseded pending requests consume no notes.
3. The worker filters missing files, selects through the shuffle engine, and atomically persists consumption before invoking audio. A selected note remains consumed if interrupted, decoding fails, or cancellation arrives just after selection. If persistence fails, playback is refused and the in-memory bag is unchanged.
4. The backend prepares short recordings into a byte-bounded PCM LRU cache, using one Media Foundation source reader on MTA workers. Cache misses and native output initialization happen asynchronously; abandoned preparations never submit playback. Long recordings use bounded streaming. See [0.1.1 limits and diagnostics](docs/PERFORMANCE-0.1.1.md).
5. A compatible stopped WASAPI shared-mode output is reused, with a fresh source/completion handler per session. A 5 ms best-effort fade gets an 8 ms allowance before Stop joins the render thread. Device/power changes and errors invalidate output. Old callbacks only complete their own session; generation checks suppress stale controller status. Native drivers can retain buffered audio.

The fade and buffer sizes are engineering settings, **not measured latency promises**. The device can retain already-buffered audio; Bluetooth adds its own latency. A stalled native decoder/driver can delay cancellation. No managed design can guarantee an immediate acoustic stop on arbitrary hardware. The Stop action stops and resets; it does not retain a resume position.

Each distinct shortcut press selects a new note. The requested double-activation window had no specified action, so no timer or double-press behavior is introduced. Manual Preview is an explicit, deterministic selection through the same controller; it does not advance the random bag. Every random selection goes through `ShuffleEngine`.

## Persistent fairness

Each stable category GUID owns independent `Known`, `Remaining`, and `Recent` IDs. A recording's ID is its SHA-256 content digest within the category, independent of its filename. Identical bytes count as one eligible note even if multiple external copies exist; changed bytes count as a different note.

- A new category starts with a uniform random permutation, built by uniform insertion. Subsequent full bags use unbiased Fisher–Yates.
- Every eligible note is consumed once before another full cycle begins.
- Choose the first remaining shuffled entry outside the last three selections. If none is available, drop the oldest history exclusion until an entry is possible. This avoids the most recent entry whenever an alternative remains. The fairness constraint may force recent selections near the end of a bag; selection never skips the rest of a cycle to satisfy history.
- Newly observed IDs are inserted at a uniformly chosen position in the **current** remaining bag. Already-consumed existing IDs remain consumed. Additions delay the next cycle; continuously adding files can continuously extend a cycle.
- Removed IDs are pruned when that category is selected again. Reintroduced IDs are newcomers after a reconciliation that observed their removal. If a removal/re-addition happens entirely between selections, content identity retains the previous state. An entirely empty category does not advance the bag.
- Renames keep the content ID/history. Editing audio creates a new ID. Category renames preserve their GUID and directory.
- Zero notes means silence/status; one repeats on each request; two alternate, including boundaries.

Bag order is uniform before history constraints; the final ordering is deliberately constrained by recent-repeat avoidance. It is not claimed to be an unconstrained uniform permutation.

## Storage and recovery

```
%LOCALAPPDATA%\LittleNotes\
  config.json             categories, display names, shortcuts
  config.json.bak         last valid prior configuration
  state.json[.bak]        shuffle and recent history
  library.json[.bak]      content IDs, paths, durations, fingerprints
  VoiceNotes\<GUID-N>\   managed original-format audio
  .staging\              pending imports; cleared after a process restart
  Trash\                 explicitly deleted managed recordings
  logs\last-error.txt    bounded last-fatal-error type/HRESULT/time
  logs\performance.tsv   bounded asynchronous monotonic timings (one previous session retained)
  startup-status.txt
  bundle-imported-v1.txt  first successful bundled import receipt
```

Binaries live under `%LOCALAPPDATA%\Programs\Samitivity`; updates do not replace data. Atomic stores write and flush a same-directory unique temporary file, then rename over the destination. Atomic replacement retains only a valid prior primary as the backup, avoiding a second durable serialization. Semantic validation rejects malformed schemas as well as malformed JSON. Partial temp files are ignored. On primary failure load the backup, otherwise defaults, and show a warning. Voice files are preserved. A filesystem/hardware failure can still lose the last commit; backup recovery can roll history back one commit, so strict never-repeat guarantees cannot survive arbitrary corruption. Windows process crashes and ordinary interrupted writes are the intended recovery boundary; there is no claim of power-loss durability for every storage controller.

Imports stage a bounded copy, decode through EOF, compute SHA-256, then move to a unique content-prefixed filename only on Save. Category directories are derived solely from GUIDs. Unsafe path components, linked/reparse paths, and network import paths are rejected. Import limits are 256 MiB and two hours. Managed originals keep exact bytes. Some malformed media can be accepted by permissive native decoders; full decoding is not a proof that a file is semantically perfect.

Save consists of individually durable configuration/import/delete operations, **not** a multi-file transaction. An interrupted Save may apply a subset; the UI reports partial failure and allows retry. Orphaned completed copies are recovered by indexing. Unpublished stage files are discarded on restart. Delete moves all indexed copies of an identity to Trash. Trash and removed category directories persist until the user removes them manually.

Startup indexes active category folders. A recursive FileSystemWatcher debounces subsequent changes for 750 ms; there is no idle scanning timer. Unchanged length/mtime fingerprints avoid repeat hashing/decoding. Explicit Refresh retries unreadable files. The library mutation lock serializes scan/import/delete; a separate snapshot lock permits UI reads during long decodes. Watcher overflow schedules a full reconciliation. This is a personal-sized library design, not a large media catalog.

## Lifecycle

A per-user named Global mutex prevents multiple instances across sessions. Named events let a subsequent Start/taskbar launch open the existing management window and let the installer request graceful exit. No TCP/HTTP IPC is used. Explorer's TaskbarCreated broadcast restores the tray icon. OS power and audio-endpoint events stop playback; the next explicit request creates fresh output resources. Device changes do not transfer a partially played note to speakers or resume it automatically.

The installer requests a visible, per-user Task Scheduler task with InteractiveToken, least privilege, logon trigger, no battery/network/idle conditions, no execution-time limit, and three restart attempts at one-minute intervals. The installer starts the app through this task so Task Scheduler owns the process. A normal user Exit returns zero and should not restart. Unexpected background failures log a bounded error and return nonzero; there is no blocking crash dialog in background mode.

Task configuration is not evidence that restart actually happens. `test-task-recovery.ps1` records observed nonzero-exit restarts. Abrupt termination, Task Scheduler service behavior, batteries, and logon still require manual checks. If task registration fails, create a disclosed Startup-folder shortcut. If both methods fail, expose startup unavailability. Fallback has no crash-restart guarantee. No external watchdog is present.

## References

- [Microsoft .NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)
- [RegisterHotKey contract and reserved keys](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey)
- [Media Foundation native formats](https://learn.microsoft.com/en-us/windows/win32/medfound/supported-media-formats-in-media-foundation)
- [RestartOnFailure task setting](https://learn.microsoft.com/en-us/windows/win32/taskschd/taskschedulerschema-restartonfailure-settingstype-element)
- [Pinned NAudio WASAPI implementation](https://github.com/naudio/NAudio/blob/v2.2.1/NAudio.Wasapi/WasapiOut.cs)
