# Recording rules (Core)

Namespace `TabForge.Features.Recording.Rules` (capture, takes, landing) and
`TabForge.Features.Recording.Model` (the repeat-take writes). UI-free: no Avalonia reference.
The Avalonia halves are `Source/src/TabForge.Avalonia/Features/Recording/Presenter/` (toggle,
live takes, landing and undo step) and `.../View/` (armed sub-rows, input picker, overlay).

- Owns: `Rules/TransportRecording.cs` (which tracks are armed, starting and ending the engine's
  recording, the status text), `Rules/MidiTakeCapture.cs` and `Rules/MidiTake.cs` (the MIDI takes of
  the armed MIDI tracks), `Rules/RecordedTakes.cs` (cutting a finished recording into takes and
  placing them on free lanes), `Rules/RecordingPasses.cs` (splitting a looped recording into passes),
  `Rules/RepeatPasses.cs` (splitting a recording through score repeats), `Model/TakeGroupEdits.cs`
  (remove and mute of a repeat take).
- Does not own: the read-only take-group rules (`TakeGroups` in `Models/`), the lane geometry
  (`ClipLanes` in `Models/`), the undo step and dirty mark (`DocumentEdits`), the engine client, the
  playback session, the window wiring (`MainWindowTransportRecord`) or any drawing.
- Entry points: `TransportRecording.Toggle`, `AnyArmed` and `DisarmTrack`, `MidiTakeCapture.Start`
  and `Disarm`, `MidiTake.Keep`, `CloseHeld` and `Finish`, `RecordedTakes.FromRecording`, `Place`
  and `StatusText`, `RecordingPasses.Split`, `TakeGroupEdits.Remove` and `ToggleMute`.
- Callers: `RecordingPresenter` (Avalonia: toggle, landing, undo step), `MainWindowTransportRecord`
  (the command wiring), `MainWindowClipKeys` and `MainWindowClipEditKeys` (group edits).
- Tests: `TransportRecordingTests`, `MidiTakeCaptureTests`, `MidiTakeTests` (capture),
  `RecordingBatchOneTests`, `CountInRecordingTests`, `RecordingLaneLifecycleTests`,
  `RecordingRealPathTests`, `LoopPassGroupTests` (landing and passes),
  `RepeatPassesTests`, `RepeatTakeLandingTests` (repeat takes). Run the Avalonia test project locally with
  `--filter "FullyQualifiedName~TransportRecordingTests"`.
