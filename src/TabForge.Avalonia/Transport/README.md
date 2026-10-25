# Transport: one document's playback

This is the per-document half of playback: a **session** runs one song's `PlaybackTransport` over the engine, pumps a frame source, keeps the
one playhead clock the views draw, and routes/answers the engine. The process-wide construction and ownership of the engine lives in
`Playback/` right next to it (see `Playback/README.md`); a change to "what plays" lands here, a change to "what is built at start-up" usually
lands there.

| Class | Job |
|---|---|
| `AvaloniaPlaybackSession` | One document's playback: the `PlaybackTransport` over the document's engine, the frame pump (one tick per screen refresh) feeding `PlayheadClock`, and the two change events the views follow (play/pause/loop; moved playhead). |
| `AvaloniaPlaybackSessions` | The sessions of one window: one per open document, created on first use and disposed with its tab; the window-wide play state event the tab strip listens to; the engine the Sound menu plays through; and the engine sync on creation, before play and on live mixer changes. |
| `PlayheadClock` | The one position every view draws: the engine's audible position carried forward by wall time, never more than `MaxLeadMs` ahead, never back except on an explicit jump, held at a start/jump until the engine reports progress; mapped to bar, beat and fraction. |
| `FrameSources` | The per-frame tick port (`IFrameSource`): a timer-driven source for tests and the display-synced source (`TopLevel.RequestAnimationFrame`, one callback per screen refresh, requested only while running). |
| `AvaloniaEngineRouting` | The engine sync of the Avalonia playback, built on the Core engine APIs: plug-in availability, GM sound, master and track levels, MIDI channel routes, transport tempo, and the live level push after a fader/pan/mute/solo change. |
| `AvaloniaEngineEvents` | One window's answers to the engine's events and settings: a crashed plug-in (quarantine save, notice, resync), a failed or slow plug-in (notice and the one "loading slowly" question), a device reopen, and audio route settings applied at once. |
| `PlayheadTrace` | The opt-in playhead trace (`TABFORGE_TRACE=playback`, written to `trace-playback.log`): one line per performed bar and one per frame for the first second after a start or jump. |
| `SessionKeyboardSongPosition` | The keyboard view's song position (ms) read from one playback session. |

Rules: playback stays light — no per-frame layout work; the clock is the only source of the drawn position; UI thread only.

Tests: `AvaloniaPlaybackSessionTests`, `AvaloniaEngineRoutingTests`, `PluginEngineEventsTests`, `PlayheadClockTests`,
`PlayheadStartAuthorityTests`, `PlayheadTraceTests`, `TransportLoopCountInTests`, `TabPlaybackSwitchTests`. Run the Avalonia test project locally with
`--filter "FullyQualifiedName~AvaloniaPlaybackSessionTests"`.
