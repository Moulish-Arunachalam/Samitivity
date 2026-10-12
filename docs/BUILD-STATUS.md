# Local build and test status — 2026-10-12

Host: Apple Silicon macOS. SDK: **10.0.401**. All existing source retained; audit corrections are described in REQUIREMENTS-AUDIT.md. Repository has no commit/remote; no CI run was dispatched.

| Check | Result |
| --- | --- |
| Full solution restore with `--locked-mode` | PASS |
| Full Release build, all four projects | PASS — 0 warnings, 0 errors |
| Checked-in `scripts/test-portable.sh` | PASS — 49 passed, 0 failed, 0 skipped |
| `scripts/test-packaging.ps1`, isolated fake compiler | PASS — 8 orchestration/privacy cases |
| PowerShell parser, packaging/build/recovery scripts | PASS — syntax only |
| Shell/XML/JSON/synthetic WAV structural checks | PASS |
| `dotnet publish`, `win-x64`, self-contained, locked restore | PASS |
| Published apphost PE architecture | AMD64 / x64 verified |
| Bundled .NET / Windows Desktop runtimes | 10.0.12 / 10.0.12 |
| Bundled CLR, hostfxr, hostpolicy, WinForms, NAudio, app assemblies | Present |
| Whitespace check on all untracked text source | PASS |
| Windows-only integration cases (10) | NOT RUN — compiled only |
| Real Inno Setup compiler / installer execution | NOT RUN — requires Windows |
| Windows laptop/manual acceptance/performance | NOT RUN |

The dependency-lock issue was resolved by declaring `win-x64` in shared RuntimeIdentifiers and retaining matching RID sections in all four lock files. A later ordinary locked restore and a later self-contained publish both pass; no unlocked CI restore is needed.

Final command sequence executed:

```sh
dotnet restore Samitivity.slnx --locked-mode
dotnet build Samitivity.slnx --no-restore -c Release -p:UseSharedCompilation=false -m:1
./scripts/test-portable.sh
dotnet publish src/Samitivity.Windows/Samitivity.Windows.csproj -c Release -r win-x64 --self-contained true -p:RestoreLockedMode=true -p:PublishSingleFile=false -p:UseSharedCompilation=false -o artifacts/publish
```

The official SDK was installed in a temporary tools directory; `SAMITIVITY_DOTNET` selected it for the portable script. Portable test-runner IPC required execution outside the restricted sandbox. Official PowerShell 7.5.4 was temporarily installed under `/tmp` for parsing and orchestration checks; no PowerShell dependency was added to the application.

Local outputs (ignored by Git):

- `artifacts/publish/`: self-contained Windows application directory; keep all its files together. It is **not an installer**, and its EXE has not been run on Windows.
- `tests/Samitivity.Tests/TestResults/portable.trx`: final 49-test report.
- Apphost SHA-256 for this local build: `1104d490f4f4eb433f831355e38f884b3d29b5f8973473d287f024508c555624`. This hashes the apphost alone, not the complete directory or a finished installer.

Windows compilation/runtime tests are distinct. Packaging tests use a fake compiler and do not validate Inno syntax, privileges, COM scheduling, or Windows security prompts. The build remains an engineering candidate, not a validated gift release.

Next: follow [WINDOWS-CI.md](WINDOWS-CI.md), then complete [WINDOWS-VALIDATION.md](WINDOWS-VALIDATION.md).
