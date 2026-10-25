# Views/TransportPopups: the transport buttons' settings popups

Three small popups open above a transport button (metronome, count-in and loop). They are the Avalonia twins of the WPF
`MetronomeSettingsPopup`, `CountInSettingsPopup` and `LoopSettingsPopup`; `MainWindowTransportToggles` opens them and the rules live in
Core (`PlaybackTransport`, `LoopSettingsRules`).

| Class | Job |
|---|---|
| `TransportPopupFrame` | The look and opening of one popup: a rounded panel above its button with a shadow, a caps title, an × close button, and the row builders the three popups share (a labelled slider with its percent, a drop-down, a check box, muted helper text). Closes on a press outside, Escape or ×. |
| `ClickSettingsPopups` | The metronome and count-in rows: volumes, click sound, boost, subdivision, count-in length and the count-in checks. Every change writes the audio settings at once and calls the change callback. |
| `LoopSettingsPopup` | The loop rows: the loop area line, "Loop button loops what is playing" and its scope, forget-the-area, the loop count, count-in before every loop, simple loop or speed trainer with its tempo boxes, and Clear loop area. A box is read on Enter or focus loss. |

Rules: no modal windows; the settings write through the callback the opener gives; the popup never changes the transport itself.

Tests: `TransportSettingsPopupTests`. Run the Avalonia test project locally with
`--filter "FullyQualifiedName~TransportSettingsPopupTests"`.
