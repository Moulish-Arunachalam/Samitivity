# Run Windows CI and download the installer

The private repository is https://github.com/Moulish-Arunachalam/Samitivity. The first push triggers Windows CI. These instructions cover source-only publication; do not add personal audio or credentials.

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
5. Open the successful workflow run's summary. In **Artifacts**, click **Samitivity-win-x64-installer**. Download the ZIP and extract `Samitivity-0.1.0-win-x64-setup.exe`. This is an **unsigned, empty-library installer**, not the private gift bundle. Artifacts expire after 14 days; rerun the workflow if necessary.
6. If a Windows step fails, open that step's logs and download **Samitivity-test-results** if present. Fix the actual error and push a new commit. Do not skip failing native tests or claim the installer passed based on a source build. Windows Server hosted runners may differ from a recipient's Windows edition, so preserve both CI and laptop evidence.
7. Test the installer under [TESTING.md](../TESTING.md) and record results in [WINDOWS-VALIDATION.md](WINDOWS-VALIDATION.md). Windows CI does not replace real keyboard/audio/installer acceptance. Build the real private bundle locally using [INSTALLATION.md](../INSTALLATION.md); never put it into a public CI artifact.

Optional, if GitHub CLI is installed and authenticated later:

```sh
gh run list --workflow windows.yml --limit 5
# Replace RUN_ID with the successful run's numeric ID.
gh run watch RUN_ID --exit-status
gh run download RUN_ID --name Samitivity-win-x64-installer --dir artifacts/download
```

The `gh` commands are optional; GitHub's web UI suffices. See the latest Actions run for actual job and artifact results; the instructions alone are not validation.
