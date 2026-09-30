# Reproducible release builds

A release build of TabForge is reproducible: the build on GitHub Actions and a build on the maintainer's PC, from the
same commit, produce a byte-identical `TabForge.exe` and identical contents in the portable zip. A release can
therefore ship the files GitHub built and attested, and anyone can rebuild them and compare.

## What is pinned

| Input | How it is fixed |
| --- | --- |
| .NET SDK (compiler, ReadyToRun compiler, runtime pack) | `global.json`: exact version, `rollForward: disable`. CI installs exactly that version (`setup-dotnet` with `global-json-file`). |
| NuGet packages | `packages.lock.json` per project, restore in locked mode. |
| Compiler output | `Deterministic`, `ContinuousIntegrationBuild` (always on in `tools/Publish.ps1`, not only when `CI=true`), `PathMap` to `/_/` so no folder name is embedded. |
| Version | One `<Version>` in `Directory.Build.props`; the git commit id is part of the informational version, and is the same in both builds. |
| Source text | `.gitattributes` forces CRLF for every text file (`* text=auto eol=crlf`), so a checkout is identical on every machine; embedded source checksums and loose text files match. |
| Build folder | `tools/Package-Release.ps1` builds from a temporary `git worktree` of the committed HEAD, never from the working folder, so uncommitted edits, stray files and local line endings cannot reach the release. It refuses a checkout that is not clean. |
| Native bridge | One binary: the committed `src/TabForge.AudioEngine/native/tfvst3.dll`, verified against `native/BUILD_PROVENANCE.md`. Both builds ship it. |
| Zip | Entries are sorted, use forward slashes and one fixed timestamp. |

Not reproducible by design: the Inno Setup installer embeds its own timestamps. Its contents come from the same
tested folder as the portable zip, so the portable zip is the file to compare.

## Checking a release

1. Let GitHub Actions build the tag (the `release` workflow) and download the `release-files` artifact.
2. On a PC with the pinned SDK (`dotnet --version` prints the version in `global.json`), commit everything and run
   `RELEASE.cmd`. It builds the committed HEAD in a temporary worktree and leaves the files in `dist\`.
3. Compare:

```powershell
.\tools\Compare-Release.ps1 -ZipA dist\TabForge-<version>-win-x64-portable.zip `
    -ZipB <download>\TabForge-<version>-win-x64-portable.zip `
    -SetupA dist\TabForge-<version>-setup.exe -SetupB <download>\TabForge-<version>-setup.exe
```

The script lists every differing zip entry (with the number of differing bytes and an explanation of the usual
causes) and exits with 0 only when all entries are identical. The installers are compared by payload when
`innounp.exe` or `innoextract.exe` is on `PATH`; otherwise the script says that an installer difference is expected.

## Updating the native bridge

The bridge changes rarely. When its source or the SDK pin changes:

1. Run the `build-bridge` workflow (Actions, Run workflow) on the commit.
2. Download the `tfvst3-bridge` artifact and copy `tfvst3.dll` to `src/TabForge.AudioEngine/native/` and
   `BUILD_PROVENANCE.md` to `native/`, unchanged.
3. Commit both files together.

## Updating the SDK

Change `global.json` to the new exact version in one commit, rebuild, and expect every managed binary to change once.
