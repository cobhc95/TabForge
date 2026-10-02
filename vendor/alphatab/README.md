# TabForge.AlphaTab: alphaTab 1.8.4 plus three patches

TabForge reads and writes score files with [alphaTab](https://www.alphatab.net/) (MPL-2.0). Upstream alphaTab refuses a
.gp3/.gp4/.gp5 file with more than 1,000 bars through a hard-coded constant that no setting can change, so TabForge ships its own
build of alphaTab 1.8.4 with **three small changes**: (1) that limit became a setting of the individual import
(`ImporterSettings.MaxGp3To5BarCount`, default 1,000 = unchanged); (2) a .gp file's exact mixer volume and balance are kept
(`PlaybackInformation.VolumeFraction`/`BalanceFraction`, see below); (3) the trill speed is written and read (note XProperty 688062467). This folder holds everything needed to see, rebuild and verify it.

| | |
|---|---|
| Package / assembly | `TabForge.AlphaTab` / `TabForge.AlphaTab.dll`, assembly version 1.8.4.3, package version `1.8.4-tabforge.3` (upstream's is `AlphaTab`; this is a different package and file name on purpose) |
| Upstream | https://github.com/CoderLine/alphaTab, tag `v1.8.4`, commit `022a45c8e42370f9e12e68949d11eada370da83d` |
| Licence | Mozilla Public License 2.0, as upstream (copyright (c) 2025 Daniel Kuschny and contributors); text in `src/TabForge/licenses/MPL-2.0_alphaTab.txt` |
| Source Code Form | upstream tag above **plus** `0001-per-import-gp3-5-bar-limit-and-tabforge-identity.patch` , `0002-gpif-exact-mixer-volume-and-balance.patch` and `0003-gpif-trill-speed.patch` in this folder, applied in order (the complete difference, 29 + 28 + 50 changed lines) |
| Built by | `tools/Build-AlphaTab.ps1` (clone of the pinned commit, `git apply`, `npm ci`, alphaTab's own transpiler, `dotnet build`) |
| Consumed as | local NuGet source `vendor/alphatab` (see `nuget.config`; the id `TabForge.AlphaTab` is mapped to this folder only), locked in `src/TabForge/packages.lock.json` |

## Patch 0001 (`0001-...patch`)

1. `ImporterSettings.ts`: new `maxGp3To5BarCount` (default 1000).
2. `Gp3To5Importer.ts`: the bar-count check uses `this.settings.importer.maxGp3To5BarCount` instead of the constant `_maxBarCount`.
   Error text and every other check (100 tracks, 100 beats per bar, bend points, notice lines, decoding buffer) are unchanged.
3. `generate-typescript.ts`: the build date stamped into the library comes from `SOURCE_DATE_EPOCH` when set (the build script sets it to the
   upstream commit's date), so the same source builds to the same binary.
4. `Directory.Build.props`, `AlphaTab.csproj`: package id, assembly name and version, so it cannot be taken for the upstream package.

The limit lives on the `Settings` object of one read; nothing is global, so imports running at the same time cannot affect each other.
TabForge sets it to `InputLimits.MaxMeasuresPerTrack` (20,000) in `Services/AlphaTabBoundary.cs`, which is also where the build is
checked at run time: a different or older component makes the import fail with an explanation.

## Patch 0002 (`0002-gpif-exact-mixer-volume-and-balance.patch`)

A .gp file stores a track's volume and balance as floats (the `ChannelStrip` parameters of the gpif, entries 12 and 13), but
alphaTab's model keeps them as the 0..16 step of .gp5 (the reader floors, the writer divides by 16), so TabForge's 0..127 values
were quantised to steps of 8 in a clean `.gp`. The patch adds two numbers to `PlaybackInformation`:

1. `model/PlaybackInformation.ts`: `volumeFraction` and `balanceFraction` (0..1, default -1 = not known).
2. `importer/GpifParser.ts`: the reader keeps the stored fractions next to the 0..16 steps it always computed.
3. `exporter/GpifWriter.ts`: the writer writes a fraction when the model carries one (>= 0), else the 0..16 step / 16 as before.
4. `Directory.Build.props`: package version 1.8.4-tabforge.2, assembly version 1.8.4.2, description.

With nothing set, the Parameters text is exactly what upstream writes, and every other property of the model is unchanged (the `long-import` self-test
compares the full model of every fixture with the unpatched upstream hashes, leaving out only the two new properties). TabForge maps 0..127 to a
fraction as value/128 in both directions (`Services/GpMixerScale.cs`): every value survives exactly and the pan centre 64 is exactly 0.5.
.gp3/.gp4/.gp5 files carry no fractions and keep the old 0..16 mapping. `AlphaTabBoundary.Inspect` refuses a component without the two properties.

## Patch 0003 (`0003-gpif-trill-speed.patch`)

.gp keeps a trill's speed in a note XProperty, id 688062467, an `Int` that is the note value in ticks of a 960-tick quarter (a real
.gp file holds 240 for a 16th and 471-474 for an 8th). alphaTab's writer wrote only the trill's target pitch and its reader set every
trill to 1/16, so a 1/32 trill reopened as 1/16.

1. `exporter/GpifWriter.ts`: every trill note gets `<XProperties><XProperty id="688062467"><Int>ticks</Int>` (480, 240, 120 or 60 for 1/8 to 1/64).
2. `importer/GpifParser.ts`: the reader takes the nearest note value (1 to 64) for the stored ticks; without the XProperty a trill stays 1/16 as before.
3. `Directory.Build.props`: package version 1.8.4-tabforge.3, assembly version 1.8.4.3, description.

The gp-fidelity self-test (`TestGpTrillSpeed`) checks the written ticks, the exact round trip of 1/8, 1/16, 1/32 and 1/64, jittered values as the .gp format writes them, and a file without the XProperty.

## Rebuild and verify

Needs git, Node.js (LTS) and npm, and the .NET 8 SDK; everything comes from github.com/CoderLine/alphaTab, npmjs.org and nuget.org.

```powershell
.\tools\Build-AlphaTab.ps1            # rebuilds in a scratch folder and checks the DLL equals the one in the committed package
.\tools\Build-AlphaTab.ps1 -Update    # only after changing the patch: replaces the package (then regenerate the lock files and docs/SBOM.md)
```

The DLL does not depend on the machine or folder it was built in (verified with two builds in different folders). It is not
byte-identical to the nuget.org `AlphaTab` DLL: the compiler build and the patched sources differ. Behaviour equals upstream: the self-test
`long-import` compares the complete model (every property alphaTab serialises) of generated .gp3, .gp4, .gp5.00 and .gp5.10 files and of the
demo song with the SHA-256 recorded from the unpatched upstream package.

Changing the patch means a new version: add a patch file that raises the suffix (next: `1.8.4-tabforge.4`, assembly version 1.8.4.4), in the
`PackageReference` in `TabForge.csproj`, in `AlphaTabBoundary.RequiredVersion` and in `nuget.config`'s consumers, because NuGet caches
packages by version and would otherwise keep serving the old one.

## Hashes

See `SHA256SUMS`: the SHA-256 of the package and of the DLL inside it, and the upstream commit. The package's SHA-512 is in `packages.lock.json` and `docs/SBOM.md`.

## Written offer

For at least three years from the date of each TabForge release that includes this component, we will give any third party, on request,
the corresponding source of `TabForge.AlphaTab` (the upstream tag and commit above plus the patch in this folder) for a charge no more than
the cost of physically performing the distribution. Ask through https://github.com/cobhc95/TabForge/issues. The patch and the build script
are also part of TabForge's source.

## Retiring the patch

If a released alphaTab makes this limit configurable, switch the `PackageReference` back to `AlphaTab`, delete this folder and the
`tabforge-alphatab` source in `nuget.config`, and set the option in `AlphaTabBoundary.CreateImportSettings`. Checked against alphaTab's
`develop` branch on 2026-10-01: not yet (the constant is still there, commented "not configurable").
