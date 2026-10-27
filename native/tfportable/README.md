# tfportable

The native audio and MIDI library for Linux and macOS (Apple Silicon), and an opt-in developer library on Windows. It is **not** part of the Windows release.

- `tfaudio.c`: [miniaudio](https://github.com/mackron/miniaudio) 0.11.25 (public domain / MIT-0) behind a plain C API: context, device lists, playback and capture streams, device events.
- `tfmidi.cpp`: [RtMidi](https://github.com/thestk/rtmidi) 6.0.0 (MIT-style) behind a plain C API: input and output ports.
- `tfportable.h`: the whole ABI (`TF_ABI_VERSION`). The managed side (`TabForge.Audio.Contracts/Platform/Portable/TfPortable.cs`) loads the library with `NativeLibrary` and calls it through function pointers; a missing library only means "backend not available".

Both libraries are fetched by CMake at pinned commits (see `CMakeLists.txt`) and used unmodified. Pass `-DMINIAUDIO_DIR=<path>` / `-DRTMIDI_DIR=<path>` to use existing checkouts.

## Build

    cmake -S native/tfportable -B build/tfportable -DCMAKE_BUILD_TYPE=Release
    cmake --build build/tfportable --config Release

- Linux: needs `libasound2-dev` (ALSA headers) for MIDI; miniaudio loads PulseAudio, ALSA and JACK at run time. Output: `libtfportable.so`.
- macOS: Apple Silicon only (`CMAKE_OSX_ARCHITECTURES=arm64`). Output: `libtfportable.dylib`.
- Windows (developer build, Visual Studio generator): output `tfportable.dll`; copy it to `native/tfportable/bin/` to let the portable backend tests run, or next to the program and set `TABFORGE_AUDIO_BACKEND=portable` / `TABFORGE_MIDI_BACKEND=portable` to try the portable backends. The Windows build keeps WASAPI/ASIO and winmm by default.

The Linux and macOS packages (Stage 5) put the library next to the program.

## Status (Stage 3)

Built and exercised on Windows only (miniaudio null device and WinMM through RtMidi). The Linux (ALSA) and macOS (Core MIDI / Core Audio) builds are written but have not been compiled yet: the first run of `.github/workflows/portable-ci.yml` with a native step is their test.
