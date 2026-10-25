# Playback: the process-wide playback parts

This is the construction-and-ownership half of playback: the one audio engine client, the MIDI output, the adapters they are built from, and the
policy for what other tabs do when the shown tab changes. The per-document run (sessions, playhead clock, engine routing and events) lives in
`Transport/` next to it (see `Transport/README.md`).

| Class | Job |
|---|---|
| `AvaloniaPlaybackHost` | The process's playback parts for the Avalonia app: the one engine client, the one winmm MIDI output, and the per-document sessions whose engines route through them. Created once at start-up. |
| `AvaloniaPlaybackComposition` | The one place the Avalonia app builds its playback adapters: the engine client (with the Windows plug-in file-trust adapter inside `PluginTrustLoadGate`), the winmm MIDI output and the NAudio device list. The WPF app composes the same adapters. |
| `TabPlaybackSwitchPolicy` | What happens to the other tabs' playback when the person switches tabs, from the PlaybackOnTabSwitch setting: continue, pause or stop it. |

The split in one line: `Playback/` decides **what exists for the whole process** (engine, outputs, adapters, tab-switch policy); `Transport/`
decides **what one playing document does** (its session, frames, clock, routing, engine events).

Tests: `AvaloniaPlaybackCompositionTests`, `AvaloniaPlaybackHostTests`, `TransportRecordingTests`, `TabPlaybackSwitchTests`. Run the Avalonia test project locally with
`--filter "FullyQualifiedName~AvaloniaPlaybackHostTests"`.
