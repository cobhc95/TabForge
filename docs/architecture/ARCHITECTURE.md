# Architecture and source map

## Layers and processes

- **Audio.Contracts** owns engine and render IPC records, message limits and shared memory. It has no app project dependency.
- **Core** references Audio.Contracts and owns models, editing, playback, formats, validation, and framework-neutral platform ports. It never references Avalonia.
- **AudioEngine** references Core and Audio.Contracts. The app starts it for audio work. Its device callback, native VST bridge and optional plug-in hosts have separate lifetimes.
- **Avalonia** references Core and AudioEngine. Program selects the process mode. App owns shared services. MainWindow composes per-window UI. Keep drawing and views out of musical timing and serialization.
- **Tests** include a separate xUnit project and an in-app headless suite. Read [Testing](../guides/TESTING.md) before running either.

## Folder owners

Paths are in the repository checkout. The list covers folders with C# or Avalonia markup. Assets, native sources and build tooling have separate lifecycles.

### TabForge.Audio.Contracts

| Folder | Owner |
| --- | --- |
| `Source/src/TabForge.Audio.Contracts/` | Engine and render messages, frame and shared-block layouts |
| `Source/src/TabForge.Audio.Contracts/Builtin/` | Parameter IDs shared between engine DSP and UI |
| `Source/src/TabForge.Audio.Contracts/Platform/Linux/` | Linux process and IPC support |
| `Source/src/TabForge.Audio.Contracts/Platform/Portable/` | Portable fallbacks |
| `Source/src/TabForge.Audio.Contracts/Platform/Windows/` | Windows identity and timer support |

### TabForge.AudioEngine

| Folder | Owner |
| --- | --- |
| `Source/src/TabForge.AudioEngine/Audio/` | Capture, clip audio, resampling and render sinks |
| `Source/src/TabForge.AudioEngine/Builtin/` | Built-in effect construction and parameters |
| `Source/src/TabForge.AudioEngine/Builtin/Compressor/` | Dynamics compressor |
| `Source/src/TabForge.AudioEngine/Builtin/Delay/` | Delay processor |
| `Source/src/TabForge.AudioEngine/Builtin/Dsp/` | Shared DSP primitives |
| `Source/src/TabForge.AudioEngine/Builtin/Eq/` | Equalizer |
| `Source/src/TabForge.AudioEngine/Builtin/Gate/` | Noise gate |
| `Source/src/TabForge.AudioEngine/Builtin/IrLoader/` | Impulse-response processing |
| `Source/src/TabForge.AudioEngine/Builtin/Limiter/` | Peak limiter |
| `Source/src/TabForge.AudioEngine/Builtin/Reverb/` | Reverb |
| `Source/src/TabForge.AudioEngine/Editors/` | Native plug-in editor handles and windows |
| `Source/src/TabForge.AudioEngine/Host/` | Engine launch modes, framing and command dispatch |
| `Source/src/TabForge.AudioEngine/Isolation/` | Per-plug-in host lifecycle and bounded callback exchanges |
| `Source/src/TabForge.AudioEngine/Mixing/` | Mixer, callback epochs, taps and retirement |
| `Source/src/TabForge.AudioEngine/Output/` | Audio device adapters and output |
| `Source/src/TabForge.AudioEngine/Plugins/` | Plug-in loaders, instances and chain runtime |
| `Source/src/TabForge.AudioEngine/Plugins/Clap/` | CLAP hosting |
| `Source/src/TabForge.AudioEngine/Session/` | Engine session state, devices, plug-ins and recording |
| `Source/src/TabForge.AudioEngine/Synth/` | Built-in General MIDI and SoundFont synthesis |

### TabForge.Avalonia

| Folder | Owner |
| --- | --- |
| `Source/src/TabForge.Avalonia/App/` | Program modes, composition root and application lifetime |
| `Source/src/TabForge.Avalonia/Autosave/` | Window recovery, scheduling and notices |
| `Source/src/TabForge.Avalonia/Capture/` | Screenshot and tutorial capture entry points |
| `Source/src/TabForge.Avalonia/Commands/` | Registration, state and dispatch contracts |
| `Source/src/TabForge.Avalonia/Diagnostics/` | App diagnostics and traces |
| `Source/src/TabForge.Avalonia/Features/` | Ordered window module registration |
| `Source/src/TabForge.Avalonia/Features/Band/` | Band cards, layout, follow, menus and window wiring |
| `Source/src/TabForge.Avalonia/Features/Help/` | Help shell, chapters, reader, search and command registration |
| `Source/src/TabForge.Avalonia/Features/KeyboardMode/` | Practice engine, dock pane, keyboard and pop-out |
| `Source/src/TabForge.Avalonia/Features/LeftStrip/` | Quick strip and Composer UI |
| `Source/src/TabForge.Avalonia/Features/LiveInput/` | Monitoring UI, live strip and tuner |
| `Source/src/TabForge.Avalonia/Features/Lyrics/` | Dock registration, song editor and per-bar editor |
| `Source/src/TabForge.Avalonia/Features/Recording/Presenter/` | Record commands and live take lifecycle |
| `Source/src/TabForge.Avalonia/Features/Recording/View/` | Arm controls, input picker and take overlays |
| `Source/src/TabForge.Avalonia/Features/Video/` | Live video, region capture and export UI |
| `Source/src/TabForge.Avalonia/MainWindow/` | Window construction, tabs and document panes |
| `Source/src/TabForge.Avalonia/MainWindow/Clips/` | File drop, lanes and clip operations |
| `Source/src/TabForge.Avalonia/MainWindow/Editing/` | Edit routing, clipboard and selections |
| `Source/src/TabForge.Avalonia/MainWindow/File/` | Open/save, import/export and media collection |
| `Source/src/TabForge.Avalonia/MainWindow/Menus/` | Menu construction and command routing |
| `Source/src/TabForge.Avalonia/MainWindow/Timeline/` | Timeline gestures, sections and zoom commands |
| `Source/src/TabForge.Avalonia/MainWindow/Tools/` | Tool window registrations that still live in the shell |
| `Source/src/TabForge.Avalonia/MainWindow/Tracks/` | Track actions, selection, and re-record commands |
| `Source/src/TabForge.Avalonia/MainWindow/Transport/` | Window-level transport and playback commands |
| `Source/src/TabForge.Avalonia/Metrics/` | Window sizing, density and layout dimensions |
| `Source/src/TabForge.Avalonia/Platform/` | Score, timeline, instrument drawings and Avalonia adapters |
| `Source/src/TabForge.Avalonia/Platform/Controls/` | Reusable controls, chrome, menus and icon primitives |
| `Source/src/TabForge.Avalonia/Platform/DragDrop/` | Shared URI parsing and clip drop preview |
| `Source/src/TabForge.Avalonia/Platform/Linux/` | Linux display and X11 drag/drop integration |
| `Source/src/TabForge.Avalonia/Platform/Windows/` | Windows UI integration |
| `Source/src/TabForge.Avalonia/Playback/` | Process-wide output, engine client and tab switching policy |
| `Source/src/TabForge.Avalonia/SelfTests/` | Headless test runner, dispatch and reporting |
| `Source/src/TabForge.Avalonia/SelfTests/Architecture/` | Layering and file-size checks |
| `Source/src/TabForge.Avalonia/SelfTests/Engine/` | Engine protocol and processing checks |
| `Source/src/TabForge.Avalonia/SelfTests/Hygiene/` | Project, docs and release checks |
| `Source/src/TabForge.Avalonia/SelfTests/Release/Dialogs/` | Dialog checks |
| `Source/src/TabForge.Avalonia/SelfTests/Release/Engine/` | Release engine coverage |
| `Source/src/TabForge.Avalonia/SelfTests/Release/Fuzz/` | Malformed-input coverage |
| `Source/src/TabForge.Avalonia/SelfTests/Release/GuitarPro/` | Guitar Pro fixtures and round trips |
| `Source/src/TabForge.Avalonia/SelfTests/Release/Persistence/` | Save and recovery checks |
| `Source/src/TabForge.Avalonia/SelfTests/Release/Playback/` | Transport and playback checks |
| `Source/src/TabForge.Avalonia/SelfTests/Release/Recording/` | Recording and media checks |
| `Source/src/TabForge.Avalonia/SelfTests/Release/Settings/` | Settings checks |
| `Source/src/TabForge.Avalonia/Settings/` | Saved values applied to the host and shared views |
| `Source/src/TabForge.Avalonia/Shell/` | Window sizing, startup and OS integration |
| `Source/src/TabForge.Avalonia/Shell/Win32/` | Windows hooks and native editor integration |
| `Source/src/TabForge.Avalonia/Transport/` | Per-document playback session, frame clock, routing and events |
| `Source/src/TabForge.Avalonia/Views/` | Shared dialogs, arrangement composition and UI areas awaiting a feature owner |
| `Source/src/TabForge.Avalonia/Views/Articulations/` | Articulation editor controls |
| `Source/src/TabForge.Avalonia/Views/Builtin/` | Shared built-in effect view controls |
| `Source/src/TabForge.Avalonia/Views/Builtin/Compressor/` | Compressor editor |
| `Source/src/TabForge.Avalonia/Views/Builtin/Delay/` | Delay editor |
| `Source/src/TabForge.Avalonia/Views/Builtin/Eq/` | Equalizer editor |
| `Source/src/TabForge.Avalonia/Views/Builtin/Gate/` | Gate editor |
| `Source/src/TabForge.Avalonia/Views/Builtin/Limiter/` | Limiter editor |
| `Source/src/TabForge.Avalonia/Views/Dialogs/` | Reusable dialogs and prompt infrastructure |
| `Source/src/TabForge.Avalonia/Views/Dock/` | Pane factories, controller and drag/drop |
| `Source/src/TabForge.Avalonia/Views/Fx/` | FX chain UI and preset integration |
| `Source/src/TabForge.Avalonia/Views/Fx/ActiveOn/` | Conditional plug-in activation UI |
| `Source/src/TabForge.Avalonia/Views/Meters/` | Shared metering UI and tick registration |
| `Source/src/TabForge.Avalonia/Views/Midi/` | MIDI controls and processor editing |
| `Source/src/TabForge.Avalonia/Views/NoteMaps/` | Note and instrument mapping UI |
| `Source/src/TabForge.Avalonia/Views/PianoRoll/` | Grid, rulers, keyboard, lanes and edit gestures |
| `Source/src/TabForge.Avalonia/Views/Presets/` | Preset manager, catalogue UI and drag/drop |
| `Source/src/TabForge.Avalonia/Views/Scales/` | Scale selection and shared panel hosts |
| `Source/src/TabForge.Avalonia/Views/TransportPopups/` | Loop, count-in, metronome and transport popups |

### TabForge.Core

| Folder | Owner |
| --- | --- |
| `Source/src/TabForge.Core/Audio/` | Engine client, routing, mixing rules and shared MIDI output |
| `Source/src/TabForge.Core/Audio/ActiveOn/` | Live plug-in activation rules |
| `Source/src/TabForge.Core/Audio/Metering/` | Shared input and master meter scales and settings |
| `Source/src/TabForge.Core/Autosave/` | Recovery data, scheduling rules and cleanup |
| `Source/src/TabForge.Core/Capture/` | Framework-neutral capture contracts |
| `Source/src/TabForge.Core/Controllers/` | Document/score editing controllers |
| `Source/src/TabForge.Core/Diagnostics/` | Diagnostics ports and probes |
| `Source/src/TabForge.Core/Docking/` | Dock data and pane catalogue |
| `Source/src/TabForge.Core/Documents/` | Song lifetime, edits, undo and per-document state |
| `Source/src/TabForge.Core/Features/Band/` | Band rules and Preferences slot |
| `Source/src/TabForge.Core/Features/Help/` | Help document model and search |
| `Source/src/TabForge.Core/Features/KeyboardMode/` | Practice timing, judging, geometry and settings |
| `Source/src/TabForge.Core/Features/KeyboardMode/Hands/` | Left/right hand assignment |
| `Source/src/TabForge.Core/Features/LeftStrip/` | Quick strip and Composer state |
| `Source/src/TabForge.Core/Features/LiveInput/` | Monitor session, MIDI routing, peak hold and tuner rules |
| `Source/src/TabForge.Core/Features/Lyrics/` | Lyrics operations |
| `Source/src/TabForge.Core/Features/Recording/Model/` | Recorded takes and take-group edits |
| `Source/src/TabForge.Core/Features/Recording/Rules/` | Record lifecycle, loop passes and MIDI take conversion |
| `Source/src/TabForge.Core/Features/TuxGuitar/` | TuxGuitar format import and export |
| `Source/src/TabForge.Core/Features/Video/` | Capture, timing, encoding, export and settings |
| `Source/src/TabForge.Core/Menus/` | Framework-neutral menu rows and command IDs |
| `Source/src/TabForge.Core/Midi/` | MIDI inputs, messages and routing rules |
| `Source/src/TabForge.Core/Midi/Processors/` | Processor catalogue, payloads and pure processing |
| `Source/src/TabForge.Core/Models/` | Song, notation, clips, settings values and mixer data |
| `Source/src/TabForge.Core/Platform/` | UI, drawing and OS abstraction ports |
| `Source/src/TabForge.Core/Platform/Contracts/` | Platform-facing service contracts |
| `Source/src/TabForge.Core/Platform/Controls/` | Shared control sizing and rules |
| `Source/src/TabForge.Core/Platform/Portable/` | Non-Windows implementations |
| `Source/src/TabForge.Core/Platform/Testing/` | Test adapter ports |
| `Source/src/TabForge.Core/Platform/Theme/` | Palette, style tokens and theme data |
| `Source/src/TabForge.Core/Platform/Windows/` | Windows-specific implementations and COM |
| `Source/src/TabForge.Core/Playback/` | Compiler, performed timeline, scheduler and MIDI routing capabilities |
| `Source/src/TabForge.Core/Playback/Articulations/` | Played note and technique transforms |
| `Source/src/TabForge.Core/Playback/Models/` | Compiled timeline and clock data |
| `Source/src/TabForge.Core/Plugins/` | Plug-in catalogue, scanning, trust and state |
| `Source/src/TabForge.Core/Plugins/ActiveOn/` | Plug-in activation data |
| `Source/src/TabForge.Core/Ports/` | Framework-neutral host interfaces |
| `Source/src/TabForge.Core/Presenters/` | Shared view state, rules and commands |
| `Source/src/TabForge.Core/Presenters/PianoRoll/` | Piano roll editing, layout and data projections |
| `Source/src/TabForge.Core/Presets/` | Preset stores, groups and catalogue |
| `Source/src/TabForge.Core/Presets/FullDemoSong/` | Built-in demo song |
| `Source/src/TabForge.Core/Rendering/` | Offline render orchestration |
| `Source/src/TabForge.Core/Score/` | Notation layout and rendering |
| `Source/src/TabForge.Core/Score/Editing/` | Score edit rules and handlers |
| `Source/src/TabForge.Core/Score/Effects/` | Effect-specific notation editing |
| `Source/src/TabForge.Core/Services/App/` | Settings-independent application services and tutorial parsing |
| `Source/src/TabForge.Core/Services/Audio/` | Import and audio-file rules |
| `Source/src/TabForge.Core/Services/Bars/` | Written bar operations |
| `Source/src/TabForge.Core/Services/Clips/` | Clip editing and lane rules |
| `Source/src/TabForge.Core/Services/Composer/` | Composer domain operations |
| `Source/src/TabForge.Core/Services/Editing/` | Selections, clipboard and edit flow |
| `Source/src/TabForge.Core/Services/Export/` | Format writing and export dispatch |
| `Source/src/TabForge.Core/Services/Export/LegacyGp/` | Guitar Pro legacy formats |
| `Source/src/TabForge.Core/Services/Features/` | Settings contribution and composition contracts |
| `Source/src/TabForge.Core/Services/Files/` | Input limits, path policy and safe writes |
| `Source/src/TabForge.Core/Services/Hotkeys/` | Command key catalogue and bindings |
| `Source/src/TabForge.Core/Services/Import/` | File routes and format readers |
| `Source/src/TabForge.Core/Services/Instruments/` | Sound banks, instruments and mappings |
| `Source/src/TabForge.Core/Services/Log/` | Trace routing and logging |
| `Source/src/TabForge.Core/Services/Media/` | Per-document media resolution, access and collection |
| `Source/src/TabForge.Core/Services/Midi/` | MIDI I/O and file interchange |
| `Source/src/TabForge.Core/Services/Music/` | Music theory, timing and scales |
| `Source/src/TabForge.Core/Services/Project/` | Project persistence and properties |
| `Source/src/TabForge.Core/Services/Reset/` | Coordinated reset policies |
| `Source/src/TabForge.Core/Services/Sections/` | Section markers and edit rules |
| `Source/src/TabForge.Core/Services/Settings/` | Settings schema, migrations, validation and catalogue |
| `Source/src/TabForge.Core/Services/SoundFont/` | SoundFont discovery and download |
| `Source/src/TabForge.Core/Services/Timeline/` | Timeline selection and placement rules |
| `Source/src/TabForge.Core/Services/Tracks/` | Track creation, conversion and track edits |
| `Source/src/TabForge.Core/Shell/` | Host-independent lifecycle and command-line rules |
| `Source/src/TabForge.Core/Text/` | Shared text and language resources |
| `Source/src/TabForge.Core/Views/EffectEditors/` | Framework-neutral effect editor logic |

## Master lists and extension points

| Need | Start here |
| --- | --- |
| Command, shortcut, menu, palette | CommandIds and TimelineMenus own shared IDs. HotkeyCatalog owns bindings. MainMenuTable owns main-menu rows. PaletteToolCatalog and ContextMenuRows reuse the registered IDs. The checkout also has a generated command map and a tool registration guide. |
| Feature module or pane | Put domain rules and views under the feature in the owning project. Add ordered registration to WindowFeatureModules. Register pane IDs and layout defaults in DockPaneTable and factories in DockPaneSetup. Use RegisterShared for one control per window. Get active document and playback through narrow providers. |
| Preferences page or setting | AppSettings owns persisted slots. Use IFeatureSettingsSource for rows, validation, page placement and defaults. CatalogComposition installs sources before settings access. Keep existing JSON names and migrations. For a new page, also register its band and layout in SettingsCatalog. |
| Note effect or MIDI processor | Extend TechniqueInfo or the processor catalogue, then update supported import, export, playback and editing. |
| Import or export format | Put format translation at the boundary, use InputLimits, leave external imports in the worker path, and preserve safe and atomic output. |
| Engine message | Add one record and Write/Read pair in EngineMessages or EngineMessageLists. Client and host share it. Preserve old readers where needed and bound all lengths. |
| Shared UI control | Check Platform/Controls, Views/Dialogs, theme tokens, draw ports and DockPaneSetup before adding another control. Reuse scale panel hosts rather than duplicating selection state. Use resize cursors on real resize grips. |
| Focused verification | Use ArrangementViewTests for arrangement composition, MainWindowMenuTests for commands, InstrumentPanelViewTests for instruments, HelpSearchTests for Help, RecordingBatchOneTests for takes, and AvaloniaPlaybackSessionTests for per-document transport. |

## State, timing and compatibility

- SongDocument owns project and per-document state. Use DocumentEdits for one undoable edit, invalidate playback only when warranted, and keep per-document media alongside the song policy. The score compiler publishes the performed timeline. Visuals follow the audible playback clock.
- Playback in the app owns process-wide audio setup. Transport owns the session for a single document. Keep preset catalogue identity shared across windows. Unsubscribe owned UI work when a window closes. Preserve recording's pre-save take commit, unsaved check, video finalisation and release order.
- A project file is bounded, validated JSON and writes atomically. A Guitar Pro file may carry a bounded compressed TabForge project entry. A clean Guitar Pro file can pair with a separate audio sidecar. AlphaTabBoundary uses the pinned patched AlphaTab build. Settings and presets keep their stored names and load older shapes through migrations.
- AudioEngine callbacks publish whole graphs and work on preallocated buffers. Keep allocation, locks, waits, disk and pipe writes off callback and capture threads. Retire replaced graphs only after the callback epoch passes. Native plug-in calls need crash breadcrumbs and isolated hosts need a bounded fallback.
- The control pipe is restricted to the current user. Shared memory and portable Wine/TCP transport are same-machine IPC. Shared blocks are fixed layouts. Windows named events and the portable signal paths have different wake-up rules.
- Recording opens capture on demand, uses a preallocated queue, and writes audio off the callback. Write silence for queue loss to preserve timing. Offline rendering preserves deterministic summation order, compensates per-chain delay and cleans partial output when it fails or is cancelled.
- Windows-specific sources are conditionally excluded from the portable target. Check both builds when moving platform files or changing global imports. Keep native provenance and [release procedures](../guides/RELEASING.md) in sync with the packages.

## Built-in self-tests

The owner runs the applicable runtime checks. Headless diagnostics support the `--selftest`, `--areas`, `--only`, `--require ci` and `--timing` switches. New checks use TestX, map to AreaOf and register in SelfTest.Run. Examples use generated fixtures and write output to scratch or the requested log path.

## Open items

Help and Lyrics are the first window modules. Shell-owned feature registration remains in the composition stages. Larger UI controllers, native DSP and package compatibility require their own checks. Setup and Portable need owner-run verification, especially video capture, transparent windows, MIDI timing and release trimming.
