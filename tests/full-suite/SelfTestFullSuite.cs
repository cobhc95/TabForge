using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using TabForge.Controllers;
using TabForge.Diagnostics;
using TabForge.Docking;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Plugins;
using TabForge.Presets;
using TabForge.Services;
using TabForge.Shell;
using TabForge.Views;
using TabForge.Visualization;

namespace TabForge;

/// <summary>The full-suite registrations and area table. Compiled only with -p:TabForgeFullSuite=true (FULL_SUITE); see tests/full-suite/README.md.</summary>
public static partial class SelfTest
{
    /// <summary>Registers every full-suite test; runs before the basic set so the window-lifetime tests start on a clean process.</summary>
    static partial void RunFullSuite()
    {
        // Window lifetime runs first, on a clean process: it asserts that closed main windows are collectable. Run after the UI tests, the
        // last closed window is pinned by ref-counted UI Automation handles held by an out-of-process UIA client on the desktop.
        Section("Window lifetime (real main windows)");
        GuardGroup("window-lifetime", TestWindowLifetime);   // repeated close, cancelled close, queued work, tab transfer, Preferences owners, engine chains
        Section("Document context (media, approvals, close)");
        GuardGroup("document-context", TestDocumentContext);   // R2: explicit media / approval context, stale work, Save As, close with several dirty tabs
        Section("Document operations (explicit-document edits, entry-point parity)");
        GuardGroup("document-operations", TestDocumentOperations);   // R3: shared edits, keyboard vs menu, save / open / close sequences without a window
        GuardGroup("document-operations", TestAddTrackMenu);   // the Add-track lane prompt, the + Track menu, converting an audio track, through a real window
        GuardGroup("document-operations", TestAudioTrackClips);   // audio track part E: drop below the tracks, lanes, MIDI take, sections, conversion
        GuardGroup("document-operations", TestClipAndSectionEdits);   // W3-K: clip commands and section/bar/area flows through real windows
        GuardGroup("interactions", TestInteractions);   // W0-1: real windows driven through the interface's entry points; known-defect checks are reported, not required
        Section("MusicTime");
        Guard(TestBarSlots);
        Guard(TestCellSlots);
        Guard(TestAnalyzeBar);
        Guard(TestBarIncompleteMarking);
        Guard(TestCursorSnap);
        Guard(TestBarDeleteGuards);
        Guard(TestBarRangeGaps);
        Guard(TestBarGridPlacement);
        Section("Duplicate bar, time and key signatures");
        Guard(TestDuplicateBarAllTracks);
        Guard(TestSignaturesCarryForward);
        Guard(TestKeySignaturesCarryForward);
        Guard(TestSignatureRoundTrips);
        Section("Automatic pitch matching");
        Guard(TestPitchMatch);
        Section("Timeline / playback");
        Guard(TestTimelineBasics);
        Guard(TestTimelineTechniques);
        Guard(TestPlaybackDifferences);
        Guard(TestLiveEditBigSong);
        Guard(TestLiveEditLoop);
        Guard(TestNoHangingNotes);
        Guard(TestTypedNotePreview);
        Guard(TestNoticeLimit);
        GuardGroup("long-import", TestLongGuitarPro35Import);
        GuardGroup("long-import", TestImportPlausibility);   // damaged-file notice: a file with unreadable bytes opens with one short warning, real files never warn
        Guard(TestFermataPlayback);
        Guard(TestRenderBarRanges);
        Guard(TestMarkStacking);
        Guard(TestEngravingCollisions);
        Guard(TestSimileBarHidesLinesAndTies);
        Guard(TestClefShapesAndChanges);
        Guard(TestVoice2HopoSlurWithLongerVoice);
        Guard(TestNewBindableCommands);
        Guard(TestTrackRowMenu);
        Guard(TestClipDragPress);
        Guard(TestClipWaveformSpan);
        Guard(TestClipSplitGlueFades);
        Guard(TestKeyRoutingOrder);
        Guard(TestHotkeyTwoSlots);
        Guard(TestHotkeySettingsMigration);
        Guard(TestNoHardWiredKeyText);
        Guard(TestKeyTextFollowsBindings);
        Guard(TestAddTrackKeys);
        Guard(TestViewMenuWording);
        Guard(TestSectionDeleteWording);
        Guard(TestMenuGestureTextFollowsBindings);
        Guard(TestMoveNoteToAdjacentString);
        Guard(TestBarAuditTool);
        Guard(TestTimelineMetronome);
        Guard(TestTimelineLoopAndOrder);
        Guard(TestTimelineRevision);
        Guard(TestPlaybackOrderSpec);
        Guard(TestTempoMath);
        Guard(TestTimelineSnapshot);
        Guard(TestSnapshotMarkCost);
        Guard(TestNoteNames);
        Section("Editor");
        Guard(TestEditorNavigation);
        Guard(TestEditorDurations);
        Guard(TestPlaybackGlowIntensity);
        Guard(TestEditorCopyPaste);
        Guard(TestNoteMapper);
        Guard(TestScoreClipCapture);
        Guard(TestScoreClipJson);
        Guard(TestScoreClipRejectsUntrustedInput);
        Guard(TestClipboardServiceFallback);
        Guard(TestTimelineClipsShareClipboard);
        Guard(TestTimelineSectionCopiesAsBars);
        Guard(TestTimelineContextMenus);
        Guard(TestPasteCommands);
        Guard(TestPasteSpecial);
        Guard(TestSelectionModel);
        Guard(TestKnobTypeIn);
        Guard(TestTooltips);
        Guard(TestTrackRowRightClick);
        Guard(TestAddTrackLane);
        Guard(TestAudioTrackEditorGuards);
        Guard(TestAudioTrackProperties);
        Guard(TestInstrumentChoiceStrings);
        Guard(TestTransposeAllVoices);
        Guard(TestCapoRepitchesNotes);
        Guard(TestContextMenuLayouts);
        Guard(TestContextMenuLean);
        Guard(TestPreferencesCatalog);
        Guard(TestRenderGuardContainment);
        Guard(TestDialogEscape);
        Guard(TestTutorialMarkdown);
        Guard(TestTutorialSearch);
        Guard(TestTutorialWindow);
        Guard(TestTutorialPdfExport);
        Guard(TestTutorialCommandAndSettings);
        Guard(TestTutorialGuides);
        Guard(TestTempoBoxText);
        Guard(TestStaffArcInsets);
        Guard(TestHarmonicNoteheadPositions);
        Guard(TestFlagShape);
        Guard(TestSlideStrokesOnStaff);
        Guard(TestPickStrokeClearance);
        Guard(TestAutomationIds);
        Guard(TestScoreContextMenuByKeyboard);
        Guard(TestEditorShiftClickAndEffectDuration);
        Guard(TestDrumEntryAndQuickAddBars);
        Guard(TestKeyboardContextMenuPlacement);
        Guard(TestTimelineAndInstrumentContextMenuByKeyboard);
        Guard(TestThemedCheckBoxAndProgressBar);
        Guard(TestDockRatioNotRewrittenByAutoFit);
        Guard(TestTrackListFit);
        Guard(TestResizeDuringPlayback);
        Guard(TestTrackRowsEndFlush);
        Guard(TestSplitterDragKeepsScore);
        Guard(TestTimelineHoverAndAudioRows);
        Guard(TestTrackColumnHeaderFit);
        Guard(TestSectionsPaneScrollsWhenShort);
        Guard(TestFollowSurvivesZoom);
        Guard(TestZoomComboShowsValue);
        Guard(TestSpeedControl);
        Guard(TestMixerSliders);
        Guard(TestMixerSlidersRealInput);
        Guard(TestMixerMuteSoloClick);
        Guard(TestTrackListFollowsMixerInPlace);
        Guard(TestSliderMappingAndGroupValues);
        Guard(TestTrackOrderingModel);
        Guard(TestMixerDragAndDrop);
        Guard(TestTrackListGroupRows);
        Guard(TestOrderAnimationAndSpeedCommands);
        Guard(TestRepeatedOpenCloseReleasesWindows);   // needs the application alive: keep it with the other window tests
        Guard(TestClosedDocumentChainsReleased);
        Guard(TestRepeatedTearOffAndMergeReleasesWindows);
        Guard(TestCommandPaletteAndPdf);
        Guard(TestTechniqueEngraving);
        Guard(TestLayoutAuditTechniqueSong);
        Guard(TestDynamicsEngraving);
        Guard(TestTemplateKeepsSetupOnly);
        Guard(TestSnapLayoutHitTest);
        Guard(TestEditorSelectionState);
        Guard(TestAutomationPeers);
        Guard(TestEditorStructurePeer);
        Guard(TestEditCommands);
        Guard(TestSelectionWideEdits);
        Guard(TestBarFill);
        Guard(TestWritingDuration);
        Guard(TestSelectionWholeBeats);
        Guard(TestRestMerge);
        Guard(TestDeleteBarClean);
        Guard(TestNoteEditAudit);
        Guard(TestReadableTextTokens);
        Guard(TestColourChoiceEntries);
        Guard(TestEngravingHeader);
        Guard(TestUserTemplatesAndFaultedChain);
        Section("Standard-notation engraving layout");
        Guard(TestNotationLayout);
        Section("Project IO / export");
        Guard(TestTrackReorder);
        Guard(TestAsciiExport);
        Guard(TestMidiExport);
        Guard(TestMidiExportTiming);
        Guard(TestTupletImport);
        Guard(TestTabUi);
        Section("Documents");
        Guard(TestDocuments);
        Guard(TestUndoController);
        Guard(TestUndoDeltaStates);
        Guard(TestEditControllers);
        Section("Window shell");
        Guard(TestBrowserTabShell);
        Section("Visualisation");
        Guard(TestNoteEvents);
        Guard(TestInstrumentVisualState);
        Guard(TestFretboardGeometry);
        Guard(TestFretboardPaneSize);
        Guard(TestAudioInstrumentPanel);
        Section("Arrangement geometry (precision)");
        Guard(TestArrangementGeometry);
        Guard(TestArrangementFollowGeometry);
        Section("Guitar Pro compatibility (real files)");
        GuardGroup("gp-fixtures", TestSyntheticGuitarProFixture);
        Guard(TestGuitarProFiles);
        Guard(TestGuitarProImportContainment);
        Guard(TestGuitarProImportWorker);
        Guard(TestRoundTripSemanticsSuite);
        Guard(TestGpRoundTripFixes);
        GuardGroup("gp-fidelity", TestGpFidelity);
        GuardGroup("gp-fidelity", TestGpMixerExact);
        GuardGroup("gp-fidelity", TestGpTrillSpeed);
        GuardGroup("gp-fidelity", TestGpLossCoverage);
        GuardGroup("gp-fidelity", TestGpCompatibilityDoc);
        Section("Synthetic fixtures (run everywhere, no local songs)");
        GuardGroup("synthetic-fixtures", TestSyntheticFixtures);
        Guard(TestFullDemoSong);
        Section("Playback depth (timing / ties / channels)");
        Guard(TestPlaybackDepth);
        Guard(TestNoOpOptionChangesDoNotRestartPlayback);
        Guard(TestSeekWhilePlayingSoundsFirstNote);
        Guard(TestSeekWhilePlayingSoundsTargetNoteOnce);
        Guard(TestDelayLineClearKeepsNewMessage);
        Guard(TestRepositionPathsSoundFirstNoteOnce);
        Guard(TestSoundingSetIncludesNoteAtPlayhead);
        Guard(TestCountInIsHeard);
        Guard(TestPasteOptionsDialog);
        Guard(TestPasteSettingsRows);
        Guard(TestSettingsStoreSharedAcrossWindows);
        Guard(TestEngineWarmOwnership);
        Guard(TestEngineMultiTabPlayback);
        Guard(TestEngineDefaultOnAndManualOffSticks);
        Guard(TestInstrumentSizeUnlockedByDefault);
        Guard(TestPerControlTextDpi);
        Guard(TestScaleFinder);
        Guard(TestMixer);
        Guard(TestMidiProcessors);
        Guard(TestRecordingPipeline);
        Guard(TestSaveTransactions);
        Guard(TestPairSaveRecovery);
        Guard(TestPairMarkerIsUntrusted);
        Guard(TestDirectTforgeOpenRecovers);
        Guard(TestSongRigs);
        Guard(TestPairSaveEveryStage);
        Guard(TestPairSaveProcessKill);
        Guard(TestProfileLeavesUserFoldersUntouched);
        Guard(TestCaptureMainWindowOffscreen);
        Guard(TestNightPluginApproval);
        Guard(TestPluginRightsNotice);
        Guard(TestAsyncSaveSequencing);
        Guard(TestPluginStateCollection);
        Guard(TestQuarantineAllowAgain);
        Guard(TestSidecarRouting);
        Guard(TestAudioDataSizeLimit);
        Guard(TestGpOpenKeepsTitle);
        Guard(TestEmbeddedProjectLimit);
        Guard(TestRecoveryCopyOverTforgeLimit);
        Guard(TestPersistenceSchema);
        Guard(TestAudioTrackModel);
        Guard(TestAudioTrackPersistence);
        Guard(TestAudioTrackConversion);
        Guard(TestAudioTrackExports);
        Guard(TestImporterNamesAndDynamics);
        Guard(TestTforgeCompression);
        Guard(TestCleanGpExportKeepsFeatures);
        Guard(TestMusicXmlExport);
        Guard(TestMusicXmlBarsFillTheTimeSignature);
        Guard(TestMusicXmlHeaderForReaders);
        Guard(TestMusicXmlGuitarPro8Encoding);
        Guard(TestIsolatedPluginGenerations);
        Guard(TestIsolatedCallbackBudget);
        Guard(TestIsolatedControlChannel);
        Guard(TestRetirementEpochBarrier);
        Guard(TestMonitorFx);
        Guard(TestEngineSlotsFollowTrackIdentity);
        Guard(TestEnginePluginEditAndStatesByIdentity);
        Guard(TestGainBitIdentical);
        Guard(TestGainRenderHash);
        Guard(TestSharedRingProducers);
        Guard(TestEngineExitCleanupOnUi);
        Guard(TestMuteSoloTruthTable);
        Guard(TestMuteSoloIsInstant);
        Guard(TestMuteSoloDoesNotDisturbPlayback);
        Guard(TestMuteDimmingIsInstant);
        Guard(TestMutedTrackDimmingSetting);
        Guard(TestMuteHoldsAcrossSeekAndRestart);
        Guard(TestRenderHonoursMute);
        Guard(TestClipChurnWhileStreaming);
        Guard(TestDiskStreamerIdle);
        Guard(TestEngineOwnerTransports);
        Guard(TestEngineOwnerIdLifecycle);
        Guard(TestRealtimePolish);
        Guard(TestPluginFactorySingleSource);
        Guard(TestWatchdogPolicy);
        Guard(TestCommandFrameRobustness);
        Guard(TestPumpOnce);
        Guard(TestTunerPitchDetection);
        Guard(TestEffectChannel);
        Guard(TestDrumAndMelodicDispatchTogether);
        Guard(TestColdStartSetupSurvivesPanic);
        Guard(TestChildProcessJob);
        Guard(TestEngineLivenessAndSlowLoad);
        Guard(TestCaptureResampling);
        Guard(TestRoutingCycles);
        Guard(TestAutoGmEveryPath);
        Guard(TestAudioTrackRouting);
        Guard(TestAudioTrackLiveMidi);
        Guard(TestReaperChainImport);
        Guard(TestSettingsWithInlinePluginStates);
        Guard(TestDataIntegrityLeftovers);
        Guard(TestAutosaveRecovery);
        Guard(TestEmergencyRecoveryNames);
        Guard(TestCallbackMetrics);
        Guard(TestAudioAudit);
        Guard(TestAudioAuditRepeatedSections);
        Guard(TestClips);
        Guard(TestMediaDropPlan);
        Guard(TestMidiFileToClip);
        Guard(TestVirtualFileDrop);
        Guard(TestSongFileDropRouting);
        Guard(TestUpdateCheck);
        Section("Editing semantics (reference behaviour)");
        Guard(TestGp5EditingSemantics);
        Guard(TestPerNoteDurationPercent);
        Guard(TestGp5SvgIcons);
        Guard(TestInstrumentArtwork);
        Guard(TestRuntimeIconAndResourceKeys);
        Guard(TestSystemBreakPreferences);
        Section("Security / untrusted inputs");
        Guard(TestSecurityInputBoundaries);
        Guard(TestMediaPathPolicy);
        Guard(TestWaveformCacheBounds);
        Guard(TestClosedTimelineIsCollected);
        Guard(TestPlayheadStyleSetting);
        Guard(TestTimelineHoverAndBarMarker);
        Guard(TestMediaDropPreviewGeometry);
        Guard(TestClipMoves);
        Guard(TestClipMoveGhost);
        Guard(TestSongExtent);
        Guard(TestSectionClips);
        Section("Malformed-input fuzzing and lifecycle");
        GuardGroup("fuzz", TestMalformedInputFuzz);
        Section("Repository hygiene");
        Guard(TestScreenGuard);
        Guard(TestTrimMerges);
        Section("Architecture (layering)");
        Guard(TestColourHexEquivalence);
        Guard(TestTraceSwitchAreas);
        Guard(TestPlayingBar);
        Guard(TestTabEditorRenderInvariance);
        Guard(TestTabEditorPlaybackAllocation);
        Guard(TestTabEditorLayoutMatrix);
        Guard(TestTabEditorFrozenSystems);
        Guard(TestTabEditorInputScript);
        Guard(TestTabEditorAutomationSnapshot);
        Guard(TestTabEditorLifetime);
        Guard(TestTabEditorPlayheadAndAppearance);

    }

    /// <summary>Areas of the full-suite tests (the basic set's areas are in SelfTest.cs).</summary>
    static partial void AddFullSuiteAreas(Dictionary<string, string> areas)
    {
        foreach (var (test, area) in FullSuiteAreas()) areas[test] = area;
    }

    private static Dictionary<string, string> FullSuiteAreas() => new(StringComparer.Ordinal)
    {
        ["TestAudioAudit"] = "audioaudit",
        ["TestAudioAuditRepeatedSections"] = "audioaudit",
        ["TestAnalyzeBar"] = "core",
        ["TestAutomationPeers"] = "core",
        ["TestBarGridPlacement"] = "core",
        ["TestBarIncompleteMarking"] = "core",
        ["TestBarSlots"] = "core",
        ["TestCellSlots"] = "core",
        ["TestCommandPaletteAndPdf"] = "core",
        ["TestCursorSnap"] = "core",
        ["TestBarDeleteGuards"] = "document-operations",
        ["TestBarRangeGaps"] = "document-operations",
        ["TestDataIntegrityLeftovers"] = "core",
        ["TestDocuments"] = "core",
        ["TestDuplicateBarAllTracks"] = "core",
        ["TestEditControllers"] = "core",
        ["TestEditorSelectionState"] = "core",
        ["TestEngravingHeader"] = "core",
        ["TestFollowSurvivesZoom"] = "core",
        ["TestKeySignaturesCarryForward"] = "core",
        ["TestKnobTypeIn"] = "core",
        ["TestLayoutAuditTechniqueSong"] = "core",
        ["TestMixerDragAndDrop"] = "core",
        ["TestMixerSliders"] = "core",
        ["TestMixerSlidersRealInput"] = "core",
        ["TestMixerMuteSoloClick"] = "core",
        ["TestOrderAnimationAndSpeedCommands"] = "core",
        ["TestPerNoteDurationPercent"] = "core",
        ["TestPlaybackDifferences"] = "core",
        ["TestPluginRightsNotice"] = "core",
        ["TestSectionsPaneScrollsWhenShort"] = "core",
        ["TestSecurityInputBoundaries"] = "core",
        ["TestSelectionModel"] = "core",
        ["TestSignatureRoundTrips"] = "core",
        ["TestSignaturesCarryForward"] = "core",
        ["TestSliderMappingAndGroupValues"] = "core",
        ["TestSnapLayoutHitTest"] = "core",
        ["TestSpeedControl"] = "core",
        ["TestTechniqueEngraving"] = "core",
        ["TestTemplateKeepsSetupOnly"] = "core",
        ["TestTraceSwitchAreas"] = "core",
        ["TestTrackListFollowsMixerInPlace"] = "core",
        ["TestTrackListGroupRows"] = "core",
        ["TestTrackOrderingModel"] = "core",
        ["TestTrackReorder"] = "core",
        ["TestTrimMerges"] = "core",
        ["TestUndoController"] = "core",
        ["TestUndoDeltaStates"] = "core",
        ["TestUpdateCheck"] = "core",
        ["TestUserTemplatesAndFaultedChain"] = "core",
        ["TestZoomComboShowsValue"] = "core",
        ["TestDocumentContext"] = "document-context",
        ["TestAddTrackMenu"] = "document-operations",
        ["TestAudioTrackClips"] = "document-operations",
        ["TestClipAndSectionEdits"] = "document-operations",
        ["TestDocumentOperations"] = "document-operations",
        ["TestSectionClips"] = "document-operations",
        ["TestAudioTrackRouting"] = "engine",
        ["TestAudioTrackLiveMidi"] = "engine",
        ["TestAutoGmEveryPath"] = "engine",
        ["TestCallbackMetrics"] = "engine",
        ["TestChildProcessJob"] = "engine",
        ["TestClipChurnWhileStreaming"] = "engine",
        ["TestColdStartSetupSurvivesPanic"] = "engine",
        ["TestCommandFrameRobustness"] = "engine",
        ["TestDiskStreamerIdle"] = "engine",
        ["TestDrumAndMelodicDispatchTogether"] = "engine",
        ["TestEffectChannel"] = "engine",
        ["TestEngineExitCleanupOnUi"] = "engine",
        ["TestEngineLivenessAndSlowLoad"] = "engine",
        ["TestEngineMultiTabPlayback"] = "engine",
        ["TestEngineOwnerIdLifecycle"] = "engine",
        ["TestEngineOwnerTransports"] = "engine",
        ["TestEnginePluginEditAndStatesByIdentity"] = "engine",
        ["TestEngineSlotsFollowTrackIdentity"] = "engine",
        ["TestEngineWarmOwnership"] = "engine",
        ["TestGainBitIdentical"] = "engine",
        ["TestGainRenderHash"] = "engine",
        ["TestGmLevelCalibration"] = "engine",
        ["TestIsolatedCallbackBudget"] = "engine",
        ["TestIsolatedControlChannel"] = "engine",
        ["TestIsolatedPluginGenerations"] = "engine",
        ["TestMixer"] = "engine",
        ["TestMonitorFx"] = "engine",
        ["TestMuteDimmingIsInstant"] = "engine",
        ["TestMuteHoldsAcrossSeekAndRestart"] = "engine",
        ["TestMuteSoloDoesNotDisturbPlayback"] = "engine",
        ["TestMuteSoloIsInstant"] = "engine",
        ["TestMuteSoloTruthTable"] = "engine",
        ["TestMutedTrackDimmingSetting"] = "engine",
        ["TestPitchMatch"] = "engine",
        ["TestPluginFactorySingleSource"] = "engine",
        ["TestPumpOnce"] = "engine",
        ["TestRealtimePolish"] = "engine",
        ["TestRenderHonoursMute"] = "engine",
        ["TestRetirementEpochBarrier"] = "engine",
        ["TestRoutingCycles"] = "engine",
        ["TestSharedRingProducers"] = "engine",
        ["TestTunerPitchDetection"] = "engine",
        ["TestWatchdogPolicy"] = "engine",
        ["TestMalformedInputFuzz"] = "fuzz",
        ["TestAsciiExport"] = "guitarpro",
        ["TestCleanGpExportKeepsFeatures"] = "guitarpro",
        ["TestGp5EditingSemantics"] = "guitarpro",
        ["TestGpCompatibilityDoc"] = "guitarpro",
        ["TestGpFidelity"] = "guitarpro",
        ["TestGpLossCoverage"] = "guitarpro",
        ["TestGpMixerExact"] = "guitarpro",
        ["TestGpRoundTripFixes"] = "guitarpro",
        ["TestGpTrillSpeed"] = "guitarpro",
        ["TestGuitarProFiles"] = "guitarpro",
        ["TestGuitarProImportContainment"] = "guitarpro",
        ["TestGuitarProImportWorker"] = "guitarpro",
        ["TestImportPlausibility"] = "guitarpro",
        ["TestImporterNamesAndDynamics"] = "guitarpro",
        ["TestLongGuitarPro35Import"] = "guitarpro",
        ["TestMidiExport"] = "guitarpro",
        ["TestMusicXmlBarsFillTheTimeSignature"] = "guitarpro",
        ["TestMusicXmlExport"] = "guitarpro",
        ["TestMusicXmlGuitarPro8Encoding"] = "guitarpro",
        ["TestMusicXmlHeaderForReaders"] = "guitarpro",
        ["TestNoticeLimit"] = "guitarpro",
        ["TestRoundTripSemanticsSuite"] = "guitarpro",
        ["TestSyntheticGuitarProFixture"] = "guitarpro",
        ["TestTupletImport"] = "guitarpro",
        ["TestScreenGuard"] = "hygiene",
        ["TestInteractions"] = "interactions",
        ["TestClosedDocumentChainsReleased"] = "leaks",
        ["TestRepeatedOpenCloseReleasesWindows"] = "leaks",
        ["TestRepeatedTearOffAndMergeReleasesWindows"] = "leaks",
        ["TestMidiProcessors"] = "midi",
        ["TestNoteNames"] = "notation",
        ["TestTransposeAllVoices"] = "notation",
        ["TestAsyncSaveSequencing"] = "persistence",
        ["TestAudioDataSizeLimit"] = "persistence",
        ["TestAudioTrackConversion"] = "persistence",
        ["TestAudioTrackExports"] = "persistence",
        ["TestAudioTrackModel"] = "persistence",
        ["TestAudioTrackPersistence"] = "persistence",
        ["TestAutosaveRecovery"] = "persistence",
        ["TestEmergencyRecoveryNames"] = "persistence",
        ["TestClipboardServiceFallback"] = "persistence",
        ["TestDirectTforgeOpenRecovers"] = "persistence",
        ["TestEmbeddedProjectLimit"] = "persistence",
        ["TestGpOpenKeepsTitle"] = "persistence",
        ["TestMediaPathPolicy"] = "persistence",
        ["TestNightPluginApproval"] = "persistence",
        ["TestPairMarkerIsUntrusted"] = "persistence",
        ["TestPairSaveEveryStage"] = "persistence",
        ["TestPairSaveProcessKill"] = "persistence",
        ["TestPairSaveRecovery"] = "persistence",
        ["TestPersistenceSchema"] = "persistence",
        ["TestPluginStateCollection"] = "persistence",
        ["TestProfileLeavesUserFoldersUntouched"] = "persistence",
        ["TestReaperChainImport"] = "persistence",
        ["TestRecoveryCopyOverTforgeLimit"] = "persistence",
        ["TestSaveTransactions"] = "persistence",
        ["TestScoreClipCapture"] = "persistence",
        ["TestScoreClipJson"] = "persistence",
        ["TestScoreClipRejectsUntrustedInput"] = "persistence",
        ["TestSettingsWithInlinePluginStates"] = "persistence",
        ["TestSidecarRouting"] = "persistence",
        ["TestSongRigs"] = "persistence",
        ["TestTforgeCompression"] = "persistence",
        ["TestCapoRepitchesNotes"] = "playback",
        ["TestCountInIsHeard"] = "playback",
        ["TestDelayLineClearKeepsNewMessage"] = "playback",
        ["TestFermataPlayback"] = "playback",
        ["TestLiveEditBigSong"] = "playback",
        ["TestLiveEditLoop"] = "playback",
        ["TestMidiExportTiming"] = "playback",
        ["TestNoHangingNotes"] = "playback",
        ["TestNoOpOptionChangesDoNotRestartPlayback"] = "playback",
        ["TestPlaybackDepth"] = "playback",
        ["TestPlaybackOrderSpec"] = "playback",
        ["TestRenderBarRanges"] = "playback",
        ["TestRepositionPathsSoundFirstNoteOnce"] = "playback",
        ["TestSeekWhilePlayingSoundsFirstNote"] = "playback",
        ["TestSeekWhilePlayingSoundsTargetNoteOnce"] = "playback",
        ["TestSnapshotMarkCost"] = "playback",
        ["TestSoundingSetIncludesNoteAtPlayhead"] = "playback",
        ["TestTempoMath"] = "playback",
        ["TestTimelineBasics"] = "playback",
        ["TestTimelineLoopAndOrder"] = "playback",
        ["TestTimelineMetronome"] = "playback",
        ["TestTimelineRevision"] = "playback",
        ["TestTimelineSnapshot"] = "playback",
        ["TestTimelineTechniques"] = "playback",
        ["TestTypedNotePreview"] = "playback",
        ["TestCaptureResampling"] = "recording",
        ["TestClipMoves"] = "recording",
        ["TestClipSplitGlueFades"] = "recording",
        ["TestClips"] = "recording",
        ["TestMediaDropPlan"] = "recording",
        ["TestMidiFileToClip"] = "recording",
        ["TestRecordingPipeline"] = "recording",
        ["TestSongExtent"] = "recording",
        ["TestSongFileDropRouting"] = "recording",
        ["TestVirtualFileDrop"] = "recording",
        ["TestWaveformCacheBounds"] = "recording",
        ["TestAddTrackKeys"] = "settings",
        ["TestEngineDefaultOnAndManualOffSticks"] = "settings",
        ["TestHotkeySettingsMigration"] = "settings",
        ["TestHotkeyTwoSlots"] = "settings",
        ["TestInstrumentSizeUnlockedByDefault"] = "settings",
        ["TestPasteSettingsRows"] = "settings",
        ["TestPlayheadStyleSetting"] = "settings",
        ["TestPreferencesCatalog"] = "settings",
        ["TestQuarantineAllowAgain"] = "settings",
        ["TestSettingsStoreSharedAcrossWindows"] = "settings",
        ["TestFullDemoSong"] = "synthetic",
        ["TestSyntheticFixtures"] = "synthetic",
        ["TestTutorialCommandAndSettings"] = "tutorial",
        ["TestTutorialGuides"] = "tutorial",
        ["TestTutorialMarkdown"] = "tutorial",
        ["TestTutorialPdfExport"] = "tutorial",
        ["TestTutorialSearch"] = "tutorial",
        ["TestTutorialWindow"] = "tutorial",
        ["TestAddTrackLane"] = "ui",
        ["TestArrangementFollowGeometry"] = "ui",
        ["TestArrangementGeometry"] = "ui",
        ["TestAudioInstrumentPanel"] = "ui",
        ["TestAudioTrackEditorGuards"] = "ui",
        ["TestAudioTrackProperties"] = "ui",
        ["TestAutomationIds"] = "ui",
        ["TestBarAuditTool"] = "ui",
        ["TestBarFill"] = "ui",
        ["TestBrowserTabShell"] = "ui",
        ["TestCaptureMainWindowOffscreen"] = "ui",
        ["TestClefShapesAndChanges"] = "ui",
        ["TestClipMoveGhost"] = "ui",
        ["TestClosedTimelineIsCollected"] = "ui",
        ["TestColourChoiceEntries"] = "ui",
        ["TestColourHexEquivalence"] = "ui",
        ["TestContextMenuLayouts"] = "ui",
        ["TestContextMenuLean"] = "ui",
        ["TestDialogEscape"] = "ui",
        ["TestDockRatioNotRewrittenByAutoFit"] = "ui",
        ["TestDrumEntryAndQuickAddBars"] = "ui",
        ["TestDynamicsEngraving"] = "ui",
        ["TestEditCommands"] = "ui",
        ["TestEditorCopyPaste"] = "ui",
        ["TestEditorDurations"] = "ui",
        ["TestEditorNavigation"] = "ui",
        ["TestEditorShiftClickAndEffectDuration"] = "ui",
        ["TestEditorStructurePeer"] = "ui",
        ["TestEngravingCollisions"] = "ui",
        ["TestFlagShape"] = "ui",
        ["TestFretboardGeometry"] = "ui",
        ["TestFretboardPaneSize"] = "ui",
        ["TestGp5SvgIcons"] = "ui",
        ["TestHarmonicNoteheadPositions"] = "ui",
        ["TestInstrumentArtwork"] = "ui",
        ["TestInstrumentChoiceStrings"] = "ui",
        ["TestInstrumentVisualState"] = "ui",
        ["TestKeyRoutingOrder"] = "ui",
        ["TestKeyTextFollowsBindings"] = "ui",
        ["TestKeyboardContextMenuPlacement"] = "ui",
        ["TestMarkStacking"] = "ui",
        ["TestMediaDropPreviewGeometry"] = "ui",
        ["TestMenuGestureTextFollowsBindings"] = "ui",
        ["TestMoveNoteToAdjacentString"] = "ui",
        ["TestNewBindableCommands"] = "ui",
        ["TestNoHardWiredKeyText"] = "ui",
        ["TestNotationLayout"] = "ui",
        ["TestNoteEvents"] = "ui",
        ["TestNoteMapper"] = "ui",
        ["TestPasteCommands"] = "ui",
        ["TestPasteOptionsDialog"] = "ui",
        ["TestPasteSpecial"] = "ui",
        ["TestPerControlTextDpi"] = "ui",
        ["TestPickStrokeClearance"] = "ui",
        ["TestPlaybackGlowIntensity"] = "ui",
        ["TestPlayingBar"] = "ui",
        ["TestReadableTextTokens"] = "ui",
        ["TestRenderGuardContainment"] = "ui",
        ["TestResizeDuringPlayback"] = "ui",
        ["TestRestMerge"] = "ui",
        ["TestDeleteBarClean"] = "ui",
        ["TestNoteEditAudit"] = "ui",
        ["TestRuntimeIconAndResourceKeys"] = "ui",
        ["TestScaleFinder"] = "ui",
        ["TestScoreContextMenuByKeyboard"] = "ui",
        ["TestSectionDeleteWording"] = "ui",
        ["TestSelectionWholeBeats"] = "ui",
        ["TestSelectionWideEdits"] = "ui",
        ["TestSimileBarHidesLinesAndTies"] = "ui",
        ["TestSlideStrokesOnStaff"] = "ui",
        ["TestSplitterDragKeepsScore"] = "ui",
        ["TestStaffArcInsets"] = "ui",
        ["TestSystemBreakPreferences"] = "ui",
        ["TestTabEditorAutomationSnapshot"] = "ui",
        ["TestTabEditorFrozenSystems"] = "ui",
        ["TestTabEditorInputScript"] = "ui",
        ["TestTabEditorLayoutMatrix"] = "ui",
        ["TestTabEditorLifetime"] = "ui",
        ["TestTabEditorPlaybackAllocation"] = "ui",
        ["TestTabEditorPlayheadAndAppearance"] = "ui",
        ["TestTabEditorRenderInvariance"] = "ui",
        ["TestTabUi"] = "ui",
        ["TestTempoBoxText"] = "ui",
        ["TestThemedCheckBoxAndProgressBar"] = "ui",
        ["TestTimelineAndInstrumentContextMenuByKeyboard"] = "ui",
        ["TestTimelineClipsShareClipboard"] = "ui",
        ["TestTimelineContextMenus"] = "ui",
        ["TestTimelineHoverAndAudioRows"] = "ui",
        ["TestTimelineHoverAndBarMarker"] = "ui",
        ["TestTimelineSectionCopiesAsBars"] = "ui",
        ["TestTooltips"] = "ui",
        ["TestTrackColumnHeaderFit"] = "ui",
        ["TestTrackListFit"] = "ui",
        ["TestTrackRowRightClick"] = "ui", ["TestTrackRowMenu"] = "ui", ["TestClipDragPress"] = "ui", ["TestClipWaveformSpan"] = "ui",
        ["TestTrackRowsEndFlush"] = "ui",
        ["TestViewMenuWording"] = "ui",
        ["TestVoice2HopoSlurWithLongerVoice"] = "ui",
        ["TestWritingDuration"] = "ui",
        ["TestWindowLifetime"] = "window-lifetime",
    };
}
