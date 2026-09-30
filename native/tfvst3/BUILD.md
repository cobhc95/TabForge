# Building tfvst3.dll

`tfvst3.dll` is TabForge's small native VST3 host bridge (`tfvst3.cpp`, `tfvst3.h`). It wraps the Steinberg
VST3 SDK's hosting classes behind a plain C interface that the audio engine calls. The build needs nothing
outside this repository except the SDK, which CMake fetches at one pinned revision.

## Toolchain

The checked-in DLL was built with:

| Tool | Version |
| --- | --- |
| Visual Studio 2026 (v18) Build Tools, MSVC | 19.51.36256.0 (x64 `cl.exe` / `link.exe`) |
| CMake | 4.4.3 (the project needs 3.20 or newer) |
| Git | any recent Git for Windows (CMake uses it to fetch the SDK) |
| Windows SDK | the one installed with the Visual Studio C++ workload |

Only the toolchain above was used to build and check this. Any MSVC that supports C++17 should work (CMake is
told to use C++17), but a different compiler will not match the checked-in DLL byte for byte (see
"Reproducibility").

## VST3 SDK: pinned, unpatched

| | |
| --- | --- |
| Repository | https://github.com/steinbergmedia/vst3sdk |
| Tag | `v3.8.1_build_84` |
| Commit | `3cdf9ca5d1f5b1b21e0a86832aa4abe55607bd96` |
| Submodule `base` | `fcf9da0bd27a16f7f03773a3a39822f28f5c8477` |
| Submodule `pluginterfaces` | `4f547e8e102b47de4a8b8aaf343c73b700786372` |
| Submodule `public.sdk` | `586dc5e6c8012c3e4b01c79389375cbe96bdb1da` |
| Patches | none |
| Licence | MIT (see `THIRD_PARTY.md` at the repository root) |

`CMakeLists.txt` fetches exactly that commit (`FetchContent`, by commit id, not by the movable tag) and only the
three submodules it uses (`base`, `pluginterfaces`, `public.sdk`; `vstgui`, the docs and the tutorials are
skipped). The SDK's own CMake project is not used; only these sources are compiled: `pluginterfaces/base`,
`public.sdk/source/common`, `public.sdk/source/vst/{vstinitiids,utility/stringconvert}` and
`public.sdk/source/vst/hosting`. Nothing is edited after the fetch.

## Build

From the repository root, in a "Developer PowerShell for VS" (or any shell where `cmake` and `git` are on `PATH`):

```powershell
cmake -S native/tfvst3 -B C:\tfb -A x64 -DTFVST3_BUILD_TEST=OFF
cmake --build C:\tfb --config Release --parallel
```

* Use a **short build folder** such as `C:\tfb`. The SDK's submodules contain very long paths, and a build folder
  deeper than roughly 100 characters makes `git` fail with "Filename too long" while fetching it. (Or run
  `git config --global core.longpaths true` first.)
* Without `-G`, CMake picks the newest installed Visual Studio. To force one:
  `-G "Visual Studio 18 2026"` or `-G "Visual Studio 17 2022"`.
* Configuration is fixed: x64, Release, static CRT (`/MT`), so the DLL needs no VC++ runtime next to it.
* `-DTFVST3_BUILD_TEST=ON` (the default) also builds `tfv3test.exe`, a console program that loads a `.vst3`
  through the bridge. It is not needed by TabForge.
* To use an SDK checkout you already have, add `-DVST3SDK_DIR=<path to the vst3sdk root>`. It must be the commit
  above, with the three submodules checked out at the commits above:
  `git -C <path> rev-parse HEAD` and `git -C <path> submodule status`.

## Output

The build writes `C:\tfb\Release\tfvst3.dll`. Copy it to

```
src/TabForge.AudioEngine/native/tfvst3.dll
```

The audio engine project copies that file next to `TabForge.exe` (`TabForge.AudioEngine.csproj`). Without the DLL
the app still runs; VST3 plug-ins are then unavailable.

## Reproducibility

The checked-in `src/TabForge.AudioEngine/native/tfvst3.dll` (636,416 bytes) has SHA-256

```
9e3e286335ead842fb062ce1f138ad1fa9ce3e4fea5488d9c10b94dc205a1589
```

**A clean build does not reproduce it byte for byte.** Rebuilding on a machine with the same compiler (MSVC
19.51.36256.0), the same CMake (4.4.3) and the same pinned SDK gives a file of the same size that differs in a
few bytes. Measured on 2026-09-30:

| Build | Source path | SHA-256 | Bytes differing from the checked-in DLL |
| --- | --- | --- | --- |
| Same compiler, same absolute source path as the original build | original checkout | `4d849e5b133ab2c160d54bbedd5634eade6da266358732c81f41fb8f346badcc` | 6 |
| Same compiler, different source path | a copy in another folder | `bb6e7d36f3de0efb4705b82600a32277fdfabef1910838270fb7a0b190f8645d` | 46 |

Two builds from the same folder differ from each other only in the two timestamps. The differences are:

1. **Link timestamps (6 bytes, always).** The PE header `TimeDateStamp` (3 changing bytes) and the export
   directory timestamp (3 changing bytes) hold the time of the link. The linker option `/Brepro` replaces them with
   a content hash, but the checked-in DLL was linked without it.
2. **Source-path-dependent names (40 more bytes when the source path differs).** MSVC derives the name of every
   anonymous namespace in a translation unit (`?A0x<hash>` in the RTTI type names, for example
   `?AVFixedQueue@?A0x66f1f950@@`) from a hash of the source file's absolute path. A different checkout folder gives
   different hashes. `/Brepro` does not remove this, so a byte-identical rebuild also needs the same source path.
3. **Compiler and linker versions.** A different MSVC version changes code generation and the linker's
   bookkeeping, so the size and most of the file can differ. This is the expected outcome on the CI runner, whose
   Visual Studio version is not the one used for the checked-in DLL.

Because of 1 and 2, the check that is meaningful is "same size, same code and data apart from timestamps and
path-hashed names", not equal hashes. A future release could make the build reproducible by building from a fixed
folder with `/Brepro` (`-DCMAKE_SHARED_LINKER_FLAGS=/Brepro -DCMAKE_CXX_FLAGS=/Brepro`); that changes the DLL,
so it is not done for the current file. The workflow `windows-ci.yml` builds the bridge from the pinned SDK on
every run and reports the hash comparison and the number of differing bytes; it never fails on a mismatch.

**Release builds.** Releases ship the checked-in DLL, in the local build and in the GitHub Actions build alike, so both
carry the same bridge. The workflow `build-bridge.yml` (run by hand whenever the bridge source or the SDK pin
changes) builds the bridge from the pinned SDK on the runner with `native/build-tfvst3.ps1`, attests it, and uploads
`tfvst3.dll` together with `native/BUILD_PROVENANCE.md` (compiler and SHA-256). Those two files are committed as they
are. `tools/Package-Release.ps1` refuses to package if the DLL's SHA-256 differs from the record. The release is then
reproducible: see `docs/REPRODUCIBLE_BUILDS.md`.

The checked-in DLL has no Authenticode signature.
