# Building, gifting, upgrading, and removing Samitivity

## Recipient requirements

Use a Windows x64 laptop with built-in Media Foundation and an ordinary Windows audio device. Windows 11 is the primary target; validate Windows 10 22H2 separately and ensure the OS remains supported. Windows N without media components cannot decode the required formats under the no-extra-installation rule. Windows S mode, managed policies, ARM64 emulation, secure desktops, and remote sessions are not promised. Bluetooth keyboards work through Windows' normal keyboard stack; reconnection and firmware behavior require the actual recipient hardware.

Installation is per user and should not show a UAC elevation prompt. Binaries go to `%LOCALAPPDATA%\Programs\Samitivity`. Notes/settings live separately under `%LOCALAPPDATA%\LittleNotes`. The app starts silently at Windows sign-in. A self-contained .NET runtime is bundled, so the recipient does not install .NET, codecs, or a media player.

## Windows build host or GitHub Actions

Install the SDK version listed in `global.json`. On a Windows build host:

```powershell
./scripts/build-windows.ps1 -Publish
./scripts/install-inno.ps1
./scripts/package.ps1
```

The second command downloads **build tooling**, verifies its pinned hash/publisher, and installs it in `.tools/inno`. It is not run on the recipient's PC. The resulting installer is `artifacts/installer/Samitivity-0.1.0-win-x64-setup.exe`. Build output is unsigned unless you arrange legitimate signing.

Alternatively, push this repository to GitHub and open **Actions → Build and test Samitivity**. The workflow compiles/tests on a Windows runner and uploads **Samitivity-win-x64-installer**, retained for 14 days. Download and unzip the artifact to get the installer. Logs and `.trx` reports are separate artifacts. The current local checkout has no configured GitHub remote, so Windows CI has not yet been run.

SDK, package dependencies/lock hashes, CI actions, and Inno compiler are pinned. Scripts reproduce the build procedure; signed installers, timestamps, absolute paths, and hosted-runner images prevent a blanket claim of bit-for-bit reproducibility. Record the commit, build log, SDK/runtime versions, artifact SHA-256, and test report for the actual gift build.

## Adding the private gift recordings

The source repository and CI artifact contain no personal recordings. Keep originals in a local folder outside Git, or under ignored `.private/`. Copy (do not move) original iPhone Voice Memos exports; M4A/AAC is decoded directly by Media Foundation. ALAC or other variants may depend on Windows capabilities and must be validated on the target. Do not rename an unsupported format to `.m4a` to try to make it work.

For a preloaded gift installer, create this local structure:

```
.private/BundledVoiceNotes/
  05c22a8f210d4dabbc9a116a9c236101/   Miss Me files
  05c22a8f210d4dabbc9a116a9c236102/   Motivation files
  05c22a8f210d4dabbc9a116a9c236103/   Just Because files
```

After the normal Windows publish:

```powershell
./scripts/package.ps1 -PrivateBundle .private/BundledVoiceNotes
```

Private bundling is intentionally refused in CI. The resulting installer contains those private bytes, so distribute it privately. On first launch the app validates each bundled file, imports eligible originals, and records a successful bundle receipt. No recording auto-plays. If import is interrupted, retry on next launch; content duplicates are skipped. Invalid bundles display a status error and do not produce a success receipt. Test the finished bundle before gifting. Management edits stay disabled until initial bundle import finishes.

The receipt prevents deleted gift notes from reappearing at every login. Later updates preserve it and do not automatically import new bundles; add later recordings through Configure → Import. This favors predictable user ownership over implicit changes during upgrades. The bundled copies remain in the application directory until uninstall; they receive the normal current-user permissions.

## Install and test

1. Open the installer, select Install, and let it configure startup and launch the tray process.
2. In the notification area, right-click Samitivity → Configure. Review shortcut/startup status; registration conflicts require reassignment and Save.
3. Test one recording per category and Stop. The Start menu shortcut opens the same window and can be pinned to the taskbar.
4. Close Configure. Shortcuts still work. Sign out/in and verify silent startup before gifting.

If you built the ordinary empty-library installer, import the supplied synthetic WAV fixtures to test before adding real notes. M4A/MP3/WAV imports use the standard file picker, preserve originals, and show decode errors in the UI. Save applies settings and pending file changes; closing a dirty window asks before discarding.

Startup is a visible per-user Task Scheduler entry named `Samitivity-<user SID>`. It uses the interactive session and least privilege. When registration is blocked, Configure reports a Startup-folder fallback with no automatic crash recovery. If both methods are blocked, Configure reports that the app must be opened from Start after sign-in. There is no elevation attempt. A task definition is not a guarantee of restart: run the acceptance probe and real-crash test in TESTING.md.

## Updates and uninstall

Use the same installer AppId and increase the application/installer version together for releases. An upgrade asks the running app to stop; unresolved unsaved changes prevent replacement. Configuration, history, and notes remain in LocalAppData. Back up that entire data folder before an upgrade if the recordings are irreplaceable; exit Samitivity before copying it for a consistent backup.

Uninstall from Windows Settings → Apps, or the Start menu uninstall shortcut. It requests a clean exit, removes the task/Startup shortcut, and removes application files. User notes, Trash, configuration, and history are preserved. To explicitly erase them, after uninstall delete `%LOCALAPPDATA%\LittleNotes` yourself. This is irreversible; there is no automatic data deletion option.

If Windows refuses to remove a startup task, uninstall stops and tells you to remove the Samitivity task before retrying. If a machine policy or service prevents the task service from being accessed at all, ask that machine's administrator to resolve the policy; do not disable security software.

## Signing and safe distribution

An unsigned new executable may trigger SmartScreen. This project does not bypass reputation checks. For a polished gift, use a legitimate Authenticode certificate/signing service, sign the published EXE/DLLs where appropriate, sign the final installer, and timestamp signatures. Keep credentials outside the repository and public CI. Signing establishes publisher identity but does not promise that warnings disappear.

Test the **downloaded final installer**, with its normal mark-of-the-web, on a standard account using default Defender and SmartScreen settings. Record the displayed publisher and warning behavior. If the artifact is flagged, investigate or submit a legitimate false-positive review. Do not request exclusions or protection changes.
