# Changelog

TabForge is actively developed; please report anything odd on the Issues page.

## 0.8.3

### At a glance

| Area | What's in it |
|---|---|
| Cross-platform | Builds for macOS and Linux, five stages of groundwork, no change on Windows |
| Recording and MIDI | MIDI keyboard monitoring, input menu, velocity meter, per-plug-in MIDI wiring |
| Timeline and takes | Drag takes between lanes and tracks, overlap and section prompts, mute and solo greying |
| Piano roll | Takes edit their own lane, playback needle, velocity fixes, empty-lane option |
| Plug-ins and audio | VST3 meter updates, FX list editing, engine re-send, DirectSound |
| Media and export | Song folder layout, missing-audio finder, video export fix, MusicXML round trip |
| Menus and status bar | New Insert menu, tidier menus, live status bar with scale detection, Full screen |

23 new, 26 changed, 35 fixed, plus 35 cross-platform and under-the-hood items.

### New
- Stretch a section by dragging its end past the next section: add empty bars, repeat its last bars, or add a repeat sign; or drag its start edge backwards past the previous section: add empty lead-in bars, repeat its opening, or take bars from the previous section (with Remember my choice).
- Section right-click menu: Remove label (keep bars) removes just the section name, leaving the music in place.
- Status bar scale readout: detects the likely scale of the current section and track; click it to see alternatives, choose your own and set what it listens to (sharps by default; an option shows musical key names such as E♭).
- Right-click an empty area of the section strip to close the gap and move later sections back (asks first if the bars aren't empty).
- MIDI files dropped onto the timeline become MIDI clips where you drop them (dropped elsewhere they still open as a song).
- Dropping audio or MIDI files from outside shows a preview of exactly where (lane and time) they will land.
- Drag audio and MIDI takes up and down to other lanes or onto other tracks (Ctrl copies).
- Drag a take below the last track to move it onto a new track.
- While a take is dragged to another lane or track, the take itself moves there (its old spot stays empty and the target lane is only lightly highlighted); with Ctrl the original stays and the copy shows at the target. Escape puts it back.
- When an edit makes clips overlap on a lane, TabForge offers to move the existing clip to a free lane (with Remember my choice).
- **Cross-platform groundwork**: all five stages below are in. TabForge now compiles and publishes for macOS (Apple Silicon) and Linux; Windows behaves as before.
- **FX list editing**: multi-select (Ctrl/Shift click, Shift+arrows, Ctrl+A), Delete, cut/copy/paste/duplicate plug-ins with their settings (also into another track's FX window), and a right-click menu. All undoable.
- **Moving a section that audio crosses** now asks whether to split the audio and move it along, or leave it in place, with "Remember my choice" (Preferences > Editing > Clips: Ask each time / Split / Leave).
- **Optional SoundFont sound engine**: Settings > Audio > "Sound engine" chooses between Windows GM (the default on Windows) and the TabForge SoundFont (GeneralUser GS, played by the built-in synth, the default on macOS and Linux). The SoundFont is not shipped: a "Download SoundFont..." button fetches it once (32 MB, checked with SHA-256). Switching applies at once.
- **Optional bundled fonts**: Settings > Appearance > "Interface font source" chooses between Windows (Segoe UI / Consolas, the default on Windows) and TabForge bundled (Inter / JetBrains Mono, SIL OFL, the default on macOS and Linux). It applies to the interface and the score.
- **Extra compact spacing**: Settings > Appearance > Spacing has a fourth option (70% bars, rows and pads). Compact is now the default spacing; a saved Comfortable switches to Compact once, and choosing Comfortable again is kept.
- MIDI input monitoring: a MIDI keyboard now sounds through the track's own instrument (its sound, volume, pan and FX) when the track is armed with a MIDI input, and through the selected track when no armed track takes that keyboard. Setting "Monitor MIDI input" (Preferences, MIDI recording): Armed tracks + selected track (default), Armed tracks only, or Off. Notes go straight to the audio engine; live input's stand-in yields to an armed track so nothing sounds twice; recording is unchanged.
- Piano roll: an optional green playback needle runs down from the playback marker (right-click menu or Preferences to turn it off).
- Score: right-click and drag selects notes without setting a loop (a plain right-click still opens the menu).
- Setting: clicking an empty take lane can open the piano roll (writing there creates a MIDI take); the default still shows the tab.
- Copy audio with this song can now keep the song and its audio together in one song folder (or the Media folder beside it, as before).
- Missing audio: right-click a missing clip to locate its file, or search a folder (and its subfolders) to relink every missing clip at once.
- New Insert menu gathers everything you can add: tracks, bars before/after the selection or several at once, sections, tempo/time/key changes, repeats, chords, text, lyrics, audio and MIDI files.

### Changed
- New prompts share one cleaner dialog style: heading, one-line summary, compact choice cards and right-aligned buttons.
- The detected scale shows in the status bar only; the fretboard shows a scale only when you pick one there (the "Show on the fretboard" option in the scale popup is off by default).
- View menu choices (notation, score page and similar) show a tick next to the active one.
- The built-in Compose / Practice / Mix / Band layout presets were removed; you can still save your own layouts or return to Default.
- The out-of-date Tutorial and Detailed guide were removed from the Help menu.
- Playback highlight follows the accent colour by default (or a custom colour); its look settings moved to Appearance and duplicates were merged. The piano roll needle, its ruler marker and its lit keys use the same colour. The playing-bar shading is on by default, subtler (5% opacity), and its opacity can be set in Appearance.
- Choice cards in dialogs are clickable anywhere and show hover and selection.
- Snap settings open in a popup above the magnet button, like the loop and metronome settings.
- "Audio track" is now "Audio/MIDI track" and "Instrument track" is now "Tab track" throughout.
- A MIDI-armed track's input meter now shows incoming note velocity, like the audio meter.
- Clicking a MIDI take now opens its track in the piano roll.
- Audio/MIDI tracks show a lane instead of bar boxes, and clicking it opens the piano roll.
- The track's input monitor and input selector are one clear button pill with its own hover on each half; the speaker half turns blue when monitoring is on.
- Compact mode and Band view: the close button moved to the far left, so the right-hand icons stay exactly where they are in the normal bar.
- Timeline: muted tracks grey out their takes and clips too, and solo greys out every track you won't hear.
- Selecting a track no longer shades its lane in the timeline (the track list keeps its selected shade).
- VST3 plug-ins now receive their live meter and display updates from their audio side (for example the FabFilter Pro-L 2 waveform).
- The notification bar under the menu row now shows only errors, warnings, video recording and audio/video render or export messages; ordinary action feedback (loop set or cleared, copied, mode switched, saved) no longer pops up.
- A new track's colour follows its instrument group (drums blue, bass yellow, guitar red, everything else green) and keeps following it when you change the instrument, until you pick a colour yourself; existing and imported tracks keep their colours. The "Unsaved track changes" list shows colour names (e.g. "Red → Glide blue", "Custom (#A12424)") instead of hex, with a little more space above "Don't ask me again".
- Preferences colour rows that only apply in Custom (Playback highlight colour, the Custom palette colours) now say "Choose Custom above to pick a colour." while greyed out, and enable at once when you choose Custom; stored colours are kept when you switch back.
- Status bar: shows Playing/Paused/Stopped/Recording, follows the playhead with bar and real time during playback, and drops the MIDI light and note-length items.
- Band view rows no longer shrink below a readable height (header, one full tab line at the current zoom, plus the fretboard when shown); with more rows than fit, the view scrolls (mouse wheel over a row, or its scroll bar) and selecting a track from a pill or the timeline scrolls its row into view; it never scrolls by itself during playback. New Preferences row "Band row minimum height" (Small / Medium / Large, default Medium) in Timeline > Band view.
- The track input menu has two single-choice lists, "Audio input" (None, Input 1, Input 2, ...) and "MIDI input" (None, All MIDI inputs, then each device), so a track can have no input, audio only, MIDI only, or both; the button reads "Input 1 + SZ-CONTROLKEY49S", "MIDI: All" or "No input", arming a track with no input says "This track has no input", and the piano roll's live keys follow the track's chosen MIDI device. Older songs migrate (one device stays, several become All MIDI inputs).
- Piano roll: the mouse wheel over the keyboard strip scrolls up and down.
- Menus reorganised: the Sound and Sections menus are gone (playback is on the transport bar, section commands are in Bar), Effects are grouped into submenus, Export is a File submenu, and every command now lives in one place (shortcuts unchanged).
- "Empty bar" is now called "Clear bar".

### Fixed
- Exported videos end with the music (no frozen frames after the last bar).
- Inserting bars, tracks or sections redraws the score and timeline at once.
- A song exported as MusicXML now opens again as the same song. Before, every note of a tab part came back twice (the notation staff was read as a second voice and put its notes on string 1 at fret 0, so tabs showed rows of 0s on the high string), piano and notation-only pitches landed on string 1 at fret 0 with the wrong pitch, every track turned red, mixer volume and pan reset (119 / centre), and dead notes, let ring, vibrato, slides, bend curves, tied notes, loudness, mute / solo, comments and chord names on rests, mix changes, D.C. / D.S. / Coda jumps, double bars, pickup bars, the guitar clef, swing, multi-line lyrics and the song details (album, authors, copyright) were lost. Track colours, volume, pan, mute / solo and section colours are now written with MusicXML fields (part colour, midi-instrument volume and pan) and TabForge notes; a MusicXML file from another program that has none gets the instrument-group track colour and the default mixer.
- View > Full screen (F11) now works; press F11 or Escape to leave it.
- Full screen: menus and toolbar buttons at the top work and highlight normally, and the window can no longer be dragged while full screen.
- Right-clicking an empty part of the section strip shows only Add section, Paste section and Timeline settings.
- A clip-overlap question answered late no longer undoes what you edited meanwhile (Cancel used to undo the newest step, whatever it was). A take drag also ends cleanly when the track list changes or a second press arrives during it, a clip narrower than a pixel no longer fails to draw its waveform, and every drop no longer reopens every audio file of the changed tracks in the engine (clips that did not change keep playing).
- A file dropped into a new song shows on the timeline at once (it sometimes stayed hidden until the window was refocused).
- A plug-in whose MIDI input is set to None no longer receives MIDI.
- Export video no longer hangs at 99% on "Finishing video…" with some hardware encoders. Export now chooses its encoder the way live recording does: on Auto it uses the hardware encoder only after that encoder passed the quick self-test. Frames are fed to a hardware encoder no faster than it encodes them, as in a live recording, so nothing piles up to be finished at the end. If a hardware encoder still makes no progress for 15 s, the video is made again with the software encoder ("Hardware encoder stalled — finishing with the software encoder…"), and that choice is remembered. The Video section of the dialog has a new Encoder choice (Auto, Hardware or Software). Cancel export now stops at once at every stage, including while finishing, and the dialog never freezes. Working files go to the app's temp folder instead of your Videos folder. Each export writes a "video export" block to errors.log.
- Playback highlights notes reached by slides, hammer-ons, pull-offs and ties, like Guitar Pro.
- Returning from the piano roll to the score no longer sometimes turns the fretboard panel off.
- Deleting the MIDI take shown in the piano roll returns to the score.
- Dragging a clip shows exactly how it will look when dropped, including repeat passes.
- Dragging a clip slightly over a repeat sign no longer makes it jump (sometimes to the song start).
- Right-clicking an empty take lane now shows lane actions (paste clip, new MIDI clip, import, record here, lane options) instead of the bar menu.
- Piano roll: editing a MIDI take's notes no longer asks whether you want to edit the tab.
- Piano roll: clicking a MIDI take now edits that take, not the track's tab.
- Clicking a track's tab after a MIDI take returns to the score; only MIDI takes open the piano roll automatically (a piano roll you chose yourself stays).
- After the audio engine restarts or the audio driver changes, it now receives the song's current clips and mixer settings again (edits no longer seem to be ignored during playback).
- MIDI takes no longer show fade-in and fade-out handles.
- DirectSound playback is steadier: its buffer now has a 100 ms floor per half (200 ms in all; it follows the Buffer size setting above that, 4 blocks per half) and the refill monitor no longer reports normal 90 ms refills as dropouts. Refills that arrive late, run the buffer dry or render slowly are now counted and logged (at most one line a second, from the engine's main thread, never the audio thread) so a crackle report can be diagnosed from the engine log.
- A new tab track (and a new song's first track) now hears all MIDI inputs by default, so plugging in a keyboard and arming records it; Audio/MIDI tracks still start with MIDI None. The MIDI log now lists the inputs found and each one opened or closed, a missing optional plug-in folder is no longer logged as an error, and a start-up failure logs its stack.
- Clicking a take or MIDI clip in the timeline no longer selects notes or bars in the tab above it.
- Dragging an audio clip or MIDI take across repeated sections no longer leaves a gap, stretches it or jumps it back a pass.
- Track headers keep their normal top-aligned layout after recording a take, and the armed input line sits directly under the track name in every track state.
- Selecting a timeline clip no longer ends redo or adds an undo step when you had no score selection of your own.
- The piano roll mode pills and live keys follow your accent colour instead of staying blue.
- Piano roll with the "Used" row fold showed a single row (and a blank key strip) when a track had only one or two distinct pitches; with fewer than three used pitches it now shows an octave either side of them.
- The plug-in parameter panel's Undo and Redo keys follow your Edit > Undo / Redo shortcuts, so a rebind works there.
- The Record video button's camera is centred in its button (it sat about 2 px high).
- Preferences, Interface font "TabForge bundled" now applies at once to every open window and later ones, and repaints the score and tab text.
- Band view: clicking a timeline track no longer makes a second fretboard appear (only the instrument panel toggle and the Piano/Tab pills show or hide it), and compact mode on top of the Band view no longer changes which track pills are highlighted.
- Piano roll: dragging the velocity of several selected notes no longer jumps to the maximum, minimum or back to the middle.
- Inserting bars (Insert > Bar > Before / After / Several bars / At the end, Bar > Insert bar (Ctrl+Insert), the timeline's Insert bar rows, Range insert, Duplicate and Repeat) now goes through one shared insert: a track shorter than the song gets its new bars at the same place as every other track instead of at its own end, sections, repeats, tempo changes and clips after the insertion point move with their bars, bars inserted in front of a section start join the section before it, and a tie into the bar after new empty bars is dropped. Bar > Delete bar now moves sections and clips back too.
- An on-beat grace note no longer moves its main note half a slot later (with a short rest before it) each time a .gp is saved and reopened.
- A .gp file with damaged view settings (PartConfiguration) is refused at once with the damaged-file message instead of running until the import time limit.

### Stage 1: Windows-only calls behind platform ports (cross-platform groundwork, no change for Windows users)
- Every Windows-only call (P/Invoke, registry, Media Foundation, WASAPI/ASIO, HWND, Program Files, Ctrl key) now sits in a Windows platform file behind a small interface, with a non-Windows stub: IHighResTimer, IChildProcessGuard (NullChildProcessGuard), IProgramFolders, IMp3Encoder, ISystemAudioReader, IFileAssociations (NullFileAssociations), IProcessIntegrity, INativeWindowHandles, and PrimaryModifier for Ctrl/Cmd.
- The portability guard baseline fell from 145 to 0; it now ignores platform files by a clear rule (Platform/Windows and Shell/Win32 folders, files named Windows*.cs or *.Windows.cs).
- New non-shipping workflow .github/workflows/portable-ci.yml builds Contracts and Core and runs the guard tests on Ubuntu and macOS.

### Stage 2: portable paths, settings and engine IPC (cross-platform groundwork, no change for Windows users)
- New IAppFolders (settings, data, cache, logs, temp, runtime, plug-in cache, SoundFonts, Music, Videos, REAPER folder): Windows keeps its exact folders; macOS uses ~/Library/Application Support/TabForge and ~/Library/Caches; Linux follows XDG_CONFIG_HOME, XDG_DATA_HOME and XDG_CACHE_HOME with ~/.config, ~/.local/share and ~/.cache fallbacks. UserPaths, the Music and Videos defaults, the REAPER folder, the crash and first-chance logs and the single-instance lock all read it.
- New IShellLauncher (open folder, file or web address, reveal in the file manager): Windows as before (the shell, Explorer), macOS open / open -R, Linux xdg-open (reveal opens the parent folder). Web addresses are limited to http and https.
- New PathComparer (ignore case on Windows and macOS, exact on Linux) now used by the plug-in scanner, plug-in trust, the media and scratch path rules, the file-save fallback list and the single-instance lock name; path joins in those rules use the platform separator. Windows results are unchanged.
- Engine IPC: Windows keeps its named shared memory and named pipes exactly. Other systems get a file-backed memory-mapped file (owner-only folder and file, removed when the creator closes) and short pipe names that fit the macOS socket-path limit. A headless test runs the portable path on Windows too.
- The portability guard now also counts user-folder lookups, shell-execute starts and Windows font names outside the platform files (baseline recorded, shrink-only).

### Stage 3: audio, MIDI and MP3 behind backends (cross-platform groundwork, no change for Windows users)
- New IAudioBackend in the engine: opening the output, the audio input, the device lists and the sample rate go through one interface. Windows uses WindowsAudioBackend, which calls the same WASAPI, ASIO and DirectSound code as before; the headless test output is unchanged. Other systems use MiniaudioBackend (miniaudio: Core Audio, ALSA, PulseAudio, PipeWire, JACK), and the settings of a Windows machine copied there become the "System audio (miniaudio)" driver on the default device.
- New IMidiInputBackend for MIDI inputs (MidiInputCapture and MidiInputHub keep their events): Windows keeps winmm through NAudio, other systems use RtMidi (Core MIDI, ALSA). MIDI output uses the same pattern through the existing IMidiDevices; the device-list header is "Microsoft MIDI Mapper (default)" on Windows and "Default MIDI output" elsewhere.
- MP3 export: Windows keeps Media Foundation; other systems encode with LAME (libmp3lame loaded as a separate file, LGPL), and MP3 export stays switched off where the library is missing.
- Sound bank: off Windows the engine never reads the Windows gm.dls. It plays the bundled GeneralUser GS SoundFont (packaging adds it in Stage 5) or, when that is missing, the synth reports "download the TabForge SoundFont" and the status bar shows the same hint, instead of staying silent; off Windows the Sound engine setting offers the SoundFont only, and tracks without a plug-in always play through the engine. Windows is unchanged.
- New native library source native/tfportable (miniaudio 0.11.25 and RtMidi 6.0.0 behind a small C ABI, built with CMake for Windows (developer build only), Linux and macOS Apple Silicon); the Windows release does not contain it. Developer switches on Windows, all off by default: TABFORGE_AUDIO_BACKEND=portable, TABFORGE_MIDI_BACKEND=portable, TABFORGE_MP3_ENCODER=lame.
- The audio engine now also builds for net10.0 on any system (-p:TabForgePortableBuild=true on Windows) with its own lock file; plug-in process isolation is still Windows-only there.
- Licence texts for miniaudio, RtMidi and LAME were added to THIRD_PARTY.md and the licenses folder.

### Stage 4: plug-in host abstraction and CLAP (cross-platform groundwork; VST2 and VST3 unchanged for Windows users)
- New plug-in module loader and editor host seams: the plug-in binary of a VST3 bundle or CLAP file is found by a per-system layout rule (Windows Contents\x86_64-win, macOS Contents/MacOS, Linux Contents/x86_64-linux or aarch64-linux; a .clap is a file on Windows and Linux, a bundle on macOS) and loaded through NativeLibrary (LoadLibrary or dlopen). Plug-in editors go through IPluginEditorHost: Windows keeps its Win32 editor windows exactly; macOS and Linux get a host that says "editor not supported yet" without crashing.
- Standard plug-in folders per system: Windows keeps today's VST3 list; macOS scans /Library/Audio/Plug-Ins/VST3 and ~/Library/Audio/Plug-Ins/VST3; Linux ~/.vst3, /usr/lib/vst3 and /usr/local/lib/vst3. VST2 stays Windows-only: the scanner skips .dll plug-ins on other systems.
- New, opt-in: **CLAP plug-ins**. Turn on "Also list CLAP plug-ins (new)" in Settings > Audio & Plug-ins or in the Add plug-in window to list .clap files (Windows %CommonProgramFiles%\CLAP and %LocalAppData%\Programs\Common\CLAP, macOS /Library/Audio/Plug-Ins/CLAP, Linux ~/.clap and /usr/lib/clap) and any .clap you browse to. They load, play (stereo audio, notes and MIDI, tempo and time signature), keep their settings in the song, and show their own editor window on Windows. They go through the same plug-in approval and "run each plug-in separately" rules as VST plug-ins, and with the box off nothing about VST scanning or loading changes.
- CLAP interface definitions come from the MIT-licensed CLAP headers (free-audio/clap); the licence text is in licenses\MIT_CLAP.txt and THIRD_PARTY.md.
- **Parameters for CLAP plug-ins**: the FX chain window shows sliders (name, value with its units as the plug-in writes them, search box, Undo and Redo of your changes, 100 rows at a time with "Show more") for a CLAP plug-in whose own window does not open, and a "Parameters" button swaps the editor for the same panel for any CLAP plug-in. The values are read twice a second only while the panel is open, and the engine is asked one question at a time.
- **REAPER chain export now includes CLAP plug-ins**: a CLAP plug-in is written as a CLAP block (its plug-in id, name, vendor and saved settings) when the engine has it loaded; one it cannot describe is named in the export notes as before. Importing still skips CLAP blocks.
- **macOS and Linux VST3 shim (not built here)**: native/tfvst3/posix builds libtfvst3.so and libtfvst3.dylib with the same C interface as tfvst3.dll (bundles loaded with dlopen and ModuleEntry / ModuleExit, or CFBundle and bundleEntry / bundleExit); the audio engine finds the bundle's binary through the module loader first. A new native-ci workflow builds it on Ubuntu and macOS. Windows still uses the checked-in tfvst3.dll, unchanged; plug-in editors on these systems are not hosted yet.

### Stage 5: video encoder port, Unix path rules and packaging scripts (cross-platform groundwork; Windows video and packages unchanged)
- New IVideoEncoder port: the Media Foundation encoder is its Windows adapter and stays the Windows default (hardware then software, golden self-tests unchanged). A portable encoder pipes raw frames to an ffmpeg program found on the PATH, next to TabForge or in TABFORGE_FFMPEG (H.264/AAC MP4; nothing is bundled). On Windows the Video encoder row offers "FFmpeg (found at ...)" only when an ffmpeg is found. Live screen recording stays Windows-only (DXGI); elsewhere the button says so, and Export video works through ffmpeg.
- Unix path rules: UnixPathRules (media) and UnixClipPathRules (engine clips) replace the drive letter, UNC and 8.3 logic off Windows; device trees (/dev, /proc, /sys) are refused and mounted volumes need approval. Windows rules are untouched.
- Plug-in host block, plug-in host pipe and import-worker pipe now work off Windows (file-backed map with polled flags, short pipe names); Windows keeps its named map and events. UnixChildProcessGuard and ParentWatchdog end child processes when the owner ends (Windows keeps the Job Object).
- Non-shipping packaging scripts tools/package-mac.sh (TabForge.app, Info.plist, entitlements, signing placeholders) and package-linux.sh (AppImage); both build libtfportable, bundle libmp3lame and the GeneralUser GS SoundFont. Publish profiles osx-arm64 and linux-x64; portable-ci.yml also builds the engine, runs the Stage 5 tests with a real ffmpeg and attempts the app publish.
- The app now compiles and publishes for linux-x64 and osx-arm64: the audio engine client, playback engine, live input, recording and render code, and the app's Playback folder are part of the portable Core build (plug-in file trust and final paths have portable adapters; only the Media Foundation encoder, DXGI capture and the Windows audio-path probe stay Windows-only). Windows is unchanged.

### Under the hood
- **Tidier source**: tests are grouped into area folders, app and Core files into topic folders, and the developer docs into architecture, guides, reference and media folders under docs/.
- Diagnostics: plug-in editor windows log their update and processing rate to help find frozen meters.
- Tests: the file-link self-test now falls back to a privilege-free reparse point when a symbolic link needs rights, so it runs instead of skipping; the .tforge save tests compare decompressed data, and the Mixer column and Preferences tests no longer skip or depend on test order.
- **.NET 10**: TabForge now builds and runs on .NET 10 (LTS); .NET 8 support ends on 10 November 2026. Nothing changes in how the app looks or behaves.
- **Portability guardrails** (first step towards macOS and Linux): the platform-neutral core now builds without Windows, CI checks that on every push, and a test stops new Windows-only calls from spreading outside the Windows platform folders.
- **One decoder for MP3 and OGG**: MP3 and OGG files now decode with built-in managed decoders (NLayer, NVorbis; both MIT) on every system; FLAC, AAC/M4A and WMA keep using Windows Media Foundation as before, and any file the new decoders reject falls back to it.
- **Single instance by lock file**: a second launch is detected with a lock file in the TabForge data folder (a crash leaves no stuck lock) instead of a Windows mutex; handing a song to the running window and bringing it to the front work as before.
- **Shell launch**: Open folder and Third-party licences open through Avalonia's launcher instead of starting Explorer or Notepad directly (licences open in your default Markdown or text app).

## 0.8.1

### New
- **MIDI piano roll**: MIDI tracks and takes open in a piano roll in place of the score; Piano / Tab pills and right-click switch any track (remembered per track). Tools: select, draw, erase (click or drag box), split, mute, glue; Ctrl/Shift selection; multi-note move/copy/resize/transpose; legato; quantize with strength and swing; triplet and dotted grids shared with the timeline snap; scale highlight and row folding; colour modes; info line; step input; velocity lane (drag bars, selected bars highlighted), CC and pitch-bend lanes; follows the timeline scroll and playback with a playhead line; live MIDI keyboard keys light up. The fretboard/keyboard panel hides in piano roll mode. Editing a tab track asks first and changes only the edited notes.
- **Media collect and relink**: saving can copy outside audio files into a "<Song> Media" folder (Ask / Always / Never); paths inside the song folder are saved relative; missing audio shows a banner, hatched clips and a Relink window; File > Collect media / Find missing media.
- **Track inputs:** the input popup has an Audio input (one) and MIDI inputs (tick any) section, updates when devices are plugged in, and recording uses only the ticked MIDI devices.
- Tab notes can be muted (silent) without changing the written tab.
- **MIDI recording**: recorded notes show live in the piano roll; Preferences > Recording: "When recording over a MIDI clip" New lane / Merge into the clip underneath, and optional "Quantize MIDI input" (snap grid, off by default); closing the window mid-recording keeps the take (undoable).

### Improvements
- **Piano roll, Reaper-style editing**: one Edit tool (left-drag on empty grid draws a note, sideways = length, up/down = pitch; drag note body/edges/top edge = move/resize/velocity with live values); right-drag box select, right-click menu, middle-drag pans, Escape cancels; double-click deletes / inserts; Ctrl+drag copies, Alt = no snap; velocity lane paint and Shift ramp; Length dropdown (Grid, fixed, triplet, dotted, Last used); key strip Piano keys / Note names (GM drum names); position and note/velocity readout; hovered-row band; "By lane" heat colours; velocity line inside tall notes; optional ghost notes from other takes (off by default); info line accepts +/- values. Wheel = time zoom, Ctrl+wheel = note height, Shift+wheel = vertical scroll, Alt+wheel = horizontal scroll.
- **Section resize by edges:** drag a section's left or right edge (10 px grab zone, brighter hint); the neighbouring section never stretches. The old knobs are retired.
- **Render:** "Time selection" renders the loop region; "Selected bars" renders only the selected bars, tracks and clips.
- The fretboard keeps its size when the score is zoomed.

### Fixes
- Piano roll: a green playhead triangle in its ruler (also when stopped; drag to move, edge arrow when off screen); piano keys as wide as note names; selecting notes in the piano roll no longer creates a loop (score/tab unchanged).
- The clip split marker no longer stays behind after a clip is moved, deleted or deselected, and never draws outside its clip.
- Arming a track grows a short timeline panel just enough to show the armed row; the input and monitor buttons have proper borders and hover states.
- FX window tuner strip: no dead gap; the cents scale shrinks before controls wrap; a small gap below the title bar; the tuner window tooltip is no longer cut off and reads more clearly.
- The video-recording button in the title bar is centred.
- **Piano roll edits vanished on mouse release** (drawn notes, box selection, note drags): the gesture is now finished and committed before the mouse is released. Selected notes keep their colour with a bright outline; muted notes are hollow.
- Piano roll: sounding notes are highlighted during playback and the view follows the playhead. Mouse wheel zooms time, Ctrl+wheel zooms key height, Shift+wheel scrolls sideways, Alt+wheel scrolls up/down.
- Piano / Tab pills moved into the menu bar (they overlapped the score and piano roll toolbar), with hover states; piano roll keys get a hover tint and the keyboard no longer shows blank space below C-1.
- Resizing a section could stretch its neighbour (e.g. Verse 2 / Chorus 1).
- The fretboard/keyboard toolbar button stayed highlighted after the panel was closed.
- Track input list was clipped and covered by its tooltip.
- Piano roll snap could use out-of-date note positions while dragging.
- **Recorded audio takes landed in the wrong place** (often many bars later) when Record was pressed while the song was already playing; the take start was converted to song time twice. Covered by a two-track, mid-song, repeat-song record/save/reload test.
- Recording while the song was already playing could stop other audio clips (the engine clock was held); it now keeps running.
- The "Recorded N s on track" notification is gone (errors and lost-audio warnings still show).

### Under the hood
- Version 0.8.1. Audit (hygiene, maintainability, security, performance, size): long files split, missing file headers added, ~560 old WPF comments rewritten, debug-symbol DLL left out of the release.
- Architecture rule fixes (shared state moved per song, size tokens, no debug prints).
- GitHub CI passes on the public repo layout; AlphaTab package rebuilt reproducibly.
- Retired the stale WPF-era playback step in REBUILD; out-of-date section, paste and clip-click tests updated to current rules.

## 0.8.0

- **Platform**
  - TabForge now runs on Avalonia instead of WPF: the first step toward running on more platforms in the future. Windows 10 / 11 64-bit only today.
  - One consistent look for every control: the same hover, pressed, focus and disabled states everywhere.
  - The interface stays responsive during playback, window moves and resizes; windows open and close at once.
  - A UI-free Core project holds the music, file and playback logic; the audio engine and plug-in host keep their own processes.
  - The portable ZIP is smaller (about 99 MB down to about 65 MB).

- **Selection and loops**
  - There are three separate things: the timeline selection (white), the score note selection and the loop region. Loop on / off is only ever changed by F9 or the Loop button.
  - Timeline selection: click a bar cell or clip (moves the playhead and scrolls the score), Shift+click extends, Ctrl+click toggles without moving the playhead, drag a box to select, Ctrl+drag a box to add.
  - The timeline selection lights only cells and clips with content.
  - The timeline selection is cleared by a new timeline selection (without Ctrl), a new score drag selection (undoable), the first Esc, or a plain click on an empty lane. A ruler click does not clear it, and loops never clear it.
  - Score selection keeps the familiar note-selection style (no rectangle). A single click places the cursor or selects one note and never makes a loop.
  - Dragging (or Shift-extending) a selection in the score sets the loop region to its span on release, replacing the old loop at once, even while playing. The Loop button is unchanged, and the timeline selection is cleared.
  - The score selection is cleared by a new timeline selection (the loop stays), a plain click in empty score (the loop stays), the first Esc, a new ruler loop or Clear loop.
  - Loop region: drag on the ruler, resize with its handles or move it by the middle; or use the ruler menu (*Loop this section*, *Loop selected bars* from the timeline selection), or drag a selection in the score. A new loop replaces the old one.
  - The loop shows as an orange bar with handles on the ruler, a band in the lanes and a tint in the score (with the notes, when it came from the score).
  - The loop is cleared by the second Esc, a plain ruler click (which moves the playhead and leaves the timeline selection alone), or *Clear loop* in the ruler menu and in the score menu inside the loop. Clearing is undoable and never toggles the Loop button.
  - Only one selection exists at a time: a timeline selection replaces a score selection and the other way round; one Ctrl+Z brings the old one back.
  - Bar commands (copy, cut, delete, paste bars and the rest) never use the loop, only the current selection.
  - A score selection is one track; a timeline selection covers all tab tracks. Copy never asks; Paste Special offers Overwrite / Merge / Insert with repeat, as one undo step.

- **Appearance and light mode**
  - Light mode redesigned: pale accent-tinted selections, a light maple fretboard and white section labels with a hairline.
  - A dark accent colour is lightened automatically when you switch to Light.
  - Every window, Preferences included, follows the Light / Dark choice and the accent colour live.
  - Spacing (Compact / Comfortable / Spacious) now really changes the padding and gaps of the interface chrome (toolbars, transport, track rows, menus, Preferences, FX and tool windows, dialogs). Score, timeline, fretboard and text size are unchanged.
  - Colour rows open a compact colour popup; colour pickers preview on one click, double-click to choose.
  - Better contrast for several light-theme texts and controls.

- **Audio and FX**
  - Monitor FX: a headphones button in the title bar. Click to open, right-click for on / off and program-wide or this-song-only. Live only, never exported.
  - Tuner strip in both FX windows: a tuner with cents scale, input and output meters, and gain / volume / pan.
  - Audio buffer and sample rate are kept per driver and device; ASIO shows its sample-rate list and readable channel names.
  - Cancel in audio settings restores every device's buffer and rate.
  - The instrument picker is rebuilt, with a live preview of the sound.
  - Reaper FX chain import and export improved (scanning, containers with indent and bypass); a plug-in that will not load as VST3 or VST2 tries the other format.
  - Plug-in hosting fixes for the VST3 helper, ReaControlMIDI and piano after restart; a lost audio device is reopened.
  - Volume and pan change live; double-click a fader or pan knob to reset it.

- **Recording**
  - Loop-recording takes: each pass of a loop recording is its own take; right-click the clip > *Takes* to switch.
  - Clips and takes that start mid-song land on song time; the live take box follows the song clock.

- **Playback and score**
  - Playback highlight colour setting: Auto, Follow accent or Custom.
  - The playback line now runs through the notes being played.
  - Section previous / next move the playhead and scroll the timeline without starting or stopping playback.
  - Repeats, multi-pass endings (1.2.3.), D.S. / D.C. / Coda / Fine and tempo changes play in the written order; starting, seeking and speed changes keep their pass.
  - Clicking in the timeline while paused plays from that point.
  - Follow playback is a setting with a hotkey; after scrolling away, resume returns to the playhead.
  - The tempo in the status bar and tempo bar follows the played position, including tempo ramps.
  - The instrument panel follows the instrument: keyboard for piano and pads, fretboard for guitars and bass. Piano and pad tracks imported from Guitar Pro no longer show a guitar fretboard.
  - The fretboard shows the fret number on hover; keyboard keys and pads give hover and press feedback.

- **Timeline**
  - Audio and MIDI clips over a repeat section have a lane per pass and play in the written order; repeats of 2 or more passes fold to one lane with a "pass k" tag.
  - Waveforms show a clear status (Approve / Retry); MIDI clips draw their notes by pitch and velocity.
  - Dragging a clip past the song end adds empty bars, and removes them if you drag back.
  - The bar highlight is grey and only on the timeline.

- **Preferences and settings**
  - Preferences respond at once; Cancel or closing asks and restores your settings. Search finds names, synonyms, pages and shortcuts.
  - Single-instance tool windows: opening Preferences, the Mixer or a similar window again brings the open one to the front.
  - Settings that did nothing are now wired up: *Show the toolbar*, *Confirm before closing unsaved*, *Icon size*, *Animation speed*, *Reduce animations*, *Duration tint*, *Playback speed*, *Band playhead line*, *Band follow like score*, *Band smooth follow*, the tab close-button choices (*Active tab only*, *Active and hover*) and *Open songs from Explorer in*.
  - Settings for features that do not exist yet are hidden until they do: *Fretboard style*, *Track volume* and *Track pan* slider / knob style, and *Keep Band order in sync*. *Show icons on tool palette* is hidden too (the palette is icon-only). *Playback line colour* is replaced by the new *Playback highlight* setting; a colour you had set moves there.

- **Live video**
  - A guard against black recordings.
  - A notice when a recording finishes or fails.
  - The last frames before the end are no longer lost.
  - Closing waits for a recording to save.

- **Fixes**
  - The app could fail to close.
  - Notes are no longer dropped on chains of legato slides.
  - Moving the live input strip, resizing Preferences or the Mixer and dragging sliders no longer freeze the screen.
  - Fewer false red bars: an empty or short second voice is fine and .gp checksums are right.
  - Timeline zoom freeze and the ruler label bug.
  - Cut no longer deletes a track; undo after a cut redraws the timeline; deleted notes no longer stay on the timeline.
  - MIDI clips obey lane plays (a greyed old take is no longer heard).
  - A truncated .gp opens up to its last full bar; .gtp / .tg files show a message to re-save as .gp5 in TuxGuitar.
  - Many smaller timeline, selection, combo-list, theme-contrast and playback fixes.

- **Under the hood**
  - Code cleanup: every source file under 500 lines, long methods split, shared constants (MIDI limit, button sizes) in one place; behaviour unchanged.
  - Audit rounds for dead settings and code hygiene, with fixes; tests updated for the new selection and loop rules.
  - The audio engine ends with the app; engine logs are written per session.

### Late fixes

- Floating plug-in windows belong to their song: they hide when you switch songs and close with the song; closing the FX window keeps them open.
- Clicking an audio or MIDI clip moves the playhead to the clicked bar, not the clip start.
- Audio tracks show a short note in the score pane instead of an empty score.

### Coming soon

- MIDI piano roll editor for MIDI tracks (piano keys, note grid, velocity lane, editing tools).

## 0.7.7

- **Playback**
  - Stopping playback or the audio engine no longer pauses the window while the engine shuts down.

- **Projects**
  - A MIDI clip with more than 256 notes saves and reopens.

- **Security**
  - Plug-in loading, linked audio paths, archive reads and the engine log file are hardened.

- **Shortcuts**
  - "Show the notes in each track lane", "Show one continuous line per track lane" and "Preview note sound" can now be given a shortcut in Settings > Shortcuts.

- **Drums**
  - The drum key map and the drum map window spell Castanets correctly.

- **Presets**
  - Renaming or duplicating a preset onto a name that already exists asks first instead of replacing the other preset.

- **Settings**
  - A saved window layout that cannot be read falls back to the default layout and keeps every other setting.

- **Appearance**
  - Error text, the warning icon and the tuner's lit string are easier to read in the dark and light themes.

- **Mixer**
  - A selected mixer strip keeps its muted and solo state in its screen-reader name.

- **Layout**
  - A settings file whose workspace has a null floating-window list loads with its docked layout kept, instead of resetting the settings to defaults.

## 0.7.6

- **Presets**
  - Renaming or copying a preset never replaces another preset of that name unless you confirm it.
  - Preset drop-downs can be organised in place: rename, delete and reorder your own presets in the open list. Every list ends with "Manage presets…".
  - Preset manager (Tools > Preset manager…) holds the plug-in presets, FX chains, containers, MIDI chains, MIDI processor presets and drum maps in one window, with search, rename, duplicate, reorder and delete.
  - Select several presets with Ctrl, Shift or Ctrl+A to delete, move or duplicate them together. Deleting asks once for the whole selection; the question can be turned off in the same window and back on in Preferences > General > Confirmations.
  - Containers have their own presets: save a container with its plug-ins and load it into another. Plug-ins that are not approved are left out and listed.
  - Each MIDI processor has its own presets; loading one is a single undo step.
- **FX chain containers**
  - Group plug-ins in a named container that opens and closes, with its own bypass. Plug-ins in a container run in series.
  - Drag plug-ins onto a container, between its plug-ins or back out. New container, Put selected in a new container (Ctrl+G), Dissolve container (Ctrl+Shift+G), rename (F2) and delete with its plug-ins.
  - Containers are saved with the project and with FX chain files. A bypassed container still counts for the track's sound route, automatic GM sound and pitch matching.
  - A container's mix is kept with the chain but is not applied to the sound yet.
  - Deleting a container that holds the only instrument switches the GM sound back on.
- **FX chain list**
  - Select several plug-ins (Ctrl+click, Shift+click, Ctrl+A, or a box dragged from the empty area), then remove, bypass or drag them together.
  - Dragged plug-ins follow the pointer, and the drop spot is marked: into a container, in front of it, behind it or between its plug-ins.
  - A Chains menu lists your saved FX chains and loads one with a click, asking whether to replace the chain or add to its end.
  - Drag the divider beside the list to resize it (double-click resets); the width is remembered.
  - Save FX chain, Load FX chain and the other FX chain window commands can be bound in Settings > Shortcuts, in their own group.
- **FX chain files**
  - "Save as FX chain for other hosts (.RfxChain)…" in the Chains menu writes each plug-in's settings, bypass and mix, with containers. Anything the file cannot hold is listed after saving.
  - Loading an .RfxChain file from another host brings in each plug-in's saved settings (VST2 and VST3) and mix, keeps containers with their name, bypass, mix and nesting, and finds plug-ins among those TabForge knows. It offers to scan when some are missing and shows one summary of what was left out.
  - VST2 plug-ins that keep their settings as plain parameters have them saved with the project and with FX chains.
- **Drum note map**
  - A new "Tabs to SD3" map sends the electric snare note (40), which tabs often use for a normal snare hit, to the centre snare (38). It replaces the earlier kit map, which changed nothing; songs that used that map now use this one.
  - The drum map boxes are drop-downs with a plain summary of the changes (for example "40 Electric Snare → 38"), with Edit map…, New map… and Manage beneath.
  - Drum map editor: one row per drum with a "Send as" number, changed rows highlighted, "Show only changed" and an Advanced list. Built-in maps are read-only until duplicated; your own can be renamed, duplicated and deleted.
  - A drum map can hold its own note-to-note table, so a remap no longer needs matching piece names.
  - The preset "Drums: GM → kit map + humanize" loads the note map with Tabs to SD3, then the humanizer, then velocity at 1.3.
- **Count-in, metronome and playback**
  - Metronome and count-in clicks play on their own hidden destination: no track's sound source, FX, mute, solo or volume applies to them. They still pass through the master.
  - A count-in of two or more bars clicks through every bar; before, only the first bar clicked, leaving a silent bar before the music.
  - Play after a pause counts in again and continues from where it paused. With a loop on, a start inside the loop counts in and starts there. With count-in on, moving to another place while the song plays counts in there.
  - A start in the middle of a bar begins exactly on the beat after the last click.
  - The playback line, the note glow and the timeline playhead move on every screen frame at any playback speed; at slow speeds they used to update less often.
- **Mixer, live input and sound**
  - The options at the top of the mixer wrap onto another line in a narrow window instead of being cut off.
  - A live input row sits under Master, with arm, the live FX chain, pan, volume and mute, the same in every tab and window.
  - Turning live input on brings back its previous FX chain without asking (the setting can still be Ask or Never).
  - A track could play much quieter, or with the wrong sound, after another song had used the same mixer slot; the slot now starts clean when its track is unloaded.
- **Plug-ins**
  - Plug-ins installed under Program Files load without an approval step ("Load installed plug-ins without asking" in Preferences > Audio & plug-ins, on by default). Plug-ins in other places still need a scan or your approval.
- **Menus, windows and focus**
  - Right-click menus, tooltips, drop-down lists, the menu bar menus and submenus open at once, without the slide or fade.
  - Focus stays in TabForge when a tool window, dialog or pop-up closes, instead of passing to another program.
- **Score**
  - Pausing or stopping playback (Space) keeps a selection, so a looped selection stays selected; Esc or a click ends it.
  - Cut and Delete on the same selected beats leave the same tidy bar: no extra cells after a cut in an overfull bar, and a bar left without notes becomes one whole-bar rest. A rest you typed yourself stays.
  - The grey hover outline also shows while a song plays, in the score and in the Band view lanes.
- **View, zoom and typing values**
  - A new score starts at 100% zoom instead of fit width; fit width is still in the zoom presets.
  - A double click on the zoom, speed, tempo or time / key button opens the same fine-tune popup as a right click, so a value can be typed; a single click still opens presets.
  - A double click on the number beside a knob types a value, as a double click on the knob does.

## 0.7.5

- **Live input**
  - The microphone button at the left of the track list toolbar (where "+ Track" was) plays an audio input or a MIDI keyboard through an app-wide live FX chain and the master, with no track and no recording. It is one for the whole program: it stays on across tabs and windows until you click it again, and is never on at start. The chain is kept with the app, not the song.
  - Right-click the button for the FX chain, the input (audio inputs and connected MIDI devices can be ticked together or alone), whether the FX window opens when it is turned on, and the audio device settings. A MIDI device plugged in or removed while live input is on is picked up.
  - When a chain was saved, the first turn-on in a run asks whether to restore it ("Remember my answer"; Preferences > Audio & Plug-ins > Live input: Ask, Always, Never).
  - The status line reads "Live input on: Input 1" or "Input 1 + MIDI". The button is lit with a red outline and glow. New bindable command `Transport.LiveInput`. Adding a track stays in the Track menu, the Add track lane and its hotkeys.
- **Audio inputs**
  - Every input of the audio device can be used for recording and live input, as one channel or as a stereo pair, up to 64. Lists show the driver's names, and the input picker's closed box shows the input's name.
  - "Inputs shown" in the audio settings chooses which of a device's inputs the track input pickers and the live input menu list; the choice is kept for each device.
  - "Use all inputs of the audio device" is on by default; off opens two input channels only, for a device that misbehaves with all its inputs open. "ASIO input: one channel on inputs 1 and 2" replaces "ASIO input: last channel"; an existing one-input set-up keeps working as before, with that input heard on both sides.
  - Safety: a device that cannot be opened with all its inputs falls back to two channels. If the input stops (the device is removed or its driver fails), the status bar says so and playback carries on. An input the device does not have stays chosen, is marked "(not available)" and is silent; the status bar says so when a track is armed or live input is on.
  - A song that uses input 3 or above is saved in a newer format, which older versions refuse to open.
  - Changing the audio device or its settings fades the sound out and back in, so speakers do not pop. A song that is playing pauses for the change and carries on by itself from the same place; a recording in progress is stopped and kept (Preferences: Protect speakers from pops when audio settings change, on by default).
- **Compact mode**
  - New toolbar button beside Keyboard mode, View > Compact mode (Ctrl+Alt+C), with a new icon of two arrows pressing onto one line. It hides the track rows and shows the master track lane at twice its height; pressing it again brings the tracks back as they were.
  - A header strip under the top bar has a capsule for each track: the selected track is lit, and a click on another capsule selects it. The strip has a close button, and the side panel hides while compact mode is on and returns afterwards if it was shown before.
  - The menu row is hidden in every layout, Band view included. The COMPACT MODE caption has a drop-down arrow and opens the menus-and-tools menu, as a right-click on the strip does. With Band view on, the Band header's right-click and drop-down offer the same rows.
  - The track list's controls become a slim column of three rows of buttons: transport, metronome, loop, mixer, snap, tuner, master volume and zoom. The Compact mode tick in its menu follows the setting.
- **Band view**
  - New defaults: the fretboard sits above the tab, only the first row shows its fretboard (the others start with the thin name strip), the first row shows two lines of its music and every other row one, and a row is exactly as tall as what it shows (no empty score under the last line). When two lines do not fit, the first row shows one line like the others; a taller window adds whole lines to every row. The fretboard gives up height before the lines do.
  - The Band layout belongs to the program, not to the song: what you change (fretboards on every row, on none or on the first only; the lines a row was dragged to, for the first row and for the others; rows per screen) is the start for every tab, and a song only keeps which of its tracks are shown and their order. "Reset view" (right-click, and now also in the BAND VIEW drop-down) returns every open tab to the defaults.
  - A roll-down button after the BAND VIEW caption (the whole caption opens it) chooses the layout: fretboard beside the tab (a column at the left, good for wide screens), or fretboard above the tab with both at the full row width (good for portrait screens). It also turns the track colour frame on or off: a rounded frame with a dim glow in each track's colour around its row. Both are kept for the whole program, are in Preferences, and have hotkey commands. Each row's fretboard toggle has a new icon.
  - The header is one row: a plain "BAND VIEW" caption with the track capsules beside it, so the lanes start higher. It has a close button at its right end, a new three-players button icon, and opens and closes only with its button or command.
  - A row whose instrument is hidden has no left column. Its score spans the whole row under a thin strip with the track name and the instrument toggle, which is lit while the instrument shows; its width does not depend on the other rows' instruments.
  - While playing, every row keeps the playing line of music in view, also when rows have different heights, and at once after a row is resized. The grey note length outline under the mouse shows in the rows as it does in the score (not while playing).
  - Dragging the divider between instrument and score follows the pointer at once; the scores are laid out to the new width when the drag ends.
  - The fretboard stays on its chosen side after entering or leaving Band view or Keyboard mode, and after switching layouts.
- **Master track lane**
  - Show master track (the track list's right-click menu, or Settings > Timeline & Tracks) adds an overview lane above the first track: one thin line per track, filled in the bars where that track plays. It is off by default.
  - Its row in the track list has the master effects button, master volume and master pan, the same as the Mixer's Master row.
  - Each line takes its track's colour (can be switched off in Preferences). The master colour and opacity are settings; opacity defaults to 25% and goes up to 50%.
- **Track list and top bar**
  - Arming a track refits the track list and scrolls the armed row with its input strip into view; a pane height you set is kept.
  - The controls column is narrower by default and cannot be dragged narrower than its buttons need. Dragged narrower than one row needs, the toolbar shows two rows (live input and transport on the first; mixer, snap, tuning, master volume and zoom on the second) and returns to one row when widened.
  - The track list's right-click menu has "Add track…" at the top.
  - The icons at the right of the top bar are grouped by kind with a thin line between the groups. An icon can be dragged to another place in the row; the order is remembered, and "Reset all panels to default positions" restores it.
- **View and tempo, Lyrics and Keyboard mode**
  - Zoom, playback speed, tempo and time / key move to a **View and tempo** pane at the bottom of the side panel, as one row of four equal buttons: a click opens presets, a right click opens a slider and a box for fine-tuning, and the wheel steps zoom and speed. It can be moved, tabbed, floated, closed and reopened from View > Panels.
  - **Lyrics** is its own pane beside Sections, so the sections list uses the full height.
  - Keyboard mode has a Track selector at the left of its bar that chooses the track the keyboard follows. Its button has a keyboard icon.
- **FX window and plug-ins**
  - When a plug-in's own screen is larger than the window, the window grows to show it, within the screen and never smaller than you made it. Scroll bars at the right and bottom reach the rest when it still does not fit.
  - Ctrl+S saves the FX chain and Ctrl+O loads one while the FX window has the focus.
  - Saving a plug-in preset lists the saved presets, so one can be replaced (an existing name asks first) or deleted.
  - A plug-in added from the plug-in list stays allowed after the program restarts.
  - The CPU figure is the audio engine's share of the whole processor, with the audio buffer load beside it.
  - A knob change in the app-wide Monitor FX chain no longer marks the open song as changed; a song's own monitor chain still does.
- **Performance, fixes and looks**
  - Exported video: Band rows follow the light or dark look of the export.
  - Playback through a MIDI output uses much less CPU.
  - The note length outline follows the mouse again while a song is paused.
  - Render window: the small drop-downs (tail, sample rate, format, threads) no longer draw their arrow over the text.
  - Message boxes follow the light or dark theme. Light theme transport buttons have see-through pastel fills.
- **Shortcuts**
  - 43 more menu commands can be given keys in Preferences > Shortcuts (no default keys were added): About, Stylesheet, audio setup, bar commands, add bass, drums and keys, notation and zoom views, note lengths, note effects, Exit.
  - New bindable commands for the Band view layout and colour frame, and `Transport.LiveInput`.

## 0.7.2

- A paused song leaves the window idle: the score is no longer laid out again and again while playback is paused.
- A song file dragged onto the window opens in the selected tab; **Open dropped files in a new tab** (Settings > Tabs) opens it in a new tab instead.
- While a song plays, the score keeps the bar being played in view after a zoom change, a window resize or any other change of the page layout.
- In a bar that holds more notes than its time signature, the playback line stays on the note that is sounding instead of running ahead of it.
- The numeric keypad's decimal key toggles the dotted note, the same as the period key.
- Delete over a selection that spans several bars removes every beat of a packed or overfull bar, and the beats left after a delete in such a bar keep their own places one after another.
- Beams on the standard staff are thick bars (half a staff space each), and the third and fourth beams of 32nd and 64th notes run unbroken across the whole beat; stems lengthen with the number of beams.
- The note length outline around the cell under the cursor can be turned off in Score settings and in the score's right-click menu.
- The thin outline around the cell under the mouse is centred on the fret number, follows the cursor when you move with the keyboard, and is as wide as the note is long.
- Notes shorter than a sixteenth (32nds, 64ths) typed one after another follow each other without a gap: they beam together and play back to back, and the fill rests cover the real remainder of the bar; selecting them all to change their length keeps every note, and every note of a bar holding more than fits can be selected.
- Pausing or stopping moves the edit cursor (yellow highlight) to the beat the playhead was on, keeping its string; the score does not scroll.
- Drum staff: sounds written in the same voice and cell share one stem and one beam (a kick and a snare together are a chord, stems up); separate up and down stems are used only when the bar is written in two voices. Every note keeps its own written length, and the percussion clef sits inside the staff, after the opening barline.
- The green glow on the notes being played shows only while the song plays; it goes when you pause or stop, and never appears while editing.
- With "Add rests automatically to fill bars" on, changing the length of the note under the cursor moves the notes after it along (no rest opens between notes); the freed or used time goes to the rests at the end of the bar. A rest you typed between notes stays.
- The edit cursor is a see-through yellow highlight on the TAB lane (also on an empty cell) and a grey box behind the selected notehead on the staff; both are hidden while the song plays.
- The green playback line shows only while the song plays (not while paused, stopped or editing), and the yellow edit cell is thicker with a soft fill so it is easy to find on an empty bar.
- A bar of sixteenth notes with several separate palm-mute groups no longer stretches across the whole line.
- Ctrl+T now opens the Add track window (also from the Track menu and the command palette); New tab moves to Ctrl+Alt+T. Your own key choices are kept.
- Drum staff: drum tracks show the percussion clef, every drum note has a stem (hands up, kick and pedal down), each direction is beamed on its own and anchored to its noteheads, ledger lines show for notes off the staff, and a rest no longer lands on the other voice's heads.
- Rests added automatically to fill a bar follow your notes: when notes grow or are written before them, a leftover automatic rest shrinks or goes instead of making the bar too long (red). The setting is now called "Add rests automatically to fill bars".
- Delete confirmations (bars, empty bars, sections) now highlight the Delete button in blue as the default, so Enter deletes and Esc cancels; Cancel is the neutral button.
- A new file starts with 4 empty bars instead of 32; pressing Right at the end of the song adds more.
- Right at the end of the song adds one bar and moves into it, also when the caret sits past a shorter track; it also works during playback (playback carries on into the new bar), and at the bar limit the status bar says why nothing was added.
- Palm-muted notes now play slightly softer than open notes (about a fifth quieter in velocity), so the mute is audible without changing the song's balance.
- Paste: notes that do not fit their bar stay in it and the bar shows red (over full), with no tie added and the next bar left alone (a copy of whole bars still fills consecutive bars); with beats selected, Paste starts at the selection and asks whether to overwrite the selection only, the whole bar, or insert; Cut on selected empty bars removes them like Delete.
- Playback: moving the cursor back inside the bar playback started in no longer leaves that bar silent; the green playhead line follows the score after an edit while paused; Delete on one selected beat turns it into a rest in place.
- Keyboard mode tooltips show the keys currently bound (Play, Wait for me, Skip, Loop).
- The status bar readout updates with less work on each playback tick.

## 0.7.1

- The status bar's tempo shows the tempo in force at the playhead or the selected bar, so it follows the song's tempo changes during playback and after a seek; the toolbar BPM box still shows the song's base tempo.
- Tempo marks in the score ("♩ = 112") are a third larger and bold, and they move aside for the labels around them.
- Settings > Score page layout and Score scrolling now change the open songs at once, the same as the score's right-click menu (before, they only applied to songs opened afterwards).
- Settings: Reset all settings and Import settings now also cover the audio device, plug-in folder, video and keyboard mode rows (approvals and the quarantine list are never copied), and a second TabForge window follows a change made in the first window's Settings.
- Settings: the Settings window opens with what the menus and transport show right now (note preview, count-in, note value), the fretboard and keyboard options redraw the instrument as soon as they change, and the track list re-fits as soon as Auto-resize or the Add-track lane changes.
- Count-in and note preview toggled from the menu are saved at once.
- Settings: a second open window now follows settings changed in the first; fretboard and keyboard rows (scale style, colours, markers, number size, string spacing, keyboard size, left-handed, note names) redraw at once; the pan/volume knob style and track list fit apply when previewed or imported.

## 0.7.0

- The Record video button (camera icon, Ctrl+Alt+V) moved from the toolbar to the title bar, left of the Settings gear, in the same caption-button style; it stays visible at every window width and shows a red dot with a ring while recording.
- Keyboard mode redesign: a real piano keyboard (key proportions and black-key positions of a piano, more octaves rather than stubby keys), lit keys glow only inside their own outline, longer notes by default (4 s ahead; - / + in the bar, Ctrl + mouse wheel, Ctrl+Alt+Plus / Minus), a control bar in the pane and the pop-out (play, stop, speed, Wait for me, loop, hands, look-ahead, note names, finger numbers, MIDI input with Any available device by default and a connected dot), hit and missed notes marked green with a check or red with a cross, a large accuracy figure and a progress line; while a MIDI keyboard is heard the selected track is silent in playback and your keys play through its sound (its mute is never changed).
- Keyboard mode: a hand engine decides which notes the left and the right hand play, for any track and without settings (span, held notes, reach, register, crossing and melody lines), for the hand colours of the falling notes.
- Moving things stay sharp at every display scale: the score playhead and its duration glow, the timeline playhead and a Band row being dragged are drawn on whole screen pixels instead of soft, half-covered columns.
- Video export frames no longer change with the display scale (the Band rows' scrollbar column has a fixed width in the export).
- Video export of Band layouts gives the same frames on every machine: the Band view's instruments and rows refresh on every frame of the export instead of by the clock.
- Video: experimental encoder options (hardware, fast export, low latency), off by default.
- Keyboard mode (experimental), View > Keyboard mode (experimental) or Ctrl+Alt+L: the selected track, whatever its kind (a guitar, bass or drum track shows at its sounding pitches), as notes falling onto a key strip in time with playback (tempo, speed and loop followed), with hand colours, note names and a toolbar button next to Band view; it switches the layout like Band mode and puts it back when turned off. Look-ahead and size are in Settings > Fretboard.
- Keyboard mode play-along: with a MIDI keyboard connected the keys you hold light (a dot when right, a cross when extra), Perfect / Good / Early / Late / Miss flash near the keys, a panel shows accuracy and streak, and the setting Wait for the right notes (Ctrl+Alt+W, skip with Ctrl+Alt+Q) pauses the song at each chord until its notes are pressed.
- Keyboard mode play-along: a Timing tolerance setting (Strict, Normal, Relaxed; Relaxed by default, so presses up to 150 ms early or late grade as best and up to 300 ms still count); Wait for me now takes the chord keys in any order and over any time, ignores wrong keys, needs a new press for a repeated note and has a Skip button; turning it on or off, or changing speed, hands or loop, no longer changes whether the song is playing, and a hold resumes by itself.
- Keyboard mode: a Pop out button (and View > Keyboard mode: view in its own window, Ctrl+Alt+O) shows the falling notes in their own window with a small control strip; F11 toggles full screen and Esc leaves it.
- Playback of an audio clip now starts at once when the machine is busy: a clip whose first block arrived after the playhead had moved on is joined where the playhead is, instead of being re-requested (and heard late or not at all).
- Narrow windows: the Record video button now steps out of the toolbar before the zoom out / in buttons do, so zoom stays reachable at the 1080 px minimum width with a 125% UI scale (Record video stays on its menu item and Ctrl+Alt+V).
- The command palette now runs the clip commands (copy, cut, split, nudge...) on the selected clip.
- Swallowed errors are now always recorded (cheaply, size-bounded) in errors.log in the diagnostics folder, with the previous file kept as errors.1.log.
- File > Export > Video (MP4): exports the song as a video that plays itself, with the full audio mix. Choose the range (whole song, selection, bars or a section), the layout (one track with its fretboard, keyboard or drums; score only; Band view; or score and Band view), the tracks, dark or light, 1080p or 4K and 30, 60 or 120 frames per second. Also reachable from the Render window.
- Video export: the progress bar runs 0 to 100% over the audio render and the frames, shows the phase and time left, and the score-only layout exports about twice as fast.
- New Record video button (camera icon left of the score zoom, Ctrl+Alt+V): it plays the song and records the chosen part of the screen (score, Band view, whole window, or score with instrument) and everything you hear to an MP4 at 1080p or 4K, 30, 60 or 120 fps. A red REC time shows in the status bar; stopping saves the file in Videos\TabForge and offers to open the folder.
- Sound > Record video (Ctrl+Alt+V) is now on the menu bar, next to Play from beginning.
- Record video: the recorded sound is at the song's nominal playback level on every driver, matching a render of the same song; a safety limiter keeps loud passages from clipping.
- Record video: the recorded audio is clean and carries the full music on every sound card driver, with no hiss.
- Fretboard preview: a short movement line no longer flashes between notes of a repeated chord during the brief gap between repeats.
- Record video: records the whole window by default, scaled whole into the frame; a region taller than wide (a portrait monitor) records a portrait 1080x1920 or 2160x3840 video, and a rotated monitor uses the fast screen capture.
- Band view: a lane whose bar holds no notes now draws its playing line at the same place as the other lanes.
- Band view: the right-click menu is grouped like the score's menus: lanes, lane layout, zoom and follow first, then a Row sizes submenu (rows per screen, instrument size and width, the row resets), then Reset view and Band settings…; the playhead line is set in Settings.

## 0.6.2 — 2026-10-09

- Hovering a track row in the track list shows a subtle hover shade; the selected track keeps its stronger highlight.
- Band view: every row now shows the same bars, turns the page together and draws its tab at the same scale, even when the rows have different widths.

## 0.6.1 — 2026-10-09

- With many tracks, the track list now takes at most about half the height, so the score always shows at least one full line of tab and notation; further tracks scroll.
- A song opened in a short score pane shows its first line of music instead of only the title.
- When the score pane is shorter than one line of music, the view keeps the TAB and cursor in sight instead of the empty space above the notation.
- Lines of music sit slightly closer together in the vertical page view.

## 0.6.0 — 2026-10-08

Band view and note effect editors.

- Score: typing a note during playback no longer moves the playhead to the cursor; a paste inserted into a bar skips empty copied spots, so the cursor always lands on a pasted beat; empty bars no longer cost extra work on every playback frame. Timeline: resizing the pane no longer redraws every track lane on each step.
- Score: pasting beats onto written beats inserts them before the cursor beat inside the same bar without a question (the bar may overfill, nothing moves into the next bar) and puts the cursor on the last pasted beat; Paste special still offers Replace
- Score: Alt+Up / Alt+Down on an empty spot wrap past the top / bottom string like Up / Down; a dot on selected beats shrinks the automatic fill rests so the bar is no longer drawn overfull; deleting a beat takes the length it was given with it (the next note is written like the beat before); Insert bar does nothing while beats are selected.
- Score: a fret typed after the last beat of an overfull triplet bar stays there instead of overwriting the note before it; a triplet that does not fit the rest of the bar is still written in that bar; End on the empty spot after the last beat stays there; Shift+Right from the last beat selects the empty spot after it and then goes on into the next bar, so a cut of that selection takes the bars; Undo after Shorter then "." on an empty spot takes back only the dot; after a triplet is taken off and the edits undone the bar is no longer marked overfull, and each note of an unfinished triplet shows its own "3".
- Score: Cut of beats selected inside one bar keeps the bar, even when every beat of a full bar is selected; a selection that crosses a barline (or a bar selection) cuts its bars and leaves the cursor on its end bar's number; taking the triplet off the middle note of a group moves the later notes up so they keep their timing; a dead note takes no harmonic (a harmonic note made dead keeps it).
- Score: Cut of beats leaves a bar empty when only rests remain and puts the cursor where the cut started, so a later paste gets no leading rests; a paste past the end of an overfull bar goes on in that bar; Undo right after Longer / Shorter on an empty spot restores the writing duration; palm mute on a tied note adds nothing.
- Score: Right in an empty last bar makes the next bar again; Redo puts the cursor back where it was when that step was undone (after a paste: the first pasted beat); Shift+Right stops at the last written beat, so Right after it goes to the beat after the selection; R on a written rest keeps it a rest; triplets fill a bar by their true length before the next bar starts.
- Score: in an overfull bar the cursor box sits on the note instead of the gap before it; Undo / Redo end the selection and keep the cursor on the same beat (the selection's last beat); Redo of a paste puts the cursor on the last pasted bar; Delete on a selection removes its beats (the following beats move left) and Backspace / Delete end the selection.
- Score: "." or the triplet pressed on an empty spot sets the duration of the next typed note again (Undo takes it back); a new beat after Right takes the length of the beat before it; the first note in an inserted bar takes the length of the last note before it (a quarter in a new song); Semitone down on fret 0 leaves the note; Undo / Redo after a long overfull bar no longer fails.
- Score: an overfull bar shows a red bar number and red staff and string lines (no shaded box; a short bar has a red number); a bar holding only the automatic fill rest is drawn empty; a dead note is an upright x head; consecutive vibrato notes share one wavy line; a lone triplet note keeps its "3"; hammer-on and legato-slide arcs on the lower strings curve below the TAB numbers.
- Score: a hammer-on marked on one note slurs to the next note on its string; an incomplete triplet shows its number without bracket pieces; a tied note shows no P.M.; a bar starting with a grace note after a time signature no longer leaves a wide gap.
- Score: Right in an empty last bar stays there instead of adding bars; after a rest written with R, Right and End stay in the bar (the rest counts as written); an overfull bar keeps taking notes after a longer note; Ctrl+End goes to the last written beat of the last bar.
- Score: Right from the last note goes to the empty spot after it and Right again to the next bar, skipping the automatic fill rests; End goes to the last written beat; an overfull bar keeps taking notes after its last beat; Insert bar puts the cursor on beat 1 of the new bar; Alt+Up / Alt+Down move the note to another string (on an empty spot, the cursor), and Move track up / down is now Alt+Shift+Up / Alt+Shift+Down.
- Import: .gp3/.gp4/.gp5 files keep a hammer-on or slide on a note with no next note yet and a tie on a note with nothing before it; let ring stays on the notes that have it instead of the whole chord; every beat of an overfull bar is kept; an empty bar opens empty instead of holding a quarter rest.
- Import: a grace note in an overfull bar is kept, and a tie with no note before it on its string reads fret 0; Tie beat (Ctrl+L) again on a tied beat keeps the tie instead of removing it.
- Score: the tie key on an empty beat, a rest or a note with nothing before it writes a tied note (fret 0 when no note comes before) and Tie beat on an empty beat or rest ties the previous beat's notes; Cut takes the beats out and the later beats move up, and cutting whole bars of a one-track song removes them; accent, staccato, dot and triplet on an empty spot do nothing ("No note here") instead of reaching the next note; duration keys and the dot change a rest just written with R; a grace note on a chord belongs to the note on the cursor string.

- Score: a short song's system starts at the left margin next to its clef; P.M., vibrato, technique labels and let ring above the TAB each take their own row (the gap grows instead of overprinting) and a one-beat let ring has no end tick; a dead note has no accidental; "Harm." sits just above its TAB fret; Y on a note with a harmonic removes it.
- Score: ties are per note: the tie command copies only the cursor string's previous note as a tied note at the current duration, pressing it again removes that tied note, other strings tie on their own and a string with nothing before it is left alone; a fret typed onto a tied note is a new, untied note; a tied note's fret stays hidden in the tab even under the cursor; a tie over a barline is drawn as one arc (split into two halves at a line break).
- Score: arrows without Shift, Home/End, Ctrl+arrows and typing a fret end the selection, so Delete, Backspace, paste, dot and duration keys act on the cursor; the cursor reaches every beat of an overfull bar and a two-digit fret there stays on one beat; Ctrl+Right on the last bar goes on to a new bar; Up on the top string and Down on the bottom string wrap within the beat.
- Score: a note or R on an empty beat or on the fill rests at the end of a bar takes the selected duration, and + / - there change only the selected duration; a fret typed on a rest followed by notes keeps the rest's length; Shorter / Longer on a selection closes the beats up instead of opening rests between notes; after undo or redo the selected duration follows the beat under the cursor; a two-digit fret is one undo step; Delete on a rest keeps the bar's rests in their simplest form.
- Score: typing, pasting, undo and bar insert or delete scroll the cursor's system into view when it ends up off screen, not only arrow keys; a fret typed onto a tied beat sounds and shows its number (the other notes stay tied).
- Score: Delete on an empty string of a chord leaves the chord alone and says "No note on this string"; "." toggles a single dot (double dot has its own button); keyboard moves scroll the cursor's system into view when it is off screen; the no-note message clears once an edit succeeds; the default dynamic (forte) is no longer marked under the first note.
- Score: the tie command on an empty beat or rest after notes writes a real tied beat (the previous beat's notes, tied, at that beat's length), drawn with tie arcs, counted in the bar and sustained in playback; pressing it again unties the note and keeps its fret.
- Score: two typed digits never go past the track's last fret (the second digit replaces the first); a time signature change keeps the cursor inside the shortened bar; a Shift+click or Shift+arrows back onto the starting beat selects that one beat; the timeline no longer fails to draw its selection after a bar delete; the demo song's keys tracks have 36 frets so every note fits.
- Score: switching track, tab, zoom or layout during playback puts the score at the playing bar in one step with no glide, and a track switch while stopped scrolls to the cursor bar after the new layout is ready, so one click is enough.
- Fretboard: a strummed chord (pick stroke, brush, arpeggio or rasgueado) shows one stroke arrow beside the nut over its strings with one combined technique tag, instead of a tag on every note.
- Score: a bar is marked short when voice 1 of a track falls short, in that track's own view; a partly filled voice 2, grace notes and a lone rest in an empty bar no longer mark it.
- Band view: Ctrl + mouse wheel, the right-click Zoom submenu or Preferences zoom the tab or notation in every lane at once; a vertical lane shows whole systems only; a row never grows taller than the pane and nothing paints outside it; changing rows per screen refits every row; a hidden instrument leaves a header strip and a full-width lane; a new "Reset view" menu item restores the view defaults (shown tracks and their order stay).
- Score: a drag inside one beat (down through its notes or a short drag within it) selects that whole beat, so Delete, Cut and Copy act on the full chord; a plain click still only moves the cursor.
- Timeline: when the track list is shorter than its rows, the Add track row stays pinned at the bottom of the pane while the tracks scroll above it.
- Score: Right arrow on the last beat of the last bar (not while playing, not with Shift) adds an empty bar at the end and moves to its first beat.
- Score: a new song shows its title and tuning block at the top from the start.
- Grace notes play as a short grace before (or on) the beat whatever their written value.
- Timeline: dragging the track-list splitter shows the rows at their real height, cropped, instead of squashed.
- Track list: stretched rows top out at 1.5x the row height; the thin dividers between track-list columns are no longer drawn (column resizing is unchanged).
- Timeline: when the track list is collapsed and scrolls, the bar ruler and section strip stay in view.
- An audio track added while the song plays joins playback at once.
- Ctrl+C, Ctrl+X and Ctrl+V on an all-tracks timeline selection copy, cut and paste every track, as the timeline menu does; Insert beat with Fill with rests off never drops a note from the bar's last slot (the bar overflows instead).
- Dotted notes: the dot key toggles from the selected beat's own dots (dotted, double-dotted, plain), and clicking the Dotted or Double-dotted tool on a beat that already has it removes the dot, for a range too.
- Double-dotted, Triplet and tuplet tools read the selected beat's own state, so pressing the lit value again clears it. The Accent, Bend, Harmonic, Trill, Tremolo bar and Grace keys now do what their tool buttons do (accent toggles; the others open their editor).
- Changing a drum track to a pitched instrument (or back) in Track properties or the track-row instrument button converts the whole track: kind, channel, default tuning, and its notes are re-mapped (one undo step; a track with notes asks to confirm first).
- Choosing a sound in the instrument catalogue while the song plays switches the track live; Select keeps it as one undo step, Cancel or Esc restores the original.
- Delete on one note of a chord removes only that note (the beat keeps its other notes and length); Delete on an empty beat removes it and the following beats move left.
- Fretboard position: the fretboard / keyboard pane can sit above the score (default) or below it above the timeline: right-click the pane > Position, Settings > Fretboard & Keyboard, or the bindable command Fretboard position (no default key). A Band view button joins the top-right toolbar icons. Fixes: a new window no longer starts in the Compose layout (fretboard below the score, no Sections pane), and a saved layout naming removed panes keeps the rest.
- Bend and tremolo bar open an editor: a themed dialog with a grid (time across, quarter-tone steps up the side) where points are dragged, added by click and removed by right-click or double-click. Presets: Bend, Bend/Release, Bend/Release/Bend, Pre-Bend, Pre-Bend/Release and Dip, Dive, Release up, Inverted Dip, Return, Release down, plus "Save as preset" for your own. OK applies the curve as one undo step, Clean removes the effect. The curve plays back as drawn and is saved in .tforge and .gp files. The effect tool buttons, the Effects menu and the note menu open it, and Bend editor and Tremolo bar editor are commands you can give a key.
- Trill, grace note and harmonic open editors in the same themed style: the trill editor sets the trill fret and its speed (16th, 32nd, 64th shown as note symbols); the grace note editor sets fret, dead note, position (before or on the beat), duration, dynamic (ppp to fff) and the transition (none, bend, slide, hammer); the harmonic editor picks Natural, Artificial, Tapped, Pinch or Semi, with a fret box for artificial and tapped. Each has built-in presets and "Save as preset", OK / Clean / Cancel and one undo step. The effect tool buttons, the Effects menu and the note menu open them, and Trill editor, Grace note editor and Harmonic editor are commands you can give a key.
- New Band view (View > Band view, command Band view with no default key, and Layouts > Band): one row per track with its full-neck fretboard, keyboard or drum pads on the left, showing the notes sounding now and next, and that track's tab on the right in the score's colours. Three rows fill the panel and further tracks scroll. While playing, the tab follows the playhead like the score (page turns of half a page near the end of the visible tab, instant or glided, as set in the score's Follow settings), and the notes sounding now glow on the tab as they do in the score; clicking a tab moves the cursor and playback there and selects the track. The Band layout shows only the Band view and the arrangement; Window > Panels > Band view switches to it and back as View > Band view does, so the Band view never lands under the timeline of another layout. The panel can be dragged or docked like any other.
- Icons: the settings cogwheel (title bar, track list header and each track's cog) is a new solid gear, and the Bend tool icon is a clean rising curve with an arrowhead.
- Band view: each row's instrument can be hidden on its own (the × on the instrument or the icon beside the track name; kept with the song), a grip between instrument and tab sets the instrument width for all rows or one row (right-click > Instrument width; double-click resets), and the green playhead line spans only the tab by default (Settings > Timeline & Tracks > Band view > Playhead line).
- Delete on a selection of rests removes those beats, and the notes after them move left.
- Editing tools: Accent and Heavy accent toggle off when pressed again; Tie (L) removes a note-level tie at the cursor; Tie note and Tie beat toggle and work on a one-beat selection; Slide and Hammer-pull clear every slide or hammer-pull kind when lit; Vibrato, Ghost and the other note effects no longer add a note on an empty string (the status line says so); Fermata applies to a selection; Backspace and Shift+Up/Down say when nothing could be changed.
- Band view: right-click opens its menu (what the lanes show, instrument size, rows per screen, reset row heights, follow like the score, zoom, Band settings…); the toolbar's Band view button stays lit while the Band view is shown; with fewer tracks shown than rows per screen, the shown rows share the whole height.
- Band view: lanes can be vertical (the default; the song wraps into lines that stack down the lane, so a tall row shows several lines, and the lane turns to the playing line) or horizontal (one line that slides sideways): Lane layout in the right-click menu, Settings > Timeline & Tracks > Band view, or the bindable command Band view: lane layout (no default key). Every lane now shows the same bar at the same place with the same zoom (during playback, after seeks, page turns, and when rows are added, removed or resized), because the lanes share one scroll position, one set of bar widths and one zoom.
- Band view: a seek or jump (stopped, or while playing) puts every lane on the new bar at once, and a stopped lane no longer stops half-way through a glided page turn; a lane showing tab and notation scales to fit its row.
- Band view: track pills across the top choose which rows show (the first three tracks by default, no limit; clicking a pill adds or removes that track's row). Rows per screen is 3 by default and can be 1 to 5, with further rows scrolling; each row's bottom edge can be dragged to give it its own height (double-click the edge to reset). Dragging a row by its name moves it smoothly among the others; this changes the Band view's order only, not the arrangement's. The notes sounding now glow on the tab, and a stopped instrument shows the notes under the cursor. Band view: show / hide the selected track's row, more rows per screen and fewer rows per screen are commands you can give a key (no default keys).
- Mixer: every group row has an arrow (and Left / Right / Enter on the focused row, or its right-click menu) to collapse or expand the group, so a collapsed group shows only its row (FX, pitch, pan, volume, M / S, and the track count). A "Collapse all / Expand all" button sits beside "Group tracks", and Mixer: collapse all groups and Mixer: expand all groups are commands you can give a key (no default keys). The state is saved with the song and shared with the track list's group boxes. Dragging a collapsed group moves all its tracks; dropping a track on one adds it and keeps it collapsed.
- Mixer: each track row has a colour chip (and "Track colour…" in its right-click menu) that opens the track colour palette; the track list, timeline and Band view follow, and undo restores it.
- Mixer: collapsing or expanding a group does not mark the song as changed; the collapsed state is saved with the song.
- Mixer groups: "By instrument" is now Guitars, Basses, Drums and Other instruments, decided by the track's instrument (kind, MIDI channel and GM program), never its name; pianos, pads, strings and vocals no longer land in Guitars. A new "Group rules…" editor (also the command Mixer: group rules) lets you add, rename, reorder and delete any number of groups, give each a colour and several rules (instrument family, GM program or range, track kind, MIDI channel, name contains); the first matching group wins, and a fallback group takes the rest. The editor applies rules to all songs (app settings, also reachable from Settings > Appearance > Track colours) or to this song only; a song with its own rules says so and can "Reset to app rules". Moving a track to another group by hand still overrides the rules for that track; "Return to rule group" in its right-click menu undoes it.
- One visual scale for the score and the fretboard: the fretboard's numbers, string labels and legend never grow much past the score's tab numbers (zooming the score zooms the fretboard with it), so a tall or wide pane no longer blows the fretboard up and the rest of the height goes to the score. New songs open at Fit width, which in the continuous views is 100% on small and medium windows and grows to up to 150% on large ones; a chosen zoom stays as set.
- Fretboard note circles default to 90% (was 100%); existing settings keep their saved size, and Settings > Fretboard & Keyboard > Appearance > Note marker size still sets any size.
- Band view keeps its layout with the song and saves it the next time you save (changing the layout does not mark the song as changed, and undo leaves it alone): which rows are shown, their order, each row's own height and the rows per screen are saved in .tforge files and in the project carried inside .gp files. New settings under Timeline & Tracks > Band view: instrument size in rows (full neck, 12 frets, small keyboard), lane content (Tab, Notation or Both), "Follow like the score" (on by default: the Band view uses the score's Follow settings; off uses the Band view's own smooth or page-by-page follow, and an older saved smooth-follow setting carries over), the rows-per-screen default, and "Keep Band order in sync with the timeline track order" (off by default; on, moving a row also moves the track and the other way round). Dragging a row near the top or bottom edge scrolls the list. An empty Band view says why. Lane content, instrument size, follow like the score / own follow and reset row heights are commands you can give a key (no default keys).
- Tabs whose score is playing show a speaker icon (in the theme's strong text colour) left of the title in place of the old green triangle, with the tooltip "Playing".
- Readability and narrow windows: the tab close cross follows the theme (it was faint in the light theme) and the unsaved dot has a thin outline; long track names in the timeline end in "…" with the full name in the tooltip (the Band view row name too); a dialog's default button (Paste, OK, Save, Show on fretboard, Open download page) takes a label colour that reads on the accent in every theme; a narrow window keeps the zoom out / in buttons by tightening the menu bar, then hiding the "Speed" label, narrowing the zoom and speed boxes, hiding the speed box and the time / key signature readouts, and only then the zoom box.
- Leaving the Band view brings back exactly the panels shown before it (the first-start layout keeps its fretboard on top and its Sections pane); the zoom box keeps "Fit width" readable in narrow windows.
- Readability: the Band view's track pills follow the theme (a shown track is accent-tinted with strong text, a hidden one is outlined with softer text, each with its track colour dot and a tooltip), the toolbar zoom buttons are clearly visible in the light theme, the effect editors' OK label reads on the accent, and fretboard note numbers always contrast with their circle. In the light theme the Structure, Rhythm and Layout panes now follow the theme (they kept a dark background), and fretboard markers are clearer: the sounding note leads, next is a solid gold ring, upcoming and recent are firmer fills, and technique tags (P.M. and so on) read in the theme's text colour. Hover and pressed shades on buttons and menus are stronger in both themes, and a selected panel tab shows its name in strong text.
- Lines between tracks are hidden by default, so the timeline and track list show only the rows' own shades; Settings > Timeline & Tracks > Show lines between tracks, the timeline's right-click menu and the bindable command Toggle lines between tracks (no default key) show them again.
- Track list: each track row shows an instrument icon between the track properties cog and the record button; clicking it opens the instrument catalogue on the instrument's family (search ready) and the change is one undo step. Audio tracks show a waveform icon. The INSTRUMENT dropdown column is gone, which leaves more room for track names. Rows are 34 px tall by default (was 30).

## 0.5.7 — 2026-10-08

Fixes and polish: mix-table changes survive jumps, playback follow, clip edges and looping, a collapsible timeline, keyboard sizing and a tidier side pane.

- Dragging audio or MIDI files onto the timeline shows the drop ghost at once with an estimated length; the files are read in the background and the ghost updates when their lengths are known.
- A clip past the end of the song always extends the song: opening a file whose clips run past the last bar adds the missing bars (with a notice), and deleting bars from under a clip keeps the song covering it; undo restores the bars.
- A clip's end can always be dragged back out: cropping an audio or MIDI clip never limits how far it can be extended again, and the song grows to hold it while you drag. Dragging an end past the source repeats it inside the clip (a dashed line marks each pass), and a clip that runs past the song end always extends the song.
- Trimming a clip's left edge keeps the audio where it is. Clip edges snap to beats when snapping is on (on by default) and are free when it is off; edges also snap to the drawn end of other clips in songs with repeats.
- Splitting a looping clip keeps the second part inside the source.
- Settings > Editing > Clips: Remove empty bars at the end when clips shrink (on by default). Deleting, cropping or moving clips earlier drops the empty bars at the end of the song, down to the last bar with notation, a section, a mix-table point, a repeat or a clip; undo restores the clip and the bars together.
- The Practice / Mixer side tab is removed: Chord finder… and Song stats… open from the Tools menu, and a track's MIDI output device is set in Sound > MIDI / Audio setup.
- The section hover tip names each drag by its key: a plain drag moves the section with its bars, and Ctrl+drag moves only its marker.
- The timeline pane can be dragged down to about three track rows; its track list and lanes then scroll together with the mouse wheel, and the chosen height is saved.
- The keyboard pane is sized from its key width (keys at most 24 px wide, 60-120 px tall, centred), and keeps its own pane height separate from the fretboard's.
- The score zoom and playback speed boxes move from the Zoom & speed side pane to the top toolbar, left of the tempo box, and collapse on narrow windows.
- Mix-table changes (instrument, volume, pan) stay in effect after jumping or seeking to a later bar.
- Playback follow resumes when the playhead loops, seeks or the playing song is shown again, and stays on through a relayout; the timeline keeps the playhead on screen near the song end.
- The bundled demo song's embedded TabForge project is re-saved to match its score (triplet feel, mixer levels and the second voice now open as written); the GP file checks count second-voice notes.

## 0.5.6 — 2026-10-06

- Add at cursor shows the section’s exact starting bar. The redundant Go button is removed; clicking a section or pressing Enter keeps the existing cursor navigation.
- Section editing opens the displayed colour; colour changes persist across similar-named sections when matching is enabled, including after saving and reopening the project.
- Section and text-entry dialogs keep their action buttons fully visible at larger interface scales.
- Audio and MIDI drag previews identify the target lane without covering adjacent track rows.
- Clip placement and drag handles remain aligned through repeats and timeline growth.

- Automated checks cover file handover between running instances, cursor beat slots, paste undo/redo, and window cleanup after failed interaction tests.
- Edit refreshes coalesce engine synchronization, and clip moves reuse measured song timing while checking for timing changes.
- Local score edits reuse unchanged bar layouts when spacing and cross-bar notation allow it.
- Delete-bars prompts reuse their owner’s window, refresh choices and shortcuts, and release hidden windows when the owner closes.
- Compatible playback seeks reuse the running schedule, preserve pause state, and discard superseded targets.
- Audio-only clip growth extends the future playback schedule without restarting MIDI or resuming a paused song.

## 0.5.5 — 2026-10-04

Track conversion both ways and a round of speed work guided by a new timing audit of about 50 actions.

### Architecture and quality
- A speed-audit tool times about 50 user actions (stopped and while playing) and reports anything over 50 ms; it guided the fixes below.
- Essential-action checks (start, opening files, tabs, save, autosave, editing, tracks, timeline, playback, windows, exports) run on every build and in the release check.
- Copy, cut and paste are checked for every selection scope on every build of a release.
- Developer docs: one test recipe everywhere, a "how to change me" card in each source folder, a `--find` lookup, and checks that every command is documented.

### Changes
- A song double-clicked in Explorer opens reliably: closing the last window now really quits TabForge (it could stay running in the background without a window and swallow the next double-click), and while TabForge runs the song opens in an open window.
- With the loop on, a selection keeps its scope: the loop is drawn over the selected rows and its span is marked on the ruler.
- Bars copied from every track still paste track for track after an Undo (they no longer shift onto the selected track).
- Selections have a scope: bars selected in the score cover that track (the timeline highlights only its row), bars selected on the timeline cover every track. The timeline's Copy, Cut and Delete follow it; Paste puts one track's bars onto the selected track and every track's bars into every track.
- The timeline's bar cells always show the bars' current notes, also after cutting and pasting a track back, reordering or duplicating tracks.
- Moving a MIDI clip on the timeline follows the clip, not the pointer: grabbing it near its top edge no longer drops it into the notation row (or a group header) where it vanished; audio tracks never take a MIDI clip into notation.
- Convert to instrument track opens the instrument picker directly with its search box ready (no Add-track window); a track with clips then asks what to do with them (MIDI clips: write as notation or keep; audio clips stay on a second lane), with a "Remember my choice" box and a setting under Settings > Editing. The track keeps its place, colour, name, mixer and plug-ins.

- Clip and bar edits (clip move, clear or remove bars, area move) no longer rebuild every track row when the tracks are unchanged, so the timeline refreshes faster.
- Preferences opens about 3x faster (audio device lists load when their rows are shown) and the delete-track and delete-bars prompts open faster.
- Entering a note with a long audio clip on the timeline no longer redraws the clip waveform (about half the time per note), and the first right-click menus open without a cold-start pause.
- Adding an empty audio track during playback no longer restarts playback (no brief dropout).
- Track row menu: every instrument track now offers "Convert to audio track…" (asks first; its notation becomes a MIDI clip on the track, the bars are emptied, existing clips stay, one undo step). Converting an audio track back to an instrument asks whether to write its MIDI clips into the notation. Bindable command "Convert track to audio track" (no default key).

## 0.5.4 — 2026-10-04

Reliability release after an external review: safer recovery, playback that never jumps or repeats notes, and a stronger release check.

### Architecture and quality
- Updated build of 0.5.4 (same version): the release workflow now prints the release test set's own summary and fails if fewer than 1,500 checks or any core test group did not run; unused documentation images are no longer published.
- Release check: every release build must now pass a release test set (the basic checks plus the core saving, recovery, import, playback, document and security tests, about 2,000 checks in a little over a minute); the full suite still runs weekly.
- Two interaction checks that were only reported as known issues are fixed and now fail the build if they ever come back.

### Fixes
- Keyboard view: the piano keys stop growing on a large pane (white keys at most 24 px wide, 120 px tall) and the keyboard stays centred.
- Playback: typing a note during playback with "advance after entry" on no longer moves the playhead to the edit cursor; the edit is heard when playback reaches it.
- Playback: a mixer, routing or plug-in change during playback resumes exactly where it was, without replaying the notes of the current beat.
- Recovery: emergency copies written after an unexpected error get a unique name, so two unsaved songs with the same file name from different folders no longer replace each other; both are offered for recovery at the next start.
- Mixer: a Mute or Solo click on a strip no longer throws a visual-tree error when the strip refreshes afterwards.

## 0.5.3 — 2026-10-04
Clip editing, a Delete prompt for bars on the timeline, cleaner Delete in the score, and a lighter, smoother timeline with long songs and long clips. The code behind the main window, the arrangement panel and the timeline was split into smaller owned parts.

### New features
- Clips: S splits the selected clip at the edit cursor (the spot last clicked on the lane) or under the playhead; both parts keep their file position, level, pitch and speed. Ctrl+Shift+G glues the clip with the clips that continue it on its lane. Fade-in and fade-out handles sit at the clip's top corners (drag to set the length; also played in renders). The clip menu has Split at cursor, Glue and Reset fade in / out.
- Bars selected on the timeline: Delete, or "Delete…" in the range menu, opens one themed prompt: clear the bars (leave a gap), remove them (close the gap), or insert a gap the size of the selection before or after it, for all tracks (the default) or this track; each option shows its key and the prompt notes any clips affected. Deleting a single bar (menu "Delete bar…" or Delete with no range) opens the same prompt. A drag across bars gives the timeline the keyboard, so Delete acts on the bars of every track instead of the score's track. Ctrl+Delete removes and closes the gap and Ctrl+Shift+Space inserts a gap before (all tracks); "Insert a gap after" is bindable. Clips follow (moved, cut at an edge, removed inside), one undo step each, and "Remember my answer" (action and scope) / Settings > Editing > Safety > "Ask what Delete does on bars" choose whether Delete asks.
- Delete on selected whole bars that hold only rests asks "Delete N empty bars?" (Cancel is the default) and removes them in one undo step; the timeline selection menu has "Delete empty bars", which removes only the empty bars of the range.
- Deleting bars (bar, section, selection) under audio or MIDI clips shows a warning first with how many clips on which tracks overlap, and Cancel leaves everything as it was.
- Settings > Fretboard & Keyboard > Appearance > "Note marker size" (60% to 160%, default 100%) scales the fretboard note circles and their numbers together; large sizes stop at the gap between strings.
- An armed audio track with MIDI input and monitoring on plays the MIDI keyboard live through its instrument plug-in; without one it stays silent.

### Editing fixes
- Delete in the score leaves merged rests by default (Settings > Editing > "When deleting notes, leave"), and a bar left without notes always becomes one whole-bar rest, including bars with tuplets or off-grid beats; Delete on rests collapses them into the fewest rests.
- A full bar offers only its beats as cursor positions: no extra narrow slot after the last beat for clicks, arrows, Shift-extend and drag selection, including the last bar of the song.
- Bars cleared in place show as empty on the timeline at once instead of keeping their old note summary.
- The acoustic drum map is listed as "Acoustic kit map (unverified)"; chains saved with its earlier name still use it.

### Architecture and performance
- Zooming the timeline stays smooth with a long audio clip that runs off-screen: the drawn waveform is scaled while zooming and redrawn once the zoom settles.
- The timeline draws only the bars within one viewport of the visible span, so a long song zoomed in no longer sends every bar to the render thread on each zoom step.
- The timeline is cached as a GPU texture only when that helps (static and under 8,000 device pixels, never during zoom or lane animation); mix points are recomputed only when the song changes.
- Long clips (wider than the view) clip their contents with a plain rectangle instead of a huge rounded mask, and the waveform is drawn as one filled outline instead of one stroked line per column, so zooming and dragging with long clips stay responsive.
- Pressing and dragging a clip responds at once, also during playback.
- MainWindow, ArrangementPanel and TrackTimeline each hand their jobs to owned controllers behind host interfaces, with no behaviour change: MainWindow 8,523 to about 7,100 lines and 236 to 197 fields; ArrangementPanel 3,849 to about 2,850 lines and 89 to 61 fields; TrackTimeline about 3,000 to about 2,600 lines and 126 to about 104 fields.
- The automated check set is split: a basic set runs on every build and push, the full suite runs weekly (and on demand) in CI and is archived; several engine-sync, add-lane and render checks were made more robust, and the public CI workflow was fixed.

## 0.5.2 — 2026-10-03
A larger update to 0.5.1: audio tracks and the Add-track lane, a track right-click menu, two shortcuts per command, live editing during playback, and a reorganised, faster core.
- Playback, mixer and view options are passed in explicitly instead of read from shared global state; each open song owns its audio-engine transport, so open songs no longer interrupt each other.
- Zooming and dragging with a long audio clip zoomed in is fast: only the visible part of the waveform is drawn (and kept between redraws) instead of the whole clip.
- Ctrl+Left/Right, Ctrl+Up/Down (bar and line jumps), Home, Ctrl+Home/End and moving past a bar end now always put the cursor on a real beat (or the append slot) of the target bar instead of an empty grid cell.
- Right-click on any track row (instrument or audio) opens a menu: Cut, Copy, Paste (after this track), Duplicate, Delete…, Rename, Colour, Properties… (audio tracks also Convert to instrument track…), each with its live key. Whole tracks (notation, clips, mixer settings, FX chain, colour, name) copy, cut, paste and duplicate; while a track row has the focus Ctrl+C, Ctrl+X, Ctrl+V, Ctrl+D and Delete act on the track (new bindable "Track" commands; the score and timeline keep their keys). Deleting a track asks first in a themed window whose default is Cancel; undo restores it.
- Pressing a clip during playback starts the drag at once (the seek and track switch wait for a click instead of running on the press), and a drag always ends when the button is released or the pointer capture is lost, so a clip no longer keeps following the mouse.
- File types are described by their extensions (.gp, .gp3–.gp5, .gpx) throughout the app and documents.
- An audio track selected during playback no longer shows the score playhead line over its empty score area; selecting a track (clicking a clip, a row or the timeline) responds at once, and moving the mouse between the score and the timeline while a song plays no longer redraws the score.
- Audio tracks now pause and stop with the transport (including when switching tabs pauses or stops another song).
- New tracks start in their instrument's colour: guitar dark red, bass dark yellow, drums dark blue, audio light blue (a song's own track colours are unchanged).
- Typing a note keeps the caret on it by default; "Advance after entering a note" (Settings > Editing) moves it on to the next beat.
- Moving the mouse over the timeline during playback no longer redraws it (section hover is drawn in the overlay layer); dragging the pane splitter re-fits the Add-track lane once after the drag settles; an audio track row is its clip lanes only (no extra notation row), in the timeline and the track list.
- Dragging the splitter between the score and the track list during playback no longer lays the score out on every step (the score keeps its picture; layout reacts to width, zoom and mode, not height), so playback visuals stay smooth.
- Clicking Mute or Solo while a song plays no longer causes a brief glitch or stall: the toggle changes the sound at once without recompiling the playing song, and the track list is no longer rebuilt (the Mixer window toggles the same way). The song is still marked as changed.
- Typing a fret on a rest or empty slot now writes the remembered writing duration (the last duration picked or written), not the rest's length: the rest is split; on a note, the duration tools show its length and typing keeps it. Selections now always cover whole beats: a range that starts or ends inside a beat (for example inside a whole-bar rest) takes the whole beat.
- Changing the duration (+ / -, duration buttons) with only rests selected now refills the whole selected time span with rests of the new value (a bar of eighth rests set to sixteenth becomes 16 sixteenth rests); a remainder that does not divide evenly is completed with the fewest rests, and the selection covers the result.
- Clicking a note to jump the playhead while a song plays no longer sometimes drops that note: a jump could lose the first message sent after it, or start a hair after the note. The note at the playhead (including the first note of bar 1) now lights up as it sounds, and a click on the clef or time-signature area selects the first bar instead of the last bar on that line.
- Opening a .tforge file directly now finishes an interrupted save of its .gp pair the same way opening the .gp does, with the same notice.
- The button changes state on the click. The Mute button is now a grey "M" box like "S" and turns red when muted; a muted track is drawn grey in its track-list row and in its timeline lane, with the strength set in Preferences > Appearance > "Muted track dimming".
- The fretboard panel opens at a medium size on a fresh profile (string spacing about 29 px at a wide window), and can no longer be dragged taller than the board's largest stretch, so no empty space appears above or below it.
- Pick-stroke marks sit a little further below the tab numbers (the fingering marks and lyrics under them move down with them), and beat text above the staff is slightly larger and easier to read.
- Resizing the arrangement timeline or the window while a song plays no longer redraws the timeline on every step, so the playhead stays smooth; the track rows now end flush with the bottom of the pane at every size (no empty strip under the last track).
- Two open songs that both have audio clips no longer interrupt each other: each song's clips follow its own playback position, and stopping one song leaves the other playing.
- The editing cursor now sits only on a note or rest, or on the one free slot right after the last note of an unfinished bar: clicking inside a long note or in a gap snaps to the nearest such position, an empty bar keeps its first position, and the arrow keys follow the same positions.
- The editing cursor box is always drawn around the beat it is on (the same spot as its fret number), at every zoom and while playing or after a seek, instead of sometimes sitting in the gap after it.
- Typing a note on the keyboard or numpad now sounds it once for the length you have set (a quarter at 120 bpm is half a second), every time, also when you type the same pitch again quickly; a note you typed earlier can no longer cut the next one short.
- The "Add track" lane now fills all the empty space below the last track, in the track list and the timeline (at least one row tall); it is one continuous zone with one centred "Add track" label, a soft glow over the whole zone on hover and an accent outline with "Drop to add an audio track" while files are dragged over it; a click or a file dropped anywhere in it acts as the lane, and the setting still turns it off.
- A bar that holds less music than its time signature (half-empty) is now marked in red like an overfull bar; a completely empty bar and a pickup bar are not.
- Settings > General > Updates has a "Check now" button that checks for a newer release even when the automatic check is off; the result window opens in front of Settings.
- The Settings window shows the TabForge version next to "Manage settings", the same text as About.
- Add track now has Ctrl+Alt+T (Hotkey 1) next to Ctrl+Shift+Insert (Hotkey 2), and every menu, tooltip, status line and the shortcuts list shows the keys you have set (Hotkey 1, and Hotkey 2 when set) instead of a fixed text; the track list's + Track button tooltip reads "Add track" with its key and its right-click menu no longer repeats Mixer.
- Every command can now have two keyboard shortcuts, Hotkey 1 and Hotkey 2, set in Settings > Shortcuts; clearing, resetting and conflict checks work per key, and shortcuts from older settings files load as Hotkey 1.
- New setting "Fill incomplete bars with rests" (Preferences, Editing, on by default): a bar you edit always adds up, empty space becomes rests, Delete turns a beat into a rest of the same length, a typed note takes its place inside a rest, and Insert beat takes the room from the rests and never drops a note (a bar that no longer fits shows red). Off keeps bars as they are. Opening a song changes nothing.
- In the score, with beats selected, Delete, Delete beats, tenuto and pitch up/down now act on the whole selection (Insert beat adds one beat at its start) (across bar lines, one undo step); with nothing selected they act on the cursor beat as before.
- An edit made while a song plays is now heard from the next bar, without stopping or jumping: the bar that is playing finishes as it was, notes already sounding are not cut or repeated, a burst of edits (typing) is applied once, and while looping an edit to a loop bar the playhead already passed is heard when the loop comes round.
- A skipped area ("Skip area during playback") now moves with its bars when you move, insert or delete a section, and undo and redo put it back; it used to stay on whatever bars were there afterwards.
- Moving, duplicating, copying or pasting a section now takes along the audio and MIDI clips that lie completely inside it, keeping the same bar and beat (also when the tempo differs at the new place); clips that cross the section's edge stay where they are. The song grows to cover a moved clip, and the whole change is one undo step.
- A recording now always lands in the song whose track was armed, even if you switch to another tab while it runs: the audio or MIDI take is added there as one undo step, and ending the recording from another tab stops the recorded song's playback (it used to stop the song you were looking at and leave the take without a clip).
- Undo and redo no longer reload every plug-in: the audio engine now knows a track by its identity, so plug-ins (and their open windows and unsaved knob settings) stay as they are unless the undo actually changes that track's plug-ins. A plug-in edit now marks only the song it belongs to as changed, not every open song, and plug-in settings are saved into the right plug-in even if the chain changed while they were being read.
- Switching tabs now keeps each tab's own selected track (the track list used to show the previous tab's selection), and a tempo or lyrics you typed but did not confirm is applied to the song you typed it in, as one undo step, instead of being lost or left behind in the box.
- The plug-in trust check now also covers plug-ins on switched-off buses, buses without tracks, a switched-off master and the monitor chain, so none of them can load without approval when switched back on.
- The plug-in approval prompt, the "own process" setting and SECURITY.md now say plainly that a plug-in runs with your Windows permissions and can read and write your files like any program, that running it in its own process (off by default) protects TabForge from a crash and not your files, and to use only plug-ins from sources you trust.
- A muted track now stays muted when you click the timeline, loop, jump or restart playback (an audio track used to start playing again after a seek), and Render to file honours mute and solo exactly like playback. One rule everywhere: with any solo active only soloed tracks play, a track that is both muted and soloed plays, otherwise muted tracks are silent. The arrangement timeline dims exactly the tracks that are not heard (so a muted, soloed track is no longer dimmed, and tracks silenced by another track's solo are).
- The song now always grows to cover what you put on its timeline: when an audio or MIDI file you drop, import, move, copy, paste, nudge or record ends after the last bar, whole empty bars (the last bar's time signature, the tempo in effect at the end) are added to every track so nothing is cut off. The added bars and the clip are one undo step. The song does not shrink when a clip is later deleted or shortened, and at the song length limit (20,000 bars) it adds up to the limit and tells you in the status bar.
- Small and faint text is larger and easier to read: fretboard numbers, track-list headers, status bar, timeline bar numbers and dock tabs.
- The compatible .gp question is now complete: exporting a compatible .gp also asks when the song has plug-ins, FX chains, clips or mixer groups (it cannot hold them; Save as .gp keeps them in the file beside it or embedded, so it still asks only about real losses), and a volume, pan or sound change or a whammy curve on a rest is listed too (it was missed before). The new page docs/reference/COMPATIBILITY.md says exactly what a compatible .gp keeps, changes and cannot hold.
- Saving a song with plug-ins or FX as a .gp and choosing the TabForge file now asks before replacing a .tforge that already exists beside it (Replace, Keep both under a numbered name, or Cancel, which writes nothing). A full copy kept beside a compatible .gp is written together with it, so an interruption leaves either both new files or the old state. While a save or export is asking its questions, the window takes no keyboard or mouse input and no import can replace the tab being saved, and a key typed in another tab during a save is no longer accepted.
- Audio tracks: a track that holds audio and MIDI clips and has no notation. Add one with the new Add-track lane (a strip labelled "Add track" under the last track, across the track list and the timeline: click it for "Audio track or Instrument", or drop audio or MIDI files on it), with the + Track button's right-click menu (Add track... / Audio track), or by dropping files below the last track. An audio track row shows a waveform and "Audio" instead of the instrument picker; selecting it shows "Audio track - no notation" in the score and the fretboard shows its empty state, and note entry, paste, transpose and note preview do nothing on it. Its Track properties window has name, colour, notes, recording input and mix only. Right-click an audio track > Convert to instrument track asks for an instrument and keeps every clip (moved to a second lane below the new tab lane), as one undo step. Preferences > Timeline & Tracks > "Show the Add-track lane" (on by default; also bindable as "Show or hide the Add-track lane") hides the strip; the + Track button always stays.
- An audio track plays silently unless an instrument plug-in plays it: it has no channel, notes or built-in synth, and it uses the Audio mixer family. Exports skip audio tracks; an export of a song with only audio tracks to .gp, MusicXML or PDF is refused with a message, and MIDI export writes the song's meta data only.
- Dragging a section in the arrangement now moves it together with its bars (the others reorder, with a preview); Ctrl+drag moves only the marker into free bars. Cutting a section takes the clips inside it in the same undo step.
- Dragging the splitter between the score and the track list shows a light shade with each track's name and one cell per visible bar (filled where the bar has notes) instead of a stretched picture, then settles once on release.
- The instrument button in a track row keeps its button height when the track rows are tall; the audio track's instrument panel shows a note instead of an instrument.
- The timeline right-click menu opens before the track switch and caret placement, which follow once it is on screen; the Settings window builds its page once on open, and the Shortcuts page appears at once and fills in its remaining groups as you watch, so both open faster.

## 0.5.1 — 2026-10-02

Window and document lifetime, per-song linked-audio context, very long songs and clean .gp fidelity.

- Render to file has two more bounds: "Custom bars" (from bar N to bar M) and "Custom sections" (from the start of one section to the end of another, listed as "Verse 1 (bar 9)"); both use the same bar-to-time mapping as "Selected bars", and Render is disabled with a short hint when the range is not valid.
- Dragging the only tab of any window (the first one included) now moves that window: no extra window is created and no empty "Untitled" tab is left behind; drop it on another window's tab bar and the tab joins it and the empty window closes. Dragging a tab out of a window with several tabs still tears it off.
- A clean .gp now keeps the Legato (slur) and Rasgueado (with its pattern) marks and reads them back, so the save question no longer lists them; a song's legato slurs from a .gp file are kept as the Legato mark.
- The track list always fits its rows: no empty band under the last track after resizing or maximising the window, restoring a layout, changing the UI scale or collapsing groups. Dragging the track list's splitter now stretches the track rows (up to 3x, kept between sessions) instead of leaving empty space; double-click the splitter, or use the Reset track row height command, to go back to the default height.
- New, off by default: View > Highlight playing bar (unbound command) shades the whole bar that is playing, across the staff and tab and behind the notes. Colour, opacity (5-60%) and "Also show the cursor's bar when stopped" are in Preferences > Playback > Appearance.
- A bend that rises quickly and then holds (or releases and then holds) is saved to a clean .gp as drawn: it reached its target halfway through the note after reopening instead of at the point you drew, and a flat stretch inside a bend is kept. Only a curve with more turns than a .gp file holds is reduced, and the save question lists only that.
- Saving or exporting a clean .gp now tells you first when the song uses something that file cannot hold (for example a long bend curve, a mix change inside a bar or a reverb send), listing what and where. You can keep a full TabForge copy (.tforge) beside it, export the compatible file only, or cancel; nothing is written on Cancel; saving the compatible file marks the song saved, while an export leaves the song exactly as it was. New unbound command File > Export compatible .gp file.
- Audible change: track volume and pan in .gp and .gpx files are now read at their exact values instead of in 16 coarse steps, so such tracks can play slightly louder or move slightly in the stereo field (the default volume now opens as 115 instead of 111). A clean .gp keeps every volume and pan value exactly; .gp3/.gp4/.gp5 files are unchanged.
- A trill's speed now survives a clean .gp: a 1/32 trill used to come back as a 1/16 trill after saving and reopening; 1/8, 1/16, 1/32 and 1/64 are kept, and the export warning no longer lists the speed.
- Fixed: a tab that is not the displayed one now updates its unsaved dot and its name as soon as that song is saved, edited, renamed or undone (before, it stayed stale until the tab bar was rebuilt).
- Fixed: undoing or redoing a retune now restores the tuning shown for the song (the tuning label and the shift) together with the strings.
- Fixed: "Write into the track's notation" from a MIDI clip whose notes fit nothing no longer pads the bar with empty cells that could not be undone; the song is left exactly as it was.
- Closing a window with several songs open now asks about every song with unsaved changes, not only the one shown; Cancel at any question keeps the window and every song as they were.
- Linked audio on a network or removable drive is allowed per song, also for songs that were never saved: allowing a folder for one unsaved song no longer allows it for every other unsaved song, and approvals given before this version to unsaved songs are no longer used (they are listed under Linked audio so they can be removed). The approvals of an unsaved song move to the file it is first saved to; Save As to another file asks again.
- A .gp song with linked audio stored by a relative path now finds it beside the song file it was opened from; each open song and each window reads its own song's audio, whichever tab is shown or played.
- Open in the current tab (Ctrl+O) now replaces the tab it was started from, even if you switched tabs while a large song file was still importing; if that tab moved to another window meanwhile, the song opens as a new tab instead.
- Saving one song while another tab is shown no longer switches tabs behind the scenes; a close that saves several songs saves each one as itself.
- The Settings window no longer drops a linked-audio folder approval that was given (for example from another window) while Settings was open.
- A linked audio file whose link leads to a network share that does not answer no longer stalls the window while the timeline redraws; closing a song frees its waveform memory; the "plug-in loading slowly" question appears once, not once per window.
- Adding, deleting or moving a track now refreshes the playback timing right away, like every other edit.
- A clean .gp export keeps the pick slides: a pick slide up or down is now written as the format's own pick-slide mark and reads back as one. Before, the mark was left out.
- A clean .gp export keeps a ghost note that sits in an accented beat: a .gp note keeps its ghost mark only when it has no accent, so the accent, heavy accent, tenuto and staccato are now written on the other notes of the chord only. Before, re-saving the file in another program could drop the ghost mark. A beat made only of ghost notes cannot carry the mark; the save-compatible-file question lists it.
- New diagnostics for comparing a song with the same song saved by another program: `TabForge.exe --gp-compare <a.gp> <b.gp> [report]` lists every difference in the notation, technique and mixer facts the round-trip tests use, `--gp-open <file.gp>` shows what opening a file reports (notice, mixer, first notes), and `--write-gp-probes <dir>` writes one-question files for other programs.
- Very long songs open: .gp3/.gp4/.gp5 files of about 1,600 to 2,000 bars with many tracks failed with "The TabForge project exceeds the 128 MiB size limit" and now open in seconds; such songs also save as .tforge and as .gp with the TabForge project inside. Project files are smaller (empty beats are no longer written out in full) and still open in TabForge 0.5.0. When a song really is too large, the message now names the song's tracks and bars instead of blaming the file.
- A closed window and its songs are now freed even while an accessibility or automation tool (for example a screen reader) is attached; before, each closed window stayed in memory.
- At a large UI scale the Sections pane no longer cuts off its Add / Go / Edit / Remove buttons: the pane scrolls when it is shorter than its content.
- Notices about a .gp file whose TabForge settings were not applied now describe the file itself.
- A score file with damaged bytes inside (the older formats have no checksum, so such a file used to open silently) still opens unchanged, with one short notice when it holds far more impossible notes, beats or unreadable text than a real song: a few odd values never trigger it.

## 0.5.0 — 2026-10-01

The first release without a beta tag.

This release went through full behavioural stress testing in the real application on real hardware, not only headless tests: scripted mouse and keyboard driving of the actual app, recorded on video and audio, including recording under disk overload, low-latency ASIO and WASAPI playback, a 30-minute long session, multi-tab playback, and a sweep over a large collection of real songs (about 1,500 files) and the installed plug-ins. Everything below is the final behaviour of that run.

### New in this release

- Timeline: audio and MIDI clips can be dragged to another lane or another track, or below the last track to make a new one. A ghost shows where the clip will land (it snaps like a dropped file; hold Alt for free placement), a missing lane is created, Ctrl+drag copies, Esc cancels, and the whole move is one undo step. Lanes left empty are removed and the lanes below close up (Preferences > Timeline & Tracks > Remove empty clip lanes automatically, on by default; a track armed for recording keeps its lanes).
- Tutorial: a Basic guide / Detailed guide switch at the top of the window (each with its own contents, search and reader; the last one is remembered), Export PDF saves the guide shown as "TabForge Basic Guide.pdf" or "TabForge Detailed Guide.pdf", and a new unbound command Help > Detailed guide… opens the Detailed Guide directly.
- Drums: every pad of the drum map (snare, hi-hat, crash, ride, toms and the rest, notes 27-87) now writes its note, and typing a drum number above 36 on a drum line (for example 38) writes that number instead of 36.
- Drums: clicking a pad writes on the TAB line given by the track's drum notation preset or custom map, not a fixed line.
- Tracks: a track added from the Track menu now has as many bars as the song instead of at least 32.
- Tab view: strum (brush) and arpeggio arrows now point the standard way: a downstroke (bass string first) points up to the top string, an upstroke points down. Playback was already right.

- Tab view: a grace note's slide or hammer-on / pull-off is now drawn as a small arc between the grace fret and its main note (a bent grace note as a rising arrow) instead of a slide stroke after the main note.

- Score layout: the gap between the notation staff and the tab staff is about 12 px smaller, so a system is shorter and the tab sits nearer the notes above it.

- Tab view: a tied-to note no longer prints its fret number again (the tie already shows in the notation); the beat under the cursor still shows it so it can be edited. In tab-only view (no notation to show the tie) the number stays.

- Notation: each system now starts with the track's real clef (treble, bass, alto, tenor, percussion, with a small 8 under an 8vb treble clef) instead of always a treble clef, and a bar that changes clef shows a smaller clef at its start.
- Timeline: drag audio files (.wav, .mp3, .flac, .ogg, .aif/.aiff, .m4a, .wma) or MIDI files (.mid, .midi) from Windows or out of a plug-in's editor onto a track. While you drag, a translucent block in the track's colour shows where the files will land: it starts at the snapped position (the snap settings; hold Alt to place freely), is exactly as long as the files (several files lie end to end, with a divider between them) and shows the name and length. Where that stretch of the lane is taken, the block moves to a new lane, shown as a lane opening under the track; below the last track it opens a new track (drum MIDI makes a drum track, other MIDI a keys track, audio an audio track). Files that cannot go somewhere show the reason on the block and the no-drop cursor. As with recorded takes, a drop over existing clips of the same kind plays on its new lane and greys out the older lane. One drop is one undo step.
- A dropped MIDI file becomes a MIDI clip that plays through the track's instrument and effects like a recorded MIDI take. It is placed on the song's own tempo map, beat for beat, so a groove dragged from a drum plug-in lands exactly on the bar grid at the song's tempo (the file's own tempo is not used; a file timed in SMPTE frames keeps its seconds). All tracks of a multi-track file are merged into one clip; a drum track takes the file's drum channel (channel 10) when the file also has other channels, a melodic track the other channels.
- Files a plug-in drags out of its temp folder, and in-memory files some plug-ins and archives drag, are copied into the song's media folder ("<song> Media" beside the saved song, or Music\TabForge Recordings for an unsaved song), so the clip keeps working after the plug-in deletes its file. Files dragged from your own folders are used where they are, as before. "Add audio file…" on a lane places files the same way.
- Song files (.tforge, .gp, .gp3, .gp4, .gp5, .gpx) dropped anywhere on the window, the timeline and the tab strip included, open in new tabs.

- Tracks with a VST instrument no longer go silent when the instrument stops playing: switching the track's FX off, bypassing or removing the instrument (even the last plug-in), a plug-in that crashed, is untrusted, is missing or fails to load now all bring in the track's General MIDI sound automatically (setting "Auto-switch to GM sound when no VST instrument plays"), and it steps back out when the instrument plays again. A GM tick or untick you made yourself is still respected.
- Sections: the two "delete" actions are now named for what they remove. The Sections panel's button is "Remove" (only the marker; the bars and notes stay) and the timeline's section menu says "Delete section and its bars…" (it asks first, and Undo restores everything; the status bar says what was removed). View > "Zoom 100% (fit width)" is now "Zoom 100%" (it sets 100%; the zoom box's "Fit width" fits the width). F3 and F8 are described as what they do: "Show track list" (brings the track list into view) and "Show / hide arrangement overview" (View > Arrangement overview now shows its F8 key); their keys are unchanged.

- Menus: the key shown beside every main-menu item is now read from your current shortcuts (and follows a rebind, a preset change or "Reverse + / - duration keys" at once); an unbound command shows no key. This fixes labels that were wrong or invented: Insert beat / Delete beats ("Ctrl +" / "Ctrl -" zoom), Double dotting (Ctrl+. is Stop), Triplet feel (Ctrl+/) and Stylesheet (F7) had keys that do nothing; the Sections menu showed Alt+Left / Right instead of Alt+Shift+Left / Right. Keys such as the semicolon, slash and bracket keys and the number keys are now written as printed (";" instead of "Oem1") in menus, tooltips and the Shortcuts page, and the note right-click menu's pitch / string items follow your bindings too.

- Note menu: "Increase duration +" / "Decrease duration -" are now "Longer note value" and "Shorter note value", matching what they do (the + key makes a note shorter, the - key longer, unless "Reverse + / - duration keys" is on). They are bindable commands (`Note.Longer` / `Note.Shorter`); the keys shown in the menu follow your bindings and the reverse setting.

- Note > "Move to higher string" / "Move to lower string" (and the same two items in the note's right-click menu) now move the selected note(s) to the next string keeping the pitch; the fret is recalculated from the tuning. If the pitch cannot be played there (fret below 0 or past the last fret) or the string already has a note in that beat, nothing changes and the status bar says why. One undo step. New bindable commands "Move note to higher / lower string" (Alt+Shift+Up / Down); the plain Up / Down arrows still move only the cursor.

- Keyboard fixes: shortcuts written with Alt and Shift together (Previous / Next section, Alt+Shift+Left / Right) now work (the key map spelled the modifiers in an order the window never produced); Alt chords reach the editor again (Alt+Left / Right step to the next / previous entered note); Ctrl+Insert, Ctrl+Delete, Shift+Insert, Ctrl+Shift+Insert / Delete and Ctrl+Shift+Up / Down no longer get swallowed as beat edits or line moves, so Insert bar, Delete bar, the section editor, Add / Delete track and Next / Previous track run from the keyboard. A bound Ctrl or Alt shortcut now always wins over the editor's own keys.
- Insert beat (Insert), Delete beats (no key), Shift pitch up / down (Shift+Up / Down) are now catalogued commands: rebindable in Preferences > Shortcuts and listed in the command palette. The editor's Ctrl+Plus / Ctrl+Minus beat insert and delete (which always zoomed instead) and Alt+Up / Down cursor moves are gone.

- New bindable commands (no default key; Preferences > Shortcuts, command palette): Export MIDI, Export ASCII tab, Project settings, Metronome on / off and Count-in on / off.

- Tracks: a drum track added with the quick-add button now starts with the default drum map's TAB lines (6) instead of 5, the same as one made in the + Track window, so no rebuild through Track properties is needed.
- Notation: a hammer-on / pull-off added with the H toggle now also draws its slur in the standard notation staff, matching the arc in the tab.
- Score: Shift+click extends the selection from the cursor to the clicked beat (like Shift+arrows); a plain click still clears it and moves the cursor.
- Score: an effect key (a technique, dead note or ghost note) pressed on an empty beat now gives the new beat the current note value instead of leaving the default.
- Plug-ins: on a fresh profile the empty Add plug-in window now has a Scan the standard VST folders button (its tooltip lists the folders). One click turns on "Also scan the standard VST folders" and scans; the default is unchanged (nothing is scanned until you choose).
- Plug-ins: a plug-in that crashed and was switched off can now be allowed again. Select it in its FX chain window and press Allow again, or use Preferences > Audio & Plug-ins > More options > Plug-ins switched off after a crash, which lists them all. It loads again on the next playback and the faulted icon on the track clears; allowing again does not approve an untrusted plug-in.
- Tooltips: long tooltips now wrap onto a few lines instead of running across the whole screen (short ones are unchanged), and every button, tab and toggle that runs a command shows its current shortcut in brackets at the end ("Play / pause (Space)"). The bracket follows your rebinding and shortcut presets at once and disappears when a command has no shortcut.
- Track list: a right-click on a control in a track row now reaches that control (knob type-in, pan menu, sliders, buttons, the number's colour menu). Only a right-click on the row's own background opens Track properties.
- Instruments: choosing a bass, keys or guitar sound for a new track (+ Track) or on an empty track (the row's Instrument button, or Track properties) gives it that instrument's own strings (a bass gets 4 strings, E A D G; keys get their key rows) instead of keeping the guitar's six. A track that already has notes keeps its strings and kind, so no note moves; Track properties says so when you pick a different kind of instrument for it.
- Capo: changing a track's capo keeps the written fret numbers and changes the sounding pitch (open string + capo + fret) of the notes already written, so playback, the staff and the fretboard agree with the new capo.
- Tools > Transpose now moves both voices, skips drum tracks (percussion numbers are instruments), moves only the selected bars when some are selected, and moves slide and trill targets with the notes. A note that would go below fret 0 or past the last fret is moved to a free string of its beat where it fits; the status bar counts any note that fits on no free string. It is a bindable command ("Transpose", unbound by default). One undo step.

- Duplicate bar now copies the bar on every track, not only the selected one: bars are shared by all tracks, so the copy lands right after the source in each track and the tracks stay lined up, like Insert bar and Delete bar. With bars selected (in the score or on the timeline) it duplicates the whole selection after its last bar and selects the copy. One undo step. A section label is not repeated on the copy.
- Time signature and key signature now carry forward: setting one on a bar applies from that bar up to the next change (or to the end of the song), on every track, and only the bar where it changes shows it. The score, the bar check (red bars), beaming, accidentals, playback and exports all use the same bars. Both dialogs have an "Only this bar" check box for a one-bar change; with several bars selected, the change applies to exactly those bars. A bar without a key of its own is exported in the song's key, as the score shows it. The toolbar now shows the signatures of the bar you are on. Changing bar 1 stops at the next change, and later bars keep what they had. One undo step.
- Notation: ledger lines now always match the staff lines (same colour, opacity and thickness, so they are as light as the staff). The separate "Ledger-line opacity" setting is gone; "Staff and ledger line opacity" under Preferences > Score & Notation sets both together, and your saved staff-line opacity is kept.
- Fretboard: new look by default for fresh settings (shaded blue scale highlight, white fret dots at original brightness, large fret numbers, natural string spacing); saved choices are kept. New "Scale highlight strength" (10-150%, 100% is the standard look) in Preferences > Fretboard > Appearance dims or brightens the scale highlight on the fretboard and keyboard in every style, with the root note always stronger; bindable as "Scale highlight brighter" / "Scale highlight dimmer" (10% steps, unbound by default).
- Fretboard layout: the empty band above the top string is gone, so the panel needs less height; the colour legend and Scales button now line up with the bottom of the fretboard, and a technique tag on the top string sits beside its marker instead of above it. New `--render-fretboard` diagnostic draws the fretboard or keyboard to a PNG.
- Timeline: the bar cell under the mouse pointer gets a soft shade (a lighter grey on empty cells, a slight lighten on filled ones), on any track row; it follows scrolling and zooming and never gets in the way of clicks, drags or selections.
- Timeline: new setting Preferences > Timeline & Tracks > Playback position marker. Line (default) is the white vertical line; Bar marker shows a small dark square in the current bar of the selected track instead (it moves bar by bar); Both shows the two together. Bindable as "Cycle playback position marker" (`View.CyclePlayheadStyle`, no default key).
- Audio audit (`--audio-audit`): no longer stops with "An item with the same key" on songs where a repeated passage gives two sections the same name and first bar.
- Notation staff: artificial, tapped, pinch and semi harmonics are written at the fretted pitch with a diamond head (they were drawn several ledger lines too high), eighth-note flags are full flags, and slides (slide in / out, shift and legato) now draw their short slanted stroke in the staff as well as in the tab.
- Diagnostics: `--capture <script.json> <outDir>` (with `--profile`) photographs the main window, menus and tool windows off-screen from a JSON script, for documentation screenshots (docs/guides/CAPTURE_SCRIPT.md).
- New **Help > Tutorial…**: the Beginner's Guide opens in its own window with a contents list, instant search across every chapter (matches highlighted in the snippets), Back / Forward (Alt+Left / Alt+Right), previous and next chapter, pictures at their natural size, tip / note / warning boxes and tables, in both themes. Ctrl+F jumps to the search box and Esc clears it; the last chapter and the window size are remembered. **Export PDF…** saves the whole guide as an A4 PDF (cover, contents with page numbers, chapter banners, running header and footer, selectable text) while the window stays usable. The command `Help.Tutorial` can be bound to a key in Preferences > Shortcuts (F1 stays with the shortcut reference).
- Clean .gp export round trips more faithfully: arpeggio strokes stay arpeggios (not brush strokes); a pickup or short last bar of an imported song stays short instead of being padded to a full bar (empty parts of it too), and an empty bar in 3/4 or another metre stays a full bar; every bar's clef is written (and the guitar clef is read back as the guitar clef); a tempo ramp cut short by the bar end or the next tempo point keeps the tempo it reaches; drum sounds outside the usual General MIDI drum range keep their number; a note whose sound differs from its string and fret (a track whose tuning the source file did not give) is written where it sounds.
- Score import: a string played mostly in harmonics or tied notes no longer comes in tuned an octave too high; a drum hit with no sound of its own in a .gp3/.gp4/.gp5 file gets the sound playback gives it, and a long drum tie keeps sustaining its first hit instead of starting new hits partway through.

- Notation staff: ties and hammer-on / pull-off slurs now stop short of an accidental that sits beside the tied note in the same chord, and start after / end before ghost-note brackets; the opening ghost bracket sits a little closer to its notes so it clears accidentals and the previous beat's stem.
- Tab view: slide lines and bend lines now start and stop at the edge of a bracketed (ghost) fret number instead of running through its brackets.
- Every rotary knob (master volume, track volume and pan, FX chain Volume, MIDI processor numbers, Track Properties) lets you type an exact value: double-click, right-click, or F2 / Enter on a focused knob opens a small editor on the knob. Units are understood ("-6 dB", "75%", "2:1", "100 ms", comma decimals); Enter or clicking away applies like a drag, Esc cancels, an invalid entry shows a red outline. Reset to default moved from double-click to Ctrl+click (and Home). Knobs with a right-click menu get "Type value…" as its first item. FX chain Volume now moves in 0.1 dB steps.

- Tab view: a rest moves up clear of the other voice's fret at the same beat, and dynamics above the tab keep a little more distance from the fret numbers.

- Engraving: a system now grows to hold the finger rings, right-hand letters and harmonic values under its TAB (they were cut off at the bottom, worst with two voices).
- Shift+F10 and the Menu key now also open the timeline's menu (the selection menu when bars are selected, otherwise the bar menu for the playing or current bar) and the fretboard / keyboard panel's menu, each with its first item focused; the instrument panel can be reached with Tab and shows an outline when focused (clicking it still leaves the focus on the score).

- Engraving: tight gaps cleared: a grace fret keeps 2.4 px from a two-digit main fret, a trill's second fret and the lyric rows (14.5 px apart) are spaced out, and the 8va caption's space includes its full height.
- Engraving: a simile bar no longer extends a P.M. line through it or gets tie stubs from its neighbours (the line ends at the bar line before it, as in the reference).
- Shift+F10 and the Menu key now open the score's right-click menu (the note menu on a note or selection, the score menu elsewhere) at the caret, with the first item focused, so the menu can be reached and used with the keyboard alone.

- Every menu item that opens a dialog now ends in the single ellipsis character "…" (the main menus used three typed dots), so all menus, buttons and hints match.
- Engraving: lyric rows under the TAB (up to three, below any fingering) no longer leave their system; a system with lyrics grows by the room they need, and the PDF export reads the grown height.
- Engraving: a beat text longer than its bar no longer runs under the next bar's number, tempo or title (they stack above it); the arrow of an arpeggio or brush stroke counts as ink, so a bar number clears its arrowhead; a repeated identical bend or whammy amount over a crowded run is printed once instead of as a pile.
- Long check-box labels now wrap instead of being cut off (the "Tint the track's row and lane" option in Track properties), and the Render window's progress bar uses the theme colours instead of a bright white strip.
- .gp3/.gp4/.gp5 songs with more than 1,000 bars now open: the score reader's fixed 1,000-bar limit is replaced by TabForge's own limit of 20,000 bars per track.

### Sound

- Audible change: rendered audio (File > Render, the WAV and MP3 master mix) now goes through a transparent safety limiter (ceiling -0.3 dBFS, 1.5 ms lookahead, about 80 ms release) after the master chain, so loud passages no longer clip the file (the demo song's mix peaked at +3.1 dBFS with 1,261 clipped samples; it now peaks at -0.3 dBFS). Audio already below the ceiling is unchanged, stems are never limited, and Monitor FX still stay out of renders. On by default; turn it off in Preferences > Audio & Plug-ins > Output device (Safety limiter on rendered audio).
- New optional safety limiter for live playback through the audio engine (same -0.3 dBFS ceiling, after the master chain and Monitor FX, adds about 1.5 ms of delay). Off by default; Preferences > Audio & Plug-ins > Output device, More options (Safety limiter on live playback).
- Playback: fixed notes that could keep sounding after they should stop (found on 16 of about 1,500 test songs): a slide-in or a before-the-beat grace note on the very first beat of a song, and a tremolo-picked note cut short by a following slide-in, left a note-off before its note-on. Tremolo attacks that fall after such a cut are now left out. A before-the-beat grace note on the first beat now plays on the beat, taking a slice off the main note, since nothing can sound before the song starts. The playback audit now lists each hanging note with its neighbouring events.
- Fermatas now hold during playback (twice the note's length) in every track, the playhead, MIDI export and audio render.
- The engine log's 10-second audio line now also shows the block size the device really delivers (for example WASAPI exclusive gave 192 frames when 64 was requested).

### Notation

- Score engraving: every marking above or below the staff (accents, fermatas, tuplet brackets and numbers, trills, wah and tap marks, 8va lines, vibrato, chord names, beat text, dynamics, let ring, harmonic captions, tempo, swing mark, bar numbers, section titles, directions) now claims its own row and the next one stacks outside it, so no two texts or marks overlap; voice 2's marks go below the staff. The space above and between the staves grows to fit the song's own content (low ledger notes, stacked marks).
- Score and tab: stacked marks keep at least 2.6 px apart (dynamics, Wah, F.B., Harm., let ring and chord names no longer touch); with two voices on the staff each voice keeps its fermata (upright above, inverted below), and the tab-only view draws a fermata both voices share once; a fade wedge clears harmonic captions.
- Score and tab: tempo changes inside a bar are now printed over the beat where they start; fermatas, chord names and beat text on rests are drawn (and beat text is kept inside its bar).
- Score and tab: the repeat-bar (simile) sign is now drawn as a proper slash with two dots (two slashes for "repeat two bars") instead of the coda sign. A repeat-bar bar shows only its sign, centred, with no notes beside it (its notes appear while the edit cursor or selection is inside it, so they can still be edited).
- Score and tab: the height of each system follows the track's string count, so 7- and 8-string tracks no longer spill below their system and 4- and 5-string basses get shorter systems.
- Score: an empty bar shows a whole-bar rest (as in the reference); very deep whammy dives are drawn compressed so they stay inside their system (the labels keep the true amounts).
- Score: grace notes now read left to right in time; tuplet numbers and other marks stack clear of short rests (a triplet number no longer runs through a 32nd or 64th rest).
- Score: fixes from a sweep over many real songs - a chord of ghost notes now gets one pair of brackets instead of overlapping ones, accidentals no longer sit on top of a displaced chord head or a ghost bracket, grace notes sit clear of the main note's accidental and get room in the bar, and the first note after a key or time signature no longer touches it.
- Alternate-ending (volta) brackets are drawn above the staff with the other bar texts, at one height across the bars of an ending: label and hook at the start, hook at the end, open when the ending continues.
- Ledger lines are drawn at full staff-line contrast (they were faint and read as stray marks), and a little longer in the default Minimal style.
- Ghost-note brackets clear the ends of ledger lines; a pre-bend arrow stands beside the higher strings' numbers instead of through them; the second voice's fingering and harmonic marks sit below the first voice's.
- Tab-only view: the empty staff area above the tablature is gone (systems are about 100 px shorter and the TAB sits right under its marks); the playback line in tab-only view now spans the tablature instead of the hidden staff.
- Tab-only view: dynamics that would print below the system (under fingering or lyric rows) now stack above the TAB instead of being cut off; wah, trill, rasgueado marks and whammy curves take part in the stacking; in notation-only view a fade in/out wedge sits under the dynamics row instead of through it.
- Tab-only view: accents, staccato and tenuto marks, let ring spans and technique labels now stack above the palm-mute lane instead of printing over it, so double P.M. and accent marks and "let ring" no longer collide.

### Editing and UI

- Preferences are reorganised: 14 pages in four captioned groups (new first page "Common settings", then General, Appearance, Score & Notation, Fretboard & Keyboard, Timeline & Tracks, Editing, Playback & Practice, Audio & Plug-ins, Recording, Tabs & Windows, Shortcuts, Files & Backups, Advanced), one centred column per page with the everyday settings shown and the rest behind "More options", each feature's colours in its own "Appearance" group, all "ask before" questions together on General, search that matches every word in any order (with everyday synonyms and the old menu names), Esc closing the window, and resets that ask first ("Reset page…" at the top of each page, "Reset all settings…" on Advanced). No setting was removed or changed in the settings file; "Follow the playhead" is now "Scroll the score while playing" and "Density" is "Spacing".
- Settings rows for every menu option that had none: Lock fretboard size, Score page layout (Page / Continuous), Score scrolling (Vertical / Horizontal), Text & fonts, the four timeline appearance options (now also remembered between sessions), Show tracks in groups in new songs, and one colour per track group. "Display" groups are called "Appearance".
- Lean right-click menus: the fretboard, score, timeline, section and track-list menus keep only frequent actions and end in ONE "... settings..." entry (Fretboard settings, Score settings, Timeline settings, Track list settings) that opens Settings on the right page and highlights the right row. Styling moved to Settings: fretboard appearance, sizes, colours and look-ahead; dark/light page, ledger lines, page turns, playback colours and Text & fonts; the timeline's four appearance toggles. Menus are at most two levels deep and never hold a same-named submenu ("Appearance and layout > Appearance" is gone). "Show all tracks as" is a button in Settings > Fretboard; "+ Track" offers only Add track and Mixer (colour items are under Colours in the track list menu; the colour of each group is in Settings > Appearance & colours).
- One menu separator style everywhere (the gap with no line in the note and timeline menus is fixed), choices of one value (Page / Continuous, durations, dynamics, pan knob / slider) are drawn as a dot instead of a tick, unticked note tools can be ticked, and "original position" reads "default position". The Effects "Chord…" / "Text…" and Sections "Go to…" menu items match the other menus, and a stale tooltip over the fretboard menu's Scale row is gone.
- Dialogs now close on Esc however they were opened (the "Repeat close" prompt ignored it when opened with its shortcut). Track properties shows the drum-channel hint only for drum tracks or channel 9, hides the tuning for keyboard tracks, and wraps the "Tint the track's row" text.
- View > Mixer / VST opens the Mixer window (before, it only selected the docked Practice / Mixer tab).
- The keyboard view's legend is no longer about three times the fretboard legend and leaves a strip clear of the keys; the Render window keeps its Render and Close buttons in a fixed footer (no scrolling at 1920x1080); the import Cancel in the status bar looks and acts like a button.
- Typing a tempo in the toolbar box now applies on Enter (before, only clicking away applied it) and keeps the cursor in the box; changing the tempo while playing recompiles from the current position so the new tempo is heard at once.
- Score information and Song properties: the notice, instructions and lyrics boxes scroll inside a bounded box, so a long notice no longer stretches the dialog.
- Long check-box labels now wrap instead of being cut off (the "Tint the track's row and lane" option in Track properties), and the Render window's progress bar uses the theme colours instead of a bright white strip.

### Files and import

- Score import: songs with a long notice (more than 4,096 characters, for example a tabber's note of a few hundred lines) failed to open with "invalid or overlong notice"; the notice may now be up to 65,536 characters (control characters and larger texts are still refused).
- MIDI export now follows playback to the millisecond: the tempo track is written as the exact time playback takes for each stretch (ramps, mid-bar tempo changes and fermata holds included), and notes that ring on past the last bar keep their full length. Before, bar starts drifted up to 17 ms in the demo song and the file ended 172 ms early. New diagnostic `TabForge.exe --midi-timing <song> <out.mid>` prints the drift.
- Tempo ramps now survive a clean .gp export: each ramp is written as the .gp format's linear (progressive) tempo point, and a progressive tempo point in a .gp file is read back as a ramp of the right length. Before, a ramp came back as an instant tempo step.
- A double bar on the last bar now survives a clean .gp export and reopen (the score reader TabForge uses drops it; the importer now reads it from the file itself).
- A track that plays transposed (for example the demo song's Sub Drop, written an octave up and sounding an octave down) now keeps its sound in a clean .gp export: the transposition is written into the track's tuning (every fret stays as written), because the .gp format's own transpose setting only changes how the notes are displayed. A keys track, or a shift that would take a string out of range, has its notes moved to the sounding string and fret instead. Before, the dive-bomb notes came back an octave too high after export and reopen. `--exportgp` takes an optional `clean` argument to write and re-read a clean file.
- Clean .gp export and import keep simile marks, tremolo-picking speeds, triplet feel, dead-slapped notes, a strum's own spread, a whammy curve's peak and dip, bend graces and slides into harmonics; 7-, 9- and 13-tuplet runs keep their exact positions on import.

### Recording and engine

- Recording: the buffer between the audio input and the disk grew from about 5 s to about 175 s at 48 kHz (67 MB while recording, shared by all armed tracks; at 192 kHz it is capped at 256 MB), so a busy disk (a big copy, a scan, a backup) no longer loses seconds of a take. If the disk stalls for longer than that, the lost stretch is still silence in place, so the take keeps its timing. When recording stops after a loss, a dialog says how many gaps and how many seconds were lost; the engine log also records each gap and the total. Writes to disk are 4 times larger.
- Closing a tab now unloads its plug-ins at once. Before, a closed song's plug-ins stayed loaded (silent) for 5 minutes like a background tab's, so opening and closing songs with a large sampler kept several copies in memory (up to about 4 GB) and added audio-thread load. Switching between open tabs still keeps the other tab's plug-ins loaded for instant switching back.

### Stability

- Starting TabForge no longer rewrites your saved dock layout: the track-list auto-fit adjusts the split for the current window only, and the layout file keeps the size you chose until you drag a splitter or change the layout yourself.
- A drawing error in any self-drawn control (timeline, fretboard and keyboard, knobs, meters, icons, tuner gauge) is now contained like the score's: the control shows a small dashed outline instead of repeating an error dialog on every repaint, and the cause is written once to the diagnostics log.
- Score and tab: an error while drawing one line of the score no longer brings up an error dialog on every repaint: that line shows a short note, the error is logged once, and the rest of the score draws normally.
- Score and tab: a hammer-on or pull-off slur in the second voice looked for its neighbours in the first voice's notes, which crashed the drawing (index out of range) on songs whose second voice has more beats than the first; the slur now follows its own voice.

### Demo song

- The first forced line break no longer leaves the one-beat pickup bar alone on line 1 (it now shares the line with the following bars).
- The drum crash that faded in over bar 10 swelled the whole kit from silence (a fade-in is a channel-wide volume ramp), so the kick, snare and the start of the snare roll were inaudible. The reverse-cymbal swell now sits alone on beats 3-4 of bar 3, rising into the kick of bar 4; bar 10 has no fade-in. The sample file was regenerated.
- The Outro piano ("music box", two octaves up) was written pp and could not be heard (about 40 dB under the mix). It now plays mf with the piano fader lifted for bars 135-140, about 20 dB under the mix, audible under the guitars; the sample file was regenerated.

### Diagnostics and tools

- `TabForge.exe --audio-audit <song> <outdir>`: renders the whole song offline through the built-in General MIDI synth with no audio device and no window (mix.wav plus one stem per track), then writes audio-report.json and audio-report.md with onset timing against the compiled timeline, clipping, clicks, DC, ringing and silence, per-section loudness, drum humanisation, a playback / MIDI export / render tempo-map consistency check and a list of moments to listen to. The click detector ignores steep edges that repeat at a steady period (the saw wave of the synth-bass programs); isolated discontinuities are still flagged. The `TABFORGE_AUDIT_NOLIMITER=1` environment variable renders the unlimited mix, to see what the safety limiter catches.
- `TabForge.exe --render-bars <song> <outdir> [--tracks all|1,3,5-7] [--views notation,tab,both]` (headless, no window): one PNG per track, bar and view cropped to the bar, `checks.json` (collisions, clipping, marks too close, and data-versus-drawing consistency for every mark) and `summary.md`. The bar audit measures accent glyphs by their real outline and no longer reports a note's own accidental as a stray key signature.
- Test runs: `--profile <folder> --approve-night-plugins all` approves every plug-in the normal scanner finds in the standard VST2/VST3 folders (path plus hash) in that profile only, for stress testing; the named list stays the default without `all`, and without `--profile` both forms are refused.

## 0.3.0-beta.2 — 2026-09-30

- Fermatas now hold during playback (twice the note's length) in every track, the playhead, MIDI export and audio render.
- New diagnostic `--write-demo-song <out.gp>` writes the built-in full demo song "Ashen Meridian" (CC0; 10 tracks, 144 bars) as a .gp with the whole project embedded, and checks that it reads back identically.
- Score import: when the protected import process (or its memory and time limits) cannot be set up, TabForge now asks before opening the file inside TabForge ("Open it anyway?", default No), once per file, instead of doing so silently.
- Opening many score files at once imports at most two at a time (the others wait, tabs still open in the selected order), and the import processes together stay within 3 GiB of memory (1.5 GiB each). Closing the window cancels the waiting imports too.
- Dynamics (ppp to fff) are now engraved in bold italic under the staff (under the TAB in tab-only view), on the first note of a track and then only where the dynamic changes; they keep clear of lyrics, beat text and palm-mute lines. Turn them off in Preferences > Score > Labels > Show dynamics.
- Linked audio is read only from places you allow (Audit 6 A6-01): clips on a network location (UNC or mapped drive) or a removable drive are not opened until you approve that folder for the song (the clip shows "not loaded (approve to load)", with a notice bar and a *Linked audio* window to allow or revoke folders); device paths (`\\.\`, `\\?\`), reserved device names and non-audio file types are refused, links are followed to their real target, and a song whose clip names a device path is rejected on load. Waveform reading now uses one bounded, cancellable background queue with limits (2 GiB, 2 hours, 64 waiting files, 5 minutes) and shows a clear clip error instead of failing silently; the engine refuses non-plain clip paths too. The *Linked audio* window is always reachable: Preferences > Audio & VST > "Manage approved folders…" and the new bindable command `Media.ManageApprovals` (unbound by default).
- Waveform cache (Audit 6 A6-05): closed timelines are no longer kept alive by the cache, outlines have a 256 MB memory budget (least recently drawn go first), and a file replaced at the same path is read again (keyed on path, size and last-write time).

## 0.3.0-beta.1 — 2026-09-30

- Score right-click menu: Copy, Cut, Paste and Paste special for the selected notes (a right-click inside a selection keeps it), with note actions grouped into Duration, Dynamics, Effects, Beat and Pitch and string submenus. The score empty-area and fretboard menus are shorter too.
- The timeline right-click menus are shorter: Copy, Cut, Paste and Delete stay on top, related items are grouped into submenus (Insert bar, All tracks, Section, Arrange, More, MIDI clip, Timeline display), shortcuts are shown, and items that do not apply are hidden. The "not built yet" advanced-conversion entry is gone from the MIDI clip menu.
- Copy, cut and paste in the score are rebuilt. Ctrl+C copies the selected beats or bars (or the beat at the cursor), Ctrl+V pastes at the cursor, Ctrl+X cuts; the score and the timeline now share one clipboard, so a bar or area copied on the timeline pastes into the score and the other way round.
- Pasting asks only what that paste needs, in one small dialog: replace or insert beats, keep the exact pitch or shift by an octave between instruments of different range, overwrite or insert before/after for bars, copy or keep the bar settings, and how to paste onto a drum track. Each question has "Remember my choice"; every remembered answer can be changed or reset to "Ask every time" in Preferences > Editing > Copy and paste (searchable).
- Copying a beat range starts at the cursor and flows over bar lines by the target's time signature (a note that crosses a bar line is split and tied; a triplet group is never split); the tracks are never made over-full.
- Pasting between instruments keeps the pitch and re-frets the notes on the target (guitar to bass, different string counts, tunings and capo) and reports notes that did not fit; drum tracks keep their drum sounds, and pitched notes can paste their rhythm onto one drum sound. Notes that cannot be placed are left out and named in the status bar.
- New Paste special (Edit menu, Ctrl+Shift+V, bindable as `Edit.PasteSpecial`): a repeat count (1-99), replace or insert for beats (overwrite, insert before or insert after for bars), an octave shift (-2 to +2), "Keep string and fret (don't re-finger)" and copy bar settings yes/no; all repeats are one undo step.
- Cutting bars in the score empties them (the bars stay, on all their voices); cutting beats turns the whole selection into rests without moving the notes after it. Insert before/after adds the bars on every track so tracks stay aligned.
- Fixed the old paste bugs: pasting over existing notes no longer overwrites silently (you are asked, or your remembered choice is used); a part-bar paste lands at the cursor instead of at its original position in the bar; a single beat pastes at the cursor instead of the first slot; cut takes the whole selection, not only the beat under the cursor.
- Copy, Cut and Paste commands are renamed from "beat" to match what they do and are in every shortcut preset (Paste special is unbound in the Alternative preset).
- Screen readers can now navigate the score by structure: the editor exposes the bars of the current system, each a one-row grid of beats named like "Bar 5, beat 3, eighth note, string 2 fret 7, palm mute". Cursor announcements now add the section when you enter a new one and the time signature or tempo when they change, and mention the note's techniques. New commands Read current bar (Ctrl+Alt+B) and Read position (Ctrl+Alt+P); nothing is built while no screen reader is attached.
- Staccato, Tenuto and Grace in the menu now do exactly what their shortcuts do (selection aware; Grace is the per-note grace-before technique, like the G key and the palette); one undo step each.
- Score files are now read in a separate TabForge import process with memory (2 GiB) and CPU-time limits. Cancel or the time limit ends that process at once and frees all of its memory, even when a damaged file makes the reader hang; the imported song is checked like a .tforge before it opens. If that process cannot start, the import runs inside TabForge as before and the status bar says so.

- Reproducible releases: the exact .NET SDK is pinned and releases are built from a clean checkout, so the GitHub-built downloads and a local build of the same commit are byte-identical (checked with `Source/tools/Compare-Release.ps1`) before the attested files are published.

## 0.2.0-beta.5 — 2026-09-30

- New bindable command "Cancel import" (`File.CancelImport`, unbound by default): cancels background score imports, same as the status-bar Cancel button.
- Plug-in browser: an unapproved or changed plug-in is no longer run to find out whether it is an instrument or an effect. It shows "approve to identify" until you approve it; approved plug-ins are identified as before, and the identify step now checks the approved file hash before loading (the isolated plug-in host checks it too).
- Edit > Copy last beat, Dotting/Double dotting, Repeat open/close and Empty bar now do exactly what their shortcuts do (one shared `EditCommands` service): repeat beat follows the active voice and cursor and no longer overwrites bar slot 15; double dot respects the bar-length rule; each takes one undo step (Dotting used to take two); repeat marks apply to every track.
- Tool palettes are keyboard-focusable (Tab enters a group, arrow keys move inside it); the active tool also shows a thick underline and "active" in its accessible name; the status bar text is a polite live region.
- No text is smaller than 11 px (mixer, FX chain, track columns, group headers, palette headings) and small secondary labels use a higher-contrast colour.
- Release builds: a release workflow builds the installer, portable zip and SHA-256 checksums from a clean checkout, ships the CI-built VST3 bridge and publishes GitHub build attestations. The binaries remain unsigned (Windows SmartScreen may warn). The portable zip uses forward-slash paths and a neutral fixed timestamp.
- The repository-source and installer checks in the self-test are required in CI instead of being skipped.
- Recording: when the disk cannot keep up, the lost input is now silence at the position where it was lost (after the older queued audio), so the take stays in time.
- Isolated plug-ins: all of them in one audio callback share a single wait deadline (75 % of the block); once it is spent, the rest of the chain is bypassed for that block.
- Score files open in the background: the window stays responsive while a song imports ("Importing <name>…" with a Cancel button in the status bar); several files can import at once and open in the order chosen. A .gp / .gpx archive that would unpack to an excessive size, and a damaged .gp3/.gp4/.gp5 header, are refused before parsing; an import that runs past 60 seconds or uses excessive memory is stopped with a clear message.

- Opening a score file keeps the song's own title (the file name is used only when the song has none).
- Saving refuses a mixer/plug-in data file (.tfaudio) larger than TabForge can read back, with a message naming the largest plug-in states; the song stays unsaved instead of silently losing its mixer on reopening. A sidecar that cannot be read is now reported when the song opens.
- Timing stays correct after in-session edits to tempo, time signatures, repeats, endings and directions (clip placement, bar positions and the plug-in transport follow every timing edit).
- Playback order: repeats, alternate endings and D.C./D.S./Coda/Fine follow one written specification with 68 checked examples. Fixed: a repeat close without an open after a finished repeat no longer loops; a D.C./D.S. inside a repeat now plays the repeat out first; after a jump only the correct ending plays.
- Saving a clean .gp keeps D.C., D.S., Coda and Fine marks.
- Built-in synthetic test songs (repeats and endings, jumps, tempo changes and ramps, mix changes, tuplets, two voices, fades and palm-mute spans, and the demo song) run on every build.
- Binaries record source paths relative to the repository (reproducible, machine-neutral builds).

## 0.2.0-beta.4 — 2026-09-30

- Monitor FX: a "MON" button (amber, headphones) in the Mixer's Master row opens a monitoring effects chain for speaker / room calibration. It plays after the master, on the live output only, and is never included in renders, stems or exports. The chain is saved with the app and applies to every project ("Use for all projects", ticked by default); untick it in the Monitor FX window and that project gets its own chain, saved with the project. New bindable command `Mixer.MonitorFx`.
- Fretboard keeps natural proportions in any window shape: the string gap is capped at 0.8x the fret width and a tall pane centres the board (labels, legend and Scales button follow) instead of stretching it; a narrow pane scales the drawing down instead of squeezing the frets. New right-click > Appearance > String spacing (Compact 75% / Natural default / Wide 150% maximum), also in Preferences > Fretboard and bindable (`View.CycleStringSpacing`).
- Several tabs play at once through the audio engine: a song that is playing keeps sounding when another tab is focused (its tracks keep their own engine slots instead of being parked), and stopping one tab silences only that tab's tracks.
- Mixer: the Master row sits at the top of the list, directly under the column headers.
- Saving as .gp writes correct zip checksums (strict readers could report the file as damaged) and keeps album, copyright, tab author, music, words, instructions and notices.
- Help > About: new "Third-party licences" button that opens the licence notices.
- Refreshed tool-palette icons (dynamics, trill, time signature, voices, P.M., L.R., text and technique letters).
- Clearer option names: the shortcut preset "Classic", fretboard preview "Show beat / + next beat / + bar / Show bar", drum maps ".gp5 file drum map" and ".gp6/.gp7 file drum map".
- New demo song *TabForge Demo - Ashen Meridian* (original, CC0) in the Samples folder.
- Fixed: a score import could report duplicate notes left over from the previous import.

- Render: the offline tempo map follows mid-bar tempo changes and ramps exactly like playback.
- Licences: SoundTouch.Net (LGPL-2.1) now ships as a separate, replaceable `SoundTouch.Net.dll` beside `TabForge.exe`; a `licenses` folder with the full licence texts of all bundled components ships in the zip and installer; THIRD_PARTY.md lists versions, sources and copyright lines; each release attaches the SoundTouch.Net 2.3.2 source zip.
- Capo: editor entry, playback, import and export now share one pitch rule (sounding pitch = tuning + capo + fret, frets relative to the capo); a note typed on a capo track no longer shifts by the capo after saving as .gp and reopening.

- Fixed: a Mixer group's level and pan counted twice after saving as .gp (with its .tfaudio) and reopening; the .tfaudio now keeps each track's own volume/pan, and older pairs are un-baked on open.

## 0.2.0-beta.3 — 2026-09-30

- Volume, pan and group sliders always land on whole values; on a 100% scale display a pointer exactly between two values could leave a half value (for example −42.5).

- Mixer slider drags are as light as the track list's: the track list's matching slider moves in place during the drag, the plug-in engine sync runs at most once per frame, and the full track-list refresh waits for the end of the drag.
- Self-test windows are invisible (off screen, transparent, never activated) during a rebuild.

- Mixer sliders (every track and group, pan and volume, and the master row) now glide through every value while dragged; they no longer jump to the end and stick after the first step (the mixer was being rebuilt under the dragged slider).
- Save safety: the "save in progress" marker of a .gp + .tfaudio save no longer contains file paths. Recovery derives every file from the song's own folder, checks each one against content hashes recorded at save time, and only restores when the pair provably matches the previous or the new save; a damaged, foreign, oversized or older-format marker, a linked file or folder, a missing backup or a locked file changes nothing and is reported. The marker is written completely and flushed before either file is replaced, the stale-file cleanup keeps files an unfinished save still needs, and a new save will not overwrite an unresolved one.
### Plug-in trust and wording
- A plug-in approval now covers the exact file: TabForge records its SHA-256 (also for network and removable locations, read once when you approve) and the audio engine re-checks that hash right before every real load, holding the file open against writers while the plug-in is created. A DLL swapped for different content of the same size and time is no longer loaded ("blocked, changed since you approved it"; approve it again in Review to load it). Routine checks in the interface still only compare size and time, so they stay cheap.
- Publisher updates are recognised by the signing key (SHA-256 of the certificate's public key, with the signature validated by Windows), not by the certificate's subject text. Older records that only stored the subject are simply re-approved when the file changes.
- A rescan no longer replaces the approved fingerprint of a changed, unsigned plug-in; it stays "changed since you approved it" until you approve it. Network or removable plug-ins approved by path only in older versions ask for one new approval.
- Plug-ins reached through a link or junction inside Program Files / Common Files no longer get folder trust unless the real file is there too.
- Wording: running plug-ins in their own process is crash isolation, not a security sandbox. The Review window, the Settings tip, the FX window and the documentation now say so and ask you to load only plug-ins you trust.
- THIRD_PARTY.md describes the native VST3 bridge accurately (only the compiled DLL is in the public tree) and lists SoundTouch.Net.
- Autosave keeps protecting long sessions: during playback and recording it now runs at twice the interval instead of waiting until playback stops. The UI thread only captures a light snapshot of the song (about 60 ms for 40 tracks x 1000 bars, where serialising it there took about 800 ms); a low-priority worker writes the recovery copy. If autosave fails (disk full, Recovery folder access denied, ...) a notice above the status bar says why, when the last good copy was made and that it retries every minute (Retry now button); a failed write never damages the previous copy. Closing, renaming or moving a tab while a copy is being written is safe, and a recovered copy you opened is kept until the song is saved or has been autosaved again.
- Release verifiability: `Source/native/tfvst3` now builds from the repository alone (CMake fetches the pinned, unpatched Steinberg VST3 SDK v3.8.1, MIT), with exact steps and the checked-in DLL's SHA-256 and reproducibility notes in `Source/native/tfvst3/BUILD.md`; `THIRD_PARTY.md` records the SDK licence. The GitHub Actions workflow builds and self-tests headlessly, audits NuGet packages, and reports the bridge build against the checked-in DLL.
- Score import now keeps minor keys (the mode was read under a name alphaTab does not have); saving as .gp no longer fails when a track has a second voice in only some bars; new semantic round-trip suite (`--roundtrip-semantics`, docs/reference/COMPATIBILITY_RESULTS.md).

## 0.2.0-beta.2 — 2026-09-30

- Score follow now turns off only when you scroll the score yourself (mouse wheel, scrollbar, scroll keys, touch); zooming, reflow and panel changes can no longer switch it off, even intermittently.

- Mixer: sliders no longer jump. Every value step used to rebuild the whole track list and timeline inside the drag, so the pointer got ahead of the UI; the sound now updates at once and the list refreshes once when idle. A fast click-then-drag is no longer taken for a double-click reset, the handle stays under the pointer, and the arrow keys / Home / End step the focused slider by 1.
- Mixer and track list share one order: drag a track or a whole group in the mixer (or Alt+Up / Alt+Down, `Track.MoveUp` / `Track.MoveDown`) and the track list reorders with it, and the other way round; both play the same movement animation at the same time. One undo step per drop. The mixer now lists groups in the track list's order.
- Track list: right-click empty space (gaps in the header strip, the column header row, empty rows area) offers "Show tracks in groups" (`View.ShowTrackGroups`), the same setting as the Mixer's "Groups in track list" box. Group rows no longer have a record-arm button. Right-click on a group row opens the Mixer at that group.

- The side pane is now "Zoom & speed": one compact row (zoom out, zoom box, zoom in, speed box) with no inner caption. Playback speed has a single control there (editable, presets 50-200 %, typed values clamp to 25-200 %); the old toolbar speed box is gone. Automation id `Transport.Speed`.

- Fixed the song sometimes starting with a piano sound instead of the track instruments (mostly the first play after startup): the stop-notes command that starts every play could arrive in the same audio block as the program and controller setup and throw it away; it now silences the notes but keeps the setup.

- The "Metronome + Zoom" panel is now just "Zoom" (the transport and metronome live in the arrangement header), and its zoom box always shows the current value (e.g. 100%, or a custom 133%) at startup, on tab switch and for every zoom path.

- Plug-in tracks (such as drums) no longer come early against the tracks on the Windows MIDI synth when the engine is not playing the whole song: the Windows MIDI latency default is now 200 ms (measured; the old guess was 60 ms, and a stored 60 is moved to 200 once). Settings > Audio & VST > Windows MIDI latency has a Measure button that plays one very quiet hit five times, reads it back and stores the median when it is plausible (20-600 ms); it only runs when you press it.

- Changed: the fretboard / keyboard size is unlocked by default. A lock saved by an older version (it was the default) is cleared once; locking it again from the right-click menu is kept.

- Changed: every song now plays through the audio engine by default, on any driver and with or without plug-ins. "Play the whole song through the audio engine" stays a normal Settings toggle; the automatic rules that switched it on or off (plug-in added or removed, ASIO selected or left) are gone, so your own choice sticks.

- Fix: palm-muted notes in .gp3/.gp4/.gp5 songs played as ~20 ms clicks. alphaTab leaves garbage in the per-note duration % of those notes and the importer took it as 1%. The value is now read only from GPX / .gp files and only when sane (5-200%); older .tforge projects imported from .gp3/.gp4/.gp5 have durations under 5% reset to 100% when opened.

- Fixed: zooming (toolbar, Ctrl+wheel, hotkeys) during playback no longer switches off score follow. Scroll detection now stays off until the zoom re-anchor has run (it was a fixed 500 ms window, missed when the re-anchor was delayed), and every layout pass schedules the re-anchor onto the playhead.
- Accessibility / automation: every button, combo box, slider, text box, tab and list item in every window gets an AutomationProperties.Name (from its tooltip or text) and a stable `Area.Control` AutomationId (for example `Transport.Play`, `Window.Close`, `Tools.WholeNote`), including controls built in code. Automation peers for the tab editor and the arrangement follow.
- Test runs: `--profile <folder> --approve-night-plugins` pre-approves sampler and drum plug-ins, a host's bundled FX and the crash-test DLL (path plus hash) in that profile only; without `--profile` the flag is refused and nothing is approved.

- One debug switch for troubleshooting: set `TABFORGE_TRACE=playback,layout,ui` (or `all`; areas playback, engine, layout, import, ui) to write traces to `%LOCALAPPDATA%\TabForge\Diagnostics\trace-<area>.log`. The older `TABFORGE_MIDI_LOG`, `TABFORGE_LAYOUT_LOG` and `TABFORGE_CAPTION_LOG` switches keep working as before.
- Windows 11 Snap Layouts: hovering the maximise button now shows the layout flyout, and the caption buttons keep their hover and pressed colours (close turns red) and clicks.
- Saving a .gp together with its .tfaudio is safer: a "save in progress" marker is kept until both files are written. If TabForge or the PC stops between the two writes, the next time you open the song the last complete pair is restored and you are told. If restoring ever fails, the backup is kept and both file paths are reported.
- SECURITY.md now describes the shipped system: separate engine and plug-in host processes (crash isolation, not a sandbox), the plug-in trust gate, and file input limits.

- Fretboard numbers are slightly smaller by default: right-click the fretboard > Appearance > Number size (Small 75%, Medium 85% default, Large 100% = the earlier size). It scales the fret number and its bubble, technique tags, the fret-number row and string labels (and the keyboard's note labels), and is also in Preferences > Fretboard.

- Playback refinements, compared with .gp5 files (Audit 3 section 6b; accents, slap/pop, staccato, 8va and Fine keep their decisions): tremolo picking plays at the written 1/8, 1/16 or 1/32 speed (it played half speed); a down-stroke plays the low string first and brush/arpeggio spread follows the written stroke speed; a grace note before the beat comes out of the previous beat and the principal note stays on the beat, an on-the-beat grace takes an eighth of a beat; a plain bend reaches its target at the middle of the note and holds it; a legato slide is one pick (the target is not attacked again); notes of a let-ring run ring together until the run ends; artificial and tapped harmonics play the file's pitch and a semi harmonic sounds the fundamental too; note duration % is imported and played; vibrato depths match (note 0.38, wide 0.94 semitone); trills alternate at the written speed; hammered-on and ghost notes play at 80%, dead notes keep full velocity but are very short; slide-outs move about 5 semitones progressively from 25% of the note; expression (CC11) is reset at every channel setup. Tempo ramps inside a bar now export to MIDI with the right tempo track and note ticks.
- Score import keeps left/right-hand fingering (saved in .tforge; older files load unchanged). A .gp3/.gp4/.gp5 file that alphaTab rejects because of an empty second voice after a two-voice bar now reports the bar it stopped at. A 9:8 tuplet bar is no longer marked overfull (imported beats sit on whole file ticks).
- Engraving refinements, compared with .gp5 files (Audit 3 section 6a): bends are curved arrows with their amount (1/4, 1/2, full, 1 1/2 ...), releases, vertical pre-bends and multi-point custom bends; whammy bar is a line diagram with values; grace notes are small slashed notes before the main note and their fret is shown small in the TAB; tremolo picking draws 1-3 slashes on the stem (and over the TAB fret); trills show "tr" with a wavy line and the trill fret in brackets; hammer/pull slurs sit above the numbers and a legato slide adds a slur to its line; let ring is one dashed span; palm mute sits under the notation staff; ghost notes read "(7)"; one accent mark above the note (heavy accent is "^"); dead notes have no extra label; harmonics read "Harm." / "A.H." / "T.H." ...; wah shows "+" / "o"; pick strokes, brush and arpeggio arrows, wavy arpeggio line in notation, rasgueado; fingering (circled left-hand numbers, p i m a c) under the TAB; volta brackets with "1." / "2."; swing symbol; segno / coda / Fine directions and a thick final barline; rests are shown only on the staff when notation is visible; the first note clears the barline and beat text no longer overlaps section titles.
- File > Export MusicXML (.musicxml, uncompressed): one part per track with notes, rests, durations, dots, tuplets, ties, grace notes, key, time and tempo, repeats, dynamics, and a tab staff with the string and fret of every note plus hammer-on / pull-off, slide, bend and harmonic marks. Bindable as "Export MusicXML".
- Clean .gp export: a grace note no longer swallows the duration of the note it ornaments (bars after it shifted), long whammy-bar curves and semi / feedback harmonics are kept instead of dropped.
- Command palette (Ctrl+Shift+A, File > Command palette): type to fuzzy-search every bindable command, see its key and press Enter to run it. Bindable as "Command palette".
- File > Export PDF: the engraved score (notation and tab) of the selected track as A4 pages, no printer needed. Bindable as "Export PDF".
- Screen readers: the tab editor now announces its cursor (track, bar, beat, string, fret and note name) as it moves.
- Autosave: songs with unsaved changes are copied to TabForge's Recovery folder every 2 minutes (Settings > General > "Autosave unsaved songs": off, 1, 2, 5, 10 or 30 minutes; skipped while playing). Your own files are never overwritten. After a crash or power loss TabForge offers to reopen the copies as unsaved tabs at the next start; saving or closing a song removes its copy.
- Tools > Tuner is now a real chromatic tuner: the engine detects the pitch of the armed input (arm a track first) and the window shows the note, a cents needle and the selected track's string tunings, with the nearest string highlighted. Bindable as "Tuner".
- Bent and whammied notes play on the track's own second (effect) MIDI channel, so a bend no longer detunes the other notes ringing on the same track. The effect channel copies the track's program, volume, pan and bend range, follows mute, solo and volume, is routed to the same engine slot, and is written to exported MIDI files; with no free channel the note stays on the track's channel.
- Score import now reads dynamics (ppp to fff) instead of giving every note the same loudness; forte stays as before, other dynamics play louder or softer. Dynamics are exported to .gp too, and beaming, forced stem direction and score notices are read from the right alphaTab fields.
- Faster undo on large songs (Audit 3 M-06): an edit now stores only the bars it changed instead of a copy of the whole song, and undo/redo rebuild only the bars that differ. Per edit this takes about 7 ms instead of about 115 ms on Blinded, and about 40 ms instead of about 1.5 s on a 40-track, 1,000-bar song; undo and redo steps, the unsaved "*" and the restored track selection work as before.
- .tforge project files are saved gzip-compressed (about 35x smaller); older uncompressed .tforge files still open. A .tforge with invalid text is reported as a damaged file.
- Clean .gp export (for other programs) keeps multi-point bends (reduced to .gp's bend shapes), pick strokes and 8va/15ma octave marks.
- Fretboard / keyboard pane can be drag-resized again once "Lock fretboard size" is unticked (the splitter clamp read stale sizes and undid every drag); the lock is off by default.
- Windows 11 Snap Layouts: hovering the maximise button shows the snap flyout.
- File > New from template and File > Save as template: your own templates are stored in %APPDATA%\TabForge\Templates and listed after the built-in ones. Both commands are bindable (unbound by default).
- A track whose plug-in crashed and was switched off shows a red "!" on its FX button (with a tooltip), not just a colour change.
- Tab-bar clicks and drags are classified by the shared, self-tested hit classifier (buttons, tabs, empty caption).
- Score engraving refinements: no stray rest for an empty second voice, large engraved time-signature numerals on the staff (shown again when it changes back), key signatures with accidentals on the correct lines and naturals when cancelling, and a tempo mark at the start. Toolbar buttons are now reachable with Tab (visible focus); after a mouse click focus returns to the score editor.
- Fretboard / keyboard pane: resizing it scales the whole drawing (strings, frets, markers, labels, legend) from 0.7x to 2x of its natural size, so it always fits and is never cut off; the pane's minimum height is the natural height at 0.7x. The top string's marker and fret label now have room inside the pane (no more overhang onto the menu bar). New "Lock fretboard size" in the fretboard's right-click menu (and bindable `View.LockInstrumentSize`, unbound), off by default: while locked the pane keeps its saved height and its splitter cannot be dragged; untick to resize. The lock and the size are saved in settings. Also a small X in the pane's top-right corner hides it (same as the toolbar button / View > Instrument view). Not yet tried in the running app.
## 0.2.0-beta.1 — 2026-09-29


### Audio engine & plug-ins
- The audio engine stays warm: the active song owns it, switching to a tab or window without plug-ins does not stop it, and the other song's chains stay loaded (silent) for 5 minutes, so switching back reloads nothing and keeps plug-in editors open. It stops after 5 minutes without engine tracks, or on exit.
- Engine liveness: TabForge pings the engine every second and restarts one that stops answering for 5 s outside a plug-in call, without switching any plug-in off. Plug-in calls have their own limits (load / preset 90 s, state save 30 s, editor 20 s, other 10 s), and a call longer than 5 s shows "... is still loading" in the status bar instead of being treated as a crash. Engine and plug-in host processes end with TabForge however it ends. Starting the engine no longer blocks the window.
- A plug-in still loading after 30 s prompts "Keep waiting / Disable it"; disabling skips it for this song only and it loads normally next time.
- Isolated plug-ins: every control command carries a request id, so a late reply is dropped instead of being taken as the next answer; the plug-in process always answers, with a failure status if the plug-in throws. Audio blocks carry a generation number and late blocks are never accepted; a plug-in that misses a deadline is bypassed for that block and only disabled when it hangs for 2 s or misses over 25 % of blocks.
- Real-time safety: a plug-in that outputs NaN / infinity is bypassed on the spot and the status bar says so; audio threads flush denormals; VST2 plug-ins get the song's real time signature, bar start and position (repeats and tempo changes included), so arpeggiators and synced effects follow 3/4, 6/8 and odd meters; VST2 automation from the audio thread no longer allocates; VST2 plug-ins with more than 32 channels are supported. Retired chains, plug-ins and clip players are freed only after the audio callback has let go of them.
- One reference-counted 1 ms timer (held only while playing), a bounded background engine log that rotates instead of being deleted, and audio-health metrics every 10 s (callback timing against its deadline, dropout counts, dropped MIDI).
- DirectSound playback no longer stutters (larger buffer, high-priority refill thread).
- Plug-in trust: scanning and approving record each plug-in's size, time, SHA-256 and signer. A changed plug-in is trusted only with the same signer; otherwise it is not loaded and the Review window says "changed since you approved it". Folder trust applies only to scanned folders under Program Files / Common Files. Plug-ins blocked as untrusted are reported as "blocked, not trusted".
- Automatic pitch matching of VST instruments (on by default): the engine measures silently which octave an instrument sounds at and applies a hidden octave correction; drum tracks are never transposed. New `--pitch-audit` diagnostic.
- FX window: bypassing a plug-in or switching "Through chain" off keeps its docked editor visible (dimmed, red BYPASSED overlay) and never shows a white box; plug-ins stay loaded while bypassed; the General MIDI synth switches on and off live; "GM sound" follows reality, with a new "Auto-switch to GM sound when no VST instrument plays" option (on by default); dragging the FX window moves the editor and overlay together; user presets store and restore each plug-in's Volume.
- New opt-in "Add as a track on startup": a remembered chain is added (never armed) to every song opened or created, and is not saved with the song. Manage it in Settings > Audio & VST > Startup tracks.
- Serial chain: MIDI flows through the plug-in list in order, each slot getting the previous slot's MIDI; per-plug-in wiring options (pass MIDI on, send MIDI output on, instrument output add / replace); "Auto-load for this instrument" saves a chain as the default for that instrument type. Add plug-in can scan the common VST folders, and FX chain `.RfxChain` files import (VST2 state imported, VST3 state not).
- Larger plug-in states (up to 16 MiB), sent one plug-in at a time; a missing or rejected state loads that plug-in with defaults and says so.

### Recording
- Input is captured at the engine's rate when the device allows it, otherwise resampled with a band-limited windowed-sinc resampler (the old linear interpolation aliased).
- Take alignment uses the device-reported input latency; new **Settings > Audio & VST > Recording offset (ms)** shifts takes further (positive = earlier).
- The recorder writes large buffers in chunks and stops cleanly when the disk is full or the drive is removed, keeping finalised files and showing a "Recording problem" message; a failed start no longer leaves files locked.
- Render waits until every chain is confirmed loaded (60 s, then fails naming the missing chains), asks "Render anyway without ..." if a plug-in did not load, and only publishes files it owns (job-owned temp files renamed into place without overwriting; cancel or failure deletes only its own files). Render uses the same graph as playback; stems stay post-fader, pre-bus; reserved Windows names are refused; MP3 encoder start-up is thread-safe.

### Mixer & routing
- Group buses and a master chain: each mixer group has an effects bus, the Master row has FX, pan and volume; chains are saved with the song. Group FX buttons in the track list and mixer open the bus chain.
- Sidechain 3/4 in the Wiring window feeds a source track's post-fader audio into inputs 3/4 of VST2 plug-ins with four or more inputs.
- MIDI output forwarding into another track's chain (loops refused); new bindable commands `Mixer.MasterFx` and `Mixer.GroupFx`.
- Mixer sliders use one fine scale everywhere (volume 0-127, pan -64..+63) and follow the track list without rebuilding.
- .gp4/.gp5 mix-table fades play, including their transition length and "all tracks" flag, so closing fade-outs ramp every track.

### MIDI processing
- Per-plug-in MIDI processors run right before their plug-in, with note-off pairing kept per list. New processors: snap to scale, chords, choke, note sanitizer, hold, repeater, arpeggiator, randomizers, velocity variation, CC / pitch-wheel LFO, step sequencer, plus audio-coupled ones (audio-to-MIDI drum trigger, MIDI EQ ducker, loop sampler, synchronized looper).
- The processing window has whole-list presets, parameter search, grouped parameters and an "Applies to" note selector. Processors reorder by dragging.

### Editing & UI
- Score and timeline selection always match: one shared selection holds the bar range, its track and the loop area, and follows its bars when bars are inserted, deleted or moved. Clicking empty timeline space clears it; clicking the timeline only seeks.
- Saved workspace layouts (View > Layouts): Compose, Practice and Mix (Ctrl+1/2/3), plus save, delete and reset.
- New toolbar button and command `View.InstrumentPanel` show / hide the fretboard / keyboard.
- Fretboard / keyboard panel: it can no longer be made too short to draw fully; its minimum height follows the instrument (about 200 px for 6 strings) and applies to splitter drags, saved layouts, docking and window resizing. The drag is clamped, the fretboard sits lower so the top marker label always fits, and an X button hides the panel. When the window is too short for every pane, a vertical scroll bar appears.
- Keyboard view shows the colour legend again, with dimmer next / upcoming notes.
- The playhead follows what is heard (live clock minus output latency).
- Zooming the score during playback keeps follow-playback on. The tuning-fork button always opens Global tuning, with a new "Shift all by (semitones)" field.
- Text is shaped for each window's own monitor DPI.
- Accessibility: record-arm and FX split buttons are keyboard-operable with focus rings; the knob exposes a range value; mute / solo / bypass states are stated in text, not colour alone; Esc closes the FX and Mixer windows.
- Track list: group headers have record-arm, mute, solo, volume, pan and FX controls; "Auto-resize track list to fit" setting; Track Properties opens the FX chain.
- The FX and MIDI windows release events and timers on close.

### Saving & data safety
- Settings are shared by all windows through one store (debounced, atomic saves, pending save written on exit), so a plug-in approval or hotkey change in one window is no longer reverted by another.
- `.gp` saves cannot damage the previous file: written to a temporary file and swapped in when complete; with "clean .gp + .tfaudio" both are written in full first and rolled back if the second fails. The `.tfaudio` remembers each track's id, so sidechain, MIDI-forward and MIDI-input links survive renamed or reordered tracks, and it notes a `.gp` changed elsewhere.
- A save no longer hides plug-in settings it could not read: the status bar says how many are missing and the song stays marked unsaved. State size is limited consistently everywhere.
- `.tforge` files store every value and a format version (older files still open); presets, FX chains and processor presets are written atomically; TabForge's own stale temp / backup files are cleaned up; chain-state files are checked against their SHA-256 name.
- Saving no longer runs a nested dispatcher frame; closing with "Save" cancels, saves and closes again. Crash-recovery copies are raw project JSON, and after an unexpected error the title says "restart recommended" and saving over a file asks first.
- `.gp` export keeps far more: whammy bar, tremolo picking, trills, tapping, slap / pop, fades, wah, brush and arpeggio strokes, grace notes, fermata, lyrics, key signatures and chord names.

### Security
- A song can no longer make TabForge load native plug-ins from arbitrary paths; only scanned or approved plug-ins load, others are disabled with a "Review..." bar. Network and removable-drive paths always need approval.
- Crash handler waits at most 3 s for a stuck UI thread; shutdown steps are individually guarded; isolated plug-in host processes are always killed on dispose; plug-in info and drum-map files are read with bounds.

### Built-in synth sound
- The built-in synth plays the sound bank as it is, except snares +2.5 dB and crashes / China / splash -4 dB. The "melodic note boost" setting is gone.
- On ASIO, the engine follows Windows volume: endpoint volume, TabForge's own slider in the Windows Volume Mixer, and the measured Windows audio path offset, so it is as loud as the Windows MIDI synth.

### Performance
- The clip disk thread sleeps or parks instead of waking up to 1000 times a second; one shared plug-in factory; the engine's GM synth plays at most 32 voices per track.

### Build & release
- Shared `Directory.Build.props` (single version, warnings as errors, locked restore, NuGet audit, deterministic builds) and `TabForge.sln`; clean scripts cover all projects; test timeouts; release packaging refuses an unprovenanced native bridge and archives PDBs; installer file version follows the app version; CI actions pinned; backups include `Source\docs\` and `.github\`; `SECURITY.md` at the repo root.
- Structure clean-up without behaviour change: recording logic in its own controller, the engine's per-session state in one object, one option table for window options, and an architecture self-test. New self-tests cover the installer's file associations, the control channel and many of the items above; `--areas` limits self-test runs.

### Fixes
- FX window: the option row wraps onto a second line when the window is narrow instead of cutting options off.
- ASIO is released after a failed start; buffer-size detection no longer relies on reflection.
- The loop-recording probe timed passes by wall clock and failed with the loop speed trainer on; it now waits for the recorder's wrap report. Recording itself was correct.

### Known shortfalls in this beta (being worked on)
Two further audits of this version (a clean-up / efficiency review and a measured quality review across a large set of real
score files) found the following. They are known, and fixing them is the work in progress for the next betas:

- Clean-up: stale files and documents, dead code, duplicated helpers, a few CPU / RAM savings, and four "wire up or remove" decisions for unfinished features.
- Data and import: note dynamics are ignored on import; the importer reads 19 properties that do not exist; `.tforge` files are about 190 times larger than they need to be and very large songs cannot be saved; a clean `.gp` export loses bends and other details in most songs; per-edit undo is slow on big songs.
- Notation and playback differences compared with .gp5: bend drawing, stray rests, tremolo speed, strum direction and grace notes.
- Planned features: tuner, autosave, command palette, PDF export and MusicXML.
- Accessibility: keyboard access to toolbar buttons and screen-reader support.
- Earlier audit leftovers: the engine session refactor is only started.

## 0.1.0-beta.6 — 2026-09-29

Resource, security and crash-risk audit (2026-09-29, notes archived). Many items below are compile-checked or self-tested only and have not yet been tried in the running app.

### Audio engine & VST hosting
- **Serial plug-in chain:** MIDI flows through the plug-in list in order. Each plug-in (instrument or effect) gets the MIDI coming out of the previous one, and its own VST2 MIDI output is merged with the pass-through, so MIDI-effect plug-ins such as Chordz shape the notes for the next slot. Instruments add their audio to the incoming audio; effects process everything before them. Offline render uses the same path.
- **Per-plug-in Wiring options (live, no rebuild):** pass incoming MIDI on, send MIDI output on, instrument output add / replace.
- **FX window:** closing a floating plug-in window re-activates the FX window; "Through chain" and "GM sound" share one row; a bypassed plug-in keeps its editor, dimmed with a red BYPASSED label; removing the last instrument plug-in ticks GM sound again; new "MIDI..." button next to Wiring... (lit when processors are on).
- **Auto-load for this instrument:** saves the chain (plug-ins, states, MIDI processors, wiring) as the default for that instrument type, applied when a song loads and to new tracks of that type.

### Mixer & routing
- **Group buses and master chain:** each mixer group has an effects bus (tracks sum into it, then the master). Group FX buttons (track-list headers and mixer group rows) open the bus chain; power bypasses it. New Master row in the mixer: FX over the whole mix, master pan and master volume (in step with the main window's knob). Bus and master chains are saved with the song; old songs load without any.
- **Sidechain:** Sidechain 3/4 in the Wiring window feeds a source track's post-fader audio into inputs 3/4 of VST2 plug-ins with four or more inputs (disabled for VST3, instruments and separate-process mode).
- **MIDI output forwarding:** "MIDI output: forward to" sends a plug-in's MIDI output into another track's chain input; loops are refused. The engine renders sources before destinations.
- New bindable commands `Mixer.MasterFx` and `Mixer.GroupFx`.

### MIDI processing
- **Per-plug-in MIDI processors:** each plug-in's processor list runs right before that plug-in in the serial chain, with note-off pairing kept per list (bypass or removal releases held notes). The General MIDI synth hears the first slot's processed MIDI when that slot is an instrument, otherwise the track's raw MIDI.
- **More processors (MIDI-only):** snap to scale/key, chord (in key / chorderizer), choke, choke group, note sanitizer, note hold, note repeater, arpeggiator, note / modal / scale randomizers, velocity variation, CC and pitch-wheel LFO and a step sequencer, with self-tests per family.
- **Audio-coupled processors:** Audio to MIDI drum trigger, MIDI EQ ducker, Loop sampler (record up to 30 s from a note) and a Super8-style synchronized looper. They see the chain's audio at their position.
- **Processing window:** whole-list presets (save, load, delete; built-ins such as "Drums: GM to kit map" and "Humanize light"); search indexes every parameter and jumps to it with a flash; parameters grouped into Applies to / Main / Advanced; an "Applies to" note selector (all / only these / all except these) with chips, an add box ("36, 38, 40-45, C2") and Learn. Note-offs follow their note-on; old files default to all notes.

### Recording & lanes
- Recorder writes large input buffers in chunks, and stops cleanly when the disk is full or the drive is removed: finalised files are kept and a "Recording problem" message is shown.
- A failed recorder start no longer leaves files locked; the input device is released on start failure or removal.

### Render
- Render restores routing first and cleans temp files; cancelled or failed exports delete partial files.
- MP3 encoder initialisation is now thread-safe.
- Render uses the same graph as playback: track chains, buses, then master; stems stay post-fader, pre-bus.
- Preset and render names can no longer use Windows reserved names (CON, NUL...).

### Editing & UI
- FX and MIDI windows unsubscribe from events and stop timers on close.
- Add plug-in errors are caught instead of crashing.
- Editor parent window handle is validated before use.

### Robustness & security
- Plug-in trust boundary: a song can no longer make TabForge load native plug-ins from arbitrary paths. Only plug-ins from your own scan (or scanned folders) or paths you approve are loaded; others are sent to the engine as disabled, a bar offers "Review…" (Allow selected / Keep disabled), and the FX window shows "Blocked: not approved" with an Allow button. Network and removable-drive paths always need approval. Approvals are kept in Settings (ApprovedPluginPaths).
- Engine watchdog restarts the engine after 10 s when a plug-in hangs during load or state calls, naming the plug-in.
- Shutdown steps are each guarded, so one error no longer skips the rest (ASIO stays unlocked); retired plug-ins are disposed at shutdown.
- Isolated plug-in host processes are always killed on dispose.
- Plug-in state is capped at 8 MB (separate-process mode contains crashes only; it is not a security sandbox); plug-in info and drum-map files are read with bounds.
- Crash handler waits at most 3 s for a stuck UI thread.

### Fixes
- ASIO is released after a failed start; buffer-size detection no longer depends on fragile reflection.

## v0.1.0-beta.5

- **Mixer follows the track list without rebuilding:** volume, pan, pitch and mute/solo changes made in the track list now update the open mixer's sliders in place; strips are rebuilt only when tracks or groups change. Not yet tried in the running app.
- **Render stems checklist:** "Stems: checked tracks" shows a checkbox per track with All / None; the selection is remembered by track name. Not yet tried in the running app.
- **Timeline navigation from clip lanes:** clicking an empty lane or a take now seeks to its bar and selects the track, like clicking the notation rows.
- **FX bypass UI:** toggling either plug-in bypass control closes and reopens the docked editor so its native view is recreated instead of leaving a blank pane.
- **Loop transport button visible again:** stacked the timeline zoom buttons vertically to free room for separate count-in, metronome and loop buttons. F9 still toggles looping.
- **Screenshot tour expanded:** new recording take-lane scene, mixer, render, FX chain, plug-in wiring, MIDI processing and add-plug-in captures. The demo takes are created in memory and removed without saving the song.

- **Render to audio file (File > Render..., Ctrl+Alt+R)**: dialog that renders the song through the audio engine faster than realtime: master mix, stems (selected or all tracks) or both; entire song, selection, selected bars or a custom range; fixed or automatic tail; file-name wildcards ($project $track $tracknumber $date $time $bpm) with silently incremented names; WAV 16/24/32-bit float or MP3 (128-320 kbps CBR, greyed out when Windows lacks the encoder); progress, speed and Cancel. Not yet tried in the running app.
- **Mixer sliders move smoothly and use one scale**: track volume and pan sliders in the mixer no longer jump in 0-16 / -8..8 steps (which looked like 100 to 0 in one move next to the group rows). Tracks now use the same fine scale as the Track Properties knobs and group rows: volume 0-127 (double-click: 100) and pan -64..+63 (double-click: centre), one unit per step, with the mouse wheel moving 1 (Ctrl+wheel 8) without scrolling the window. The main track list's volume and pan sliders (and the volume knob readout) use the same scale.

- **Group header rows in the track list have controls**: aligned with the track columns, each group header now has record-arm (lit when all tracks are armed, half-lit when some are; a click arms or disarms them all), group mute and solo, group volume (0-200 %, 100 = unchanged) and group pan (-64..+63) sliders that behave exactly like the mixer's group rows (and follow the volume / pan column visibility), and an FX button whose effects part is greyed out ("Group effects bus: coming soon") with a power toggle that switches every plug-in chain in the group on or off.

- **Groups in the track list no longer bury the last tracks**: ticking *Groups in track list*, or collapsing / expanding a group, now resizes the arrangement panel to fit every row including the group headers (and shrinks it back when unticked). This whole auto-resize can be switched off with the new persisted setting *Auto-resize track list to fit* (default on): Settings > Timeline > Track controls, right-click on empty space in the track list, or the new unbound hotkey *Auto-resize track list to fit*.

- **Track Properties has an "Effects & instruments (FX)…" button** in the Mixer card that opens the track's FX chain window (same as the FX button in the mixer and the *Track FX chain* hotkey).

- **Recorded clips keep the normal cursor**: hovering the body of a clip now shows the arrow; the resize cursor appears only at a clip's left/right edges and resets when you leave them.

- **Stopping a recording returns to where it began**: Esc, Space or the stop button now end the recording and playback together and put the cursor back at the position where recording started, so Space plays the take from its start. Space/Esc are unchanged when not recording.

- **Wiring window for plug-ins** (FX window > *Wiring…*, or the new hotkeys *Plug-in wiring* / *Plug-in MIDI processing*, unbound by default): the channel-pins box is now a window with the audio input (L+R, mono sum, left, right, swap), a MIDI input source (this track, another track picked by a stable id, or none) with a channel filter (all or 1-16), and greyed-out places for sidechain, MIDI output forwarding and MIDI input configuration that come later. The engine routes the chosen track's MIDI (with the channel filter) to the instrument live, without reloading the chain; songs saved earlier open with the track's own MIDI on all channels. The plug-in role box is gone: the role is always detected from the plug-in.

- **MIDI processing in front of a plug-in** (wiring window > *Configure MIDI input…*, or the *Plug-in MIDI processing* hotkey): a searchable catalog (name, description, keywords) and an ordered list with enable ticks, move up/down and remove, with a parameter panel for the selected processor (knob drags are one undo step). The processors run in the audio engine, sample-accurate, on everything the instrument plays (song, live keys, other tracks routed to it), and are saved with the song and rig presets. This first batch: channel filter / remap (16 rows, solo), channel router (incl. route one note to a channel), note range filter, transpose (with premultiply), note map with drum-map presets (General MIDI, plus vendor maps marked *(unverified)* until checked; your own maps go in `%APPDATA%\TabForge\DrumMaps`), velocity (scale + offset, fixed, compressor, random, mix), humanizer, MIDI delay (ms + beats + samples, "notes only" option), program / bank select and CC sender (on load / playback start / edit), CC mapper, all-notes-off, and a MIDI log that only runs while its window is open. Every note-off leaves with the pitch and channel its note-on used, even if a setting is edited while the note is held; stopping playback, a panic, or changing the shape of the list releases every sounding note. Sysex, generators (arpeggiator, sequencer, LFOs...) and audio-coupled processors come later.
- **Settings search no longer lags while typing**: the search box now waits ~120 ms after the last keystroke before filtering (instead of rebuilding every setting row on each key), and the hotkey search text is built once and cached, so the first keystrokes feel instant. What matches is unchanged.

- **Plug-in tracks stay in time with Windows MIDI tracks over ASIO**: the audio engine now places notes using a smoothed clock (a delay-locked loop) instead of re-reading the time on every ASIO callback, so callback jitter and the true sample rate no longer make drums drift early or late; and the Windows MIDI delay thread now runs at 1 ms timer resolution with a 3 ms precision spin, so delayed notes are no longer up to ~15 ms late.
- **Rewind to beginning (and first/last bar) no longer stops playback**: while the song is playing, jumping to the start (or first/last bar) restarts playback from there and keeps playing; when stopped it just moves the cursor.
- **Adding a second copy of a plug-in no longer freezes the FX chain window, and each copy opens its own editor**: the new plug-in's window opens only once the engine has finished loading it (until then the window says "Starting the plug-in…"), the old docked editor is closed before the add is sent, and the window is no longer rebuilt twice per add. Editors are now matched to plug-ins by slot instead of file path, so a second Superior Drummer 3 no longer shows the first one's editor.
- **"Play the whole song through the audio engine" turns itself on** when a plug-in is added to any track or the audio driver is switched to ASIO (and when a song with plug-ins opens while ASIO is active). It is really ticked in Settings; untick it and it stays off until the next such event. The new tick box *Turn on automatically when a plug-in is added or ASIO is selected* (on by default) switches this off. Older settings that were "automatic" now start ticked.
- **Track volume, pan, mute, solo and the master volume now reach plug-in (VST) tracks live** whether or not the whole song plays through the engine, including while stopped and from the mixer window.
- **Windows MIDI latency setting** (Settings > Audio & VST, default 60 ms): plug-in tracks no longer sound ahead of tracks on the Windows MIDI synth. TabForge now compensates for the synth's own output latency as well as the engine's (Windows MIDI is delayed by the difference, or plug-in tracks are when the synth is slower). Raise it if plug-in tracks are early, lower it if they lag; changes apply live.

### Audio recording and audio clips
- **Clip lanes**: a track can hold several clip lanes. Recording goes to the first lane with room over the whole take (a new lane when something is in the way). Loop recording makes one take per pass, each on its own lane; the newest plays and older takes grey out. Each lane has a play button: click to hear only that lane, Ctrl+click to add or remove lanes. Audio and MIDI takes never grey each other out.
- **Takes are chosen by clicking them**: click a take on the timeline and its lane is the one that plays (Ctrl+click adds another); the greyed takes don't play. The lane selector buttons are gone.
- **Snap button** (magnet, beside the mixer button; Alt+S; right-click for the settings window): clips snap to the grid (bar, 1/2 to 1/32), to the edges of other clips and to the playhead when moved or trimmed. Hold Alt to bypass.
- **Live recording view**: the take grows on its lane while recording, with the input waveform (audio) or the notes (MIDI).
- **Input level meter** on armed tracks (shown whenever the track is armed, recording or not).
- **MIDI recording** (loop laps are counted from the loop wrapping, so playing the same beat on every pass still makes separate takes): choose *MIDI (all inputs)* as a track's input. The notes play live through the track and are recorded as a MIDI clip. Drag a MIDI clip onto a track's notation row, or use *Write into the track's notation*, to turn it into tab (quantised to 16ths; lowest fret on a free string; drums by note number).
- **Clip keys** while a clip is selected: Delete, arrows (move by a beat, Shift = 10 ms, Up/Down = lane), Esc, Ctrl+C / X / V, Ctrl+D (duplicate), Ctrl+M (mute), F2 (properties). All are rebindable in Settings > Hotkeys.
- Clips can be dragged to another lane or another track.
- **Ctrl+R records**. *Repeat selection* moved to Ctrl+Shift+R.
- **Audio device on the status bar**: driver, device, latency, sample rate and buffer. Click it for Settings > Audio & VST.

### ASIO
- **ASIO works end to end.** The audio device item on the status bar shows the driver, latency, sample rate and the real buffer size (e.g. `ASIO: Audient USB Audio ASIO Driver (4 ms · 48 kHz · 32 smp)`), also while the engine is idle (then it shows the chosen driver and that it starts with the first track that needs it). Click it for Settings > Audio & VST.
- **Settings > Audio & VST** now has an ASIO page: the driver list follows the audio driver; *Configure…* opens the driver's own control panel (buffer size, routing); ASIO output first channel; *Enable ASIO inputs* with first and last input channel using the driver's own names (`1: Analogue 1`), so a guitar on input 2 alone is one mono input.
- **Recording and monitoring go through the ASIO driver** (a second WASAPI input next to an exclusive ASIO driver could not work). Other drivers get a *Recording device* setting (it was missing).
- **Play the whole song through the audio engine** (Settings > Audio & VST, off by default): tracks without plug-ins normally play on Windows MIDI, which never uses the audio driver (ASIO does not apply to them and the Windows volume controls them). On: every track is played by TabForge's General MIDI synth through the chosen driver.
- **Buffer size works with ASIO**: it is requested from the driver (inside its limits) and the status bar shows the real size (tested 32, 64, 128, 512). Type any size from 16 to 8192 or pick one from the longer list.
- **ASIO output range** like inputs: first and last channel, one channel = mono output (the mix summed to it). Input and output ranges were tested with a single mono input 2 and output 1.
- **Live monitoring button** on an armed track's lane (speaker icon, on by default): off = the input is still recorded and metered but not played (MIDI input too).
- **Follow the Windows volume** (ASIO, on by default; the check box restarts the output with or without it): ASIO bypasses the Windows mixer, so the volume keys did nothing. When on, TabForge scales its audio by the Windows master volume and mute (a software level; the interface's own knob is not touched).
- The audio thread runs in the Windows "Pro Audio" scheduling class (MMCSS, time critical): fewer dropouts at small buffers.
- Changing a driver, device, channel or rate in Settings reaches a running engine at once.
- Settings rows with a wide editor (device names, a selector plus a button) put the editor under the label instead of squeezing the label into a sliver.

### Settings
- The *Playback* and *Audio* pages are one page, **Playback & sound**, with new icons for it, for *Audio & VST* (faders) and for *Editing* (pencil).

### Plug-ins
- **Plug-ins that crashed the host now load**: VST3 plug-ins such as Druminator and Westwood crashed inside their own bus-arrangement call (TabForge passed an empty list as a null pointer). All 167 plug-ins in the test folders now load in the engine, with no failures, in a total of 57 s (was 123 s, with two failing).
- **Adding a plug-in loads it at once**: the FX chain switches on when you add a plug-in (it used to stay off, so the plug-in never loaded and its window stayed blank, which looked like the plug-in refusing to load).
- **Add plug-in identifies plug-ins in parallel** (several probe processes at once, about 10 s for 88 plug-ins from a cold cache instead of 30 s or more).
- The engine logs how long each plug-in takes to load.
- **Add plug-in: Remember plug-ins** check box: the list is saved and the window opens instantly from it (no scan each time); *Rescan* updates it. Same setting as Settings > Audio & VST.
- Changing a chain (adding, removing, moving or bypassing a plug-in) no longer reloads the plug-ins already running. A sampler plug-in kept losing its preset and stalled for a while when a second plug-in was added; both are fixed. Bypassed plug-ins stay loaded, so switching them back on is instant. Two copies of one plug-in each keep their own settings.
- Add plug-in: click a column header (Name, Vendor, Format, Role) to sort, click again to reverse; the rows use the theme's hover and selection colours (the bright highlight is gone).

### Track list and mixer
- Fixed: turning *Play the whole song through the audio engine* back on (or adding an effect to a track that had none) started the engine's synths on the default piano. Every channel (guitars, basses, keys, drums, anything routed to the engine) now gets its programs, volume and pan again when the engine's chain is ready, including any changes made earlier in the song.
- **Side panel button**: hides instantly (one layout rebuild instead of one per panel) and shows the panel again exactly as it was: same place, tab order, selected tab and sizes.
- **+ Track right-click menu**: colour tracks by group (guitars, basses, drums…), set each group's colour, colour ranges of tracks by hand, show groups in the track list.
- **Groups in the track list** (option in the mixer and the + Track menu): a header per group; collapse or expand it; drag the header to move the whole group.
- Mixer: track volume and pan use the 0-16 / -8..8 scales, as in the track list (volume 0–16, double-click 13; pan −8…+8, double-click centre). Dragging no longer jumps to 0 or 127. Each group has a colour swatch. Check boxes hide the volume or pan column in the track list.
- **Record-arm** button (red) on every track row, where the colour square was (the track colour is now on the track number's right-click menu). Armed tracks monitor the chosen audio input (Input 1, Input 2 or stereo) live through the track's FX chain. Bindable *Arm track for recording* hotkey.
- **Record** button on the transport (bindable *Record* hotkey): starts playback and records every armed track to a WAV file in "<song> Media" beside the song (or Music\TabForge Recordings), placed in time with the song, latency compensated.
- **Audio lanes**: a track with recordings or dropped audio files gets an audio lane under its row, in the track's colour, with a waveform. Drag a clip to move it, drag its edges to trim it; right-click for Duplicate, Copy, Cut, Paste, Delete, Mute and Properties (name, volume, pitch in semitones, speed). Double-click opens Properties. Drop WAV/MP3/AIFF/FLAC/OGG/M4A files on a track (or use *Add audio file…* on the lane) to add them. All edits are undoable and saved with the song.
- **Track colour tint**: track rows and lanes are tinted with the track colour (20% by default, stronger in the dark theme), set in Settings > Appearance and per track in Track properties.

### Mixer
- New **Mixer** button (fader icon beside the tuning fork; bindable *Mixer* hotkey). Tracks are shown as rows grouped by instrument (guitars, basses, keys, drums, other), compact (guitars, basses, drums, everything else) or not grouped. Group rows set level (%), pan and pitch for all their tracks, with group mute and solo; right-click a track to move it to another group. Changes are live, undoable and saved with the song; exporting to .gp folds them into each track.

### VST plug-ins and FX chains
- **VST2 and VST3 hosting** (instruments and effects), with each plug-in's own window, saved state, bypass and wet/dry. The chain runs top to bottom: a VST instrument, then effects in series.
- **FX button** on every track row and mixer row: *FX* opens the chain, the power switch plays the track through the chain or plain Windows MIDI (switching never opens windows).
- **Effects on the MIDI sound**: with effects but no VST instrument, the track's General MIDI sound is rendered by the engine from Windows' own sound bank so the effects can shape it. A *MIDI sound* switch per track (chain window and mixer) turns it off. This synth runs only in that case.
- **Audio output** through WASAPI (shared or exclusive), ASIO or DirectSound, set in the new **Settings > Audio & VST** page (device, sample rate, buffer size). Windows MIDI tracks are delayed by the engine's latency so everything stays in time.
- **Plug-in folders** are added by browsing (Settings or the Add plug-in window); nothing is scanned automatically unless *Also scan the standard VST folders* is on. VST2 DLLs are recognised from their export table without loading them; only 64-bit plug-ins are listed.
- **Crash safety**: plug-ins run in a separate audio engine process, started only when a track needs it. If a plug-in crashes or freezes the audio, TabForge names it, switches it off and restarts the engine without it; playback continues. *Run each plug-in in its own process* (off by default) isolates every plug-in so a crash loses only that plug-in.

### Saving songs with audio settings
- Saving a song that uses plug-ins, FX or mixer groups as `.gp` asks once per song: `.gp` with the audio settings inside (recommended), a clean `.gp` plus a `.tfaudio` file that TabForge re-applies when it opens the `.gp` (most compatible with other programs), or `.tforge`. Songs without these settings save as `.gp` exactly as before.

### Other
- Light theme: the transport bar background now follows the theme's panel colour (it was a fixed near-white).
- FX buttons on unselected track rows now respond to clicks.
- The built-in General MIDI sound was measured against the Windows GS synth through the same output: every instrument tested (piano, guitars, bass, strings, drums) matches within about half a decibel, with the same attack and decay, and no added reverb (the Windows synth adds none).
- Settings > Audio & VST > Output device lists your actual devices (or ASIO drivers).
- The FX button keeps the normal mouse pointer.
- Fixed: songs never sent their notes to plug-ins (VST instruments stayed silent); every open song now routes plug-in tracks to the audio engine. A test plays a song into a sampler plug-in and checks the track's level.
- A track keeps its own instrument (guitar, bass…) when plug-ins are added: its General MIDI instrument and the instrument list no longer turn into "VST: …".

### FX chain window
- Drag plug-ins to reorder; tick to enable / bypass; double-click to float a plug-in's window.
- The selected plug-in's own controls are shown **docked** in the chain window (Options menu or Settings to turn off); floating windows can be kept on top.
- A bar for the selected plug-in: **presets** (the plug-in's own programs plus your saved presets, **+** to save or delete), **channel wiring** (stereo, mono, left, right, swap), **role** (Auto-detected by default, or Instrument / Effect), **wet** knob and bypass.
- **FX menu**: add / remove, **save and load FX chains** (`.tfchain`, kept in your TabForge folder), add a saved chain to the end, clear.
- Roles are detected automatically: VST3 plug-ins say what they are; others are asked once in a throwaway process (a crashing plug-in cannot affect TabForge), and the answer is remembered.

### Add plug-in window
- Columns for **name, vendor, format and role**; type to filter; double-click or Enter adds.
- Scanning shows its **progress**; unknown plug-ins are identified in the background while the window is open.
- **Remember the plug-in list** (Settings > Audio & VST, opt-in): folders are scanned once and remembered; *Rescan* updates them. *Folder settings…* opens those settings; several folders can be added at once.
- The unclear *Add* and *Scan folders* buttons were removed, as was the old preview "VST rack" in the Practice / Mixer panel (a *Track FX* box now opens the FX chain and the mixer).

### Side panel
- A **side panel button** (in the toolbar beside project settings, and the bindable *Show / hide side panel* hotkey) hides the tools, sections and practice panels for more score space, and brings back exactly the ones that were shown.

## v0.1.0-beta.4

Playback timing audit across 1,694 score files (.gp3, .gp4, .gp5, GPX and .gp), with every song's bar order and bar length compared bar by bar against a reference player. Files whose playback timing differed: **304 before, 47 after**. Each of the remaining 47 was checked, and in those cases TabForge follows the notation where the reference player does not (details below). Also new: keyboard scale tools, scale-highlight styles, adjustable fret markers, and opening songs from Explorer as new tabs.

### Tempo
- **A tempo change now lasts until the next one.** Before, some songs fell back to the song's starting tempo after a mix-table tempo change: in Aces High the tempo went up at bar 10 and dropped very low at bar 11. Fixed.
- **Tempo changes partway through a bar** (a mix-table tempo on a later beat) now take effect on that beat. Notes, mix-table points and the playhead follow the new tempo, and later bars continue at it.
- **Several tempo automations on the same beat:** the last one wins. Before, the first one was used.
- **A tempo automation at the very end of a bar** now starts the next bar. Before, it was lost (e.g. To Live Is to Die played long passages at the wrong tempo).
- Tempo changes inside a bar are saved back into .gp files when exporting.

### Repeats and alternate endings (rewritten; compared with score files)
- **Each repeat close plays its own repeat count.** A "x2" close under a "1.2.3." ending plays twice and then moves on to the 4th ending (Aces High, Revelations, I Disappear, Cyanide).
- **Repeats with several closes**, each under its own ending (1st, 2nd, 3rd… each with a close), take each ending in turn (The Call of Ktulu).
- **Endings covering several passes** ("1.2.", "1.3.", "2.4."…) play on every pass they list. Before, only the first listed pass played.
- **An ending bracket covers every bar up to its repeat close.** The last ending (with no close) covers only its own bars.
- **A bar can be both an ending and the start of the next repeat.** It plays as the ending, then repeats with the new section (One, Spit Out the Bone).
- **An ending can start on the repeat's first bar** (Seek & Destroy bass solo).
- **One-bar repeats** (a bar that opens and closes its own repeat) play every pass. Before, they were skipped.
- **Two repeat opens before one close:** playback goes back to the later open.
- **A close without an open** goes back to the most recent open, or to bar 1.
- **Repeats can play up to 99 times.** Before, they were capped at 8 (songs with x11–x14 repeats were cut short). The Repeat close dialog accepts 2–99.

### D.S., D.C., Coda and Fine
- **Jumps from score files are now followed.** D.S., D.C., D.S./D.C. al Coda, al Double Coda, al Fine, To Coda / To Double Coda, Segno, Coda and Fine were imported but ignored during playback, so songs using them played the wrong structure.
- **After a D.S. or D.C.** the music plays straight through, as the notation says: no repeats, and only the last ending of each repeat. At To Coda it jumps to the Coda; it stops at Fine.

### Mix table
- **Volume, pan and effect fades** now start from the current level. Before, each fade jumped back to the track's starting level first.
- Mix-table points in bars with tempo changes are placed by the actual tempo.

### Timing reference notes
- **Incomplete imported bars** (every track's notes end early) still play only as long as their notes.
- **Where TabForge and alphaTab's player differ,** TabForge follows the notation:
  - after D.S./D.C., alphaTab plays 1st and 2nd endings back to back;
  - a few repeat layouts get extra or jumbled passes in alphaTab;
  - in a few files alphaTab holds single bars for 12–240 seconds.
- **New `--audit-timing <folder> <report>` diagnostic** compares bar order, bar length, time signatures, mid-bar tempo changes and mix-table point counts for every score file in a folder.

### Instrument panel: scales and appearance
- **Keyboard view gets the scale tools.** The right-click menu has the same scale items as the fretboard (select scale grouped by key, Find scale…, preview, note names). A highlighted scale is shown on the keys, with its root marked more strongly.
- **Keyboard key colours:** right-click > Appearance > Key colours. "Match the theme" (soft grey keys in dark, white in light; the default), always grey, or always white.
- **Right-click > Scale > Clear selection** removes the highlighted scale. Also bindable as *Clear scale highlight* (no default key).
- **Scale highlight style:** right-click > Appearance > Scale highlight style.
  - *Shaded* (the previous look);
  - *Circles* (small dots on each scale note);
  - *Rings*.
- **Scale highlight colour:** Blue, Green, Amber, Purple, Red, Teal or Grey.
- **Fret markers:** the position dots on the fretboard are slightly brighter by default. Right-click > Appearance > Fret marker colour (Default, White, Silver, Amber, Blue, Green) and Fret marker brightness (Original, Brighter, Bright, Brightest).
- **The Scales window** (Scales button / Tools > Scale finder) has the highlight style and colour choices too, applied at once.
- All appearance choices are saved for every song and TabForge window, and are also in Settings > Fretboard.

### Opening files from Explorer
- **Double-clicking a song in Explorer while TabForge is running** opens it as a new tab in that window instead of starting a second TabForge.
- **Settings > General > Open songs from Explorer in:** "A new tab" (default) or "A new window".
- **How the hand-off is limited:**
  - it uses a local named pipe that only your own Windows account can open;
  - it accepts a single short message;
  - that message must be a full path to an existing file with a song extension (.tforge, .gp, .gp3, .gp4, .gp5, .gpx);
  - the file is opened through the normal import path.

### Testing
- **Self-test:** 1,050 checks, 0 failed. New checks cover:
  - persistent and mid-bar tempo;
  - every repeat and ending layout above;
  - appearance defaults and invalid settings values;
  - the Explorer path filter.
- **Live playback test:** PASS.

## v0.1.0-beta.3

Score import audit: every note of every instrument, and every drum hit, now imports.

### Score import: every drum hit is now imported
A new audit (`--audit-drums <folder> <report>`) compared every track of **1,694 real score files** (.gp3, .gp4, .gp5, GPX and .gp) against the reference reader, note by note, and round-tripped every drum part through TabForge's own `.gp` export. Fixes found by it:
- **`.gp` drum tracks were empty.** Their drum notes point into the track's own articulation list; TabForge read that index as a drum number and discarded every note. Drums in .gp songs now appear and play.
- **GPX (.gpx) drums lost 20–60% of their hits**: The format's extended articulations (half-open hi-hat 92, snare rim shot 91, ride edge 93, splash, crash, bell, percussion…) are now mapped to their General MIDI sounds using the format's own articulation table.
- **.gp3/4/5 drum hits with an unusual value** (e.g. the "0" many older drum tabs use) are kept and shown as written (silent in playback) instead of vanishing — up to 40% of some drum parts.
- **Flams and drags kept their grace note but lost the main hit** on the same drum; a grace note and its principal note are no longer merged as duplicates.
- **Stray bytes before a .gp3/.gp4/.gp5 header** (e.g. a line break added by a download) no longer make the file unreadable.
- **Songs over 1,000 bars in .gp3–5 format** (a fixed limit in the score reader library) now say exactly that, and that saving them as `.gp` or `.gpx` from another program opens them.
- **`.gp` export**: drum parts are written with the format's own articulation definitions (names, staff lines, noteheads), so .gp can read the drums; the round trip keeps every drum sound and count identical.
- **Result**: 1,690 of 1,694 files import every distinct note of every instrument; no file loses notes. Of the rest, 3 were damaged downloads (cut off mid-file) and 5 are the 1,000-bar .gp3–5 songs above; one track keeps 31 of 32 notes.

### Testing
- Self-test: **1,030 checks pass**, 0 fail (1 skipped), including new checks for .gpx/.gp articulation lookup, extended drum ids, the .gp3–5 fret fallback, header junk and the update check; live playback test passes.

## v0.1.0-beta.2

- Fixed .gp5 drum ties with missing destination pitches: inherit the original percussion articulation rather than creating spurious drum-kit hits. Invalid percussion notes no longer inherit a default kit pitch.
- Added regression checks for tied drum notes and percussion articulation precedence, investigated against “Iron Maiden - The Trooper (ver 5).gp5”.

## v0.1.0-beta.1

First beta: a large round of new features, a full audit of every setting and every menu command, a reworked light theme, and many fixes found along the way.

### New: update check (tightly limited)
- TabForge can tell you when a newer release is on GitHub: a theme-matched **Update available** window shows the
  new and current version numbers, **Open download page** (opens the release page in your browser) and **Later**,
  plus a visible **Check for updates automatically** tick box to opt out on the spot.
- **On by default, easy to turn off**: the first setting on **Settings > General** (*Updates*), searchable
  ("update"), and in the update window. **Help > Check for updates…** checks on demand (also bindable).
- **Security limits** — the only network access in TabForge:
  - one anonymous HTTPS GET, at most once a day, ~8 s after start-up, to one fixed address
    (`api.github.com/repos/cobhc95/TabForge/releases`);
  - nothing sent except the required User-Agent `TabForge/<version>`; no cookies, credentials, identifiers or song data;
  - TLS 1.2/1.3 with normal certificate validation, no redirects, no decompression, 10 s timeout, reply capped at
    256 KB (headers 64 KB) and must be JSON;
  - only `tag_name` and `draft` are read; tags must be plain versions (strict, time-limited pattern); nothing from the
    reply is displayed, opened or saved;
  - the download page is built locally for the validated version on `github.com/cobhc95/TabForge` and opens only when
    you click; TabForge never downloads or installs anything;
  - offline / blocked networks stay silent; switched off, nothing runs; tests, probes and screenshot runs never go online.
- 15 new self-tests cover version ordering (alpha < beta < release, beta.10 > beta.9), reply parsing (drafts,
  malformed tags and non-JSON ignored, foreign links never used), the locally built page and the setting's placement.

### New: Add track window
- The **+ Track** button opens an **Add track** window: the same window as Track properties, for the new track — instrument catalogue with pictures and search (*Change…*), MIDI program and channel, volume/pan knobs, tuning editor with presets, frets, capo, name, colour, performer and notes.
- **Position**: add it last (default), first, after the selected track, or as any track number you type.
- The new track takes the type of the instrument you pick (drums, bass, keys or guitar), and its name follows the instrument unless you type your own. Cancel adds nothing.
- Track > Add Guitar / Bass / Drums / Keys still add a default track instantly.

### New: instrument panel that matches the instrument
- Out of the box the panel shows what suits each track: a **fretboard with the track's own strings** for stringed instruments (6-string guitar → 6 strings, 4- or 5-string bass → 4 or 5, also oud, cello, violin, viola, ukulele, mandolin, banjo, sitar, harp, koto… recognised by name or General MIDI program), **drum pads** for drum tracks, and a **keyboard** for piano, organ, flute, brass, synths and everything else.
- **Keyboard sizes**: 88 keys (full piano, default), 76, 61, 49, 37 or 25; smaller keyboards shift by octaves to keep the sounding notes in view. Right-click the panel (when a keyboard is shown) or Settings > Fretboard > *Keyboard size*.
- Right-click the panel: **Show this track as** (one track) or **Show all tracks as** (every track) — Match the instrument, Fretboard, Keyboard or Drums. Settings > Fretboard > *Default instrument view* sets the default.
- Bindable command *Switch instrument view* (Settings > Hotkeys; no default key).

### New: scale finder
- **Find scale**: analyses the notes of the **selection** (made in the score or on the timeline) or the **entire song**, for the selected track or all tracks, and lists every key and scale they fit, best first — "100% of the notes fit · every scale note is played" — because several scales usually match. Notes are weighted by length; drums and dead notes are ignored. On *Blinded* it suggests C Natural Minor first (Drop C).
- **All scales**: pick any key and scale directly, without searching.
- *Show on fretboard* highlights the chosen scale; *Clear highlight* removes it.
- Open it from the new **Scales** button beside the fretboard (under the now / next / upcoming / recent legend), the fretboard right-click menu (*Scale > Select scale / Find scale…*), Tools > Scale finder…, or a bindable hotkey.
- The window follows the theme, like every other dialog.

### Light theme and theme presets
- **Dark, Light and System are now presets**: choosing one sets the interface, accent, text, icons and score page together; any colour can still be changed on its own afterwards. Custom keeps your colours as they are.
- The **light theme is a soft grey** instead of near-white: grey chrome and panels, darker workspace, a soft grey score page with slightly darker staff lines.
- **Wood fretboard** in the light theme (dark rosewood neck, silver frets, pale strings, bone nut), like a real instrument.
- **Readable icons everywhere**: pale icon art turns charcoal on light backgrounds and coloured icons use a deeper shade; the tool palette and Settings sidebar icons are clearly visible; icons redraw when the theme changes.
- **Transport buttons** (play, stop, skip, count-in, metronome, loop) use clean pastel tints of their own colour with deeper icons and borders, and a softer glow when active; the count-in icon no longer draws a dark square.
- **Track controls** in light mode: lighter mute / solo buttons, readable solo "S", lighter slider tracks; scroll bar thumbs follow the theme.
- **Contrast fixes**: the Settings OK button has white text again; the automatic readability guard now fixes any text below 4:1 contrast (was 2.2:1).

### Settings audit — every setting checked
- New self-test for **every row in Settings**: it stores the value it is given, survives save / reload / validation, and changing it never changes a different setting.
- New live probe (`--probe-settings`) that changes each setting in the running app and compares screenshots, plus playback and selection checks for the settings that only show while playing or selecting.
- Fixed settings that did nothing or only applied after a restart:
  - **UI scale** only ever applied once (a frozen transform made every later change fail silently) — it now scales the whole window live.
  - **Tab shape, close-button mode** and **tab title size** never updated tabs that were already open.
  - **Default score display** only applied when the window was resized; it now also switches the open tab (like the View menu) and no longer overrides a tab's own display on unrelated changes.
  - **Scale highlight, fretboard frets, left-handed, note names and preview settings** redraw the fretboard immediately.
  - **Same colour for similar sections** recolours the sections immediately.
  - **Open projects in the current tab** was never used (Ctrl+O always replaced the tab); Ctrl+O now follows it, Ctrl+Shift+O always opens a new tab.
- The playback-line colour and thickness settings now say they apply to the score's playback line.
- Test probes never write your settings file.

### Menu audit — every command checked
- A headless probe (`--probe-menus`) clicks all 142 main-menu items through their real handlers on a copy of a song: dialogs are photographed and cancelled, Windows file / print dialogs and message boxes are closed automatically, and every edit is checked to be fully reversed by Undo. Result: 0 errors; every item does its job.
- Fixes found by it:
  - **Tools > Scale finder** now opens the new scale finder (it used to update a hidden panel).
  - **Sound > MIDI / Audio setup** now opens the Practice / Mixer panel at the track outputs.
  - **Note > Shift pitch down** on an open string moves the note to the same pitch on the next lower free string instead of doing nothing.
  - **Ticked menu items** no longer shift their text out of line with the rest of the menu.

### Other fixes and changes
- **Maximised window**: the window now fills exactly the screen area above the taskbar — the status bar and timeline scroll bar are no longer hidden under the taskbar, and the tabs and the minimise / maximise / close buttons reach the very screen edges and corners (throw the mouse into the corner to hit Close), on every monitor and DPI.
- **Count-in**: right-clicking the count-in button opens its settings again (volume, length, sound, song start / every section / every loop); count-in playback verified with a new probe.
- **Section hover tooltips** on the timeline appear again (explaining drag, Ctrl+drag, edge resize and right-click) without affecting section dragging.
- **Status bar**: the unused "string N" is gone (Measure · track · cell).
- **Version**: 0.1.0-beta.1 in the window, About, Settings, installer and file names.

### Documentation
- README: refreshed screenshots, light-theme example, and the new features (Add track, instrument views, scale finder, keyboard sizes).
- TOOLS_AND_HOTKEYS.md: Add track, instrument panel and scale finder sections; new commands *Switch instrument view* and *Scale finder*.

### Testing
- Self-test: **1,022 checks pass**, 0 fail (1 skipped); live playback test passes (max latency ≈ 0.3 ms).

## v0.1.0-alpha.6

- Continuous horizontal score layout centers the one-line score vertically in the available score area.
- Smooth playback follow is the default for fresh settings; existing saved follow choices remain unchanged.
- New tabs and opened songs inherit the last chosen horizontal/vertical scrolling and continuous/page layout preferences, while already-open tabs keep their own view state.

## v0.1.0-alpha.5

### Display and navigation
- Choose horizontal score scrolling (one continuous line) or vertical score scrolling (wrapped lines); playback follow supports smooth page turns and configurable lookahead.
- New documents use Continuous (seamless) score layout by default; existing document layout choices remain preserved.
- Loop wraps restart visual playhead reporting so the score caret, follow scrolling and arrangement continue tracking every pass. Loop-start attacks are no longer skipped after scheduler wake-up overshoot, and channel-state restoration scans only the relevant loop interval to avoid needless work and audible pauses.
- Zoom/reflow is treated as a layout change, not a manual scroll, and playback follow checks the playhead independently of score repaint cadence.
- Drag-selecting bars in the score mirrors the highlighted range in the timeline; selecting a range in the timeline mirrors it back to the score, and ordinary clicks clear both selections.
- Section-button hover tooltips explain plain marker dragging, Ctrl+drag with bars and edge dragging to resize.
- Maximised windows now use WPF's native WindowChrome work-area and DPI handling, removing the bottom/right inset and keeping the caption buttons at the screen edge.
- Fretboard sizing and score follow are improved for more display/layout configurations.
- Continuous score layout reflows to the current window width after resizing or maximizing; horizontal and vertical playback follow recover promptly after layout changes.
- The timeline fits the song's track count on opening, switching songs, adding or deleting tracks, and undo/redo. On shorter windows the score yields height before timeline tracks are clipped.
- Score selections set precise loop start/end cells, including selections beginning mid-bar; timeline selections keep whole-bar loop bounds. Toggling the loop button retains the chosen range and the selection in both views.
- Escape clears score and timeline selections together, regardless of which view created the range.

### Editing and workflow
- Editing an existing fret no longer moves the cursor to the next beat; new note entry still auto-advances when enabled.
- Section markers can be moved independently of their bars, with Ctrl+drag retaining block movement; add sections with M or from the section lane.
- Tooltips expose hotkeys, and checkbox/global-tuning dialogs are clearer.

### Reliability
- Fixed a crash when dragging panel or document tabs while the layout is rebuilding.

## v0.1.0-alpha.4

### Downloads
- Portable zip (extract anywhere) and a Windows installer; the installer can optionally make TabForge open .gp/.gp5/.gp4/.gp3/.gpx/.tforge files. The same switch is in Settings > General > Windows integration.
- The version is shown in Help > About and at the bottom of Settings.

### Files and instruments
- Save as .gp (now the default) with every TabForge setting kept inside the file.
- `.gpx` tracks keep their real instrument (e.g. Tin Whistle / Acoustic Piano programs, Mandolin, Violin) instead of being forced to guitar or bass; instruments without strings show real note numbers instead of rows of zeros.
- Extended-range basses and guitars are named with their string count ("Electric Bass (Finger) (5 strings)"); tuning presets for 5/6/7-string bass and 7/8/9-string guitar.

### Editing
- `+` / `-` follow a common tab-editor convention (+ = shorter), reversible in Settings; notes can be lengthened even past the bar end (the bar turns red), optional strict mode.
- Resize sections by dragging their edges; Check Bars (F4) now agrees with the red bars.
- Dragging a section moves it as a block into free bars only (same length, leaves a gap, no track animation); Ctrl+drag keeps the old behaviour and moves the section with its bars. Add a section with M, the Sections panel or right-click on the section lane.
- Tooltips show their hotkey in brackets (e.g. "Add new section (M)") and no longer get stuck on the side-panel icons.
- Fewer accidental selections: a range needs a deliberate drag. Esc clears a selected area but keeps the loop on.

### Playback and display
- Changing settings while playing no longer stalls playback; score follow keeps following after layout changes.
- Horizontal score scrolling (one line that scrolls/follows right) and a smooth page-turn follow style, both on the score's right-click menu.
- Loop without a selection loops the whole song without highlighting every bar.
- Section list colours match the timeline; long beat text no longer stretches a bar across the line; duration glow defaults to off.

### Reliability
- Crash safety: an unexpected error is logged and unsaved songs are copied to %LOCALAPPDATA%\TabForge\Recovery.
- Security: crafted .gp files can no longer exhaust memory; stricter error handling throughout.
- Fixed a crash when dragging panel tabs (e.g. Practice / Mixer) or document tabs while the layout was being rebuilt.
## v0.1.0-alpha.3

### Performance
- Much lower CPU while dragging tracks, sections and selections: lanes slide as retained GPU layers, text is shaped once and replayed, and selection/loop overlays redraw on their own layer.
- Lower memory: JSON snapshots no longer retain large pooled buffers; undo snapshots are taken in the background.
- Dropping a track during playback no longer pauses or rewinds (seamless arrangement swap at the next bar).

### Playback, loop and metronome
- Seamless looping (no gap at the wrap); score and timeline follow the loop.
- Loop settings: number of loops (or infinite) with a loops-left badge, count-in before every loop, speed trainer.
- Timeline area selection (drag across bars): copy, cut, paste, move (click to drop), delete, loop, skip during playback.
- Section context menu: "Loop section" toggles with a tick.
- Boosted (layered) metronome, count-in popup with its own volume (default 70 %), length and sound.
- Master volume always controls everything (tracks, metronome, note preview).

### Mixer and tracks
- Volume (0–16) and pan (−8…+8) sliders with the value on the handle; double-click resets; slider or knob per control in Settings.
- Global tuning button: tuning-fork icon opens a preset / per-string global tuning window; click the number to type a shift, double-click to edit inline, right-click for quick options.
- Full General MIDI instrument catalogue (128 programs + 9 drum kits) with vector badges tinted by the track colour; instrument menu (family on top, all families as submenus) and a searchable picture catalogue in Track properties. Drum kits switch the track to channel 10.

### Appearance
- Light theme reworked to neutral greys across the whole app (menus, timeline, fretboard, score paper, settings, title bars, popups); theme changes now reach every control.
- UI scale and density scale the entire window, popups and menus.
- Score text & fonts per area (fret numbers, techniques, chords, lyrics, bar info, header): font, size, bold, italic, colour, outline.
- Colour chooser with presets, swatches, hue/saturation/brightness and hex for playback colours.

### Fixed (engine technique audit)
- **Audio engine instruments were out of tune** (electric guitars up to 0.8 semitone sharp on high notes, e.g. bar 112 of Blinded): the sound bank's per-sample fine tune was applied with the wrong sign. Mean error over all instruments 0.26 → 0.07 semitone.
- A **Fade out** note left the channel's expression at 0, so every later note on that track was silent (and the fade was a hard cut). It is now a ramp and the level is restored when the note ends.
- **Wah open / close** left the mod wheel up, so later notes kept its vibrato; it now resets when the note ends.
- Docked plug-in editors are owned popups (menus stay open), clipped to the FX window; the floating editor no longer shows a title-bar icon.
- New diagnostics: `--probe-gm`, `--audit-gm <song>`, `--audit-gm-techniques` (measure pitch of every note through the engine's GM synth).
