# Reproducible release builds

A release build of TabForge is reproducible: the build on GitHub Actions and a build on the maintainer's PC, from the
same commit, produce a byte-identical `TabForge.exe` and identical contents in the portable zip. A release can
therefore ship the files GitHub built and attested, and anyone can rebuild them and compare.

## What is pinned

| Input | How it is fixed |
| --- | --- |
| .NET SDK (compiler, ReadyToRun compiler, runtime pack) | `global.json`: exact version, `rollForward: disable`. CI installs exactly that version (`setup-dotnet` with `global-json-file`). |
| NuGet packages | `packages.lock.json` per project, restore in locked mode. |
| alphaTab | `TabForge.AlphaTab`: upstream alphaTab 1.8.4 plus four patches, built from source by `Source/tools/Build-AlphaTab.ps1` and checked in as `vendor/alphatab/*.nupkg` (a local NuGet source, `nuget.config`). CI rebuilds it with the SDK that built it (`Source/vendor/alphatab/global.json`, 8.0.425; the app itself uses the `global.json` SDK) and fails when the DLL differs; see `Source/vendor/alphatab/README.md`. |
| Compiler output | `Deterministic`, `ContinuousIntegrationBuild` (always on in `Source/tools/Publish.ps1`, not only when `CI=true`), `PathMap` to `/_/` so no folder name is embedded. |
| Version | One `<Version>` in `Directory.Build.props`; the version string carries no git commit id (`IncludeSourceRevisionInInformationalVersion` is `false`, and source-control links are off in the PDB), so a build of the same source is identical whichever clone or commit id it was built from. |
| Source text | `.gitattributes` forces CRLF for every text file (`* text=auto eol=crlf`), so a checkout is identical on every machine; embedded source checksums and loose text files match. |
| Build folder | `Source/tools/Package-Release.ps1` builds from a temporary `git worktree` of the committed HEAD, never from the working folder, so uncommitted edits, stray files and local line endings cannot reach the release. It refuses a checkout that is not clean. |
| Native components | Four committed binaries, each checked against its own build record: `tfvst3.dll` (`Source/native/BUILD_PROVENANCE.md`), and `tfvst3-x86.dll`, `tfvst32.exe` and `tfvst64.exe` (their build records are kept with the release tooling and are not part of the public export). All four ship loose beside `TabForge.exe`. |
| Zip | Entries are sorted, use forward slashes and one fixed timestamp. |

Not reproducible by design: the Inno Setup installer embeds its own timestamps. Its contents come from the same
tested folder as the portable zip, so the portable zip is the file to compare.

## Checking a release

1. Let GitHub Actions build the tag (the `release` workflow) and download the `release-files` artifact.
2. On a PC with the pinned SDK (`dotnet --version` prints the version in `global.json`), commit everything and run
   `.\Source\tools\Package-Release.ps1` from the repository root (it needs Inno Setup 6 or later installed; `-InnoCompiler <path to ISCC.exe>` names it).
   It builds the committed HEAD in a temporary worktree, tests that exact build and leaves the files in `dist\`.
3. Compare:

```powershell
.\Source\tools\Compare-Release.ps1 -ZipA dist\TabForge-<version>-win-x64-portable.zip `
    -ZipB <download>\TabForge-<version>-win-x64-portable.zip `
    -SetupA dist\TabForge-<version>-setup.exe -SetupB <download>\TabForge-<version>-setup.exe
```

The script lists every differing zip entry (with the number of differing bytes and an explanation of the usual
causes) and exits with 0 only when all entries are identical. The installers are compared by payload when
`innounp.exe` or `innoextract.exe` is on `PATH`; otherwise the script says that an installer difference is expected.

## The native components

The four native binaries are not rebuilt for a release: `tfvst3.dll`, `tfvst3-x86.dll` (the VST3 host bridge and its
32-bit build), `tfvst32.exe` and `tfvst64.exe` (the one helper source in both bitnesses). The release ships the files
committed in the repository (`Source/src/TabForge.AudioEngine/native/`), so the GitHub build and a local build carry
exactly the same helpers. Two rebuilds of a native component from the same source never match byte for byte, so each
committed file is produced once and then only reused:

1. `tfvst3.dll`: the maintainer runs the `build-bridge` workflow (Actions, Run workflow) whenever the bridge source or the
   SDK pin changes. It builds the bridge from the pinned, unpatched SDK on the runner, records the compiler and SHA-256 in
   `Source/native/BUILD_PROVENANCE.md`, attests the DLL and uploads both files.
2. `tfvst3-x86.dll`: the 32-bit bridge build script kept with the release tooling builds it from the same SDK pin and
   writes the 32-bit build record beside it.
3. `tfvst32.exe` and `tfvst64.exe`: the helper build script kept with the release tooling builds both from the same
   private helper source, runs the native self-tests and writes both build records.
4. The maintainer commits each DLL/EXE with its build record as they are. `Source/tools/Package-Release.ps1` refuses to
   package when any shipped file's SHA-256 differs from its record.

### What the CI comparison checks

The `native-bridge` job of `windows-ci` rebuilds the 64-bit bridge on every run and runs `Source/tools/Compare-NativeBridge.ps1`,
which parses the PE headers of both files, masks the expected differences and requires everything else to be
identical. The expected differences are:

| Category | Where | Why it differs |
| --- | --- | --- |
| Link timestamps | COFF header `TimeDateStamp`, export and resource directory timestamps, the timestamp of each debug directory entry | the linker stamps the link time (it runs without `/Brepro`) |
| PDB identity | CodeView (RSDS) GUID and PDB path, the PE checksum, a `/Brepro` content hash record | a new GUID per link, the build folder, and the checksum follows the content |
| Source-path hash names | the 8 hex digits of `?A0x<hash>` anonymous-namespace names in the RTTI names | MSVC hashes the absolute path of each source file, and the runner's checkout folder differs from the maintainer's |

The job passes when the files are identical or differ only in those categories (the script prints how many bytes
differed in each), and fails when the size differs or any code or data byte differs outside them (a different bridge
source, SDK or compiler). In that case run `build-bridge` and commit its output. A difference in these categories does not
mean the shipped bridge is wrong; it is the expected result of rebuilding.

## Updating the native components

The components change rarely. When the VST3 bridge source or the SDK pin changes:

1. Run the `build-bridge` workflow (Actions, Run workflow) on the commit for `tfvst3.dll`; run the 32-bit bridge build
   kept with the release tooling for `tfvst3-x86.dll`.
2. Download/copy each DLL to `Source/src/TabForge.AudioEngine/native/` and its build record to `Source/native/`,
   unchanged.
3. Commit each pair together.

The helper (`tfvst32.exe` and `tfvst64.exe`) works the same way: the helper build script kept with the release tooling
rebuilds both, runs the native self-tests in each bitness, writes both build records, and `Package-Release.ps1` refuses
a mismatch. Its test-only binaries are built for local testing only and are never shipped.

## Updating the SDK

Change `global.json` to the new exact version in one commit, rebuild, and expect every managed binary to change once.
