# Third-party components

TabForge is an independent project. Guitar Pro is a trademark of Arobas Music. VST and ASIO are trademarks of Steinberg Media Technologies GmbH. Other product and company names are trademarks of their owners. TabForge is not affiliated with, sponsored or endorsed by any of them.

The full licence texts of the bundled components are in the `licenses` folder next to `TabForge.exe` (in the source
tree: `src/TabForge/licenses/`). Every text there is an unmodified copy taken from the package or runtime it belongs to.

## Important notice: SoundTouch.Net (LGPL-2.1-or-later)

**TabForge uses SoundTouch.Net 2.3.2, which is licensed under the GNU Lesser General Public License, version 2.1 or
later (LGPL-2.1-or-later). You may replace `SoundTouch.Net.dll` in this folder with a modified build of SoundTouch.Net.**
`SoundTouch.Net.dll` is a separate file beside `TabForge.exe`; it is not packed into the executable, so you can swap
it without rebuilding TabForge. TabForge only calls it through its public API.

- Licence text: [licenses/LGPL-2.1_SoundTouch.Net.txt](licenses/LGPL-2.1_SoundTouch.Net.txt)
- Copyright: Olaf Woudenberg 2011-2019 (SoundTouch.Net); the SoundTouch algorithms are by Olli Parviainen (https://www.surina.net/soundtouch/).
- Source of the exact version used (tag `2.3.2`, commit `98e5b8fd2f8efed0ddf7c8f66b435bfb231659dc`):
  https://github.com/owoudenberg/soundtouch.net/tree/98e5b8fd2f8efed0ddf7c8f66b435bfb231659dc
- The package is the unmodified NuGet package `SoundTouch.Net` 2.3.2.
- **Source attached to the release:** every TabForge release on GitHub also attaches `SoundTouch.Net-2.3.2-source.zip`,
  a copy of that repository at the commit above.
- **Written offer:** for at least three years from the date of each TabForge release that includes SoundTouch.Net, we will
  give any third party, on request, a complete machine-readable copy of the corresponding source code of SoundTouch.Net
  (the version named above), for a charge no more than the cost of physically performing the distribution. Ask through
  https://github.com/cobhc95/TabForge/issues.

## alphaTab (MPL-2.0)

TabForge uses alphaTab for Guitar Pro reading and `.gp` (GP7) writing, as its own **modified build** of alphaTab 1.8.4,
shipped as the assembly `TabForge.AlphaTab.dll` (NuGet package `TabForge.AlphaTab` 1.8.4-tabforge.3). It is not the upstream
`AlphaTab` package and carries a different name on purpose.

- The modifications (MPL-2.0 section 3.2: the Source Code Form of the modified files stays available, see below), three patches:
  (1) alphaTab refuses a Guitar Pro 3-5 file with more than 1,000 bars through a hard-coded constant. The patch turns that constant into
  a setting of each individual import (`ImporterSettings.MaxGp3To5BarCount`, default 1,000, i.e. unchanged); TabForge sets it to its own
  per-track limit of 20,000 bars. It also pins the build date stamped into the library and renames the package and assembly.
  (2) A Guitar Pro 7/8 file (gpif) stores a track's mixer volume and balance as fractions, while alphaTab's model keeps only a 0..16
  step of them. The patch adds `PlaybackInformation.VolumeFraction` and `BalanceFraction` (default -1, i.e. unchanged), which the gpif
  reader fills and the gpif writer prefers when set, so the exact values survive.
  (3) A Guitar Pro 7/8 file stores a trill's speed in a note XProperty (id 688062467, the note value in ticks); alphaTab neither wrote it
  nor read it (every trill read as 1/16). The patch writes it for every trill and reads it back as the nearest note value; it also raises
  the package version to 1.8.4-tabforge.3.
  The patches are `vendor/alphatab/0001-per-import-gp3-5-bar-limit-and-tabforge-identity.patch` (29 changed lines in 5 files) and
  `vendor/alphatab/0002-gpif-exact-mixer-volume-and-balance.patch` (28 changed lines in 4 files) and
  `vendor/alphatab/0003-gpif-trill-speed.patch` (50 changed lines in 3 files); every release also carries them as
  `licenses/alphaTab-<patch name>`.
- Corresponding source (the Source Code Form of the modified files): upstream tag `v1.8.4`, commit
  `022a45c8e42370f9e12e68949d11eada370da83d`, plus those patches, applied in order. `tools/Build-AlphaTab.ps1` rebuilds the DLL from them (and
  verifies it equals the shipped one); `vendor/alphatab/README.md` describes the build, the hashes and the written offer below.
- Written offer: for at least three years from the date of each TabForge release that includes `TabForge.AlphaTab`, we will give any
  third party, on request, a complete machine-readable copy of its corresponding source (the upstream commit above plus the patches),
  for a charge no more than the cost of physically performing the distribution. Ask through
  https://github.com/cobhc95/TabForge/issues.
- An earlier TabForge file that replayed alphaTab's reader through private reflection (`Gp3To5LongSongReader.cs`) is gone; no
  other alphaTab-derived source file remains in TabForge. The rest of TabForge stays under its MIT licence.
- Copyright (c) 2025, Daniel Kuschny and Contributors.
- Project: https://www.alphatab.net/
- Source code form (tag `v1.8.4`, commit `022a45c8e42370f9e12e68949d11eada370da83d`):
  https://github.com/CoderLine/alphaTab/tree/022a45c8e42370f9e12e68949d11eada370da83d
- Licence: Mozilla Public License 2.0 (MPL-2.0). Text: [licenses/MPL-2.0_alphaTab.txt](licenses/MPL-2.0_alphaTab.txt)
  (also at https://www.mozilla.org/MPL/2.0/). The Source Code Form of alphaTab is available at the link above.

## Bravura music font (SIL OFL 1.1)

The alphaTab library embeds the Bravura font (version 1.38) as the resource `AlphaTab.Platform.Skia.Bravura.otf`
inside `TabForge.AlphaTab.dll` (unchanged by TabForge's patch).

- Copyright (c) 2020, Steinberg Media Technologies GmbH (http://www.steinberg.net/), with Reserved Font Name "Bravura".
- Licence: SIL Open Font License, Version 1.1. Text: [licenses/OFL-1.1_Bravura.txt](licenses/OFL-1.1_Bravura.txt)
  (the licence field of the font file itself).
- Source: https://github.com/steinbergmedia/bravura

## AlphaSkia (BSD-3-Clause)

The `AlphaSkia` NuGet package (3.4.135) is the managed binding used by alphaTab. Only the managed `AlphaSkia.dll` is
shipped; no native Skia library is included.

- Copyright (c) 2025, Daniel Kuschny.
- Source: https://github.com/CoderLine/alphaSkia
- Licence: BSD 3-Clause. Text: [licenses/BSD-3-Clause_AlphaSkia.txt](licenses/BSD-3-Clause_AlphaSkia.txt) (the project's
  LICENSE file, which also carries the Skia and HarfBuzz notices).

## NAudio (MIT)

The audio engine uses the `NAudio` NuGet packages (2.2.1) for audio output (WASAPI, ASIO, DirectSound, MIDI).

- Copyright 2020 Mark Heath.
- Source: https://github.com/naudio/NAudio
- Licence: MIT. Text: [licenses/MIT_NAudio.txt](licenses/MIT_NAudio.txt)

ASIO is a trademark and software of Steinberg Media Technologies GmbH.

## MeltySynth (MIT)

The audio engine uses the `MeltySynth` NuGet package (2.4.1) to render a track's General MIDI sound when effects
process it (it plays Windows' own `gm.dls` sound bank, converted in memory; the bank is not redistributed).

- Copyright (c) 2021 Nobuaki Tanaka. The licence file also carries the notices for C# Synth (Alex Veltsistas, 2014)
  and TinySoundFont (Bernhard Schelling, based on SFZero by Steve Folta).
- Source: https://github.com/sinshu/meltysynth
- Licence: MIT. Text: [licenses/MIT_MeltySynth_and_notices.txt](licenses/MIT_MeltySynth_and_notices.txt)

## Microsoft .NET runtime, WindowsDesktop runtime and packages (MIT)

TabForge is a self-contained .NET 8 application: the .NET runtime (8.0.31) and the Windows Desktop runtime (WPF,
Windows Forms) are part of `TabForge.exe`, together with `System.Drawing.Common` and
`Microsoft.Win32.SystemEvents` (9.0.8). The PDF export (below) adds `Microsoft.Extensions.Logging.Abstractions` (8.0.3),
`Microsoft.Extensions.DependencyInjection.Abstractions` (8.0.2) and `System.Security.Cryptography.Pkcs` (8.0.1), also from
dotnet/runtime under the same MIT licence.

- Copyright (c) .NET Foundation and Contributors / Microsoft Corporation.
- Licences: [licenses/MIT_DotNet_runtime_LICENSE.txt](licenses/MIT_DotNet_runtime_LICENSE.txt),
  [licenses/MIT_DotNet_WindowsDesktop_runtime.txt](licenses/MIT_DotNet_WindowsDesktop_runtime.txt),
  [licenses/MIT_Microsoft_System.Drawing.Common.txt](licenses/MIT_Microsoft_System.Drawing.Common.txt)
- Notices for the third-party code inside them:
  [licenses/THIRD-PARTY-NOTICES_DotNet_runtime.txt](licenses/THIRD-PARTY-NOTICES_DotNet_runtime.txt),
  [licenses/THIRD-PARTY-NOTICES_System.Drawing.Common.txt](licenses/THIRD-PARTY-NOTICES_System.Drawing.Common.txt)
- Source: https://github.com/dotnet/runtime and https://github.com/dotnet/wpf

## VST 2 plug-in hosting (TabForge's own code)

TabForge hosts VST 2 plug-ins through its own, independent C# implementation of the plug-in binary interface: a
plug-in is a 64-bit DLL that exports `VSTPluginMain`, and TabForge talks to it through the plain C structures and
dispatcher opcodes that such a DLL expects. The code is in `src/TabForge.AudioEngine/Plugins/Vst2Plugin.cs` (and, for
the headless tests, `Vst2TestEffect.cs`) and is covered by TabForge's own MIT licence.

- TabForge contains no Steinberg VST 2 SDK headers or code (no `aeffect.h`, `aeffectx.h` or other file from the VST 2
  SDK, and none of their text), and none are distributed with TabForge. To work with existing plug-ins, the code has
  to use the same structure layouts and numeric constants as the binary interface; it declares them itself.
- VST is a trademark of Steinberg Media Technologies GmbH. TabForge is not affiliated with, sponsored or endorsed by
  Steinberg.
- VST 3 plug-ins are hosted by a different component, the native bridge below, which uses the MIT-licensed VST 3 SDK.

## Steinberg VST 3 SDK and the native bridge (`tfvst3.dll`)

VST3 plug-ins are hosted by `tfvst3.dll`, a small C++ bridge (about 600 KB) built against the Steinberg VST 3 SDK
(v3.8.1, build 84) with MSVC and a static runtime. The compiled bridge is `src/TabForge.AudioEngine/native/tfvst3.dll`,
which the audio engine project copies next to the executable; its C++ source is in `native/tfvst3`. The engine
project treats the DLL as optional: without it the solution still builds and runs, but VST3 plug-ins cannot be loaded
(VST2 plug-ins and everything else are unaffected).
`native/tfvst3` (the VST3 host bridge, `tfvst3.dll`) is built from the Steinberg VST 3 SDK and statically links the
SDK's hosting code. The SDK is not vendored: `native/tfvst3/CMakeLists.txt` fetches one pinned, unpatched revision
(tag `v3.8.1_build_84`, commit `3cdf9ca5d1f5b1b21e0a86832aa4abe55607bd96`; build steps in `native/tfvst3/BUILD.md`).
The checked-in `src/TabForge.AudioEngine/native/tfvst3.dll` is a build of that revision.

- Source: https://github.com/steinbergmedia/vst3sdk (components used: `base`, `pluginterfaces`, `public.sdk`)
- License: MIT, Copyright (c) 2026 Steinberg Media Technologies GmbH (the `LICENSE.txt` of the SDK and of each of
  those three components). Since SDK version 3.8.0 the SDK is MIT only; the earlier GPLv3 and Steinberg
  proprietary licences are no longer offered, and this project uses no earlier version. MIT is compatible with
  TabForge's own MIT licence. Its terms require the copyright notice and licence text to accompany copies of the
  Software, including a redistributed `tfvst3.dll`; the notice is reproduced here and in
  [licenses/MIT_Steinberg_VST3_SDK.txt](licenses/MIT_Steinberg_VST3_SDK.txt):
- VST is a registered trademark of Steinberg Media Technologies GmbH. ASIO is a trademark of Steinberg Media
  Technologies GmbH. TabForge is not affiliated with or endorsed by Steinberg.

```
MIT License

Copyright (c) 2026, Steinberg Media Technologies GmbH

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## PDFsharp and MigraDoc (MIT)

The Beginner's Guide (Help > Tutorial) is exported as a PDF with the `PDFsharp-MigraDoc-WPF` NuGet package (and its
`PDFsharp-WPF` dependency), the unmodified packages.

- Version: 6.2.4.
- Copyright (c) 2001-2026 empira Software GmbH, Troisdorf (Cologne Area), Germany.
- Project: https://docs.pdfsharp.net/ , source: https://github.com/empira/PDFsharp
- Licence: MIT. Text: [licenses/MIT_PDFsharp_MigraDoc.txt](licenses/MIT_PDFsharp_MigraDoc.txt).
- The WPF build uses Windows' own font and image support; no GDI+ and no browser engine.

## Sample song

TabForge includes one original demo song, *TabForge Demo - Ashen Meridian* (`Samples\` in the app folder, `samples/`
in the source), written for TabForge and dedicated to the public domain (CC0 1.0). No third-party songs or
transcriptions are included.

## Icons

All icon sets (`src/TabForge/Assets/Icons/`: Transport, Tools, Instruments) and the application icon were designed
for TabForge. They are part of TabForge and covered by its MIT licence (see `LICENSE`). The dynamics, trill and digit
glyphs of some tool icons are outlines taken from the Bravura font (SIL OFL 1.1, see above). Details:
`src/TabForge/Assets/ASSETS.md`.
