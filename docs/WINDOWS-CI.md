# Run Windows CI and download a release

The repository is https://github.com/Moulish-Arunachalam/Samitivity. Version tags publish permanent GitHub Releases after Windows and portable CI pass. Never add personal audio or credentials.

1. Use the existing **private** `Moulish-Arunachalam/Samitivity` repository. Keep its visibility private.
2. For the initial source publication, run the following from your local checkout. Skip the initial commit/remote setup if they already exist.

```sh
cd /path/to/Samitivity

git add .github .gitignore Directory.Build.props global.json \
  Samitivity.slnx Samitivity.Portable.slnx \
  src tests scripts installer samples docs \
  README.md ARCHITECTURE.md TESTING.md SECURITY.md INSTALLATION.md \
  THIRD-PARTY-NOTICES.md

git diff --cached --check
git diff --cached --name-only
# Review this list. Only the two synthetic WAV fixtures should be audio.
# No .private files, personal recordings, build outputs, or signing credentials.

git commit -m "Build Samitivity offline Windows voice-note companion"
git remote add origin https://github.com/Moulish-Arunachalam/Samitivity.git
git push -u origin main
```

3. Open the repository's **Actions** tab. Enable Actions if GitHub asks. The push automatically starts **Build and test Samitivity**. Alternatively, select that workflow → **Run workflow** → branch `main` → **Run workflow**.
4. Wait for **both** `portable` and `windows` jobs to succeed. The Windows job restores locked packages, compiles all projects, runs portable/native tests, publishes self-contained x64, runs packaging guards, verifies/downloads Inno Setup, and compiles the installer. A macOS-only green result is insufficient.
5. Open [Releases](https://github.com/Moulish-Arunachalam/Samitivity/releases) or use the [stable latest installer link](https://github.com/Moulish-Arunachalam/Samitivity/releases/latest/download/Samitivity-latest-win-x64-setup.exe). Every release lists its version-specific installer and SHA-256 file; prior releases remain downloadable. These installers are unsigned and have an empty library. The Actions artifact expires after 14 days and is intended for build diagnostics.
6. If a Windows step fails, open that step's logs and download **Samitivity-test-results** if present. Fix the actual error and push a new commit. Do not skip failing native tests or claim the installer passed based on a source build. Windows Server hosted runners may differ from a recipient's Windows edition, so preserve both CI and laptop evidence.
7. Test the installer under [TESTING.md](../TESTING.md) and record results in [WINDOWS-VALIDATION.md](WINDOWS-VALIDATION.md). Windows CI does not replace real keyboard/audio/installer acceptance. Build the real private bundle locally using [INSTALLATION.md](../INSTALLATION.md); never put it into a public CI artifact.

Optional, if GitHub CLI is installed and authenticated later:

```sh
gh run list --workflow windows.yml --limit 5
# Replace RUN_ID with the successful run's numeric ID.
gh run watch RUN_ID --exit-status
gh run download RUN_ID --name Samitivity-win-x64-installer --dir artifacts/download
```

The `gh` commands are optional; GitHub's web UI suffices. To create a release, update the project version, push the commit, and push a matching `vMAJOR.MINOR.PATCH` tag. To backfill an existing historical version whose tag predates this release workflow, use **Actions → Build and test Samitivity → Run workflow** on `main`, set **version_tag** to that existing tag, and wait for both test jobs and the release job. The tag must match that commit's application version. Older commits that do not contain `scripts/test-installer.ps1` skip only that later-added installer smoke test; their builds, native tests and packaging checks still run.
