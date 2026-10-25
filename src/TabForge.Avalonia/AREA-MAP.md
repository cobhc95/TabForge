# AREA-MAP: where each area lives, and the one test class to run

Paths are relative to `src/TabForge.Avalonia/` unless they start with `src/`. Every path below exists in the tree as written.
Tests live in the local Avalonia test project (not part of the public export); run it with a class-name filter, for example
`--filter "FullyQualifiedName~ArrangementViewTests"`.

| Area | The 5–8 files that own it | Run |
|---|---|---|
| **Timeline / arrangement** | `Platform/TimelineView.cs`, `Platform/TimelineView.Drawing.cs`, `Platform/TrackHeaderColumn.cs`, `Platform/MasterLaneStrip.cs`, `Platform/TimelineHitTester.cs`, `Views/TimelineInputController.cs`, `Views/TimelineClipGestureController.cs`, `MainWindowTimelineBars.cs` | `ArrangementViewTests` |
| **Arrangement construction / row stack** | `Views/ArrangementView.cs`, `Views/ArrangementComposition.cs`, `Views/ArrangementTrackRows.cs`, `Views/ArrangementTopBand.cs`, `Views/ArrangementRowSource.cs` | `ArrangementViewTests` |
| **Range delete / timeline commands** | `MainWindowRangeDelete.cs`, `MainWindowRangeCommands.cs`, `MainWindowKeyboard.cs`, `Views/ContextMenuRows.cs` | `TimelineDeleteRoutingTests` |
| **Fretboard / instrument** | `Platform/InstrumentPanelView.cs`, `Platform/InstrumentPanelView.Layouts.cs`, `Platform/InstrumentPanelView.Playback.cs`, `Platform/InstrumentPanelStrip.cs`, `Platform/FretboardPlaybackMarkers.cs`, `Platform/FretboardSettingsBinding.cs`, `Platform/FretboardContextMenu.cs`, `Platform/FretboardNoteEntry.cs` | `InstrumentPanelViewTests` |
| **Shell / window / tabs / dock** | `MainWindow.cs`, `MainWindowTabs.cs`, `Views/TabStripView.cs`, `Views/PreferencesWindow.cs`, `Views/StatusBarView.cs`, `Views/Dock/DockWorkspaceController.cs`, `Views/Dock/DockPaneSetup.cs`, `Platform/AvaloniaWindowChrome.cs` | `MainWindowMenuTests` |
| **Score / notation** | `Platform/ScorePreviewView.cs`, `Platform/ScorePreviewView.Zoom.cs`, `Platform/ScorePreviewInput.cs`, `Platform/ScorePlayheadBinding.cs`, `Platform/ScorePlayheadOverlay.cs`, `MainWindowScoreRows.cs`, `MainWindowNotationMenu.cs`, `Platform/ScorePdfExport.cs` | `ScorePreviewEditorTests` |
| **Presets** | `Views/Presets/PresetRows.cs`, `Views/Presets/PresetRowIcons.cs`, `Views/Presets/PresetRowText.cs`, `Views/Presets/PresetManagerWindow.cs`, `Views/Presets/PresetManagerWindow.Keys.cs`, `Views/Presets/PresetManagerWindow.Rows.cs`, `Views/Presets/PresetManager.cs`, `MainWindowPresetManager.cs` | `PresetRowsTests` |
| **Audio / clip lanes** | `Platform/TimelineAudioClips.cs`, `Platform/TimelineWaveforms.cs`, `Platform/ClipEditService.cs`, `Views/TimelineClipGestureController.cs`, `MainWindowClipRefresh.cs`, `MainWindowClipKeys.cs`, `MainWindowClipProperties.cs`, `src/TabForge.Core/Models/AudioClipModel.cs` | `ClipEditingBatchFourTests` |
| **Recording / live takes** | `Features/Recording/Presenter/RecordingPresenter.cs`, `Features/Recording/Presenter/LiveTakes.cs`, `Features/Recording/View/TimelineLiveTakeLayer.cs`, `Features/Recording/View/ArmedRowLayer.cs`, `Features/Recording/View/ArmedRowPainter.cs`, `Features/Recording/View/AudioInputPicker.cs`, `MainWindowLiveInput.cs`, `src/TabForge.Core/Features/Recording/Rules/TransportRecording.cs`, `src/TabForge.Core/Features/Recording/Rules/MidiTakeCapture.cs` | `RecordingBatchOneTests` |
| **Audio drop / import** | `MainWindowAudioDrop.cs`, `MainWindowFileDrop.cs`, `Platform/AddTrackBar.cs`, `Platform/AddTrackStrip.cs`, `src/TabForge.Core/Services/AudioDropPlanner.cs` | `AudioDropPlannerTests` |
| **Playback** | `Transport/AvaloniaPlaybackSession.cs`, `Transport/AvaloniaPlaybackSessions.cs`, `Transport/PlayheadClock.cs`, `Transport/FrameSources.cs`, `Transport/AvaloniaEngineRouting.cs`, `Transport/AvaloniaEngineEvents.cs`, `Playback/AvaloniaPlaybackHost.cs`, `Playback/AvaloniaPlaybackComposition.cs` | `AvaloniaPlaybackSessionTests` |

## Audio / lanes owners and the test class that covers each

| Owner | Covered by |
|---|---|
| `MainWindowTransportRecord.cs` | `TransportRecordingTests`, `MidiTakeCaptureTests`, `RecordingBatchOneTests`, `RecordingLiveTakeTests` |
| `MainWindowAudioDrop.cs` | `AudioDropPlannerTests` |
| `Features/Recording/Presenter/RecordingPresenter.cs` | `TransportRecordingTests`, `MidiTakeCaptureTests`, `CountInRecordingTests`, `RecordingBatchOneTests` |
| `Features/Recording/Presenter/LiveTakes.cs` | `RecordingLiveTakeTests`, `LiveTakeFoldTests` |
| `Features/Recording/View/ArmedRowLayer.cs` | `ArmedRowTests` |
| `Features/Recording/View/ArmedRowPainter.cs` | `ArmedRowTests` |
| `Features/Recording/View/AudioInputPicker.cs` | `ArmedRowTests`, `ClipSnapPluginDialogTests` |
| `Features/Recording/View/TimelineLiveTakeLayer.cs` | `RecordingLiveTakeTests` |
| `Platform/TimelineAudioClips.cs` | `TimelineAudioClipTests`, `ClipEditingBatchFourTests`, `ClipLaneGeometryTests`, `RecordingLaneLifecycleTests` |
| `Views/TimelineClipGestureController.cs` | `TimelineAudioClipTests`, `ClipEditingBatchFourTests` |
| `MainWindowClipRefresh.cs` | `RecordingLaneLifecycleTests` |
| `MainWindowClipKeys.cs` | `MainWindowClipKeysTests` |
| `MainWindowClipProperties.cs` | `ClipPropertiesTests`, `MainWindowMenuTests` |
| `src/TabForge.Core/Services/AudioDropPlanner.cs` | `AudioDropPlannerTests` |
| `src/TabForge.Core/Features/Recording/Rules/TransportRecording.cs` | `TransportRecordingTests`, `MidiTakeCaptureTests` |
| `src/TabForge.Core/Features/Recording/Rules/MidiTakeCapture.cs` | `MidiTakeCaptureTests` |
| `src/TabForge.Core/Models/TakeGroups.cs` | `RepeatTakeLandingTests` |
| `src/TabForge.Core/Features/Recording/Model/TakeGroupEdits.cs` | `RepeatTakeLandingTests` |
| `src/TabForge.Core/Models/AudioClipModel.cs` | `ClipLaneGeometryTests`, `RecordingLaneLifecycleTests` |

The timeline's `ArrangementView` is now composition-split: `Views/ArrangementComposition.cs` builds the controls and returns their `Parts` record
(timeline, header column, row stack, input controller, name editor, sideways viewer, top band, splitter and root content), while `ArrangementView`
keeps the event wiring and public surface. `Views/ArrangementTrackRows.cs` is the plan's **TrackRowCoordinator** (row source, armed sub-row layer,
row scroll bar, timeline subscriptions, natural maximum height, selected-row reload); it landed under that row-stack name, so read that file rather
than looking for a separate coordinator file.

The pair most often confused: `Playback/` builds and owns the **process-wide** playback parts (engine client, MIDI output, adapters, tab-switch
policy); `Transport/` runs **one document's** playback (session, frame pump, playhead clock, engine routing and events). Read
`Transport/README.md` and `Playback/README.md` before moving anything between them.

More maps: `../../docs/COMMANDS.md` (repo root; every command id and the file that registers it, generated by `tools/command-map.py`),
`Views/Dock/README.md` (dock pane ids to their factories and content files), and the folder READMEs: `Transport/`, `Playback/`, `Autosave/`,
`Lyrics/`, `Platform/Controls/`, `Views/Dock/`, `Views/Midi/`, `Views/Presets/`, `Views/TransportPopups/`, `Views/Fx/`.

Every path above is checked against the tree by `DocTruthTests` (`tests/TabForge.Avalonia.Tests/DocTruthTests.cs`); `tools/command-map.py` exists for regenerating the command map. (`work/` is gitignored, so audit notes there are not linked from this file.)
