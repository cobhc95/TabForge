# Recording views (Avalonia)

Namespace `TabForge.Avalonia.Features.Recording.View`. The visible half of recording: the armed
sub-rows, the input picker and the live-take overlay. No capture, landing or undo logic lives here.

- Owns: `TimelineLiveTakeLayer.cs` (the growing red take boxes over the timeline), `ArmedRowLayer.cs`
  (the monitor toggle and input picker controls of each armed track plus the level poll),
  `ArmedRowPainter.cs` (the armed strip's geometry, drawing and texts), `AudioInputPicker.cs`
  (the input ComboBox rows and their refresh).
- Does not own: the takes being drawn (`LiveTakes` in `Presenter/`), the per-frame feed and landing
  (`RecordingPresenter` in `Presenter/`), the capture rules (`TransportRecording`, `MidiTakeCapture`
  in Core), the timeline and row geometry (`TimelineView`, `TrackHeaderColumn` in `Platform/`),
  the arm itself, the engine sync, or the input list (`AudioInputs` in Core `Models/`).
- Entry points: `TimelineLiveTakeLayer.Takes` and `Draw`, `ArmedRowLayer.Show` and `Sync` (with the
  `SettingChanged` event and the `InputPeakOf` feed), `ArmedRowPainter.Draw` and `DrawLaneHint`
  (positions via `StripTopOf`, `MonitorLeft`, `InputLeft`, `MeterLeft`), `AudioInputPicker.Bind`,
  `Refresh` and `SampleWindow`.
- Callers: `TimelineView` and `TimelineView.Drawing` (build and repaint `TimelineLiveTakeLayer`;
  `UseLiveTakes`, `RefreshLiveTakes`), `TrackHeaderColumn` (draws `ArmedRowPainter.Draw`),
  `ArrangementTrackRows` and `ArrangementView` (own the `ArmedRowLayer`), `ArmedRowLayer` (binds
  `AudioInputPicker`), `PreviewToolsMenu` (opens `AudioInputPicker.SampleWindow`).
- Tests: `RecordingLiveTakeTests`, `RecordingRealPathTests` and `LiveTakeFoldTests` (the overlay),
  `ArmedRowTests` (the sub-rows, painter and picker), `ClipSnapPluginDialogTests` (the picker window
  and refresh). Run the Avalonia test project locally with
  `--filter "FullyQualifiedName~RecordingLiveTakeTests"`.
