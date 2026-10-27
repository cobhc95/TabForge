# Third-party components

TabForge is an independent project. VST and ASIO are trademarks of Steinberg Media Technologies GmbH. Other product and company names are trademarks of their owners. TabForge is not affiliated with, sponsored or endorsed by any of them.

The full licence texts of the bundled components are in the `licenses` folder next to `TabForge.exe` (in the source
tree: `Source/src/TabForge.Avalonia/licenses/`). Every text there is an unmodified copy taken from the package or runtime it belongs to.

## Important notice: SoundTouch.Net (LGPL-2.1-or-later)

**TabForge uses SoundTouch.Net 2.3.2, which is licensed under the GNU Lesser General Public License, version 2.1 or
later (LGPL-2.1-or-later). You may replace `SoundTouch.Net.dll` in this folder with a modified build of SoundTouch.Net.**
`SoundTouch.Net.dll` is a separate file beside `TabForge.exe`; it is not packed into the executable, so you can swap
it without rebuilding TabForge. TabForge only calls it through its public API.

- Licence text: [licenses/LGPL-2.1_SoundTouch.Net.txt](src/TabForge.Avalonia/licenses/LGPL-2.1_SoundTouch.Net.txt)
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

TabForge uses alphaTab for reading score files and writing `.gp`, as its own **modified build** of alphaTab 1.8.4,
shipped as the assembly `TabForge.AlphaTab.dll` (NuGet package `TabForge.AlphaTab` 1.8.4-tabforge.4). It is not the upstream
`AlphaTab` package and carries a different name on purpose.

- The modifications (MPL-2.0 section 3.2: the Source Code Form of the modified files stays available, see below), four patches:
  (1) alphaTab refuses a .gp3/.gp4/.gp5 file with more than 1,000 bars through a hard-coded constant. The patch turns that constant into
  a setting of each individual import (`ImporterSettings.MaxGp3To5BarCount`, default 1,000, i.e. unchanged); TabForge sets it to its own
  per-track limit of 20,000 bars. It also pins the build date stamped into the library and renames the package and assembly.
  (2) A .gp file (gpif) stores a track's mixer volume and balance as fractions, while alphaTab's model keeps only a 0..16
  step of them. The patch adds `PlaybackInformation.VolumeFraction` and `BalanceFraction` (default -1, i.e. unchanged), which the gpif
  reader fills and the gpif writer prefers when set, so the exact values survive.
  (3) A .gp file stores a trill's speed in a note XProperty (id 688062467, the note value in ticks); alphaTab neither wrote it
  nor read it (every trill read as 1/16). The patch writes it for every trill and reads it back as the nearest note value; it also raises
  the package version to 1.8.4-tabforge.3.
  (4) A .gp3/.gp4/.gp5 note can carry a hammer-on, a slide or a tie whose partner note does not exist yet; alphaTab cleared the flag. The
  patch keeps the file's value in three new note fields (`Note.UnlinkedHammerPullOrigin`, `UnlinkedSlideOutType`, `UnlinkedTieDestination`)
  and raises the package version to 1.8.4-tabforge.4.
  The patches are `Source/vendor/alphatab/0001-per-import-gp3-5-bar-limit-and-tabforge-identity.patch` (29 changed lines in 5 files),
  `Source/vendor/alphatab/0002-gpif-exact-mixer-volume-and-balance.patch` (28 changed lines in 4 files),
  `Source/vendor/alphatab/0003-gpif-trill-speed.patch` (50 changed lines in 3 files) and
  `Source/vendor/alphatab/0004-keep-unlinked-gp3-5-note-links.patch` (30 changed lines in 2 files); every release also carries them as
  `licenses/alphaTab-<patch name>`.
- Corresponding source (the Source Code Form of the modified files): upstream tag `v1.8.4`, commit
  `022a45c8e42370f9e12e68949d11eada370da83d`, plus those patches, applied in order. `Source/tools/Build-AlphaTab.ps1` rebuilds the DLL from them (and
  verifies it equals the shipped one); `Source/vendor/alphatab/README.md` describes the build, the hashes and the written offer below.
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
- Licence: Mozilla Public License 2.0 (MPL-2.0). Text: [licenses/MPL-2.0_alphaTab.txt](src/TabForge.Avalonia/licenses/MPL-2.0_alphaTab.txt)
  (also at https://www.mozilla.org/MPL/2.0/). The Source Code Form of alphaTab is available at the link above.

## Bravura music font (SIL OFL 1.1)

The alphaTab library embeds the Bravura font (version 1.38) as the resource `AlphaTab.Platform.Skia.Bravura.otf`
inside `TabForge.AlphaTab.dll` (unchanged by TabForge's patch).

- Copyright (c) 2020, Steinberg Media Technologies GmbH (http://www.steinberg.net/), with Reserved Font Name "Bravura".
- Licence: SIL Open Font License, Version 1.1. Text: [licenses/OFL-1.1_Bravura.txt](src/TabForge.Avalonia/licenses/OFL-1.1_Bravura.txt)
  (the licence field of the font file itself).
- Source: https://github.com/steinbergmedia/bravura

## Inter and JetBrains Mono fonts (SIL OFL 1.1)

The optional "TabForge bundled" interface font (Settings > Appearance > Interface font) uses Inter 4.1 (Regular, SemiBold, Bold) and
JetBrains Mono 2.304 (Regular, Bold), shipped unchanged as Avalonia resources.

- Inter: Copyright (c) 2016 The Inter Project Authors (https://github.com/rsms/inter). Text: [licenses/OFL-1.1_Inter.txt](src/TabForge.Avalonia/licenses/OFL-1.1_Inter.txt)
- JetBrains Mono: Copyright 2020 The JetBrains Mono Project Authors (https://github.com/JetBrains/JetBrainsMono). Text: [licenses/OFL-1.1_JetBrainsMono.txt](src/TabForge.Avalonia/licenses/OFL-1.1_JetBrainsMono.txt)

## GeneralUser GS SoundFont (downloaded on demand)

The optional "TabForge SoundFont" sound engine plays GeneralUser GS v2.0.3 (S. Christian Collins). The 32 MB file is NOT shipped with
TabForge: the Settings button "Download SoundFont..." fetches it once from https://github.com/mrbumpy409/GeneralUser-GS, checks its SHA-256
and stores it in the TabForge data folder. It is played by MeltySynth (see above).

- Licence: free use, including in software, with modification and repackaging allowed. Text: [licenses/GeneralUser-GS_LICENSE.txt](src/TabForge.Avalonia/licenses/GeneralUser-GS_LICENSE.txt)

## AlphaSkia (BSD-3-Clause)

The `AlphaSkia` NuGet package (3.4.135) is the managed binding used by alphaTab. Only the managed `AlphaSkia.dll` is
shipped; no native Skia library is included.

- Copyright (c) 2025, Daniel Kuschny.
- Source: https://github.com/CoderLine/alphaSkia
- Licence: BSD 3-Clause. Text: [licenses/BSD-3-Clause_AlphaSkia.txt](src/TabForge.Avalonia/licenses/BSD-3-Clause_AlphaSkia.txt) (the project's
  LICENSE file, which also carries the Skia and HarfBuzz notices).

## NAudio (MIT)

The audio engine uses the `NAudio` NuGet packages (2.2.1) for audio output (WASAPI, ASIO, DirectSound, MIDI).

- Copyright 2020 Mark Heath.
- Source: https://github.com/naudio/NAudio
- Licence: MIT. Text: [licenses/MIT_NAudio.txt](src/TabForge.Avalonia/licenses/MIT_NAudio.txt)

ASIO is a trademark and software of Steinberg Media Technologies GmbH.

## MeltySynth (MIT)

The audio engine uses the `MeltySynth` NuGet package (2.4.1) to render a track's General MIDI sound when effects
process it (it plays Windows' own `gm.dls` sound bank, converted in memory; the bank is not redistributed).

- Copyright (c) 2021 Nobuaki Tanaka. The licence file also carries the notices for C# Synth (Alex Veltsistas, 2014)
  and TinySoundFont (Bernhard Schelling, based on SFZero by Steve Folta).
- Source: https://github.com/sinshu/meltysynth
- Licence: MIT. Text: [licenses/MIT_MeltySynth_and_notices.txt](src/TabForge.Avalonia/licenses/MIT_MeltySynth_and_notices.txt)

## NLayer and NVorbis (MIT)

The NLayer (3.0.0, MP3) and NVorbis (0.10.5, Ogg Vorbis) NuGet packages are managed decoders that read MP3 and OGG
audio on every platform. Other formats (WAV, AIFF, FLAC, AAC/M4A, WMA) still use NAudio and, on Windows, Media Foundation.

- Copyright (c) 2018 Mark Heath, Andrew Ward & Contributors; Copyright (c) 2020 Andrew Ward (NVorbis).
- Sources: https://github.com/naudio/NLayer and https://github.com/NVorbis/NVorbis
- Licence: MIT. Texts: [licenses/MIT_NLayer.txt](src/TabForge.Avalonia/licenses/MIT_NLayer.txt), [licenses/MIT_NVorbis.txt](src/TabForge.Avalonia/licenses/MIT_NVorbis.txt)

## miniaudio and RtMidi: the Linux and macOS audio and MIDI library (`tfportable`)

On Linux and macOS (Apple Silicon) TabForge talks to sound cards and MIDI devices through `libtfportable` (`tfportable.dll` in the Windows dev build),
a small C library built from `Source/native/tfportable` that wraps two libraries, each pinned to an exact commit and used unmodified:

- **miniaudio** 0.11.25 (commit `9634bedb5b5a2ca38c1ee7108a9358a4e233f14d`), by David Reid: Core Audio on macOS, ALSA, PulseAudio, PipeWire and JACK on Linux.
  Licence: public domain or MIT-0 (your choice). Text: [licenses/PublicDomain-MIT-0_miniaudio.txt](src/TabForge.Avalonia/licenses/PublicDomain-MIT-0_miniaudio.txt).
  Source: https://github.com/mackron/miniaudio
- **RtMidi** 6.0.0 (commit `1e5b49925aa60065db52de44c366d446a902547b`), copyright (c) 2003-2023 Gary P. Scavone: Core MIDI on macOS, the ALSA sequencer on Linux.
  Licence: MIT-style (its request to send modifications to the author is not binding). Text: [licenses/MIT-style_RtMidi.txt](src/TabForge.Avalonia/licenses/MIT-style_RtMidi.txt).
  Source: https://github.com/thestk/RtMidi
- The Windows release does not contain this library: Windows keeps WASAPI/ASIO/DirectSound and winmm. It is only an opt-in developer path on Windows
  (environment variables `TABFORGE_AUDIO_BACKEND=portable` and `TABFORGE_MIDI_BACKEND=portable`).

## LAME MP3 encoder (LGPL-2.0)

On Linux and macOS (and on Windows only with the developer switch `TABFORGE_MP3_ENCODER=lame`) MP3 export uses the LAME encoder, loaded at run time as
the separate, replaceable file `libmp3lame` (`libmp3lame.so.0`, `libmp3lame.0.dylib`, `libmp3lame.dll`). TabForge only calls its public C API
(`lame_init` ... `lame_encode_flush`) and does not link it into any executable. You may replace the library with a modified build. The Windows release
encodes MP3 with Media Foundation and does not contain LAME.

- Licence: GNU Library General Public License 2.0 (LGPL-2.0). Text: [licenses/LGPL-2.0_LAME.txt](src/TabForge.Avalonia/licenses/LGPL-2.0_LAME.txt)
- Project and source: https://lame.sourceforge.io/ (the Linux and macOS packages bundle the unmodified LAME 3.100 library and attach its source).
- The unit tests on Windows use the `NAudio.Lame` 2.1.0 package (MIT) only to obtain `libmp3lame.64.dll`; nothing from it ships.
- Written offer: for at least three years from the date of each TabForge release that includes LAME we will give any third party, on request, a complete
  machine-readable copy of the corresponding LAME source, for a charge no more than the cost of physically performing the distribution. Ask through
  https://github.com/cobhc95/TabForge/issues.

## FFmpeg (optional external program; not shipped)

TabForge can write video through an `ffmpeg` program that is already on the computer (on the PATH, next to TabForge, or named by the
`TABFORGE_FFMPEG` variable). It is the video encoder on macOS and Linux, and an opt-in choice on Windows (Preferences > Video encoder:
"FFmpeg (found at ...)", shown only when an ffmpeg is found). TabForge starts it as a separate process and pipes raw frames and sound to it;
it links no FFmpeg code and ships no FFmpeg file in any package.

- Project: https://ffmpeg.org/ . Licence: LGPL-2.1-or-later, or GPL-2.0-or-later for builds with libx264; which one applies is a property of the
  ffmpeg you install, not of TabForge. TabForge itself stays MIT because it only runs the program.
- Official sources and builds: https://ffmpeg.org/download.html (for example `brew install ffmpeg`, `apt install ffmpeg`, or the Windows builds linked there).

## Microsoft .NET runtime and packages (MIT)

TabForge is a self-contained .NET 10 application: the .NET runtime (10.0.14) is part of `TabForge.exe`, together with
`System.Drawing.Common` and `Microsoft.Win32.SystemEvents` (9.0.8), which the alphaTab package brings in.

- Copyright (c) .NET Foundation and Contributors / Microsoft Corporation.
- Licences: [licenses/MIT_DotNet_runtime_LICENSE.txt](src/TabForge.Avalonia/licenses/MIT_DotNet_runtime_LICENSE.txt),
  [licenses/MIT_Microsoft_System.Drawing.Common.txt](src/TabForge.Avalonia/licenses/MIT_Microsoft_System.Drawing.Common.txt)
- Notices for the third-party code inside them:
  [licenses/THIRD-PARTY-NOTICES_DotNet_runtime.txt](src/TabForge.Avalonia/licenses/THIRD-PARTY-NOTICES_DotNet_runtime.txt),
  [licenses/THIRD-PARTY-NOTICES_System.Drawing.Common.txt](src/TabForge.Avalonia/licenses/THIRD-PARTY-NOTICES_System.Drawing.Common.txt)
- Source: https://github.com/dotnet/runtime

## VST 2 plug-in hosting (TabForge's own code)

TabForge hosts VST 2 plug-ins through its own, independent C# implementation of the plug-in binary interface: a
plug-in is a 64-bit DLL that exports `VSTPluginMain`, and TabForge talks to it through the plain C structures and
dispatcher opcodes that such a DLL expects. The code is in `Source/src/TabForge.AudioEngine/Plugins/Vst2Plugin.cs` (and, for
the headless tests, `Vst2TestEffect.cs`) and is covered by TabForge's own MIT licence.

- TabForge contains no Steinberg VST 2 SDK headers or code (no `aeffect.h`, `aeffectx.h` or other file from the VST 2
  SDK, and none of their text), and none are distributed with TabForge. To work with existing plug-ins, the code has
  to use the same structure layouts and numeric constants as the binary interface; it declares them itself.
- VST is a trademark of Steinberg Media Technologies GmbH. TabForge is not affiliated with, sponsored or endorsed by
  Steinberg.
- VST 3 plug-ins are hosted by a different component, the native bridge below, which uses the MIT-licensed VST 3 SDK.

## Steinberg VST 3 SDK and the native bridge (`tfvst3.dll`)

VST3 plug-ins are hosted by `tfvst3.dll`, a small C++ bridge (about 600 KB) built against the Steinberg VST 3 SDK
(v3.8.1, build 84) with MSVC and a static runtime. The compiled bridge is `Source/src/TabForge.AudioEngine/native/tfvst3.dll`,
which the audio engine project copies next to the executable; its C++ source is in `Source/native/tfvst3`. The engine
project treats the DLL as optional: without it the solution still builds and runs, but VST3 plug-ins cannot be loaded
(VST2 plug-ins and everything else are unaffected).
`Source/native/tfvst3` (the VST3 host bridge, `tfvst3.dll`) is built from the Steinberg VST 3 SDK and statically links the
SDK's hosting code. The SDK is not vendored: `Source/native/tfvst3/CMakeLists.txt` fetches one pinned, unpatched revision
(tag `v3.8.1_build_84`, commit `3cdf9ca5d1f5b1b21e0a86832aa4abe55607bd96`; build steps in `Source/native/tfvst3/BUILD.md`).
The checked-in `Source/src/TabForge.AudioEngine/native/tfvst3.dll` is a build of that revision.

- Source: https://github.com/steinbergmedia/vst3sdk (components used: `base`, `pluginterfaces`, `public.sdk`)
- License: MIT, Copyright (c) 2026 Steinberg Media Technologies GmbH (the `LICENSE.txt` of the SDK and of each of
  those three components). Since SDK version 3.8.0 the SDK is MIT only; the earlier GPLv3 and Steinberg
  proprietary licences are no longer offered, and this project uses no earlier version. MIT is compatible with
  TabForge's own MIT licence. Its terms require the copyright notice and licence text to accompany copies of the
  Software, including a redistributed `tfvst3.dll`; the notice is reproduced here and in
  [licenses/MIT_Steinberg_VST3_SDK.txt](src/TabForge.Avalonia/licenses/MIT_Steinberg_VST3_SDK.txt):
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

## CLAP plug-in hosting (interface definitions from the CLAP headers, MIT)

CLAP plug-ins (opt-in: Settings > Audio & Plug-ins > Also list CLAP plug-ins, or the same box in the Add plug-in window) are hosted by TabForge's own C#
code in `Source/src/TabForge.AudioEngine/Plugins/Clap`. To talk to a CLAP plug-in the code re-declares the C structures and constants of the CLAP 1.2
interface (entry, plug-in factory, plug-in, host, process, events, audio buffers and the params, state, gui, audio-ports, latency and tail extensions)
as C# structs; their layout is checked against the official headers by a unit test. No CLAP header or source file is copied into or shipped with TabForge,
and no CLAP code is linked or bundled; a `.clap` file is loaded from the user's own plug-in folders only.

- Source: https://github.com/free-audio/clap (headers read at commit `a47f6badb49d948fd009998f28309cdab78979c9`, CLAP 1.2.10)
- License: MIT, Copyright (c) 2021 Alexandre BIQUE. MIT is compatible with TabForge's own MIT licence; the notice is kept with the licence texts:
  [licenses/MIT_CLAP.txt](src/TabForge.Avalonia/licenses/MIT_CLAP.txt)
- TabForge is not affiliated with the CLAP project or its maintainers.

## Sample song

TabForge includes one original demo song, *TabForge Demo - Ashen Meridian* (the file *TabForge Demo - Ashen Meridian.gp* in the Samples folder at the repository root, next to the Source folder),
written for TabForge and dedicated to the public domain (CC0 1.0). No third-party songs or
transcriptions are included.

## Icons

All icon sets (`Source/src/TabForge.Avalonia/Assets/Icons/`: Transport, Tools, Instruments) and the application icon were designed
for TabForge. They are part of TabForge and covered by its MIT licence (see `LICENSE`). The dynamics, trill and digit
glyphs of some tool icons are outlines taken from the Bravura font (SIL OFL 1.1, see above). Details:
`Source/src/TabForge.Avalonia/Assets/ASSETS.md`.
