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
        GuardGroup("window-lifetime", TestWindowLifetime);
        GuardGroup("window-lifetime", TestStartupFileOpen);   // a command-line song and a second-launch hand-over open in a fresh window (.gp, .gp5, .tforge)   // repeated close, cancelled close, queued work, tab transfer, Preferences owners, engine chains
        GuardGroup("window-lifetime", TestSingleInstanceProcessHandover);   // Explorer-style hand-over between two real app processes and last-window shutdown
        Section("Document context (media, approvals, close)");
        GuardGroup("document-context", TestDocumentContext);   // R2: explicit media / approval context, stale work, Save As, close with several dirty tabs
        Section("Document operations (explicit-document edits, entry-point parity)");
        GuardGroup("document-operations", TestDocumentOperations);   // R3: shared edits, keyboard vs menu, save / open / close sequences without a window
        GuardGroup("document-operations", TestConvertToInstrumentFlow);
        GuardGroup("document-operations", TestAddTrackMenu);   // the Add-track lane prompt, the + Track menu, converting an audio track, through a real window
        GuardGroup("document-operations", TestTrackIconButton);   // the track-row instrument icon: the catalogue it opens, one undo step, the audio icon
        GuardGroup("document-operations", TestAudioTrackClips);   // audio track part E: drop below the tracks, lanes, MIDI take, sections, conversion
        GuardGroup("document-operations", TestClipAndSectionEdits);   // W3-K: clip commands and section/bar/area flows through real windows
        GuardGroup("interactions", TestInteractions);   // W0-1: real windows driven through the interface's entry points; known-defect checks are reported, not required
        Section("MusicTime");
        Guard(TestBarSlots);
        Guard(TestCellSlots);
        Guard(TestAnalyzeBar);
        Guard(TestBarIncompleteMarking);
        Guard(TestBarFillPerTrackVoice);
        Guard(TestCursorSnap);
        Guard(TestInsertBeatKeepsNotes);
        Guard(TestBarDeleteGuards);
        Guard(TestBarRangeGaps);
        Guard(TestReusableBarRangePrompt);
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
        Guard(TestLiveInstrumentPreview);
        Guard(TestDrumToPitchedConversion);
        Guard(TestNoHangingNotes);
        Guard(TestTypedNotePreview);
        Guard(TestNoticeLimit);
        GuardGroup("long-import", TestLongGuitarPro35Import);
        GuardGroup("long-import", TestImportPlausibility);   // damaged-file notice: a file with unreadable bytes opens with one short warning, real files never warn
        Guard(TestFermataPlayback);
        Guard(TestRenderBarRanges);
        Guard(TestMarkStacking);
        Guard(TestEngravingCollisions);
        Guard(TestScoreReferenceLook);
        Guard(TestGp5BarLook);
        Guard(TestSimileBarHidesLinesAndTies);
        Guard(TestClefShapesAndChanges);
        Guard(TestVoice2HopoSlurWithLongerVoice);
        Guard(TestNewBindableCommands);
        Guard(TestTrackRowMenu);
        Guard(TestMenuPopupWarmup);
        Guard(TestSelectionScope);
        Guard(TestSelectionClipboardMatrix);
        Guard(TestTimelineCellsCurrent);
        Guard(TestClipDragPress);
        Guard(TestClipWaveformSpan);
        Guard(TestClipSplitGlueFades);
        Guard(TestClipEdgesAndLoops);
        Guard(TestTrimEmptyBars);
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
        Guard(TestRightArrowAppendsBarAtEnd);
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
        Guard(TestTrackSilhouetteMap);
        Guard(TestAudioTrackEditorGuards);
        Guard(TestAudioTrackProperties);
        Guard(TestInstrumentChoiceStrings);
        Guard(TestTransposeAllVoices);
        Guard(TestCapoRepitchesNotes);
        Guard(TestContextMenuLayouts);
        Guard(TestContextMenuLean);
        Guard(TestPreferencesCatalog);
        Guard(TestSettingsFileSplitSnapshots);
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
        Guard(TestTrackListFitCapsAtScore);
        Guard(TestTrackListCollapse);
        Guard(TestResizeDuringPlayback);
        Guard(TestTrackRowsEndFlush);
        Guard(TestSplitterDragKeepsScore);
        Guard(TestSectionAutoScroll);
        Guard(TestTimelineHoverAndAudioRows);
        Guard(TestTrackColumnHeaderFit);
        Guard(TestSectionsPaneScrollsWhenShort);
        Guard(TestExplicitSectionColourCopies);
        Guard(TestFollowSurvivesZoom);
        Guard(TestFollowResumesAfterSeek);
        Guard(TestVerticalFollowGlideLands);
        Guard(TestTrackSwitchSnapsFollow);
        Guard(TestFollowIgnoresClampedScroll);
        Guard(TestZoomComboShowsValue);
        Guard(TestSpeedControl);
        Guard(TestToolbarZoomAndSpeed);
        Guard(TestToolbarZoomSpeedNarrow);
        Guard(TestMixerSliders);
        Guard(TestMixerSlidersRealInput);
        Guard(TestMixerMuteSoloClick);
        Guard(TestTrackListFollowsMixerInPlace);
        Guard(TestSliderMappingAndGroupValues);
        Guard(TestTrackOrderingModel);
        Guard(TestMixerDragAndDrop);
        Guard(TestMixerGroupCollapse);
        Guard(TestMixerCollapseIsViewState);
        Guard(TestMixerGroupRules);
        Guard(TestMixerAppGroupRules);
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
        Guard(TestEffectCurveMath);
        Guard(TestEffectEditors);
        Guard(TestEffectEditorFiles);
        Guard(TestOrnamentEditors);
        Guard(TestOrnamentEditorFiles);
        Guard(TestSelectionWideEdits);
        Guard(TestEditToolToggles);
        Guard(TestTieFillsEmptyBeat);
        Guard(TestTiePerString);
        Guard(TestScoreLookBatch2);
        Guard(TestDeleteOnEmptyStringKeepsChord);
        Guard(TestDotKeyTogglesSingleDot);
        Guard(TestKeyMoveScrollsCursorIntoView);
        Guard(TestBackspaceClearsNoNoteStatus);
        Guard(TestDefaultDynamicUnmarked);
        Guard(TestGp5DiffReplay);   // GP5 differential harness: replays action sequences (tools/gp5diff); a short self-check without TABFORGE_GP5DIFF_DIR
        Guard(TestBarFill);
        Guard(TestWritingDuration);
        Guard(TestWorkflow);   // workflow area: a guitarist's editing stories through real keys and tools (--areas workflow)
        Guard(TestEntryDurationWorkflow);   // workflow area: entry durations against GP5 with the rest fill on
        Guard(TestNavigationWorkflow);   // workflow area: cursor navigation, overfull entry and Insert bar against GP5
        Guard(TestGp5EditStories);   // workflow area: ties, cut, marks and durations on empty spots and rests against GP5
        Guard(TestGp5NavStories);   // workflow area: Right at the end, Alt+Down, overfull, paste, End and Ctrl+End, anchor through undo against GP5
        Guard(TestGp5CursorStories);   // workflow area: Right makes a bar at the end, the cursor after Undo / Redo, paste after a selection, End after a restore, a bar keeps adding, against GP5
        Guard(TestGp5SelectionEndStories);   // workflow area: overfull cursor box, Undo / Redo / Backspace / Delete end the selection, Redo of a paste, against GP5
        Guard(TestGp5CutPasteStories);   // workflow area: cut inside a bar, paste past an overfull bar, Undo of Longer, palm mute on a tie, against a quiet GP5
        Guard(TestGp5Batch5Stories);   // workflow area: cut beats vs bars, triplet off in a group, dead and harmonic, against a quiet GP5
        Guard(TestGp5Batch6Stories);   // workflow area: Alt+Up/Down wrap, Dot shrinks fill rests, delete beat length, Insert bar with a selection (quiet GP5)
        Guard(TestGp5Batch6RegressionStories);   // workflow area: overfull triplet bars, End and Shift+Right on the empty spot, Undo of a dot, against a quiet GP5
        Guard(TestGp5EntryStories);   // workflow area: writing duration, inserted bar, cut of an empty last bar, undo / redo validity against GP5
        Guard(TestWorkflowMonkey);   // workflow area: seeded human monkey, invariants after every action (--monkey-seeds / --monkey-actions)
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
        Guard(TestScoreLayoutPartial);
        Section("Project IO / export");
        Guard(TestTrackReorder);
        Guard(TestAsciiExport);
        Guard(TestMidiExport);
        Guard(TestMidiExportTiming);
        Guard(TestTupletImport);
        Guard(TestGp5OwnFilesImport);
        Guard(TestGp5TieGraceImport);
        Guard(TestTieBeatKeepsTie);
        Guard(TestTabUi);
        Guard(TestPlayingSpeakerTab);
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
        Guard(TestScoreScaleMatchesFretboard);
        Guard(TestDockDefaultsAndFretboardPosition);
        Guard(TestKeyboardPaneSize);
        Guard(TestBandViewRows);
        Guard(TestBandLaneCache);
        Guard(TestBandLaneClick);
        Guard(TestBandViewLifecycle);
        Guard(TestBandLayoutPreset);
        Guard(TestBandNeverDocked);
        Guard(TestBandPillsAndRows);
        Guard(TestBandRowSizing);
        Guard(TestBandReorder);
        Guard(TestBandNoteGlow);
        Guard(TestBandStoppedInstruments);
        Guard(TestBandLayoutSaved);
        Guard(TestBandSettings);
        Guard(TestBandLanesInSync);
        Guard(TestBandVerticalLanes);
        Guard(TestBandRowsSamePage);
        Guard(TestBandRowsSamePageHorizontal);
        Guard(TestBandRowsSamePageDemo);
        Guard(TestBandLaneZoom);
        Guard(TestBandViewReset);
        Guard(TestBandWideLaneSync);
        Guard(TestBandInstrumentRows);
        Guard(TestBandMenu);
        Guard(TestBandFollow);
        Guard(TestBandLayoutSafety);
        Guard(TestBandLaneContent);
        Guard(TestBandReorderAutoScroll);
        Guard(TestBandEmptyState);
        Guard(TestBandPillContrast);
        Guard(TestFramedIconContrast);
        Guard(TestFretMarkerLabelContrast);
        Guard(TestFretStrumArrow);
        Guard(TestEffectEditorOkContrast);
        Guard(TestTrackNameEllipsis);
        Guard(TestHoverSurfaceContrast);
        Guard(TestAudioInstrumentPanel);
        Section("Arrangement geometry (precision)");
        Guard(TestArrangementGeometry);
        Guard(TestArrangementFollowGeometry);
        Guard(TestSectionColourEditing);
        Section("Guitar Pro compatibility (real files)");
        GuardGroup("gp-fixtures", TestSyntheticGuitarProFixture);
        Guard(TestGuitarProFiles);
        Guard(TestGuitarProImportContainment);
        Guard(TestGuitarProImportWorker);
        Guard(TestRoundTripSemanticsSuite);
        Guard(TestGpRoundTripFixes);
        Guard(TestGpRoundTripCounts);
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
        Guard(TestPlaybackScheduleReuse);
        Guard(TestMixPointsSurviveSeek);
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
        Guard(TestToolsChordFinderAndSongStats);
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
        Guard(TestInstrumentToAudioConversion);
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
        Guard(TestEngineSyncDeferredRequests);
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
        Guard(TestAudioTrackAddDuringPlayback);
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
        Guard(TestTrackLinesSetting);
        Guard(TestTrackLinesRedraw);
        Guard(TestSectionTipWording);
        Guard(TestMediaDropPreviewGeometry);
        Guard(TestTimelineSongTimeRepeatGrowth);
        Guard(TestClipMoves);
        Guard(TestClipMoveGhost);
        Guard(TestMidiClipMoves);
        Guard(TestSongExtent);
        Guard(TestSongCoversClipsOnLoadAndDelete);
        Guard(TestDragStartPlaceholdersAndAuditEntries);
        Guard(TestLongAudioClipGrowthPlayback);
        Guard(TestAudioGrowthReservation);
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
        ["TestBarFillPerTrackVoice"] = "core",
        ["TestBarSlots"] = "core",
        ["TestCellSlots"] = "core",
        ["TestCommandPaletteAndPdf"] = "core",
        ["TestCursorSnap"] = "core",
        ["TestInsertBeatKeepsNotes"] = "core",
        ["TestBarDeleteGuards"] = "document-operations",
        ["TestBarRangeGaps"] = "document-operations",
        ["TestReusableBarRangePrompt"] = "document-operations",
        ["TestExplicitSectionColourCopies"] = "core",
        ["TestDataIntegrityLeftovers"] = "core",
        ["TestDocuments"] = "core",
        ["TestDuplicateBarAllTracks"] = "core",
        ["TestEditControllers"] = "core",
        ["TestEditorSelectionState"] = "core",
        ["TestEngravingHeader"] = "core",
        ["TestFollowSurvivesZoom"] = "core",
        ["TestFollowResumesAfterSeek"] = "core",
        ["TestVerticalFollowGlideLands"] = "core",
        ["TestTrackSwitchSnapsFollow"] = "core",
        ["TestFollowIgnoresClampedScroll"] = "core",
        ["TestKeySignaturesCarryForward"] = "core",
        ["TestKnobTypeIn"] = "core",
        ["TestLayoutAuditTechniqueSong"] = "core",
        ["TestMixerDragAndDrop"] = "core",
        ["TestMixerGroupCollapse"] = "core",
        ["TestMixerCollapseIsViewState"] = "core",
        ["TestMixerGroupRules"] = "core",
        ["TestMixerAppGroupRules"] = "core",
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
        ["TestTrackIconButton"] = "document-operations",
        ["TestConvertToInstrumentFlow"] = "document-operations",
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
        ["TestAudioTrackAddDuringPlayback"] = "engine",
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
        ["TestEngineSyncDeferredRequests"] = "engine",
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
        ["TestGpRoundTripCounts"] = "guitarpro",
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
        ["TestGp5OwnFilesImport"] = "guitarpro",
        ["TestGp5TieGraceImport"] = "guitarpro",
        ["TestTieBeatKeepsTie"] = "ui",
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
        ["TestInstrumentToAudioConversion"] = "persistence",
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
        ["TestLiveInstrumentPreview"] = "playback",
        ["TestDrumToPitchedConversion"] = "playback",
        ["TestMidiExportTiming"] = "playback",
        ["TestNoHangingNotes"] = "playback",
        ["TestNoOpOptionChangesDoNotRestartPlayback"] = "playback",
        ["TestPlaybackScheduleReuse"] = "playback",
        ["TestMixPointsSurviveSeek"] = "playback",
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
        ["TestClipEdgesAndLoops"] = "recording",
        ["TestTrimEmptyBars"] = "recording",
        ["TestClips"] = "recording",
        ["TestMediaDropPlan"] = "recording",
        ["TestMidiFileToClip"] = "recording",
        ["TestRecordingPipeline"] = "recording",
        ["TestSongExtent"] = "recording",
        ["TestSongCoversClipsOnLoadAndDelete"] = "recording",
        ["TestDragStartPlaceholdersAndAuditEntries"] = "recording",
        ["TestLongAudioClipGrowthPlayback"] = "recording",
        ["TestAudioGrowthReservation"] = "recording",
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
        ["TestTrackLinesSetting"] = "settings",
        ["TestPreferencesCatalog"] = "settings",
        ["TestSettingsFileSplitSnapshots"] = "settings",
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
        ["TestTrackSilhouetteMap"] = "ui",
        ["TestArrangementFollowGeometry"] = "ui",
        ["TestSectionColourEditing"] = "ui",
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
        ["TestMidiClipMoves"] = "ui",
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
        ["TestEffectCurveMath"] = "ui",
        ["TestEffectEditors"] = "ui",
        ["TestEffectEditorFiles"] = "persistence",
        ["TestOrnamentEditors"] = "ui",
        ["TestOrnamentEditorFiles"] = "persistence",
        ["TestEditorCopyPaste"] = "ui",
        ["TestEditorDurations"] = "ui",
        ["TestEditorNavigation"] = "ui", ["TestRightArrowAppendsBarAtEnd"] = "ui",
        ["TestEditorShiftClickAndEffectDuration"] = "ui",
        ["TestEditorStructurePeer"] = "ui",
        ["TestEngravingCollisions"] = "ui",
        ["TestScoreReferenceLook"] = "ui", ["TestGp5BarLook"] = "ui",
        ["TestFlagShape"] = "ui",
        ["TestFretboardGeometry"] = "ui",
        ["TestFretboardPaneSize"] = "ui", ["TestScoreScaleMatchesFretboard"] = "ui", ["TestDockDefaultsAndFretboardPosition"] = "ui",
        ["TestKeyboardPaneSize"] = "ui",
        ["TestBandViewRows"] = "ui", ["TestBandRowsSamePage"] = "ui", ["TestBandRowsSamePageHorizontal"] = "ui", ["TestBandRowsSamePageDemo"] = "ui", ["TestBandLaneCache"] = "ui", ["TestBandLaneClick"] = "ui", ["TestBandViewLifecycle"] = "ui", ["TestBandLayoutPreset"] = "ui", ["TestBandNeverDocked"] = "ui",
        ["TestBandPillsAndRows"] = "ui", ["TestBandRowSizing"] = "ui", ["TestBandReorder"] = "ui", ["TestBandNoteGlow"] = "ui", ["TestBandStoppedInstruments"] = "ui",
        ["TestBandLayoutSaved"] = "ui", ["TestBandLayoutSafety"] = "ui", ["TestBandSettings"] = "ui", ["TestBandLanesInSync"] = "ui", ["TestBandInstrumentRows"] = "ui", ["TestBandVerticalLanes"] = "ui", ["TestBandLaneZoom"] = "ui", ["TestBandViewReset"] = "ui", ["TestBandWideLaneSync"] = "ui", ["TestBandMenu"] = "ui", ["TestBandFollow"] = "ui", ["TestBandLaneContent"] = "ui", ["TestBandReorderAutoScroll"] = "ui", ["TestBandEmptyState"] = "ui",
        ["TestBandPillContrast"] = "ui", ["TestFramedIconContrast"] = "ui", ["TestFretMarkerLabelContrast"] = "ui", ["TestFretStrumArrow"] = "ui", ["TestEffectEditorOkContrast"] = "ui", ["TestTrackNameEllipsis"] = "ui", ["TestHoverSurfaceContrast"] = "ui",
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
        ["TestTimelineSongTimeRepeatGrowth"] = "recording",
        ["TestMenuGestureTextFollowsBindings"] = "ui",
        ["TestMoveNoteToAdjacentString"] = "ui",
        ["TestNewBindableCommands"] = "ui",
        ["TestNoHardWiredKeyText"] = "ui",
        ["TestNotationLayout"] = "ui",
        ["TestScoreLayoutPartial"] = "ui",
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
        ["TestToolsChordFinderAndSongStats"] = "ui",
        ["TestScoreContextMenuByKeyboard"] = "ui",
        ["TestSectionDeleteWording"] = "ui",
        ["TestSelectionWholeBeats"] = "ui",
        ["TestSelectionWideEdits"] = "ui",
        ["TestEditToolToggles"] = "ui",
        ["TestTieFillsEmptyBeat"] = "ui",
        ["TestTiePerString"] = "ui",
        ["TestScoreLookBatch2"] = "ui",
        ["TestDeleteOnEmptyStringKeepsChord"] = "ui",
        ["TestDotKeyTogglesSingleDot"] = "ui",
        ["TestKeyMoveScrollsCursorIntoView"] = "ui",
        ["TestBackspaceClearsNoNoteStatus"] = "ui",
        ["TestGp5DiffReplay"] = "ui",
        ["TestDefaultDynamicUnmarked"] = "ui",
        ["TestSimileBarHidesLinesAndTies"] = "ui",
        ["TestSlideStrokesOnStaff"] = "ui",
        ["TestSectionAutoScroll"] = "ui",
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
        ["TestPlayingSpeakerTab"] = "ui",
        ["TestTempoBoxText"] = "ui",
        ["TestThemedCheckBoxAndProgressBar"] = "ui",
        ["TestTimelineAndInstrumentContextMenuByKeyboard"] = "ui",
        ["TestTimelineClipsShareClipboard"] = "ui",
        ["TestTimelineContextMenus"] = "ui",
        ["TestMenuPopupWarmup"] = "ui",
        ["TestTimelineHoverAndAudioRows"] = "ui",
        ["TestTimelineHoverAndBarMarker"] = "ui",
        ["TestTrackLinesRedraw"] = "ui",
        ["TestSectionTipWording"] = "ui",
        ["TestTimelineSectionCopiesAsBars"] = "ui",
        ["TestTooltips"] = "ui",
        ["TestTrackColumnHeaderFit"] = "ui",
        ["TestTrackListFit"] = "ui", ["TestTrackListFitCapsAtScore"] = "ui", ["TestTrackListCollapse"] = "ui",
        ["TestTrackRowRightClick"] = "ui", ["TestTrackRowMenu"] = "ui", ["TestSelectionScope"] = "ui", ["TestSelectionClipboardMatrix"] = "ui", ["TestTimelineCellsCurrent"] = "ui", ["TestClipDragPress"] = "ui", ["TestClipWaveformSpan"] = "ui",
        ["TestTrackRowsEndFlush"] = "ui",
        ["TestViewMenuWording"] = "ui",
        ["TestVoice2HopoSlurWithLongerVoice"] = "ui",
        ["TestWritingDuration"] = "ui",
        ["TestWorkflow"] = "workflow",
        ["TestWorkflowMonkey"] = "workflow",
        ["TestEntryDurationWorkflow"] = "workflow",
        ["TestNavigationWorkflow"] = "workflow",
        ["TestGp5EditStories"] = "workflow",
        ["TestGp5NavStories"] = "workflow",
        ["TestGp5CursorStories"] = "workflow",
        ["TestGp5SelectionEndStories"] = "workflow",
        ["TestGp5CutPasteStories"] = "workflow",
        ["TestGp5Batch5Stories"] = "workflow",
        ["TestGp5Batch6Stories"] = "workflow",
        ["TestGp5Batch6RegressionStories"] = "workflow",
        ["TestGp5EntryStories"] = "workflow",
        ["TestWindowLifetime"] = "window-lifetime",
        ["TestStartupFileOpen"] = "window-lifetime",
        ["TestSingleInstanceProcessHandover"] = "release",
    };
}
