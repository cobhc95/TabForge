# Feature map

Where to look when you change a feature: the main code, the pathway to use, and the self-tests that cover it. Start here, then read `CONTRIBUTING.md` for the rules and `ARCHITECTURE.md` for the design.

The feature table is kept by hand. The test tables at the end are generated from the test registry (`src/TabForge/SelfTests/SelfTest.cs`); a self-test fails when they are out of date or when this page names a test, area, group or type that does not exist. To refresh the generated part, build and run `TabForge.exe --feature-map` from the repository root.

All run commands are `TabForge.exe --selftest <log> --areas <area>`; read the last line of the log (`N passed, M failed, K skipped`). A group is run with the whole suite and `--require ci`.

## Features

| Feature | Main code | Pathway to use | Tests |
| --- | --- | --- | --- |
| Editing and notation | `src/TabForge/Models/` (`SongProject`, `TrackModel`, `MeasureModel`, `TabCell`); `src/TabForge/Services/` (`EditCommands`); `src/TabForge/Views/` (`TabEditorControl`, `StaffNotationRenderer`); undo in `UndoController` | A model edit is `DocumentEdits.Run` (one undo step, one dirty change, one timeline invalidation). Commands are bound through `HotkeyCatalog`. | `--areas ui`, `--areas notation`; `TestEditCommands`, `TestEditorEntry`, `TestNotationLayout`, `TestEngravingCollisions`, `TestUndoController` |
| Playback | `src/TabForge/Playback/` (`ScoreToMidiCompiler`, `ScoreTimeline`, `PlaybackEngine`, `PlaybackOrder`) | The compiled timeline is authoritative; views only read it (`PlaybackEngine.TimelineChanged`). | `--areas playback`; `TestTimelineBasics`, `TestPlaybackOrderSpec`, `TestNoHangingNotes`; headless check: `TabForge.exe --playtest <song>` |
| Mixer and audio engine | `src/TabForge.AudioEngine/` (`MixEngine`, `TrackChain`, `ClipPlayer`); client in `AudioEngineClient`; wire types in `src/TabForge.Audio.Contracts/` | The UI reaches the engine only through `AudioEngineClient`; every message has a bounded reader. Audio-callback code does not allocate or lock. | `--areas engine`; `TestMixer`, `TestMuteSoloTruthTable`, `TestWatchdogPolicy`, `TestCommandFrameRobustness`; `TestArchitectureLayering` |
| Recording | `RecordingController`; engine side `InputCapture`, `Recorder` | The controller talks to the engine through the client; the recorded take becomes a clip on the timeline. | `--areas recording`; `TestRecordingPipeline`, `TestCaptureResampling`, `TestClips` |
| Import and export (.gp, .gp5, MIDI, MusicXML, ASCII) | `src/TabForge/Services/` (`GuitarProImporter`, `GuitarProExporter`, `AlphaTabBoundary`, `ImportWorker`, `MidiFileImport`, `MidiExportService`, `MusicXmlExportService`, `AsciiExportService`, `AudioDataFile`) | Reading goes through the import worker with limits (`ScoreImportQueue`, `InputLimits`). Saving goes through `DocumentSaveFlow`; files are written with `FilePathPolicy.WriteAtomically`. | `--areas guitarpro`, `--areas persistence`; groups gp-fixtures, gp-fidelity, long-import, fuzz; `TestGuitarProImportWorker`, `TestMidiExport`, `TestMusicXmlExport`, `TestAsciiExport`, `TestPairSaveRecovery` |
| Timeline and clips | `src/TabForge/Views/` (`ArrangementPanel`, `TrackTimeline`); `ArrangementController` | Clip and lane edits are `DocumentEdits.Run`. Audio files are handled with the document's `MediaContext`. | `--areas recording` (clips, drops), `--areas ui` (geometry, menus); `TestClipMoves`, `TestMediaDropPlan`, `TestArrangementGeometry`, `TestTimelineContextMenus` |
| Settings and Preferences | `src/TabForge/Services/` (`AppSettings`, `AppSettingsStore`, `SettingsValidator`, `SettingsMigration`, `SettingsCatalog`); `PreferencesWindow` | One shared `AppSettingsStore`. A setting has a default, a bound in the validator, a migration entry and a Preferences row. | `--areas settings`; `TestEverySettingIsWired`, `TestPreferencesCatalog`, `TestSettingsStoreSharedAcrossWindows` |
| Windows, tabs and documents | `src/TabForge/Documents/` (`DocumentSession`, `DocumentController`, `DocumentSaveFlow`, `DocumentCloseFlow`, `DocumentPlacement`); `src/TabForge/Shell/` (`TabWindowRegistry`, `OwnedSubscriptions`); `MediaContext` | One `DocumentSession` per song. Subscribe through `OwnedSubscriptions`; open songs through `DocumentPlacement`; save through `DocumentSaveFlow`. | groups window-lifetime, document-context, document-operations (`--areas window-lifetime`, `--areas document-context`, `--areas document-operations`); `TestWindowLifetime`, `TestDocumentContext`, `TestDocumentOperations`, `TestDocuments` |
| Plug-ins | `src/TabForge/Plugins/` (`PluginTrust`, `MidiProcessorCatalog`); engine side `Vst2Plugin`, `RemotePlugin` | A song only names plug-in paths; `PluginTrust` decides what loads. Isolation is optional crash isolation, not a sandbox. | `--areas engine`, `--areas midi`; `TestIsolatedPluginGenerations`, `TestPluginFactorySingleSource`, `TestMidiProcessors`, `TestNightPluginApproval` |
| Tutorial | `src/TabForge/Views/` (`TutorialWindow`, `TutorialRenderer`, `TutorialPdfExporter`); `TutorialLibrary` | Guide text is Markdown under `docs/tutorial/`, read by `TutorialLibrary`. | `--areas tutorial`; `TestTutorialGuides`, `TestTutorialMarkdown`, `TestTutorialSearch` |
| Rendering to file | `src/TabForge/Rendering/` (`RenderJob`, `RenderSpecBuilder`); engine side `OfflineRenderer` | A render is a job built from the document and run by the engine's offline renderer. | `--areas playback`, `--areas engine`; `TestRenderBarRanges`, `TestRenderHonoursMute` |

## Tests

Generated from the registry. Each test is registered once in `SelfTest.Run` and belongs to one area (tests with no area always run) and, for the CI-critical ones, to one group. A test with no area has the name `core`.

<!-- BEGIN GENERATED TESTS (TabForge.exe --feature-map rewrites this block) -->

### Areas

| Area | Tests | Run only this area |
| --- | --- | --- |
| core (no area) | 47 | always runs |
| architecture | 4 | `--selftest <log> --areas architecture` |
| audioaudit | 2 | `--selftest <log> --areas audioaudit` |
| document-context | 1 | `--selftest <log> --areas document-context` |
| document-operations | 7 | `--selftest <log> --areas document-operations` |
| engine | 42 | `--selftest <log> --areas engine` |
| fuzz | 1 | `--selftest <log> --areas fuzz` |
| guitarpro | 24 | `--selftest <log> --areas guitarpro` |
| hygiene | 11 | `--selftest <log> --areas hygiene` |
| interactions | 1 | `--selftest <log> --areas interactions` |
| leaks | 3 | `--selftest <log> --areas leaks` |
| midi | 1 | `--selftest <log> --areas midi` |
| notation | 2 | `--selftest <log> --areas notation` |
| persistence | 31 | `--selftest <log> --areas persistence` |
| playback | 25 | `--selftest <log> --areas playback` |
| recording | 11 | `--selftest <log> --areas recording` |
| settings | 10 | `--selftest <log> --areas settings` |
| smoke | 6 | `--selftest <log> --areas smoke` |
| synthetic | 2 | `--selftest <log> --areas synthetic` |
| tutorial | 6 | `--selftest <log> --areas tutorial` |
| ui | 101 | `--selftest <log> --areas ui` |
| window-lifetime | 1 | `--selftest <log> --areas window-lifetime` |

### Groups

A group is a named set of tests with a minimum check count; `--require ci` makes the CI groups mandatory (see `docs/TESTING.md`).

| Group | Tests |
| --- | --- |
| architecture | TestArchitectureLayering, TestArchitectureDocumentOperations, TestArchitectureGuards, TestEveryTestHasAnArea |
| document-context | TestDocumentContext |
| document-operations | TestDocumentOperations, TestAddTrackMenu, TestAudioTrackClips, TestClipAndSectionEdits |
| fuzz | TestMalformedInputFuzz |
| gp-fidelity | TestGpFidelity, TestGpMixerExact, TestGpTrillSpeed, TestGpLossCoverage, TestGpCompatibilityDoc |
| gp-fixtures | TestSyntheticGuitarProFixture |
| interactions | TestInteractions |
| long-import | TestLongGuitarPro35Import, TestImportPlausibility |
| synthetic-fixtures | TestSyntheticFixtures |
| window-lifetime | TestWindowLifetime |

### Every test

#### core (no area)

| Test | Group | File |
| --- | --- | --- |
| TestAnalyzeBar |  | `tests/full-suite/SelfTestCore.cs` |
| TestAutomationPeers |  | `tests/full-suite/Editor/SelfTestSelection.cs` |
| TestBarGridPlacement |  | `tests/full-suite/Notation/SelfTestBarGrid.cs` |
| TestBarIncompleteMarking |  | `tests/full-suite/Editor/SelfTestBarIncomplete.cs` |
| TestBarSlots |  | `tests/full-suite/SelfTestCore.cs` |
| TestCellSlots |  | `tests/full-suite/SelfTestCore.cs` |
| TestCommandPaletteAndPdf |  | `tests/full-suite/Editor/SelfTestSelection.cs` |
| TestCursorSnap |  | `tests/full-suite/Editor/SelfTestCursorSnap.cs` |
| TestDataIntegrityLeftovers |  | `tests/full-suite/Persistence/SelfTestDataIntegrity.cs` |
| TestDocuments |  | `tests/full-suite/SelfTestCore.cs` |
| TestDuplicateBarAllTracks |  | `tests/full-suite/Notation/SelfTestDuplicateBar.cs` |
| TestEditControllers |  | `tests/full-suite/SelfTestCore.cs` |
| TestEditorSelectionState |  | `tests/full-suite/Editor/SelfTestSelection.cs` |
| TestEngravingHeader |  | `tests/full-suite/Editor/SelfTestSelection.cs` |
| TestFollowSurvivesZoom |  | `tests/full-suite/Editor/SelfTestSelection.cs` |
| TestKeySignaturesCarryForward |  | `tests/full-suite/Notation/SelfTestBarSignatures.cs` |
| TestKnobTypeIn |  | `tests/full-suite/Views/SelfTestKnob.cs` |
| TestLayoutAuditTechniqueSong |  | `tests/full-suite/Editor/SelfTestSelection.cs` |
| TestMixerDragAndDrop |  | `tests/full-suite/Views/SelfTestMixerUi.cs` |
| TestMixerMuteSoloClick |  | `tests/full-suite/Views/SelfTestMixerUi.cs` |
| TestMixerSliders |  | `tests/full-suite/Views/SelfTestMixerUi.cs` |
| TestMixerSlidersRealInput |  | `tests/full-suite/Views/SelfTestMixerUi.cs` |
| TestOrderAnimationAndSpeedCommands |  | `tests/full-suite/Views/SelfTestMixerUi.cs` |
| TestPerNoteDurationPercent |  | `tests/full-suite/GuitarPro/SelfTestGp5.cs` |
| TestPlaybackDifferences |  | `tests/full-suite/Playback/SelfTestPlaybackDifferences.cs` |
| TestPluginRightsNotice |  | `tests/full-suite/Views/SelfTestPluginRightsNotice.cs` |
| TestSectionsPaneScrollsWhenShort |  | `tests/full-suite/Views/SelfTestSectionsScroll.cs` |
| TestSecurityInputBoundaries |  | `tests/full-suite/Settings/SelfTestSecurity.cs` |
| TestSelectionModel |  | `tests/full-suite/Editor/SelfTestSelection.cs` |
| TestSignatureRoundTrips |  | `tests/full-suite/Notation/SelfTestBarSignatures.cs` |
| TestSignaturesCarryForward |  | `tests/full-suite/Notation/SelfTestBarSignatures.cs` |
| TestSliderMappingAndGroupValues |  | `tests/full-suite/Views/SelfTestMixerUi.cs` |
| TestSnapLayoutHitTest |  | `tests/full-suite/Editor/SelfTestSelection.cs` |
| TestSpeedControl |  | `tests/full-suite/Playback/SelfTestSpeed.cs` |
| TestTechniqueEngraving |  | `tests/full-suite/Editor/SelfTestSelection.cs` |
| TestTemplateKeepsSetupOnly |  | `tests/full-suite/Editor/SelfTestSelection.cs` |
| TestTraceSwitchAreas |  | `tests/full-suite/Views/SelfTestTrace.cs` |
| TestTrackListFollowsMixerInPlace |  | `tests/full-suite/Views/SelfTestMixerUi.cs` |
| TestTrackListGroupRows |  | `tests/full-suite/Views/SelfTestMixerUi.cs` |
| TestTrackOrderingModel |  | `tests/full-suite/Views/SelfTestMixerUi.cs` |
| TestTrackReorder |  | `tests/full-suite/SelfTestCore.cs` |
| TestTrimMerges |  | `tests/full-suite/Editor/SelfTestTrim.cs` |
| TestUndoController |  | `tests/full-suite/SelfTestCore.cs` |
| TestUndoDeltaStates |  | `tests/full-suite/Persistence/SelfTestUndo.cs` |
| TestUpdateCheck |  | `tests/full-suite/Views/SelfTestUpdates.cs` |
| TestUserTemplatesAndFaultedChain |  | `tests/full-suite/Editor/SelfTestSelection.cs` |
| TestZoomComboShowsValue |  | `tests/full-suite/SelfTestCore.cs` |

#### architecture

| Test | Group | File |
| --- | --- | --- |
| TestArchitectureDocumentOperations | architecture | `src/TabForge/SelfTests/Architecture/SelfTestArchitecture.cs` |
| TestArchitectureGuards | architecture | `src/TabForge/SelfTests/Architecture/SelfTestArchitecture.cs` |
| TestArchitectureLayering | architecture | `src/TabForge/SelfTests/Architecture/SelfTestArchitecture.cs` |
| TestEveryTestHasAnArea | architecture | `src/TabForge/SelfTests/Architecture/SelfTestArchitecture.cs` |

#### audioaudit

| Test | Group | File |
| --- | --- | --- |
| TestAudioAudit |  | `tests/full-suite/Architecture/SelfTestAudioAudit.cs` |
| TestAudioAuditRepeatedSections |  | `tests/full-suite/Architecture/SelfTestAudioAudit.cs` |

#### document-context

| Test | Group | File |
| --- | --- | --- |
| TestDocumentContext | document-context | `tests/full-suite/Lifecycle/SelfTestDocumentContext.cs` |

#### document-operations

| Test | Group | File |
| --- | --- | --- |
| TestAddTrackMenu | document-operations | `tests/full-suite/Editor/SelfTestAddTrackLane.cs` |
| TestAudioTrackClips | document-operations | `tests/full-suite/Recording/SelfTestAudioTrackClips.cs` |
| TestBarDeleteGuards |  | `tests/full-suite/Editor/SelfTestBarDeleteGuards.cs` |
| TestBarRangeGaps |  | `tests/full-suite/Editor/SelfTestBarRangeGaps.cs` |
| TestClipAndSectionEdits | document-operations | `tests/full-suite/Lifecycle/SelfTestClipSectionEdits.cs` |
| TestDocumentOperations | document-operations | `tests/full-suite/Lifecycle/SelfTestDocumentOperations.cs` |
| TestSectionClips |  | `tests/full-suite/Lifecycle/SelfTestSectionClips.cs` |

#### engine

| Test | Group | File |
| --- | --- | --- |
| TestAudioTrackLiveMidi |  | `tests/full-suite/Engine/SelfTestAudioTrackRouting.cs` |
| TestAudioTrackRouting |  | `tests/full-suite/Engine/SelfTestAudioTrackRouting.cs` |
| TestAutoGmEveryPath |  | `tests/full-suite/Engine/SelfTestAutoGm.cs` |
| TestCallbackMetrics |  | `tests/full-suite/Recording/SelfTestRecording.cs` |
| TestChildProcessJob |  | `tests/full-suite/Engine/SelfTestEngineWatchdog.cs` |
| TestClipChurnWhileStreaming |  | `src/TabForge/SelfTests/Engine/SelfTestEngineHeadless.cs` |
| TestColdStartSetupSurvivesPanic |  | `tests/full-suite/Engine/SelfTestColdSetup.cs` |
| TestCommandFrameRobustness |  | `tests/full-suite/Engine/SelfTestEngineWatchdog.cs` |
| TestDiskStreamerIdle |  | `src/TabForge/SelfTests/Engine/SelfTestEngineHeadless.cs` |
| TestDrumAndMelodicDispatchTogether |  | `tests/full-suite/Engine/SelfTestMidiSync.cs` |
| TestEffectChannel |  | `tests/full-suite/Engine/SelfTestEffectChannel.cs` |
| TestEngineExitCleanupOnUi |  | `src/TabForge/SelfTests/Engine/SelfTestEngineHeadless.cs` |
| TestEngineLivenessAndSlowLoad |  | `tests/full-suite/Engine/SelfTestEngineWatchdog.cs` |
| TestEngineMultiTabPlayback |  | `tests/full-suite/Settings/SelfTestSettingsStore.cs` |
| TestEngineOwnerIdLifecycle |  | `src/TabForge/SelfTests/Engine/SelfTestEngineHeadless.cs` |
| TestEngineOwnerTransports |  | `src/TabForge/SelfTests/Engine/SelfTestEngineHeadless.cs` |
| TestEnginePluginEditAndStatesByIdentity |  | `tests/full-suite/Engine/SelfTestEngineIdentity.cs` |
| TestEngineSlotsFollowTrackIdentity |  | `tests/full-suite/Engine/SelfTestEngineIdentity.cs` |
| TestEngineWarmOwnership |  | `tests/full-suite/Settings/SelfTestSettingsStore.cs` |
| TestGainBitIdentical |  | `tests/full-suite/Engine/SelfTestGain.cs` |
| TestGainRenderHash |  | `tests/full-suite/Engine/SelfTestGainRender.cs` |
| TestIsolatedCallbackBudget |  | `tests/full-suite/Engine/SelfTestEngineLifetime.cs` |
| TestIsolatedControlChannel |  | `tests/full-suite/Engine/SelfTestEngineLifetime.cs` |
| TestIsolatedPluginGenerations |  | `tests/full-suite/Engine/SelfTestEngineLifetime.cs` |
| TestMixer |  | `tests/full-suite/Views/SelfTestMixer.cs` |
| TestMonitorFx |  | `tests/full-suite/Engine/SelfTestMonitorFx.cs` |
| TestMuteDimmingIsInstant |  | `tests/full-suite/Engine/SelfTestMuteSoloFast.cs` |
| TestMuteHoldsAcrossSeekAndRestart |  | `tests/full-suite/Engine/SelfTestMuteSolo.cs` |
| TestMuteSoloDoesNotDisturbPlayback |  | `tests/full-suite/Engine/SelfTestMuteSoloPlayback.cs` |
| TestMuteSoloIsInstant |  | `tests/full-suite/Engine/SelfTestMuteSoloFast.cs` |
| TestMuteSoloTruthTable |  | `tests/full-suite/Engine/SelfTestMuteSolo.cs` |
| TestMutedTrackDimmingSetting |  | `tests/full-suite/Engine/SelfTestMuteSoloFast.cs` |
| TestPitchMatch |  | `tests/full-suite/Engine/SelfTestPitch.cs` |
| TestPluginFactorySingleSource |  | `tests/full-suite/Engine/SelfTestEngineWatchdog.cs` |
| TestPumpOnce |  | `tests/full-suite/Engine/SelfTestEngineWatchdog.cs` |
| TestRealtimePolish |  | `tests/full-suite/Engine/SelfTestEngineRealtime.cs` |
| TestRenderHonoursMute |  | `tests/full-suite/Engine/SelfTestMuteSolo.cs` |
| TestRetirementEpochBarrier |  | `tests/full-suite/Engine/SelfTestEngineLifetime.cs` |
| TestRoutingCycles |  | `tests/full-suite/Recording/SelfTestRecording.cs` |
| TestSharedRingProducers |  | `src/TabForge/SelfTests/Engine/SelfTestEngineHeadless.cs` |
| TestTunerPitchDetection |  | `tests/full-suite/Engine/SelfTestTuner.cs` |
| TestWatchdogPolicy |  | `tests/full-suite/Engine/SelfTestEngineWatchdog.cs` |

#### fuzz

| Test | Group | File |
| --- | --- | --- |
| TestMalformedInputFuzz | fuzz | `tests/full-suite/Fuzz/SelfTestFuzz.cs` |

#### guitarpro

| Test | Group | File |
| --- | --- | --- |
| TestAsciiExport |  | `tests/full-suite/SelfTestCore.cs` |
| TestCleanGpExportKeepsFeatures |  | `tests/full-suite/GuitarPro/SelfTestImportContract.cs` |
| TestGp5EditingSemantics |  | `tests/full-suite/GuitarPro/SelfTestGp5.cs` |
| TestGpCompatibilityDoc | gp-fidelity | `tests/full-suite/GuitarPro/SelfTestGpCompatibilityDoc.cs` |
| TestGpFidelity | gp-fidelity | `tests/full-suite/GuitarPro/SelfTestGpFidelity.cs` |
| TestGpLossCoverage | gp-fidelity | `tests/full-suite/GuitarPro/SelfTestGpLossCoverage.cs` |
| TestGpMixerExact | gp-fidelity | `tests/full-suite/GuitarPro/SelfTestGpMixerExact.cs` |
| TestGpRoundTripFixes |  | `tests/full-suite/GuitarPro/SelfTestGpRoundTripFixes.cs` |
| TestGpTrillSpeed | gp-fidelity | `tests/full-suite/GuitarPro/SelfTestGpTrillSpeed.cs` |
| TestGuitarProFiles |  | `tests/full-suite/SelfTestCore.cs` |
| TestGuitarProImportContainment |  | `tests/full-suite/GuitarPro/SelfTestImportContainment.cs` |
| TestGuitarProImportWorker |  | `tests/full-suite/GuitarPro/SelfTestImportWorker.cs` |
| TestImportPlausibility | long-import | `tests/full-suite/GuitarPro/SelfTestImportPlausibility.cs` |
| TestImporterNamesAndDynamics |  | `tests/full-suite/GuitarPro/SelfTestImportContract.cs` |
| TestLongGuitarPro35Import | long-import | `tests/full-suite/GuitarPro/SelfTestLongGp5.cs` |
| TestMidiExport |  | `tests/full-suite/SelfTestCore.cs` |
| TestMusicXmlBarsFillTheTimeSignature |  | `tests/full-suite/GuitarPro/SelfTestImportContract.cs` |
| TestMusicXmlExport |  | `tests/full-suite/GuitarPro/SelfTestImportContract.cs` |
| TestMusicXmlGuitarPro8Encoding |  | `tests/full-suite/GuitarPro/SelfTestImportContract.cs` |
| TestMusicXmlHeaderForReaders |  | `tests/full-suite/GuitarPro/SelfTestImportContract.cs` |
| TestNoticeLimit |  | `tests/full-suite/GuitarPro/SelfTestNoticeLimit.cs` |
| TestRoundTripSemanticsSuite |  | `tests/full-suite/GuitarPro/SelfTestRoundTripSemantics.cs` |
| TestSyntheticGuitarProFixture | gp-fixtures | `tests/full-suite/GuitarPro/SelfTestGpFixture.cs` |
| TestTupletImport |  | `tests/full-suite/SelfTestCore.cs` |

#### hygiene

| Test | Group | File |
| --- | --- | --- |
| TestDebuggingDocInSync |  | `src/TabForge/SelfTests/Hygiene/SelfTestDebuggingDoc.cs` |
| TestFeatureMapInSync |  | `src/TabForge/SelfTests/Hygiene/SelfTestFeatureMap.cs` |
| TestInstallerAssociationParity |  | `src/TabForge/SelfTests/Hygiene/SelfTestSourceHygiene.cs` |
| TestLooseSoundTouchAndLicenseTexts |  | `src/TabForge/SelfTests/Hygiene/SelfTestSourceHygiene.cs` |
| TestOnlyOption |  | `src/TabForge/SelfTests/SelfTestOnly.cs` |
| TestPublicDocsConsistency |  | `src/TabForge/SelfTests/Hygiene/SelfTestDocsConsistency.cs` |
| TestRequireArgumentStrings |  | `src/TabForge/SelfTests/SelfTestRequirements.cs` |
| TestRequiredGroupGate |  | `src/TabForge/SelfTests/SelfTestRequirements.cs` |
| TestScreenGuard |  | `tests/full-suite/Views/SelfTestScreen.cs` |
| TestSourceControlCharacters |  | `src/TabForge/SelfTests/Hygiene/SelfTestSourceHygiene.cs` |
| TestStartHereAndRecipesInSync |  | `src/TabForge/SelfTests/Hygiene/SelfTestStartHere.cs` |

#### interactions

| Test | Group | File |
| --- | --- | --- |
| TestInteractions | interactions | `tests/full-suite/Lifecycle/SelfTestInteractions.cs` |

#### leaks

| Test | Group | File |
| --- | --- | --- |
| TestClosedDocumentChainsReleased |  | `tests/full-suite/Lifecycle/SelfTestLifecycle.cs` |
| TestRepeatedOpenCloseReleasesWindows |  | `tests/full-suite/Lifecycle/SelfTestLifecycle.cs` |
| TestRepeatedTearOffAndMergeReleasesWindows |  | `tests/full-suite/Lifecycle/SelfTestTabTransfer.cs` |

#### midi

| Test | Group | File |
| --- | --- | --- |
| TestMidiProcessors |  | `tests/full-suite/Views/SelfTestMixer.cs` |

#### notation

| Test | Group | File |
| --- | --- | --- |
| TestNoteNames |  | `tests/full-suite/Notation/SelfTestScaleFinder.cs` |
| TestTransposeAllVoices |  | `tests/full-suite/Editor/SelfTestTrackRows.cs` |

#### persistence

| Test | Group | File |
| --- | --- | --- |
| TestAsyncSaveSequencing |  | `tests/full-suite/Persistence/SelfTestPersistence.cs` |
| TestAudioDataSizeLimit |  | `tests/full-suite/Persistence/SelfTestPersistence.cs` |
| TestAudioTrackConversion |  | `tests/full-suite/Persistence/SelfTestAudioTrack.cs` |
| TestAudioTrackExports |  | `tests/full-suite/Persistence/SelfTestAudioTrackExports.cs` |
| TestAudioTrackModel |  | `tests/full-suite/Persistence/SelfTestAudioTrack.cs` |
| TestAudioTrackPersistence |  | `tests/full-suite/Persistence/SelfTestAudioTrack.cs` |
| TestAutosaveRecovery |  | `tests/full-suite/Persistence/SelfTestAutosave.cs` |
| TestClipboardServiceFallback |  | `tests/full-suite/Persistence/SelfTestClipboard.cs` |
| TestDirectTforgeOpenRecovers |  | `tests/full-suite/Persistence/SelfTestPairRecovery.cs` |
| TestEmbeddedProjectLimit |  | `tests/full-suite/Persistence/SelfTestPersistence.cs` |
| TestEmergencyRecoveryNames |  | `tests/full-suite/Persistence/SelfTestEmergencyRecovery.cs` |
| TestGpOpenKeepsTitle |  | `tests/full-suite/Persistence/SelfTestPersistence.cs` |
| TestMediaPathPolicy |  | `tests/full-suite/Recording/SelfTestMediaAccess.cs` |
| TestNightPluginApproval |  | `tests/full-suite/Engine/SelfTestNightPlugins.cs` |
| TestPairMarkerIsUntrusted |  | `tests/full-suite/Persistence/SelfTestPairRecovery.cs` |
| TestPairSaveEveryStage |  | `tests/full-suite/Persistence/SelfTestPairRecovery.cs` |
| TestPairSaveProcessKill |  | `tests/full-suite/Persistence/SelfTestPairRecovery.cs` |
| TestPairSaveRecovery |  | `tests/full-suite/Persistence/SelfTestPersistence.cs` |
| TestPersistenceSchema |  | `tests/full-suite/Persistence/SelfTestPersistence.cs` |
| TestPluginStateCollection |  | `tests/full-suite/Persistence/SelfTestPersistence.cs` |
| TestProfileLeavesUserFoldersUntouched |  | `tests/full-suite/Hygiene/SelfTestProfile.cs` |
| TestReaperChainImport |  | `tests/full-suite/Recording/SelfTestRecording.cs` |
| TestRecoveryCopyOverTforgeLimit |  | `tests/full-suite/Persistence/SelfTestPersistence.cs` |
| TestSaveTransactions |  | `tests/full-suite/Persistence/SelfTestPersistence.cs` |
| TestScoreClipCapture |  | `tests/full-suite/Persistence/SelfTestClipboard.cs` |
| TestScoreClipJson |  | `tests/full-suite/Persistence/SelfTestClipboard.cs` |
| TestScoreClipRejectsUntrustedInput |  | `tests/full-suite/Persistence/SelfTestClipboard.cs` |
| TestSettingsWithInlinePluginStates |  | `tests/full-suite/Recording/SelfTestRecording.cs` |
| TestSidecarRouting |  | `tests/full-suite/Persistence/SelfTestPersistence.cs` |
| TestSongRigs |  | `tests/full-suite/Persistence/SelfTestSongRigs.cs` |
| TestTforgeCompression |  | `tests/full-suite/GuitarPro/SelfTestImportContract.cs` |

#### playback

| Test | Group | File |
| --- | --- | --- |
| TestCapoRepitchesNotes |  | `tests/full-suite/Editor/SelfTestTrackRows.cs` |
| TestCountInIsHeard |  | `tests/full-suite/Playback/SelfTestPlayback.cs` |
| TestDelayLineClearKeepsNewMessage |  | `tests/full-suite/Playback/SelfTestPlayback.cs` |
| TestFermataPlayback |  | `tests/full-suite/Playback/SelfTestFermata.cs` |
| TestLiveEditBigSong |  | `tests/full-suite/Playback/SelfTestLiveEdit.cs` |
| TestLiveEditLoop |  | `tests/full-suite/Playback/SelfTestLiveEdit.cs` |
| TestMidiExportTiming |  | `tests/full-suite/Engine/SelfTestMidiTiming.cs` |
| TestNoHangingNotes |  | `tests/full-suite/Playback/SelfTestPlaybackHanging.cs` |
| TestNoOpOptionChangesDoNotRestartPlayback |  | `tests/full-suite/Playback/SelfTestPlayback.cs` |
| TestPlaybackDepth |  | `tests/full-suite/Playback/SelfTestPlayback.cs` |
| TestPlaybackOrderSpec |  | `tests/full-suite/Playback/SelfTestPlaybackOrderSpec.cs` |
| TestRenderBarRanges |  | `tests/full-suite/Notation/SelfTestRenderBounds.cs` |
| TestRepositionPathsSoundFirstNoteOnce |  | `tests/full-suite/Playback/SelfTestPlayback.cs` |
| TestSeekWhilePlayingSoundsFirstNote |  | `tests/full-suite/Playback/SelfTestPlayback.cs` |
| TestSeekWhilePlayingSoundsTargetNoteOnce |  | `tests/full-suite/Playback/SelfTestPlayback.cs` |
| TestSnapshotMarkCost |  | `tests/full-suite/Playback/SelfTestTimelineSnapshot.cs` |
| TestSoundingSetIncludesNoteAtPlayhead |  | `tests/full-suite/Playback/SelfTestPlayback.cs` |
| TestTempoMath |  | `tests/full-suite/Playback/SelfTestTempoMath.cs` |
| TestTimelineBasics |  | `tests/full-suite/SelfTestCore.cs` |
| TestTimelineLoopAndOrder |  | `tests/full-suite/SelfTestCore.cs` |
| TestTimelineMetronome |  | `tests/full-suite/SelfTestCore.cs` |
| TestTimelineRevision |  | `tests/full-suite/Views/SelfTestTimelineRevision.cs` |
| TestTimelineSnapshot |  | `tests/full-suite/Playback/SelfTestTimelineSnapshot.cs` |
| TestTimelineTechniques |  | `tests/full-suite/SelfTestCore.cs` |
| TestTypedNotePreview |  | `tests/full-suite/Playback/SelfTestTypedNotePreview.cs` |

#### recording

| Test | Group | File |
| --- | --- | --- |
| TestCaptureResampling |  | `tests/full-suite/Engine/SelfTestEngineWatchdog.cs` |
| TestClipMoves |  | `tests/full-suite/Recording/SelfTestClipMove.cs` |
| TestClipSplitGlueFades |  | `tests/full-suite/Recording/SelfTestClipSplitGlueFades.cs` |
| TestClips |  | `tests/full-suite/Recording/SelfTestClips.cs` |
| TestMediaDropPlan |  | `tests/full-suite/Recording/SelfTestMediaDrop.cs` |
| TestMidiFileToClip |  | `tests/full-suite/Recording/SelfTestMediaDrop.cs` |
| TestRecordingPipeline |  | `tests/full-suite/Recording/SelfTestRecording.cs` |
| TestSongExtent |  | `tests/full-suite/Recording/SelfTestSongExtent.cs` |
| TestSongFileDropRouting |  | `tests/full-suite/Recording/SelfTestMediaDrop.cs` |
| TestVirtualFileDrop |  | `tests/full-suite/Recording/SelfTestMediaDrop.cs` |
| TestWaveformCacheBounds |  | `tests/full-suite/Recording/SelfTestMediaAccess.cs` |

#### settings

| Test | Group | File |
| --- | --- | --- |
| TestAddTrackKeys |  | `tests/full-suite/Views/SelfTestKeyTextGuard.cs` |
| TestEngineDefaultOnAndManualOffSticks |  | `tests/full-suite/Settings/SelfTestSettingsStore.cs` |
| TestHotkeySettingsMigration |  | `tests/full-suite/Settings/SelfTestHotkeySlots.cs` |
| TestHotkeyTwoSlots |  | `tests/full-suite/Settings/SelfTestHotkeySlots.cs` |
| TestInstrumentSizeUnlockedByDefault |  | `tests/full-suite/Settings/SelfTestSettingsStore.cs` |
| TestPasteSettingsRows |  | `tests/full-suite/Persistence/SelfTestPasteDialog.cs` |
| TestPlayheadStyleSetting |  | `tests/full-suite/Views/SelfTestTimelineHover.cs` |
| TestPreferencesCatalog |  | `tests/full-suite/Settings/SelfTestPreferences.cs` |
| TestQuarantineAllowAgain |  | `tests/full-suite/Settings/SelfTestSecurity.cs` |
| TestSettingsStoreSharedAcrossWindows |  | `tests/full-suite/Settings/SelfTestSettingsStore.cs` |

#### smoke

| Test | Group | File |
| --- | --- | --- |
| TestEditorEntry |  | `src/TabForge/SelfTests/Smoke/SelfTestSmoke.cs` |
| TestEverySettingIsWired |  | `src/TabForge/SelfTests/Settings/SelfTestSettingsAudit.cs` |
| TestFretMarkerSize |  | `src/TabForge/SelfTests/Settings/SelfTestFretMarkerSize.cs` |
| TestHeadlessDeviceReconfigure |  | `src/TabForge/SelfTests/Engine/SelfTestEngineHeadless.cs` |
| TestModelRoundTrip |  | `src/TabForge/SelfTests/Smoke/SelfTestSmoke.cs` |
| TestProjectRoundtrip |  | `src/TabForge/SelfTests/Smoke/SelfTestSmoke.cs` |

#### synthetic

| Test | Group | File |
| --- | --- | --- |
| TestFullDemoSong |  | `tests/full-suite/GuitarPro/SelfTestFullDemoSong.cs` |
| TestSyntheticFixtures | synthetic-fixtures | `tests/full-suite/GuitarPro/SelfTestSyntheticFixtures.cs` |

#### tutorial

| Test | Group | File |
| --- | --- | --- |
| TestTutorialCommandAndSettings |  | `tests/full-suite/Views/SelfTestTutorial.cs` |
| TestTutorialGuides |  | `tests/full-suite/Views/SelfTestTutorial.cs` |
| TestTutorialMarkdown |  | `tests/full-suite/Views/SelfTestTutorial.cs` |
| TestTutorialPdfExport |  | `tests/full-suite/Views/SelfTestTutorial.cs` |
| TestTutorialSearch |  | `tests/full-suite/Views/SelfTestTutorial.cs` |
| TestTutorialWindow |  | `tests/full-suite/Views/SelfTestTutorial.cs` |

#### ui

| Test | Group | File |
| --- | --- | --- |
| TestAddTrackLane |  | `tests/full-suite/Editor/SelfTestAddTrackLane.cs` |
| TestArrangementFollowGeometry |  | `tests/full-suite/Playback/SelfTestArrangement.cs` |
| TestArrangementGeometry |  | `tests/full-suite/SelfTestCore.cs` |
| TestAudioInstrumentPanel |  | `tests/full-suite/Views/SelfTestAudioInstrumentPanel.cs` |
| TestAudioTrackEditorGuards |  | `tests/full-suite/Editor/SelfTestAddTrackLane.cs` |
| TestAudioTrackProperties |  | `tests/full-suite/Editor/SelfTestAddTrackLane.cs` |
| TestAutomationIds |  | `tests/full-suite/Views/SelfTestAutomationIds.cs` |
| TestBarAuditTool |  | `tests/full-suite/Notation/SelfTestBarAudit.cs` |
| TestBarFill |  | `tests/full-suite/Editor/SelfTestBarFill.cs` |
| TestBrowserTabShell |  | `tests/full-suite/SelfTestCore.cs` |
| TestCaptureMainWindowOffscreen |  | `tests/full-suite/Recording/SelfTestCapture.cs` |
| TestClefShapesAndChanges |  | `tests/full-suite/Notation/SelfTestEngravingCollisions.cs` |
| TestClipDragPress |  | `tests/full-suite/Recording/SelfTestClipDragPress.cs` |
| TestClipMoveGhost |  | `tests/full-suite/Recording/SelfTestClipMove.cs` |
| TestClipWaveformSpan |  | `tests/full-suite/Recording/SelfTestClipWaveformSpan.cs` |
| TestClosedTimelineIsCollected |  | `tests/full-suite/Recording/SelfTestMediaAccess.cs` |
| TestColourChoiceEntries |  | `tests/full-suite/Editor/SelfTestSelection.cs` |
| TestColourHexEquivalence |  | `tests/full-suite/Views/SelfTestColourHex.cs` |
| TestContextMenuLayouts |  | `tests/full-suite/Editor/SelfTestSelection.cs` |
| TestContextMenuLean |  | `tests/full-suite/Editor/SelfTestSelection.cs` |
| TestDeleteBarClean |  | `tests/full-suite/Editor/SelfTestRestMerge.cs` |
| TestDialogEscape |  | `tests/full-suite/Views/SelfTestDialogEscape.cs` |
| TestDockRatioNotRewrittenByAutoFit |  | `tests/full-suite/Views/SelfTestDockRatio.cs` |
| TestDrumEntryAndQuickAddBars |  | `tests/full-suite/Editor/SelfTestEditorInput.cs` |
| TestDynamicsEngraving |  | `tests/full-suite/Editor/SelfTestSelection.cs` |
| TestEditCommands |  | `tests/full-suite/Editor/SelfTestSelection.cs` |
| TestEditorCopyPaste |  | `tests/full-suite/SelfTestCore.cs` |
| TestEditorDurations |  | `tests/full-suite/SelfTestCore.cs` |
| TestEditorNavigation |  | `tests/full-suite/SelfTestCore.cs` |
| TestEditorShiftClickAndEffectDuration |  | `tests/full-suite/Editor/SelfTestEditorInput.cs` |
| TestEditorStructurePeer |  | `tests/full-suite/Editor/SelfTestEditorStructure.cs` |
| TestEngravingCollisions |  | `tests/full-suite/Notation/SelfTestEngravingCollisions.cs` |
| TestFlagShape |  | `tests/full-suite/Notation/SelfTestNotationBatch3.cs` |
| TestFretboardGeometry |  | `tests/full-suite/SelfTestCore.cs` |
| TestFretboardPaneSize |  | `tests/full-suite/Views/SelfTestFretboardPaneSize.cs` |
| TestGp5SvgIcons |  | `tests/full-suite/GuitarPro/SelfTestGp5.cs` |
| TestHarmonicNoteheadPositions |  | `tests/full-suite/Notation/SelfTestNotationBatch3.cs` |
| TestInstrumentArtwork |  | `tests/full-suite/GuitarPro/SelfTestGp5.cs` |
| TestInstrumentChoiceStrings |  | `tests/full-suite/Editor/SelfTestTrackRows.cs` |
| TestInstrumentVisualState |  | `tests/full-suite/SelfTestCore.cs` |
| TestKeyRoutingOrder |  | `tests/full-suite/Editor/SelfTestMenuKeys.cs` |
| TestKeyTextFollowsBindings |  | `tests/full-suite/Views/SelfTestKeyTextGuard.cs` |
| TestKeyboardContextMenuPlacement |  | `tests/full-suite/Editor/SelfTestKeyboardContextMenu.cs` |
| TestMarkStacking |  | `tests/full-suite/Notation/SelfTestMarkStacking.cs` |
| TestMediaDropPreviewGeometry |  | `tests/full-suite/Recording/SelfTestMediaDrop.cs` |
| TestMenuGestureTextFollowsBindings |  | `tests/full-suite/Editor/SelfTestMenuKeys.cs` |
| TestMoveNoteToAdjacentString |  | `tests/full-suite/Editor/SelfTestMenuKeys.cs` |
| TestNewBindableCommands |  | `tests/full-suite/Editor/SelfTestMenuKeys.cs` |
| TestNoHardWiredKeyText |  | `tests/full-suite/Views/SelfTestKeyTextGuard.cs` |
| TestNotationLayout |  | `tests/full-suite/Notation/SelfTestNotation.cs` |
| TestNoteEditAudit |  | `tests/full-suite/Editor/SelfTestCursorSnap.cs` |
| TestNoteEvents |  | `tests/full-suite/SelfTestCore.cs` |
| TestNoteMapper |  | `tests/full-suite/Notation/SelfTestNoteMapper.cs` |
| TestPasteCommands |  | `tests/full-suite/Persistence/SelfTestPasteCommands.cs` |
| TestPasteOptionsDialog |  | `tests/full-suite/Persistence/SelfTestPasteDialog.cs` |
| TestPasteSpecial |  | `tests/full-suite/Persistence/SelfTestPasteSpecial.cs` |
| TestPerControlTextDpi |  | `tests/full-suite/Settings/SelfTestSettingsStore.cs` |
| TestPickStrokeClearance |  | `tests/full-suite/Notation/SelfTestNotationBatch3.cs` |
| TestPlaybackGlowIntensity |  | `tests/full-suite/SelfTestCore.cs` |
| TestPlayingBar |  | `tests/full-suite/Editor/SelfTestPlayingBar.cs` |
| TestReadableTextTokens |  | `tests/full-suite/Editor/SelfTestSelection.cs` |
| TestRenderGuardContainment |  | `tests/full-suite/Views/SelfTestRenderGuard.cs` |
| TestResizeDuringPlayback |  | `tests/full-suite/Views/SelfTestResizeDuringPlayback.cs` |
| TestRestMerge |  | `tests/full-suite/Editor/SelfTestRestMerge.cs` |
| TestRuntimeIconAndResourceKeys |  | `tests/full-suite/GuitarPro/SelfTestGp5.cs` |
| TestScaleFinder |  | `tests/full-suite/Notation/SelfTestScaleFinder.cs` |
| TestScoreContextMenuByKeyboard |  | `tests/full-suite/Editor/SelfTestKeyboardContextMenu.cs` |
| TestSectionDeleteWording |  | `tests/full-suite/Editor/SelfTestMenuKeys.cs` |
| TestSelectionWholeBeats |  | `tests/full-suite/Editor/SelfTestWritingDuration.cs` |
| TestSelectionWideEdits |  | `tests/full-suite/Editor/SelfTestSelectionWideEdits.cs` |
| TestSimileBarHidesLinesAndTies |  | `tests/full-suite/Notation/SelfTestEngravingCollisions.cs` |
| TestSlideStrokesOnStaff |  | `tests/full-suite/Notation/SelfTestNotationBatch3.cs` |
| TestSplitterDragKeepsScore |  | `tests/full-suite/Views/SelfTestResizeDuringPlayback.cs` |
| TestStaffArcInsets |  | `tests/full-suite/Notation/SelfTestStaffArcs.cs` |
| TestSystemBreakPreferences |  | `tests/full-suite/GuitarPro/SelfTestGp5.cs` |
| TestTabEditorAutomationSnapshot |  | `tests/full-suite/Views/SelfTestTabEditorPins.cs` |
| TestTabEditorFrozenSystems |  | `tests/full-suite/Views/SelfTestTabEditorPins.cs` |
| TestTabEditorInputScript |  | `tests/full-suite/Views/SelfTestTabEditorPins.cs` |
| TestTabEditorLayoutMatrix |  | `tests/full-suite/Views/SelfTestTabEditorPins.cs` |
| TestTabEditorLifetime |  | `tests/full-suite/Views/SelfTestTabEditorPins.cs` |
| TestTabEditorPlaybackAllocation |  | `tests/full-suite/Views/SelfTestTabEditorPins.cs` |
| TestTabEditorPlayheadAndAppearance |  | `tests/full-suite/Views/SelfTestTabEditorPins.cs` |
| TestTabEditorRenderInvariance |  | `tests/full-suite/Views/SelfTestTabEditorPins.cs` |
| TestTabUi |  | `tests/full-suite/SelfTestCore.cs` |
| TestTempoBoxText |  | `tests/full-suite/Playback/SelfTestTempoBox.cs` |
| TestThemedCheckBoxAndProgressBar |  | `tests/full-suite/Views/SelfTestThemedControls.cs` |
| TestTimelineAndInstrumentContextMenuByKeyboard |  | `tests/full-suite/Editor/SelfTestKeyboardContextMenu.cs` |
| TestTimelineClipsShareClipboard |  | `tests/full-suite/Recording/SelfTestTimelineClips.cs` |
| TestTimelineContextMenus |  | `tests/full-suite/Recording/SelfTestTimelineClips.cs` |
| TestTimelineHoverAndAudioRows |  | `tests/full-suite/Views/SelfTestResizeDuringPlayback.cs` |
| TestTimelineHoverAndBarMarker |  | `tests/full-suite/Views/SelfTestTimelineHover.cs` |
| TestTimelineSectionCopiesAsBars |  | `tests/full-suite/Recording/SelfTestTimelineClips.cs` |
| TestTooltips |  | `tests/full-suite/Views/SelfTestTooltips.cs` |
| TestTrackColumnHeaderFit |  | `tests/full-suite/Editor/SelfTestTrackListFit.cs` |
| TestTrackListFit |  | `tests/full-suite/Editor/SelfTestTrackListFit.cs` |
| TestTrackRowMenu |  | `tests/full-suite/Editor/SelfTestTrackRowMenu.cs` |
| TestTrackRowRightClick |  | `tests/full-suite/Editor/SelfTestTrackRows.cs` |
| TestTrackRowsEndFlush |  | `tests/full-suite/Views/SelfTestResizeDuringPlayback.cs` |
| TestViewMenuWording |  | `tests/full-suite/Editor/SelfTestMenuKeys.cs` |
| TestVoice2HopoSlurWithLongerVoice |  | `tests/full-suite/Notation/SelfTestMarkStacking.cs` |
| TestWritingDuration |  | `tests/full-suite/Editor/SelfTestWritingDuration.cs` |

#### window-lifetime

| Test | Group | File |
| --- | --- | --- |
| TestWindowLifetime | window-lifetime | `tests/full-suite/Lifecycle/SelfTestWindowLifetime.cs` |

<!-- END GENERATED TESTS -->
