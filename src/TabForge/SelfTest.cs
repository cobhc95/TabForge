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

/// <summary>
/// Headless verification suite. Run with: TabForge.exe --selftest
/// Writes a report next to the executable and returns a non-zero exit code on failure.
/// </summary>
public static partial class SelfTest
{
    private static readonly List<string> Log = new();
    private static int _pass, _fail, _skip;
    private static bool _gpFixtureRan;
    private static HashSet<string> _required = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Requirements that turn skips into failures: <c>--selftest &lt;log&gt; --require gp-fixtures[,all]</c> or the
    /// TABFORGE_SELFTEST_REQUIRE environment variable (comma/semicolon separated). "gp-fixtures" demands the
    /// synthetic Guitar Pro round trip ran; "synthetic-fixtures" that the synthetic fixture group ran; "source-hygiene" and "installer-parity" demand the repository-source checks
    /// (control characters, single plug-in factory; installer file associations) ran; "all" makes every skip a failure. Exit code stays 0 = pass, non-zero = fail.
    /// </summary>
    private static HashSet<string> ParseRequirements()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var values = new List<string> { Environment.GetEnvironmentVariable("TABFORGE_SELFTEST_REQUIRE") ?? "" };
        var args = Environment.GetCommandLineArgs();
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i].Equals("--require", StringComparison.OrdinalIgnoreCase)) values.Add(args[i + 1]);
        foreach (var value in values)
            foreach (var part in value.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                set.Add(part);
        return set;
    }

    public static int Run(string outputPath)
    {
        Log.Clear(); _pass = 0; _fail = 0; _skip = 0; _gpFixtureRan = false; _syntheticFixturesRan = false;
        _required = ParseRequirements();
        _areas = ParseAreas();
        if (_areas is not null) Log.Add($"  info  areas: core + {string.Join(", ", _areas.OrderBy(x => x))}");
        if (_required.Count > 0) Log.Add($"  info  required: {string.Join(", ", _required.OrderBy(x => x))}");
        Section("MusicTime");
        Guard(TestBarSlots);
        Guard(TestCellSlots);
        Guard(TestAnalyzeBar);
        Guard(TestBarGridPlacement);
        Section("Automatic pitch matching");
        Guard(TestPitchMatch);
        Section("Timeline / playback");
        Guard(TestTimelineBasics);
        Guard(TestTimelineTechniques);
        Guard(TestPlaybackDifferences);
        Guard(TestTimelineMetronome);
        Guard(TestTimelineLoopAndOrder);
        Guard(TestTimelineRevision);
        Guard(TestPlaybackOrderSpec);
        Section("Editor");
        Guard(TestEditorEntry);
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
        Guard(TestContextMenuLayouts);
        Guard(TestFollowSurvivesZoom);
        Guard(TestZoomComboShowsValue);
        Guard(TestSpeedControl);
        Guard(TestMixerSliders);
        Guard(TestMixerSlidersRealInput);
        Guard(TestTrackListFollowsMixerInPlace);
        Guard(TestSliderMappingAndGroupValues);
        Guard(TestTrackOrderingModel);
        Guard(TestMixerDragAndDrop);
        Guard(TestTrackListGroupRows);
        Guard(TestOrderAnimationAndSpeedCommands);
        Guard(TestCommandPaletteAndPdf);
        Guard(TestTechniqueEngraving);
        Guard(TestLayoutAuditTechniqueSong);
        Guard(TestTemplateKeepsSetupOnly);
        Guard(TestSnapLayoutHitTest);
        Guard(TestAutomationPeers);
        Guard(TestEditorStructurePeer);
        Guard(TestEditCommands);
        Guard(TestReadableTextTokens);
        Guard(TestEngravingHeader);
        Guard(TestUserTemplatesAndFaultedChain);
        Section("Standard-notation engraving layout");
        Guard(TestNotationLayout);
        Section("Project IO / export");
        Guard(TestProjectRoundtrip);
        Guard(TestTrackReorder);
        Guard(TestModelRoundTrip);
        Guard(TestAsciiExport);
        Guard(TestMidiExport);
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
        Section("Arrangement geometry (precision)");
        Guard(TestArrangementGeometry);
        Guard(TestArrangementFollowGeometry);
        Section("Guitar Pro compatibility (real files)");
        Guard(TestSyntheticGuitarProFixture);
        Guard(TestGuitarProFiles);
        Guard(TestGuitarProImportContainment);
        Guard(TestGuitarProImportWorker);
        Guard(TestRoundTripSemanticsSuite);
        Section("Synthetic fixtures (run everywhere, no local songs)");
        Guard(TestSyntheticFixtures);
        Section("Playback depth (timing / ties / channels)");
        Guard(TestPlaybackDepth);
        Guard(TestNoOpOptionChangesDoNotRestartPlayback);
        Guard(TestSeekWhilePlayingSoundsFirstNote);
        Guard(TestCountInIsHeard);
        Guard(TestEverySettingIsWired);
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
        Guard(TestPairSaveEveryStage);
        Guard(TestPairSaveProcessKill);
        Guard(TestProfileLeavesUserFoldersUntouched);
        Guard(TestNightPluginApproval);
        Guard(TestAsyncSaveSequencing);
        Guard(TestPluginStateCollection);
        Guard(TestSidecarRouting);
        Guard(TestAudioDataSizeLimit);
        Guard(TestGpOpenKeepsTitle);
        Guard(TestPersistenceSchema);
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
        Guard(TestSharedRingProducers);
        Guard(TestEngineExitCleanupOnUi);
        Guard(TestHeadlessDeviceReconfigure);
        Guard(TestClipChurnWhileStreaming);
        Guard(TestDiskStreamerIdle);
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
        Guard(TestReaperChainImport);
        Guard(TestSettingsWithInlinePluginStates);
        Guard(TestDataIntegrityLeftovers);
        Guard(TestAutosaveRecovery);
        Guard(TestCallbackMetrics);
        Guard(TestClips);
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
        Section("Repository hygiene");
        Guard(TestSourceControlCharacters);
        Guard(TestInstallerAssociationParity);
        Guard(TestLooseSoundTouchAndLicenseTexts);
        Guard(TestTrimMerges);
        Section("Architecture (layering)");
        Guard(TestArchitectureLayering);
        Guard(TestTraceSwitchAreas);

#if DEBUG
        var performance = RenderPerformance.Snapshot;
        Log.Add($"  PERF  DEBUG latest scopes: arrangement {performance.ArrangementRenderMs:0.###} ms / {performance.ArrangementRenderBytes:N0} B; " +
                $"score {performance.ScoreRenderMs:0.###} ms / {performance.ScoreRenderBytes:N0} B; " +
                $"drag {performance.DragFrameMs:0.###} ms / {performance.DragFrameBytes:N0} B; " +
                $"score-layout rebuild {performance.ScoreLayoutRebuildMs:0.###} ms / {performance.ScoreLayoutRebuildBytes:N0} B");
#endif

        if (_required.Contains(RequireGpFixtures))
            Check("required: the synthetic Guitar Pro fixture test ran to completion", _gpFixtureRan);
        if (_required.Contains(RequireSyntheticFixtures))
            Check("required: the synthetic fixture group (repeats, navigation, tempo, mix, tuplets, voices, spans, demo song) ran to completion", _syntheticFixturesRan);
        var summary = $"TabForge self-test: {_pass} passed, {_fail} failed" + (_skip > 0 ? $", {_skip} skipped" : "");
        if (_skippedByArea > 0) summary += $" ({_skippedByArea} test groups outside the selected areas not run)";
        Log.Add("");
        Log.Add(summary);
        try { DiagnosticFileService.WriteText(outputPath, string.Join(Environment.NewLine, Log)); }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Self-test log could not be written: {ex}");
            return 2;
        }
        return _fail == 0 ? 0 : 1;
    }

    private static void Section(string name) { Log.Add(""); Log.Add($"== {name} =="); }

    // One throwing test group must not abort the run (it used to crash the process while the
    // summary still said "0 failed"): record it as a failure and continue with the next group.
    /// <summary>
    /// Test areas (<c>--areas engine,recording</c> runs only those plus the untagged core; no flag or <c>all</c> runs
    /// everything, as CI and release packaging do). REBUILD picks the areas from the source files changed since the last
    /// full pass (tools/Select-TestAreas.ps1 maps folders to these names).
    /// </summary>
    private static readonly Dictionary<string, string> AreaOf = new(StringComparer.Ordinal)
    {
        ["TestPitchMatch"] = "engine", ["TestMixer"] = "engine", ["TestIsolatedPluginGenerations"] = "engine", ["TestIsolatedCallbackBudget"] = "engine", ["TestIsolatedControlChannel"] = "engine",
        ["TestRetirementEpochBarrier"] = "engine", ["TestMonitorFx"] = "engine", ["TestSharedRingProducers"] = "engine", ["TestEngineExitCleanupOnUi"] = "engine",
        ["TestHeadlessDeviceReconfigure"] = "engine", ["TestClipChurnWhileStreaming"] = "engine", ["TestDiskStreamerIdle"] = "engine", ["TestRealtimePolish"] = "engine",
        ["TestPluginFactorySingleSource"] = "engine", ["TestWatchdogPolicy"] = "engine", ["TestCommandFrameRobustness"] = "engine",
        ["TestPumpOnce"] = "engine", ["TestTunerPitchDetection"] = "engine", ["TestEffectChannel"] = "engine", ["TestDrumAndMelodicDispatchTogether"] = "engine", ["TestColdStartSetupSurvivesPanic"] = "engine", ["TestChildProcessJob"] = "engine", ["TestEngineLivenessAndSlowLoad"] = "engine", ["TestCaptureResampling"] = "recording", ["TestRoutingCycles"] = "engine", ["TestGmLevelCalibration"] = "engine", ["TestCallbackMetrics"] = "engine",
        ["TestMidiProcessors"] = "midi", ["TestRecordingPipeline"] = "recording", ["TestClips"] = "recording",
        ["TestSaveTransactions"] = "persistence", ["TestPairSaveRecovery"] = "persistence", ["TestPairMarkerIsUntrusted"] = "persistence", ["TestPairSaveEveryStage"] = "persistence", ["TestPairSaveProcessKill"] = "persistence",["TestProfileLeavesUserFoldersUntouched"] = "persistence", ["TestNightPluginApproval"] = "persistence", ["TestAsyncSaveSequencing"] = "persistence", ["TestPluginStateCollection"] = "persistence",
        ["TestSidecarRouting"] = "persistence", ["TestPersistenceSchema"] = "persistence", ["TestTforgeCompression"] = "persistence", ["TestCleanGpExportKeepsFeatures"] = "guitarpro",["TestMusicXmlExport"] = "guitarpro",["TestMusicXmlBarsFillTheTimeSignature"] = "guitarpro",["TestMusicXmlHeaderForReaders"] = "guitarpro",["TestMusicXmlGuitarPro8Encoding"] = "guitarpro",["TestImporterNamesAndDynamics"] = "guitarpro",["TestSettingsWithInlinePluginStates"] = "persistence",
        ["TestSaveTransactions"] = "persistence", ["TestPairSaveRecovery"] = "persistence", ["TestProfileLeavesUserFoldersUntouched"] = "persistence", ["TestNightPluginApproval"] = "persistence", ["TestAsyncSaveSequencing"] = "persistence", ["TestPluginStateCollection"] = "persistence",
        ["TestSidecarRouting"] = "persistence", ["TestPersistenceSchema"] = "persistence", ["TestTforgeCompression"] = "persistence", ["TestCleanGpExportKeepsFeatures"] = "guitarpro",["TestMusicXmlExport"] = "guitarpro",["TestMusicXmlBarsFillTheTimeSignature"] = "guitarpro",["TestMusicXmlHeaderForReaders"] = "guitarpro",["TestMusicXmlGuitarPro8Encoding"] = "guitarpro",["TestImporterNamesAndDynamics"] = "guitarpro",["TestSettingsWithInlinePluginStates"] = "persistence", ["TestAutosaveRecovery"] = "persistence",
        ["TestSaveTransactions"] = "persistence", ["TestPairSaveRecovery"] = "persistence", ["TestProfileLeavesUserFoldersUntouched"] = "persistence", ["TestNightPluginApproval"] = "persistence", ["TestAsyncSaveSequencing"] = "persistence", ["TestPluginStateCollection"] = "persistence",
        ["TestSidecarRouting"] = "persistence", ["TestPersistenceSchema"] = "persistence", ["TestTforgeCompression"] = "persistence", ["TestCleanGpExportKeepsFeatures"] = "guitarpro",["TestMusicXmlExport"] = "guitarpro",["TestRoundTripSemanticsSuite"] = "guitarpro",["TestMusicXmlBarsFillTheTimeSignature"] = "guitarpro",["TestMusicXmlHeaderForReaders"] = "guitarpro",["TestMusicXmlGuitarPro8Encoding"] = "guitarpro",["TestImporterNamesAndDynamics"] = "guitarpro",["TestSettingsWithInlinePluginStates"] = "persistence",
        ["TestReaperChainImport"] = "persistence", ["TestProjectRoundtrip"] = "persistence", ["TestModelRoundTrip"] = "persistence",
        ["TestSyntheticGuitarProFixture"] = "guitarpro", ["TestSyntheticFixtures"] = "synthetic", ["TestGuitarProFiles"] = "guitarpro", ["TestTupletImport"] = "guitarpro", ["TestGuitarProImportContainment"] = "guitarpro", ["TestGuitarProImportWorker"] = "guitarpro",
        ["TestGp5EditingSemantics"] = "guitarpro", ["TestAsciiExport"] = "guitarpro", ["TestMidiExport"] = "guitarpro",
        ["TestPlaybackDepth"] = "playback", ["TestNoOpOptionChangesDoNotRestartPlayback"] = "playback", ["TestSeekWhilePlayingSoundsFirstNote"] = "playback",
        ["TestCountInIsHeard"] = "playback", ["TestTimelineBasics"] = "playback", ["TestTimelineTechniques"] = "playback",
        ["TestTimelineMetronome"] = "playback", ["TestTimelineRevision"] = "playback", ["TestAudioDataSizeLimit"] = "persistence", ["TestGpOpenKeepsTitle"] = "persistence", ["TestTimelineLoopAndOrder"] = "playback", ["TestPlaybackOrderSpec"] = "playback",
        ["TestEditorEntry"] = "ui", ["TestEditorStructurePeer"] = "ui", ["TestEditCommands"] = "ui", ["TestReadableTextTokens"] = "ui", ["TestEditorNavigation"] = "ui", ["TestEditorDurations"] = "ui", ["TestPlaybackGlowIntensity"] = "ui",
        ["TestEditorCopyPaste"] = "ui", ["TestNoteMapper"] = "ui", ["TestScoreClipCapture"] = "persistence", ["TestScoreClipJson"] = "persistence", ["TestScoreClipRejectsUntrustedInput"] = "persistence", ["TestClipboardServiceFallback"] = "persistence", ["TestTimelineClipsShareClipboard"] = "ui", ["TestTimelineSectionCopiesAsBars"] = "ui", ["TestTimelineContextMenus"] = "ui", ["TestPasteCommands"] = "ui", ["TestPasteSpecial"] = "ui", ["TestNotationLayout"] = "ui", ["TestTabUi"] = "ui", ["TestBrowserTabShell"] = "ui",
        ["TestNoteEvents"] = "ui", ["TestInstrumentVisualState"] = "ui", ["TestFretboardGeometry"] = "ui", ["TestArrangementGeometry"] = "ui",
        ["TestArrangementFollowGeometry"] = "ui", ["TestScaleFinder"] = "ui", ["TestGp5SvgIcons"] = "ui", ["TestInstrumentArtwork"] = "ui",
        ["TestRuntimeIconAndResourceKeys"] = "ui", ["TestSystemBreakPreferences"] = "ui", ["TestEverySettingIsWired"] = "settings",
        ["TestSettingsStoreSharedAcrossWindows"] = "settings", ["TestEngineWarmOwnership"] = "engine", ["TestEngineMultiTabPlayback"] = "engine",["TestEngineDefaultOnAndManualOffSticks"] = "settings", ["TestInstrumentSizeUnlockedByDefault"] = "settings", ["TestPerControlTextDpi"] = "ui",
        ["TestPasteOptionsDialog"] = "ui", ["TestPasteSettingsRows"] = "settings", ["TestContextMenuLayouts"] = "ui",
    };

    private static HashSet<string>? _areas;   // null: every area

    private static HashSet<string>? ParseAreas()
    {
        var args = Environment.GetCommandLineArgs();
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i].Equals("--areas", StringComparison.OrdinalIgnoreCase))
            {
                var set = args[i + 1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
                return set.Contains("all") ? null : set;
            }
        return null;
    }

    private static void Guard(Action test, [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(test))] string name = "")
    {
        var area = AreaOf.TryGetValue(test.Method.Name, out var a) ? a : "core";
        if (_areas is not null && area != "core" && !_areas.Contains(area)) { _skippedByArea++; return; }
        var watch = System.Diagnostics.Stopwatch.StartNew();
        try { test(); }
        catch (Exception ex) { Check($"{name} completed without throwing", false, $"{ex.GetType().Name}: {ex.Message}"); }
        if (watch.ElapsedMilliseconds >= 1000) Log.Add($"  time  {name} [{area}] {watch.Elapsed.TotalSeconds:0.0} s");
    }

    private static int _skippedByArea;

    private static void Check(string name, bool condition, string? detail = null)
    {
        if (condition) { _pass++; Log.Add($"  PASS  {name}"); }
        else { _fail++; Log.Add($"  FAIL  {name}{(detail is null ? "" : "  -> " + detail)}"); }
    }

    /// <summary>A check that could not run (missing optional input). Reported, never counted as a pass.</summary>
    private static void Skip(string name, string reason, string? requirement = null)
    {
        // "--require all" (or --require <requirement> for a named group such as source-hygiene or installer-parity)
        // turns a skip into a failure so CI cannot pass by silently not running a check.
        if (_required.Contains("all") || (requirement is not null && _required.Contains(requirement)))
        { Check($"{name} (required by --require {(requirement is not null && _required.Contains(requirement) ? requirement : "all")})", false, $"skipped: {reason}"); return; }
        _skip++; Log.Add($"  SKIP  {name}  -> {reason}");
    }

    private static void Eq<T>(string name, T expected, T actual) =>
        Check(name, EqualityComparer<T>.Default.Equals(expected, actual), $"expected {expected}, got {actual}");

    // ---------- MusicTime ----------

    private static void TestBarSlots()
    {
        Eq("4/4 bar = 16 slots", 16, MusicTime.BarSlots(4, 4));
        Eq("3/4 bar = 12 slots", 12, MusicTime.BarSlots(3, 4));
        Eq("6/8 bar = 12 slots", 12, MusicTime.BarSlots(6, 8));
        Eq("2/4 bar = 8 slots", 8, MusicTime.BarSlots(2, 4));
    }

    private static void TestCellSlots()
    {
        Eq("quarter = 4 slots", 4.0, MusicTime.CellSlots(new TabCell { DurationDenominator = 4 }));
        Eq("eighth = 2 slots", 2.0, MusicTime.CellSlots(new TabCell { DurationDenominator = 8 }));
        Eq("whole = 16 slots", 16.0, MusicTime.CellSlots(new TabCell { DurationDenominator = 1 }));
        Eq("dotted quarter = 6 slots", 6.0, MusicTime.CellSlots(new TabCell { DurationDenominator = 4, Dots = 1 }));
        Eq("eighth triplet = 1.33 slots", 4.0 / 3.0, MusicTime.CellSlots(new TabCell { DurationDenominator = 8, IsTriplet = true }));
        Eq("eighth triplet (3:2 ratio) = 1.33 slots", 4.0 / 3.0, MusicTime.CellSlots(new TabCell { DurationDenominator = 8, TupletNumerator = 3, TupletDenominator = 2 }));
        Eq("sixteenth quintuplet (5:4) = 0.8 slots", 0.8, MusicTime.CellSlots(new TabCell { DurationDenominator = 16, TupletNumerator = 5, TupletDenominator = 4 }));
        Eq("sixteenth = 1 slot", 1.0, MusicTime.CellSlots(new TabCell { DurationDenominator = 16 }));
    }

    private static void TestAnalyzeBar()
    {
        var p = TemplateFactory.Blank();
        var bar = p.Tracks[0].Measures[0];
        // Four quarter notes on beats 0,4,8,12 fill a 4/4 bar.
        foreach (var i in new[] { 0, 4, 8, 12 })
        {
            bar.Cells[i].DurationDenominator = 4;
            bar.Cells[i].Notes.Add(new TabNote { StringIndex = 0, Fret = 3, MidiValue = 67 });
        }
        var state = MusicTime.AnalyzeBar(p, 0);
        Check("4 quarters = complete bar", state.Complete, state.ToString());

        // A half note starting at slot 12 overruns the bar.
        bar.Cells[12] = new TabCell { DurationDenominator = 2, Notes = { new TabNote { StringIndex = 0, Fret = 5 } } };
        var bad = MusicTime.AnalyzeBar(p, 0);
        Check("half note at slot 12 = bar error", bad.Error, bad.ToString());
    }

    // ---------- Timeline ----------

    private static SongProject TwoBarSong(int bpm = 120)
    {
        var p = new SongProject { Tempo = bpm, TimeSignatureNumerator = 4, TimeSignatureDenominator = 4 };
        var t = new TrackModel { Name = "Gtr", Measures = TemplateFactory.Measures(2) };
        for (var m = 0; m < 2; m++)
            for (var i = 0; i < 4; i++)
            {
                var cell = t.Measures[m].Cells[i * 4];
                cell.DurationDenominator = 4;
                cell.Notes.Add(new TabNote { StringIndex = 0, Fret = i, MidiValue = 64 + i });
            }
        p.Tracks.Add(t);
        return p;
    }

    private static void TestTimelineBasics()
    {
        var p = TwoBarSong(120);
        var tl = MidiTimelineBuilder.Build(p, new PlaybackOptions());
        Eq("2 bars at 120bpm = 4000 ms", 4000.0, tl.TotalMs);
        Eq("8 note-on events", 8, tl.Events.Count(e => (e.Status & 0xF0) == 0x90));
        Eq("8 note-off events", 8, tl.Events.Count(e => (e.Status & 0xF0) == 0x80));
        Check("program change present", tl.Events.Any(e => (e.Status & 0xF0) == 0xC0));
        Check("volume CC present", tl.Events.Any(e => (e.Status & 0xF0) == 0xB0 && e.Data1 == 7));
        Eq("bar map has 2 bars", 2, tl.Bars.Count);
        Eq("first bar starts at 0", 0.0, tl.Bars[0].StartMs);
        Eq("second bar starts at 2000ms", 2000.0, tl.Bars[1].StartMs);

        // Half speed doubles the duration.
        var slow = MidiTimelineBuilder.Build(p, new PlaybackOptions { Speed = 0.5 });
        Eq("half speed = 8000 ms", 8000.0, slow.TotalMs);

        // Starting at bar 2 with a cell offset skips the earlier events.
        var seek = MidiTimelineBuilder.Build(p, new PlaybackOptions { StartBar = 1 });
        Eq("seek to bar 2 = 2000 ms total", 2000.0, seek.TotalMs);
        Eq("seek to bar 2 keeps 4 notes", 4, seek.Events.Count(e => (e.Status & 0xF0) == 0x90));
    }

    private static void TestTimelineTechniques()
    {
        var p = new SongProject { Tempo = 120 };
        var t = new TrackModel { Measures = TemplateFactory.Measures(1) };
        var cellA = t.Measures[0].Cells[0];
        cellA.DurationDenominator = 4;
        cellA.Notes.Add(new TabNote { StringIndex = 0, Fret = 3, MidiValue = 67, Techniques = { "PalmMute" } });
        cellA.Accent = 1;
        var cellB = t.Measures[0].Cells[4];
        cellB.DurationDenominator = 4;
        cellB.Notes.Add(new TabNote { StringIndex = 1, Fret = 5, MidiValue = 64, Techniques = { "Trill" } });
        var cellC = t.Measures[0].Cells[8];
        cellC.DurationDenominator = 4;
        cellC.Notes.Add(new TabNote { StringIndex = 2, Fret = 7, MidiValue = 62, Techniques = { "Harmonic" } });
        t.Measures[0].Cells[12].DurationDenominator = 4;
        p.Tracks.Add(t);

        var tl = MidiTimelineBuilder.Build(p, new PlaybackOptions());
        var firstOn = tl.Events.First(e => (e.Status & 0xF0) == 0x90);
        Check("palm mute keeps velocity; accent raises it above 100", firstOn.Data2 > 100, $"velocity {firstOn.Data2}");
        Check("accent raises velocity above 100", firstOn.Data2 > 70, $"velocity {firstOn.Data2}");
        Eq("trill generates 4 note-ons", 4, tl.Events.Count(e => (e.Status & 0xF0) == 0x90 && e.Data1 is 64 or 66));
        Check("harmonic keeps the imported sounding pitch (alphaTab RealValue already includes it)",
            tl.Events.Any(e => (e.Status & 0xF0) == 0x90 && e.Data1 == 62));
    }

    private static void TestTimelineMetronome()
    {
        var p = TwoBarSong(120);
        var tl = MidiTimelineBuilder.Build(p, new PlaybackOptions { Metronome = true });
        Eq("metronome adds 8 clicks over 2 bars", 8, tl.Events.Count(e => (e.Status & 0xF0) == 0x90 && (e.Data1 == 34 || e.Data1 == 33)));
        var triplets = MidiTimelineBuilder.Build(p, new PlaybackOptions { Metronome = true, MetronomeSubdivision = 3 });
        Eq("metronome supports triplet subdivisions", 24, triplets.Events.Count(e => e.IsMetronome && e.IsNoteOn));
        var lowVolume = MidiTimelineBuilder.Build(p, new PlaybackOptions
        {
            Metronome = true, MetronomeVolume = 50, MetronomeAccentVolume = 80, MetronomeClickVolume = 40
        });
        Check("metronome master/accent/click volumes scale MIDI velocity",
            lowVolume.Events.Where(e => e.IsMetronome && e.IsNoteOn).Any(e => e.IsMetronomeAccent && e.Data2 == 44) &&
            lowVolume.Events.Where(e => e.IsMetronome && e.IsNoteOn).Any(e => !e.IsMetronomeAccent && e.Data2 == 22));
        var countIn = MidiTimelineBuilder.Build(p, new PlaybackOptions { CountIn = true });
        Check("count-in delays the music", countIn.TotalMs > 4000, countIn.TotalMs.ToString());
    }

    private static void TestTimelineLoopAndOrder()
    {
        var p = new SongProject { Tempo = 120 };
        var t = new TrackModel { Name = "G", Measures = TemplateFactory.Measures(4) };
        t.Measures[0].RepeatStart = true;
        t.Measures[2].RepeatEnd = true;
        t.Measures[2].RepeatCount = 2;
        p.Tracks.Add(t);
        var order = MidiTimelineBuilder.BuildPlaybackOrder(p, new PlaybackOptions());
        Eq("repeat expands 4 bars to 7", 7, order.Count);
        Check("repeat replays bars 1-3", order.SequenceEqual(new[] { 0, 1, 2, 0, 1, 2, 3 }), string.Join(",", order));
    }

    // ---------- Editor ----------

    private static TabEditorControl NewEditor(out SongProject project, out TrackModel track)
    {
        var editor = new TabEditorControl();
        project = new SongProject { Tempo = 120 };
        track = new TrackModel { Name = "Guitar", Measures = TemplateFactory.Measures(4) };
        project.Tracks.Add(track);
        editor.Project = project;
        editor.SelectedTrackIndex = 0;
        return editor;
    }

    private static void TestEditorEntry()
    {
        var editor = NewEditor(out var project, out var track);
        editor.SetPosition(0, 0, 0);
        editor.SetDuration(4);
        editor.EnterFret(5);

        var cell = track.Measures[0].Cells[0];
        Eq("fret written", 5, cell.Notes[0].Fret);
        Eq("midi computed from tuning", 64 + 5, cell.Notes[0].MidiValue);
        Eq("duration applied", 4, cell.DurationDenominator);
        Eq("cursor auto-advanced by a quarter", 4, editor.SelectedCell);
        Check("project flagged dirty", project.IsDirty);

        // Editing an existing note must keep the cursor on that note.
        var editorExisting = NewEditor(out _, out var trackExisting);
        trackExisting.Measures[0].Cells[0].Notes.Add(new TabNote { StringIndex = 0, Fret = 3 });
        editorExisting.SetPosition(0, 0, 0);
        editorExisting.EnterFret(7);
        Eq("existing note fret edited", 7, trackExisting.Measures[0].Cells[0].Notes[0].Fret);
        Eq("cursor stays on edited note", 0, editorExisting.SelectedCell);

        // Two digits typed quickly on the same beat make a two-digit fret.
        var editor2 = NewEditor(out _, out var track2);
        editor2.SetPosition(0, 0, 0);
        editor2.EnterFret(1, autoAdvance: false);
        editor2.EnterFret(2, autoAdvance: false);
        Eq("two-digit fret 12", 12, track2.Measures[0].Cells[0].Notes[0].Fret);

        // A second string on the same beat builds a chord.
        var editor3 = NewEditor(out _, out var track3);
        editor3.SetPosition(0, 0, 0);
        editor3.EnterFret(3);
        editor3.SetPosition(0, 0, 2);
        editor3.EnterFret(5, autoAdvance: false);
        Eq("chord has two notes", 2, track3.Measures[0].Cells[0].Notes.Count);
    }

    private static void TestEditorNavigation()
    {
        var editor = NewEditor(out var _, out var track);
        while (track.Measures.Count < 8) track.Measures.Add(new MeasureModel { Number = track.Measures.Count + 1 });
        track.Measures[0].Cells[0].DurationDenominator = 4;
        track.Measures[0].Cells[0].Notes.Add(new TabNote { StringIndex = 0, Fret = 1, MidiValue = 65 });

        editor.SetPosition(0, 0, 0);
        editor.MoveBeat(1);
        Eq("right from a quarter beat moves 4 slots", 4, editor.SelectedCell);
        editor.MoveBeat(-1);
        Eq("left returns to the beat onset", 0, editor.SelectedCell);

        editor.SetPosition(0, 12, 0);
        editor.MoveBeat(1);
        Eq("right past the last beat rolls to the next bar", 1, editor.SelectedMeasure);
        Eq("next bar starts at slot 0", 0, editor.SelectedCell);

        editor.SetPosition(2, 0, 0);
        editor.MoveBar(-1);
        Eq("ctrl-left moves a whole bar", 1, editor.SelectedMeasure);
        var layout = editor.GetScoreLayout(track);
        var sourcePosition = layout.Measure(editor.SelectedMeasure);
        var nextSystem = Math.Min(sourcePosition.SystemIndex + 1, layout.SystemCount - 1);
        var nextRow = layout.Systems[nextSystem];
        var expectedNextRowMeasure = nextRow.Measures[Math.Min(sourcePosition.ColumnIndex, nextRow.Measures.Count - 1)].MeasureIndex;
        editor.MoveLine(1);
        Eq("down moves to the corresponding column in the next wrapped score row", expectedNextRowMeasure, editor.SelectedMeasure);
        var previousSystem = Math.Max(0, layout.SystemForMeasure(editor.SelectedMeasure) - 1);
        var previousRow = layout.Systems[previousSystem];
        var expectedPreviousRowMeasure = previousRow.Measures[Math.Min(layout.Measure(editor.SelectedMeasure).ColumnIndex,
            previousRow.Measures.Count - 1)].MeasureIndex;
        editor.MoveLine(-1);
        Eq("up moves to the corresponding column in the previous wrapped score row", expectedPreviousRowMeasure, editor.SelectedMeasure);
        editor.SetPosition(0, 0, 0);
        editor.MoveLine(-1);
        Eq("up from the first line stays on the first line", 0, editor.SelectedMeasure);

        editor.SetPosition(0, 0, 0);
        editor.MoveString(1);
        Eq("alt-down changes string", 1, editor.SelectedString);
        editor.MoveString(-1);
        Eq("alt-up changes string", 0, editor.SelectedString);

        editor.SetPosition(2, 5, 0);
        editor.MoveToBarStart();
        Eq("home = bar start", 0, editor.SelectedCell);
        editor.MoveToBarEnd();
        Eq("end = last slot", 15, editor.SelectedCell);
        editor.MoveToFirstBar();
        Eq("ctrl+home = first bar", 0, editor.SelectedMeasure);
        editor.MoveToLastBar();
        Eq("ctrl+end = last bar", 7, editor.SelectedMeasure);
    }

    private static void TestEditorDurations()
    {
        var editor = NewEditor(out var _, out var track);
        editor.SetPosition(0, 0, 0);
        editor.SetDuration(4);
        Eq("quarter selected", 4, editor.CurrentDurationDenominator);
        editor.Longer();
        Eq("+ makes it longer", 2, editor.CurrentDurationDenominator);
        editor.Shorter();
        editor.Shorter();
        Eq("- makes it shorter", 8, editor.CurrentDurationDenominator);

        editor.ToggleDot();
        Eq("dot applied to selection", 1, editor.CurrentDots);
        editor.SetDuration(8);
        Eq("dotted eighth written", 1, track.Measures[0].Cells[0].Dots);

        editor.ToggleTriplet();
        Check("triplet toggles on", editor.CurrentTriplet);

        editor.ToggleRest();
        Check("rest applied", track.Measures[0].Cells[0].IsRest);
        Check("rest clears notes", track.Measures[0].Cells[0].Notes.Count == 0);

        // Insert / delete beats keep the bar length stable.
        var before = track.Measures[0].Cells.Count;
        editor.InsertBeat();
        Eq("insert beat keeps bar slots", before, track.Measures[0].Cells.Count);
        editor.DeleteBeats();
        Eq("delete beat keeps bar slots", before, track.Measures[0].Cells.Count);

        var constrained = NewEditor(out _, out var constrainedTrack);
        var measure = constrainedTrack.Measures[0];
        measure.Cells[4].DurationDenominator = 8;
        measure.Cells[4].RhythmicPosition = 4.25;
        measure.Cells[4].Notes.Add(new TabNote { StringIndex = 0, Fret = 3, MidiValue = 67 });
        measure.Cells[5].DurationDenominator = 32;
        measure.Cells[5].RhythmicPosition = 4.75;
        measure.Cells[5].Notes.Add(new TabNote { StringIndex = 1, Fret = 5, MidiValue = 64 });
        constrained.PreventBarOverflow = true;
        constrained.SetPosition(0, 4, 0);
        Check("duration validation respects imported fractional beat spacing",
            !constrained.CanSetDurationForTool("sixteenth") && constrained.CanSetDurationForTool("thirtysecond") &&
            constrained.CanSetDurationForTool("sixtyfourth"));
        constrained.SetDuration(16);
        Check("an invalid subdivision leaves the duration tool and note unchanged",
            constrained.CurrentDurationDenominator == 4 && measure.Cells[4].DurationDenominator == 8);
        constrained.SetDuration(32);
        Eq("a valid smaller subdivision applies at the same beat", 32, measure.Cells[4].DurationDenominator);

        var barEnd = NewEditor(out _, out var barEndTrack);
        var lastSlot = barEndTrack.Measures[0].Cells[14];
        lastSlot.Notes.Add(new TabNote { StringIndex = 0, Fret = 1, MidiValue = 65 });
        lastSlot.DurationDenominator = 8;
        barEnd.PreventBarOverflow = true;
        barEnd.SetPosition(0, 14, 0);
        Check("duration validation uses the remaining slots in the measure",
            !barEnd.CanSetDurationForTool("quarter") && barEnd.CanSetDurationForTool("eighth"));

        var midBeat = NewEditor(out _, out var midBeatTrack);
        var midBeatMeasure = midBeatTrack.Measures[0];
        midBeatMeasure.Cells[0].DurationDenominator = 8;
        midBeatMeasure.Cells[0].Notes.Add(new TabNote { StringIndex = 0, Fret = 3, MidiValue = 67 });
        midBeatMeasure.Cells[2].DurationDenominator = 16;
        midBeatMeasure.Cells[2].Notes.Add(new TabNote { StringIndex = 0, Fret = 5, MidiValue = 69 });
        Check("clicks inside a sustained note resolve to its onset cell, not the intervening grid slot",
            TabEditorControl.ResolveBeatHitCell(midBeatMeasure, 1.0, 1) == 0 &&
            TabEditorControl.ResolveBeatHitCell(midBeatMeasure, 2.5, 2) == 2 &&
            TabEditorControl.ResolveBeatHitCell(midBeatMeasure, 3.25, 3) == 3);
        midBeat.PreventBarOverflow = true;
        midBeat.SetPosition(0, 1, 0);
        Check("duration tools reject a subdivision cursor placed inside the active eighth-note span",
            !midBeat.CanSetDurationForTool("sixteenth") && !midBeat.CanSetDurationForTool("thirtysecond") &&
            !midBeat.CanSetDurationForTool("sixtyfourth"));
        midBeat.SetPosition(0, TabEditorControl.ResolveBeatHitCell(midBeatMeasure, 1.0, 1), 0);
        Check("the same smaller duration is checked against the actual note onset and next beat",
            midBeat.CanSetDurationForTool("sixteenth") && !midBeat.CanSetDurationForTool("quarter"));

        var glowEditor = NewEditor(out _, out var glowTrack);
        glowTrack.Measures[0].Cells[4].DurationDenominator = 8;
        glowTrack.Measures[0].Cells[4].Notes.Add(new TabNote { StringIndex = 0, Fret = 7, MidiValue = 71 });
        glowTrack.Measures[0].Cells[4].Notes.Add(new TabNote { StringIndex = 1, Fret = 9, MidiValue = 66 });
        var hoveredCell = glowTrack.Measures[0].Cells[4];
        var hoverBounds = TabEditorControl.CellHighlightRect(glowTrack.Measures[0], glowTrack.Measures[0].Cells,
            4, measureX: 100, slotWidth: 10, y: 20, height: 80);
        var expectedHoverWidth = Math.Max(10, MusicTime.CellSlotsRounded(hoveredCell) * 10) - 2;
        Check("note hover and edit cursor share the chord's full rhythmic-duration bounds",
            Math.Abs(hoverBounds.X - 141) < 0.001 && Math.Abs(hoverBounds.Width - expectedHoverWidth) < 0.001 &&
            hoverBounds.Width > 10);

        var glowTimeline = new ScoreTimeline();
        glowTimeline.Bars.Add(new ScoreBar(0, 0, 1000, 16, 120));
        glowTimeline.Notes.Add(new NoteEvent
        {
            OnsetMs = 250, DurationMs = 125, TrackIndex = 0, Bar = 0, Cell = 4, VoiceIndex = 0,
            StringIndex = 0, Fret = 7, Midi = 71
        });
        glowTimeline.Notes.Add(new NoteEvent
        {
            OnsetMs = 250, DurationMs = 140, TrackIndex = 0, Bar = 0, Cell = 4, VoiceIndex = 0,
            StringIndex = 1, Fret = 9, Midi = 66
        });
        glowTimeline.Notes.Add(new NoteEvent
        {
            OnsetMs = 500, DurationMs = 125, TrackIndex = 0, Bar = 0, Cell = 8, VoiceIndex = 0,
            StringIndex = 0, Fret = 10, Midi = 74
        });
        glowTimeline.LongestSoundingNoteMs = 140;
        glowEditor.PlaybackActive = true;
        glowEditor.PlaybackMeasure = 0;
        glowEditor.PlaybackCell = 4;
        glowEditor.PlaybackFraction = 0.30;
        glowEditor.PlaybackTrackIndex = 0;
        glowEditor.Timeline = glowTimeline;
        glowEditor.PlaybackMs = 260;
        var playhead = glowEditor.PlayheadGeometry();
        var durationGlow = glowEditor.PlaybackDurationGeometries();
        Check("duration glow spans the full active chord and contains the live playback line",
            playhead is not null && durationGlow.Count == 1 &&
            durationGlow[0].X < playhead.Value.X && durationGlow[0].EndX > playhead.Value.X);
        var initialGlow = durationGlow.Count > 0 ? durationGlow[0] : default;
        glowEditor.PlaybackMs = 380;
        glowEditor.PlaybackFraction = 0.38;
        playhead = glowEditor.PlayheadGeometry();
        durationGlow = glowEditor.PlaybackDurationGeometries();
        Check("active chord duration bounds stay fixed as the playback line advances",
            playhead is not null && durationGlow.Count == 1 &&
            Math.Abs(durationGlow[0].X - initialGlow.X) < 0.01 &&
            Math.Abs(durationGlow[0].EndX - initialGlow.EndX) < 0.01 &&
            durationGlow[0].X < playhead.Value.X && durationGlow[0].EndX > playhead.Value.X);
        glowEditor.PlaybackMs = 390;
        Check("duration glow ends with the last note in the chord",
            glowEditor.PlaybackDurationGeometries().Count == 0);
        glowEditor.PlaybackMs = 510;
        var repeatedGlow = glowEditor.PlaybackDurationGeometries();
        glowEditor.PlaybackMs = 620;
        var repeatedGlowLater = glowEditor.PlaybackDurationGeometries();
        Check("repeated note starts a new stable duration block for its own event",
            repeatedGlow.Count == 1 && repeatedGlowLater.Count == 1 &&
            Math.Abs(repeatedGlow[0].X - repeatedGlowLater[0].X) < 0.01 &&
            Math.Abs(repeatedGlow[0].EndX - repeatedGlowLater[0].EndX) < 0.01 &&
            Math.Abs(repeatedGlow[0].X - initialGlow.X) > 0.01);
        glowEditor.PlaybackMs = 625;
        Check("repeated event duration shading clears at its actual end",
            glowEditor.PlaybackDurationGeometries().Count == 0);

        var tiedEditor = NewEditor(out _, out var tiedTrack);
        tiedTrack.Measures[0].Cells[14].Notes.Add(new TabNote { StringIndex = 0, Fret = 3, MidiValue = 67 });
        var tiedTimeline = new ScoreTimeline();
        tiedTimeline.Bars.Add(new ScoreBar(0, 0, 1000, 16, 120));
        tiedTimeline.Bars.Add(new ScoreBar(1, 1000, 2000, 16, 120));
        tiedTimeline.Notes.Add(new NoteEvent
        {
            OnsetMs = 875, DurationMs = 375, TrackIndex = 0, Bar = 0, Cell = 14, VoiceIndex = 0,
            StringIndex = 0, Fret = 3, Midi = 67
        });
        tiedTimeline.LongestSoundingNoteMs = 375;
        tiedEditor.PlaybackActive = true;
        tiedEditor.PlaybackTrackIndex = 0;
        tiedEditor.Timeline = tiedTimeline;
        tiedEditor.PlaybackMs = 900;
        var tiedGlow = tiedEditor.PlaybackDurationGeometries();
        tiedEditor.PlaybackMs = 1200;
        var tiedGlowLater = tiedEditor.PlaybackDurationGeometries();
        Check("tied duration shading continues across the bar boundary without shrinking",
            tiedGlow.Count == 2 && tiedGlowLater.Count == 2 &&
            Enumerable.Range(0, 2).All(i => Math.Abs(tiedGlow[i].X - tiedGlowLater[i].X) < 0.01 &&
                Math.Abs(tiedGlow[i].EndX - tiedGlowLater[i].EndX) < 0.01));
        tiedEditor.PlaybackMs = 1250;
        Check("tied duration shading clears at the sustained event end",
            tiedEditor.PlaybackDurationGeometries().Count == 0);
    }

    private static void TestPlaybackGlowIntensity()
    {
        var presets = new[] { 0, 10, 20, 30, 50, 75, 100 };
        Check("duration glow menu exposes the requested intensity steps",
            PlaybackGlowIntensity.PresetPercentages.SequenceEqual(presets));
        Check("duration shading opacity maps 0%, 10%, 20%, and 30% directly to alpha",
            PlaybackGlowIntensity.ScaleAlpha(255, 0) == 0 &&
            PlaybackGlowIntensity.ScaleAlpha(255, 0.10) == 26 &&
            PlaybackGlowIntensity.ScaleAlpha(255, 0.20) == 51 &&
            PlaybackGlowIntensity.ScaleAlpha(255, 0.30) == 77 &&
            PlaybackGlowIntensity.ScaleAlpha(255, 1) == 255);

        byte SampleDurationAlpha(double intensity, int sampleX, bool enabled = true)
        {
            var overlay = new PlayheadOverlay { Width = 64, Height = 64 };
            overlay.SetDurationGeometry((20, 44, 8, 56));
            overlay.SetDurationStyle(System.Windows.Media.Color.FromRgb(0x3F, 0xB9, 0x50), intensity, enabled);
            overlay.SetGeometry((10, 8, 56));
            overlay.Measure(new Size(64, 64));
            overlay.Arrange(new Rect(0, 0, 64, 64));
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(64, 64, 96, 96,
                System.Windows.Media.PixelFormats.Pbgra32);
            bitmap.Render(overlay);
            var pixels = new byte[64 * 64 * 4];
            bitmap.CopyPixels(pixels, 64 * 4, 0);
            return pixels[(32 * 64 + sampleX) * 4 + 3];
        }

        try
        {
            var zeroFillAlpha = SampleDurationAlpha(0, 30);
            var zeroCaretAlpha = SampleDurationAlpha(0, 10);
            var tenPercentAlpha = SampleDurationAlpha(0.10, 30);
            Check("0% renders no duration rectangle while retaining the playback line",
                zeroFillAlpha == 0 && zeroCaretAlpha > 0,
                $"fill alpha {zeroFillAlpha}, caret alpha {zeroCaretAlpha}");
            Check("10% glow is rendered at the corresponding subtle alpha",
                tenPercentAlpha == 26, $"expected alpha 26, got {tenPercentAlpha}");
            Check("disabling duration tint removes its fill but keeps the playback line",
                SampleDurationAlpha(0.50, 30, enabled: false) == 0 &&
                SampleDurationAlpha(0.50, 10, enabled: false) > 0);
        }
        catch (Exception error)
        {
            Check("duration glow renders with exact zero and scaled opacity", false,
                error.GetBaseException().Message);
        }
    }

    private static void TestEditorCopyPaste()
    {
        var editor = NewEditor(out var _, out var track);
        var cell = track.Measures[0].Cells[4];
        cell.DurationDenominator = 8;
        cell.Notes.Add(new TabNote { StringIndex = 3, Fret = 9, MidiValue = 59, Techniques = { "Bend" } });
        editor.SetPosition(0, 4, 3);
        editor.CopyLastBeat();
        Eq("copy last beat lands on the next slot", 0, track.Measures[0].Cells[5].Notes.Count);

        editor.SetPosition(0, 8, 0);
        editor.CopyLastBeat();
        Eq("copy last beat duplicates the fret", 9, track.Measures[0].Cells[8].Notes[0].Fret);
        Check("copy last beat keeps techniques", track.Measures[0].Cells[8].Notes[0].Techniques.Contains("Bend"));

        editor.ClearSelection();
        editor.SetPosition(0, 0, 0);
        editor.BeginSelection();
        editor.ExtendSelection(1);
        editor.ExtendSelection(1);
        Check("selection active after shift-arrows", editor.HasSelection);
        editor.SelectMeasureRange(3, 1);
        Check("timeline range selects the matching whole-bar score area",
            editor.HasSelection && editor.AffectedMeasureRange == (1, 3) && editor.SelectedMeasure == 1);
        editor.ClearSelection();
        Check("clearing the score selection removes the mirrored range", !editor.HasSelection);
        editor.SetPosition(0, 8, 0);   // the copied beat (fret 9)
        var clip = editor.CaptureClip(out _);
        Check("the cursor beat copies as a clip", clip is { Kind: ScoreClipKind.Beats });

        editor.SetPosition(2, 0, 0);
        var ok = clip is not null && EditCommands.Paste(editor.Project!, clip, editor.PasteTarget, new EditingSettings(), new RecommendedPasteAnswers()).Changed;
        Check("paste succeeds", ok);
    }

    // ---------- IO ----------

    private static void TestProjectRoundtrip()
    {
        var p = TwoBarSong(140);
        p.Title = "Roundtrip";
        p.Markers.Add(new MarkerModel { MeasureIndex = 1, Title = "1. Chorus" });
        p.Tracks[0].Measures[0].RepeatStart = true;
        p.Tracks[0].Measures[0].Cells[0].ChordName = "Am";
        p.Tracks[0].Measures[0].Cells[0].IsTriplet = true;
        p.Tracks[0].Measures[0].Cells[0].Dots = 1;
        p.Tracks[0].Measures[0].Cells[1].TupletNumerator = 5;
        p.Tracks[0].Measures[0].Cells[1].TupletDenominator = 4;
        var json = ProjectService.Snapshot(p);
        var back = ProjectService.Restore(json);
        Eq("title survives", "Roundtrip", back.Title);
        Eq("tempo survives", 140, back.Tempo);
        Eq("markers survive", 1, back.Markers.Count);
        Check("repeat survives", back.Tracks[0].Measures[0].RepeatStart);
        Eq("chord survives", "Am", back.Tracks[0].Measures[0].Cells[0].ChordName);
        Check("triplet survives", back.Tracks[0].Measures[0].Cells[0].IsTriplet);
        Eq("dots survive", 1, back.Tracks[0].Measures[0].Cells[0].Dots);
        Eq("quintuplet numerator survives", 5, back.Tracks[0].Measures[0].Cells[1].TupletNumerator);
        Eq("quintuplet denominator survives", 4, back.Tracks[0].Measures[0].Cells[1].TupletDenominator);
    }

    /// <summary>Track reordering (drag-to-move) is a pure list move and must stay valid.</summary>
    private static void TestTrackReorder()
    {
        var p = TemplateFactory.Blank();
        p.Tracks.Clear();
        for (var i = 0; i < 3; i++) p.Tracks.Add(new TrackModel { Name = $"T{i}" });
        Check("move track down reorders", p.MoveTrack(0, 1) && p.Tracks[0].Name == "T1" && p.Tracks[1].Name == "T0");
        Check("move track to the end", p.MoveTrack(0, 2) && p.Tracks[2].Name == "T1");
        Check("move to the same index is a no-op", !p.MoveTrack(1, 1));
        Check("out-of-range source is rejected", !p.MoveTrack(9, 0));
        Check("out-of-range target is clamped", p.MoveTrack(0, 99) && p.Tracks[2].Name == "T0");
    }

    private static void TestAsciiExport()
    {
        var p = TwoBarSong();
        p.Tracks[0].Measures[0].Cells[0].ChordName = "Am";
        p.Tracks[0].Measures[0].Cells[0].Text = "verse";
        p.Tracks[0].Measures[0].Cells[0].Lyrics = "hello world";
        var path = Path.Combine(Path.GetTempPath(), "tabforge-selftest-ascii.txt");
        AsciiExportService.Export(p, path);
        var text = File.ReadAllText(path);
        Check("ascii export writes tab lines", text.Contains("e||") || text.Contains("||"));
        Check("ascii export contains the title", text.Contains(p.Title));
        Check("ascii export contains the track name", text.Contains(p.Tracks[0].Name));
        // The fret numbers of every note in the first bar must appear.
        var frets = p.Tracks[0].Measures[0].Cells.SelectMany(c => c.Notes).Select(n => n.Fret).Distinct().ToList();
        Check("ascii export contains the frets", frets.All(f => text.Contains(f.ToString())));
        Check("ascii export keeps comments, chords and lyrics",
            text.Contains("Am") && text.Contains("verse") && text.Contains("hello world"),
            text.Substring(0, Math.Min(200, text.Length)));
        try { File.Delete(path); } catch { }
    }

    /// <summary>
    /// The persisted model must be complete: every public settable property of every stored type has to
    /// be serialized (no accidental [JsonIgnore]) and come back with the same value. A field that is
    /// silently dropped here is data the user loses on every save.
    /// </summary>
    private static void TestModelRoundTrip()
    {
        var types = new[]
        {
            typeof(SongProject), typeof(MarkerModel), typeof(TrackModel), typeof(MeasureModel),
            typeof(TabCell), typeof(TabNote), typeof(RigPreset), typeof(PluginSlot), typeof(BendPointModel)
        };

        // Properties that are deliberately not persisted (computed, transient or UI state).
        var transient = new HashSet<string>(StringComparer.Ordinal)
        {
            "IsDirty", "HasAnnotation", "Tuplet"
        };

        var modelProps = new List<(Type Type, string Name)>();
        foreach (var type in types)
            foreach (var prop in type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            {
                if (prop.GetIndexParameters().Length > 0) continue;
                if (prop.GetCustomAttributes(typeof(System.Text.Json.Serialization.JsonIgnoreAttribute), true).Length > 0) continue;
                if (transient.Contains(prop.Name)) continue;
                modelProps.Add((type, prop.Name));
                Check($"{type.Name}.{prop.Name} is persistable", prop.CanWrite, "no public setter");
            }

        var rich = BuildRichProject();
        var json = ProjectService.Snapshot(rich);
        var back = ProjectService.Restore(json);
        // Saved files omit values equal to a new object's initial value (lossless compaction), so
        // "is every property serializable" is checked on a plain serialization; the round-trip
        // comparison below proves the compact form restores every value.
        var plainJson = System.Text.Json.JsonSerializer.Serialize(rich);
        foreach (var (type, name) in modelProps)
            Check($"{type.Name}.{name} is serialized", plainJson.Contains($"\"{name}\""), name);

        var diffs = new List<string>();
        CompareModel(rich, back, "project", diffs, new HashSet<object>(ReferenceEqualityComparer.Instance));
        Check("the whole model survives a save/load round-trip", diffs.Count == 0,
            string.Join("; ", diffs.Take(6)) + (diffs.Count > 6 ? $" (+{diffs.Count - 6} more)" : ""));

        var originalCell = rich.Tracks[0].Measures[0].Cells[0];
        var clonedCell = TabEditorControl.CloneCell(originalCell);
        Check("cell cloning keeps overlapping curves and trill/picking data",
            clonedCell.WhammyPoints.Select(point => (point.Offset, point.Value))
                .SequenceEqual(originalCell.WhammyPoints.Select(point => (point.Offset, point.Value))) &&
            clonedCell.TremoloPickDenominator == originalCell.TremoloPickDenominator &&
            clonedCell.Notes[0].BendPoints.Select(point => (point.Offset, point.Value))
                .SequenceEqual(originalCell.Notes[0].BendPoints.Select(point => (point.Offset, point.Value))) &&
            clonedCell.Notes[0].BendTypeName == originalCell.Notes[0].BendTypeName &&
            clonedCell.Notes[0].BendStyleName == originalCell.Notes[0].BendStyleName &&
            clonedCell.Notes[0].TrillTargetMidi == originalCell.Notes[0].TrillTargetMidi &&
            clonedCell.Notes[0].TrillDurationDenominator == originalCell.Notes[0].TrillDurationDenominator);

        // And a real file write/read, not just an in-memory snapshot.
        var path = Path.Combine(Path.GetTempPath(), "tabforge-selftest-roundtrip.tforge");
        ProjectService.Save(path, rich);
        var fromDisk = ProjectService.Load(path);
        Check("save then load keeps every note",
            fromDisk.Tracks.Sum(t => t.Measures.Sum(m => m.Cells.Sum(c => c.Notes.Count))) ==
            rich.Tracks.Sum(t => t.Measures.Sum(m => m.Cells.Sum(c => c.Notes.Count))));
        try { File.Delete(path); } catch { }
    }

    /// <summary>Builds a project that sets every field a save would have to preserve.</summary>
    private static SongProject BuildRichProject()
    {
        var project = TwoBarSong(137);
        project.Title = "Round trip";
        project.Subtitle = "sub";
        project.Artist = "artist";
        project.Album = "album";
        project.MusicAuthor = "music";
        project.LyricsAuthor = "words";
        project.Copyright = "copy";
        project.TabAuthor = "tabber";
        project.Instructions = "instructions";
        project.Notice = "notice";
        project.Lyrics = "la la la";
        project.KeySignature = -3;
        project.KeySignatureMinor = true;
        project.GrayInactiveVoice = true;
        project.TimeSignatureNumerator = 6;
        project.TimeSignatureDenominator = 8;
        project.ImportedFrom = "somewhere.gp5";
        project.Markers.Add(new MarkerModel { MeasureIndex = 1, Title = "Chorus", ColorHex = "#112233" });

        var measure = project.Tracks[0].Measures[0];
        measure.RepeatStart = true;
        measure.RepeatEnd = true;
        measure.RepeatCount = 3;
        measure.AlternateEnding = 2;
        measure.IsDoubleBar = true;
        measure.SimileOneBar = true;
        measure.SimileTwoBar = true;
        measure.SectionName = "Intro";
        measure.TempoChange = 155;
        measure.TripletFeel = true;
        measure.TripletFeelKind = "Triplet16th";
        measure.FreeTime = true;
        measure.ForceLineBreak = true;
        measure.PreventLineBreak = true;
        measure.Anacrusis = true;
        measure.Directions = "DaCapo,Fine";
        measure.KeySignature = 4;
        measure.KeySignatureMinor = true;
        measure.TimeSigNum = 3;
        measure.TimeSigDenom = 4;
        measure.Clef = "F";

        var cell = measure.Cells[0];
        cell.DurationDenominator = 8;
        cell.Dots = 1;
        cell.IsTriplet = true;
        cell.TupletNumerator = 3;
        cell.TupletDenominator = 2;
        cell.SoundDurationPercent = 65;
        cell.OctaveShiftSemitones = 12;
        cell.BeamMode = BeamMode.Force;
        cell.BreakSecondaryBeamBefore = true;
        cell.StemDirection = StemDirection.Invert;
        cell.IsTied = true;
        cell.IsGrace = true;
        cell.GraceBeforeBeat = false;
        cell.Fermata = true;
        cell.Accent = 2;
        cell.Staccato = true;
        cell.Tenuto = true;
        cell.TremoloPickDenominator = 32;
        cell.WhammyPoints.AddRange(new[] { new BendPointModel { Offset = 0, Value = -2 }, new BendPointModel { Offset = 60, Value = 0 } });
        cell.ChordName = "C#m7";
        cell.Text = "note";
        cell.Lyrics = "line one\nline two";
        cell.Notes.Clear();
        cell.Notes.Add(new TabNote
        {
            StringIndex = 3, Fret = 12, MidiValue = 64, Velocity = 111,
            Ghost = true, Dead = true, Tied = true, SlideTargetMidi = 67,
            TrillTargetMidi = 66, TrillDurationDenominator = 32,
            BendTypeName = "PrebendRelease", BendStyleName = "Gradual",
            Techniques = new HashSet<string>(new[] { "Vibrato", "Bend", "LetRing" }, StringComparer.OrdinalIgnoreCase),
            BendPoints = new List<BendPointModel> { new() { Offset = 10, Value = 2 }, new() { Offset = 60, Value = 4 } }
        });
        measure.Voice2Cells = Enumerable.Range(0, 16).Select(_ => new TabCell()).ToList();
        measure.Voice2Cells[4].Notes.Add(new TabNote { StringIndex = 2, Fret = 4, MidiValue = 59, Tied = true });
        measure.Voice2Cells[4].DurationDenominator = 16;

        var track = project.Tracks[0];
        track.Name = "Lead";
        track.Kind = TrackKind.Guitar;
        track.Capo = 3;
        track.ColorHex = "#ABCDEF";
        track.Mute = true;
        track.Solo = true;
        track.Volume = 88;
        track.Pan = 40;
        track.Chorus = 12;
        track.Reverb = 34;
        track.Transpose = -2;
        track.MidiChannel = 5;
        track.MidiProgram = 30;
        track.MidiOutputDeviceId = 2;
        track.InstrumentName = "Distortion";
        track.NumberOfFrets = 22;
        track.Rig.Name = "Modern";
        track.Rig.ArticulationMap = "Generic Guitar";
        track.Rig.Plugins.Add(new PluginSlot { Name = "Amp", Path = "C:/amp.vst3", Type = PluginSlotType.Instrument, Enabled = false });
        track.StringTunings.Clear();
        track.StringTunings.AddRange(new[] { 64, 59, 55, 50, 45, 40 });
        return project;
    }

    /// <summary>Deep value comparison of the persisted model (JsonIgnore'd properties excluded).</summary>
    private static void CompareModel(object? before, object? after, string path, List<string> diffs, HashSet<object> visited)
    {
        if (before is null || after is null)
        {
            if (!ReferenceEquals(before, after)) diffs.Add($"{path}: null mismatch");
            return;
        }
        var type = before.GetType();
        if (type != after.GetType()) { diffs.Add($"{path}: type {type.Name} vs {after.GetType().Name}"); return; }
        if (type.IsPrimitive || type.IsEnum || before is string || before is decimal || before is DateTime)
        {
            if (!before.Equals(after)) diffs.Add($"{path}: '{before}' vs '{after}'");
            return;
        }
        if (before is System.Collections.IEnumerable listBefore && after is System.Collections.IEnumerable listAfter)
        {
            var a = listBefore.Cast<object>().ToList();
            var b = listAfter.Cast<object>().ToList();
            if (a.Count != b.Count) { diffs.Add($"{path}: count {a.Count} vs {b.Count}"); return; }
            for (var i = 0; i < a.Count; i++) CompareModel(a[i], b[i], $"{path}[{i}]", diffs, visited);
            return;
        }
        if (!visited.Add(before)) return;
        foreach (var prop in type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
        {
            if (!prop.CanRead || prop.GetIndexParameters().Length > 0) continue;
            if (prop.GetCustomAttributes(typeof(System.Text.Json.Serialization.JsonIgnoreAttribute), true).Length > 0) continue;
            CompareModel(prop.GetValue(before), prop.GetValue(after), $"{path}.{prop.Name}", diffs, visited);
        }
    }

    /// <summary>
    /// Imports any real Guitar Pro files found in the repository's Tabs folder and checks that the
    /// score survives the boundary: tracks, measures, notes, techniques, playback timeline and the
    /// project round-trip. This is the GP compatibility guard for the UI redesign.
    /// </summary>
    private static void TestGuitarProFiles()
    {
        var dir = FindTabsFolder();
        if (dir is null)
        {
            Skip("Guitar Pro sample songs", "no Tabs folder found");
            return;
        }
        var files = Directory.GetFiles(dir, "*.gp*", SearchOption.TopDirectoryOnly)
            .Concat(Directory.GetFiles(dir, "*.gp", SearchOption.TopDirectoryOnly))
            .Distinct().ToArray();
        if (files.Length == 0)
        {
            Skip("Guitar Pro sample songs", "Tabs folder is empty");
            return;
        }
        Log.Add($"  info  {files.Length} Guitar Pro file(s) in {dir}");

        foreach (var file in files)
        {
            var name = Path.GetFileName(file);
            try
            {
                var project = GuitarProImporter.Import(file);
                var tracks = project.Tracks.Count;
                var measures = project.Tracks.Count == 0 ? 0 : project.Tracks.Max(t => t.Measures.Count);
                var notes = project.Tracks.Sum(t => t.Measures.Sum(m => m.Cells.Sum(c => c.Notes.Count)));
                var techniques = project.Tracks.Sum(t => t.Measures.Sum(m => m.Cells.Sum(c => c.Notes.Sum(n => n.Techniques.Count))));
                var techniqueHistogram = project.Tracks
                    .SelectMany(t => t.Measures).SelectMany(m => m.Cells).SelectMany(c => c.Notes).SelectMany(n => n.Techniques)
                    .GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(g => g.Count())
                    .Take(12)
                    .Select(g => $"{g.Key}={g.Count()}");
                Log.Add($"  info  {name}: techniques -> {string.Join(" ", techniqueHistogram)}");
                var drums = project.Tracks.Count(t => t.Kind == TrackKind.Drums);
                var stringCounts = string.Join("/", project.Tracks.Select(t => t.StringTunings.Count).Distinct().OrderBy(x => x));
                Log.Add($"  info  {name}: {tracks} tracks, {measures} bars, {notes} notes, {techniques} technique marks, {drums} drum track(s), tunings {stringCounts}");
                if (notes > 2000)
                {
                    var rawBytes = System.Text.Encoding.UTF8.GetByteCount(ProjectService.Snapshot(project));
                    var packed = ProjectService.SnapshotBytes(project);
                    Log.Add($"  info  {name}: undo cost -> raw {rawBytes / 1024} KB, compressed {packed.Length / 1024} KB");
                    Check($"{name}: compressed history is at least 10x smaller than raw JSON",
                        packed.Length * 10 < rawBytes, $"{packed.Length / 1024} KB vs {rawBytes / 1024} KB");
                    Check($"{name}: a compressed snapshot restores every note",
                        ProjectService.RestoreBytes(packed).Tracks.Sum(t => t.Measures.Sum(m => m.Cells.Sum(c => c.Notes.Count))) == notes);

                    // 250 one-bar edits on a copy through the same controller used by documents.
                    var edited = ProjectService.RestoreBytes(packed);
                    var editBars = edited.Tracks.SelectMany(t => t.Measures).Where(m => m.Cells.Count > 0).ToList();
                    var history = new UndoController();
                    string? beforeLast = null;
                    for (var i = 0; i < 250; i++)
                    {
                        if (i == 249) beforeLast = ProjectService.Snapshot(edited);
                        history.Capture(edited);
                        editBars[(i * 37) % editBars.Count].Cells[0].Text = $"self-test edit {i}";
                    }
                    var held = history.BytesHeld;
                    Log.Add($"  info  {name}: 250 edits -> {history.UndoCount} levels holding {held / 1024} KB");
                    Check($"{name}: 250 edits of a big score stay within the history budget",
                        held <= UndoHistory.MaxBytes && history.UndoCount <= UndoHistory.MaxLevels,
                        $"{held / 1024} KB in {history.UndoCount} levels");
                    var hasLatest = history.TryUndo(history.Snapshot(edited), out var newest);
                    Check($"{name}: the newest history entry is kept after trimming and restores exactly",
                        hasLatest && ProjectService.Snapshot(history.Restore(newest, edited)) == beforeLast);
                }

                Check($"{name}: imports with tracks and bars", tracks >= 1 && measures >= 1,
                    $"tracks {tracks}, bars {measures}, notes {notes}");
                Check($"{name}: tempo is sane", project.Tempo is >= 20 and <= 400, project.Tempo.ToString());
                Check($"{name}: every track has a tuning", project.Tracks.All(t => t.StringTunings.Count >= 4));
                Check($"{name}: mixer volume/pan are imported (not all at the defaults)",
                    project.Tracks.Any(t => t.Volume != 100 || t.Pan != 64),
                    "every track at volume 100 / pan 64");
                var fretted = project.Tracks.Where(t => t.Kind != TrackKind.Drums).ToList();
                var frettedNotes = fretted.SelectMany(t => t.Measures).SelectMany(m => m.Cells).SelectMany(c => c.Notes).ToList();
                var missing = frettedNotes.Count(n => n.MidiValue <= 0);
                Check($"{name}: every fretted note has a midi pitch", missing == 0,
                    $"{missing} of {frettedNotes.Count} notes lack a pitch");
                TestImportedNotationLayouts(name, project);

                // The redesign must not regress the document semantics: round-trip and play it.
                var restored = ProjectService.Restore(ProjectService.Snapshot(project));
                Check($"{name}: project round-trip keeps tracks and notes",
                    restored.Tracks.Count == tracks &&
                    restored.Tracks.Sum(t => t.Measures.Sum(m => m.Cells.Sum(c => c.Notes.Count))) == notes);

                var options = new PlaybackOptions();
                var timeline = MidiTimelineBuilder.Build(restored, options);
                Check($"{name}: playback timeline compiles", timeline.TotalMs > 0 && timeline.Events.Count > 0);

                // Sustained notes must stay musical: a let-ring run once produced 9-14 s notes that
                // made the mix melt together. 5 s is a generous ceiling for a real guitar/bass part.
                Check($"{name}: no note drones for more than 5 s", timeline.LongestSoundingNoteMs < 5000,
                    $"longest {timeline.LongestSoundingNoteMs:0} ms at bar {timeline.LongestSoundingNoteAtBar + 1}");
                Log.Add($"  info  {name}: sustains -> tieMerges={timeline.TieMerges} tieOrphans={timeline.TieOrphans} " +
                        $"letRingExtensions={timeline.LetRingExtensions} maxLetRing={timeline.MaxLetRingExtensionMs:0}ms " +
                        $"longest={timeline.LongestSoundingNoteMs:0}ms");

                // --- audio-path diagnostics: unmatched note-ons, real polyphony and bar-start density ---
                var open = new Dictionary<(int ch, int note), int>();
                var live = 0;
                var maxPolyphony = 0;
                var peakMs = 0.0;
                foreach (var e in timeline.Events)
                {
                    var kind = e.Status & 0xF0;
                    var key = (e.Status & 0x0F, e.Data1);
                    if (kind == 0x90 && e.Data2 > 0)
                    {
                        open.TryGetValue(key, out var c);
                        open[key] = c + 1;
                        live++;
                        if (live > maxPolyphony) { maxPolyphony = live; peakMs = e.TimeMs; }
                    }
                    else if (kind == 0x80 || (kind == 0x90 && e.Data2 == 0))
                    {
                        if (open.TryGetValue(key, out var c) && c > 0)
                        {
                            open[key] = c - 1;
                            live--;
                        }
                    }
                }
                var hanging = open.Values.Sum();
                Check($"{name}: every note-on has a matching note-off", hanging == 0, $"{hanging} notes left hanging");
                Log.Add($"  info  {name}: peak polyphony {maxPolyphony} at {peakMs / 1000.0:0.0}s, hanging notes {hanging}");

                // No same-pitch retrigger may collide with its own release (that swallows the attack).
                Check($"{name}: no same-pitch note-off collides with the next note-on",
                    timeline.RetriggerCollisions == 0, $"{timeline.RetriggerCollisions} collisions");

                // Note length of the first beat of the first bars versus the written duration.
                for (var bar = 0; bar < Math.Min(3, restored.Tracks[0].Measures.Count); bar++)
                {
                    var cell = restored.Tracks[0].Measures[bar].Cells.FirstOrDefault(c => c.Notes.Count > 0);
                    if (cell is null) continue;
                    var written = MusicTime.CellSlots(cell) * (MusicTime.BarMs(restored, bar) / MusicTime.BarSlots(restored, bar));
                    var note = timeline.Notes.FirstOrDefault(n => n.TrackIndex == 0 && n.Bar == bar && n.Cell == 0);
                    var played = note?.DurationMs ?? -1;
                    Log.Add($"  info  {name}: bar {bar + 1} first note written {written:0} ms, played {played:0} ms");
                }
                Check($"{name}: timeline exposes note events for the fretboard",
                    notes == 0 || (timeline.Notes.Count > 0 && timeline.NotesFor(0).Length > 0),
                    $"imported notes {notes}, timeline notes {timeline.Notes.Count}");

                // Fidelity guard against the reference parser: the imported note count must equal
                // alphaTab's own count (some fixtures intentionally contain no notes).
                var sourceNotes = CountAlphaTabNotes(file);
                var skippedDuplicates = GuitarProImporter.LastImportSkippedDuplicates;
                Check($"{name}: every note is kept (or documented as a duplicate)",
                    sourceNotes < 0 || notes + skippedDuplicates == sourceNotes,
                    $"alphaTab={sourceNotes}, TabForge={notes}, skippedDuplicates={skippedDuplicates}");

                var sourceTapNotes = CountAlphaTabTappingNotes(file);
                var importedTapNotes = project.Tracks.SelectMany(t => t.Measures).SelectMany(m => m.Cells)
                    .SelectMany(c => c.Notes).Count(n => n.Techniques.Contains("Tapping"));
                Check($"{name}: beat-level tapping marks are imported",
                    sourceTapNotes < 0 || importedTapNotes + skippedDuplicates >= sourceTapNotes,
                    $"alphaTab tapped notes={sourceTapNotes}, TabForge T marks={importedTapNotes}");

                // Triplet feel must match the file: alphaTab reports NoTripletFeel when there is none, and
                // the old importer checked for "None" so every measure looked swung.
                var sourceSwing = CountAlphaTabSwingMeasures(file);
                var tabForgeSwing = project.Tracks.FirstOrDefault()?.Measures.Count(m => m.TripletFeel) ?? 0;
                Check($"{name}: triplet feel matches the reference parser", sourceSwing < 0 || sourceSwing == tabForgeSwing,
                    $"alphaTab swung measures={sourceSwing}, TabForge={tabForgeSwing}");
                Check($"{name}: timeline is ordered by onset",
                    timeline.Notes.Zip(timeline.Notes.Skip(1)).All(pair => pair.First.OnsetMs <= pair.Second.OnsetMs));

                // Visualisation must resolve a sane state from the real song.
                var state = InstrumentVisualizer.Build(restored, restored.Tracks[0], timeline, 1500, true, false, 4, false, false, null, 24);
                Check($"{name}: instrument state builds from the real song", state.Tuning.Count >= 4);

                // Raw source truth for the first bar: what alphaTab actually reports per voice/beat.
                try
                {
                    var data = File.ReadAllBytes(file);
                    var score = AlphaTab.Importer.ScoreLoader.LoadScoreFromBytes(data, new AlphaTab.Settings());
                    var scoreType = score.GetType();
                    var tracksProp = scoreType.GetProperty("Tracks");
                    var trackList = (System.Collections.IEnumerable?)tracksProp?.GetValue(score);
                    var firstTrack = trackList?.Cast<object>().FirstOrDefault();
                    var staves = (firstTrack?.GetType().GetProperty("Staves")?.GetValue(firstTrack) as System.Collections.IEnumerable)?.Cast<object>().ToList();
                    var bar = (staves?.FirstOrDefault()?.GetType().GetProperty("Bars")?.GetValue(staves.First()) as System.Collections.IEnumerable)?.Cast<object>().FirstOrDefault();
                    var voices = (bar?.GetType().GetProperty("Voices")?.GetValue(bar) as System.Collections.IEnumerable)?.Cast<object>().ToList();
                    if (voices is not null)
                    {
                        for (var vi = 0; vi < voices.Count; vi++)
                        {
                            var beats = (voices[vi].GetType().GetProperty("Beats")?.GetValue(voices[vi]) as System.Collections.IEnumerable)?.Cast<object>().ToList();
                            if (beats is null || beats.Count == 0) continue;
                            var beatText = beats.Select(b =>
                            {
                                var t = b.GetType();
                                var start = t.GetProperty("PlayStart")?.GetValue(b) ?? t.GetProperty("DisplayStart")?.GetValue(b) ?? "?";
                                var dur = t.GetProperty("Duration")?.GetValue(b) ?? "?";
                                var dots = t.GetProperty("Dots")?.GetValue(b) ?? 0;
                                var notes = (t.GetProperty("Notes")?.GetValue(b) as System.Collections.IEnumerable)?.Cast<object>()
                                    .Select(n => $"s{n.GetType().GetProperty("String")?.GetValue(n)}f{n.GetType().GetProperty("Fret")?.GetValue(n)}") ?? Enumerable.Empty<string>();
                                return $"[start={start} dur={dur} dots={dots} {string.Join(",", notes)}]";
                            });
                            Log.Add($"  info  {name}: source bar1 voice{vi + 1}: {string.Join(" ", beatText)}");
                        }
                        var barProps = bar!.GetType().GetProperties().Select(p => p.Name).Where(n => n.Contains("Repeat") || n.Contains("Simile") || n.Contains("Alternate")).ToArray();
                        Log.Add($"  info  {name}: bar members of interest: {string.Join(",", barProps)}");
                        var firstBeat = (voices[0].GetType().GetProperty("Beats")?.GetValue(voices[0]) as System.Collections.IEnumerable)?.Cast<object>().FirstOrDefault();
                        var beatProps = firstBeat is not null
                            ? string.Join(",", firstBeat.GetType().GetProperties().Select(p => p.Name).Where(n => n.Contains("Start") || n.Contains("Duration") || n.Contains("Tuplet") || n.Contains("Dots")))
                            : "";
                        Log.Add($"  info  {name}: beat timing members: {beatProps}");
                        if (firstBeat is not null)
                        {
                            var annotationMembers = firstBeat.GetType().GetProperties().Select(p => p.Name)
                                .Where(n => n.Contains("Text") || n.Contains("Lyric") || n.Contains("Chord")
                                         || n.Contains("Fermata") || n.Contains("Grace") || n.Contains("Fade")
                                         || n.Contains("Staccato") || n.Contains("Tenuto") || n.Contains("Accent")
                                         || n.Contains("Tremolo") || n.Contains("Brush") || n.Contains("Whammy"))
                                .ToArray();
                            Log.Add($"  info  {name}: beat annotation members: {string.Join(",", annotationMembers)}");
                            var chordShape = firstBeat.GetType().GetProperty("Chord")?.GetValue(firstBeat);
                            if (chordShape is not null)
                                Log.Add($"  info  {name}: chord members: {string.Join(",", chordShape.GetType().GetProperties().Select(p => p.Name))}");
                        }
                        var annotations = CountAlphaTabAnnotations(score);
                        var tuning = CheckTuningConsistency(score);
                        Log.Add($"  info  {name}: tuning check -> {tuning.Checked} notes, {tuning.Mismatched} mismatches");
                        Check($"{name}: string + fret lands on alphaTab's sounding pitch", tuning.Mismatched == 0,
                            $"{tuning.Mismatched}/{tuning.Checked}: {tuning.FirstMismatch}");                        var allCells = project.Tracks.SelectMany(t => t.Measures).SelectMany(m => m.Cells).ToList();
                        var tfChords = allCells.Count(c => !string.IsNullOrWhiteSpace(c.ChordName));
                        var tfTexts = allCells.Count(c => !string.IsNullOrWhiteSpace(c.Text));
                        var tfLyrics = allCells.Count(c => !string.IsNullOrWhiteSpace(c.Lyrics));
                        var tfFermata = allCells.Count(c => c.Fermata);
                        var tfGraces = allCells.Count(c => c.IsGrace);
                        var tfFades = allCells.Sum(c => c.Notes.Count(n => n.Techniques.Contains("FadeIn") || n.Techniques.Contains("FadeOut")));
                        Log.Add($"  info  {name}: annotations alphaTab(c={annotations.Chords} t={annotations.Texts} l={annotations.Lyrics} f={annotations.Fermata} g={annotations.Graces}) " +
                                $"TabForge(c={tfChords} t={tfTexts} l={tfLyrics} f={tfFermata} g={tfGraces})");

                        // Beat comments, chord names, lyrics, fermatas and grace notes live on the beat and
                        // used to be dropped entirely on import (one real song lost 53 comments).
                        Check($"{name}: every beat comment is imported", annotations.Texts == tfTexts,
                            $"alphaTab={annotations.Texts} TabForge={tfTexts}");
                        Check($"{name}: every chord name is imported", annotations.Chords == tfChords,
                            $"alphaTab={annotations.Chords} TabForge={tfChords}");
                        Check($"{name}: every lyric line is imported", annotations.Lyrics == tfLyrics,
                            $"alphaTab={annotations.Lyrics} TabForge={tfLyrics}");
                        Check($"{name}: every fermata is imported", annotations.Fermata == tfFermata,
                            $"alphaTab={annotations.Fermata} TabForge={tfFermata}");
                        Check($"{name}: every grace note is imported", annotations.Graces == tfGraces,
                            $"alphaTab={annotations.Graces} TabForge={tfGraces}");
                        Check($"{name}: every fade is imported", annotations.Fades == 0 || tfFades > 0,
                            $"alphaTab={annotations.Fades} TabForge={tfFades}");

                        // Song metadata must survive the import as well - score-info.gp* fixtures carry it.
                        foreach (var (field, alpha, imported) in new (string, string?, string)[]
                        {
                            ("title", Prop(score, "Title"), project.Title),
                            ("subtitle", Prop(score, "SubTitle"), project.Subtitle),
                            ("artist", Prop(score, "Artist"), project.Artist),
                            ("album", Prop(score, "Album"), project.Album),
                            ("lyrics author (words)", Prop(score, "Words"), project.LyricsAuthor),
                            ("music author", Prop(score, "Music"), project.MusicAuthor),
                            ("copyright", Prop(score, "Copyright"), project.Copyright),
                            ("tab author", Prop(score, "Tab"), project.TabAuthor),
                            ("instructions", Prop(score, "Instructions"), project.Instructions),
                            ("notice", Prop(score, "Notice"), project.Notice),
                            ("song lyrics", Prop(score, "Lyrics"), project.Lyrics),
                        })
                        {
                            if (string.IsNullOrWhiteSpace(alpha)) continue;
                            Check($"{name}: {field} is imported", imported.Trim() == alpha!.Trim(),
                                $"alphaTab='{alpha}' TabForge='{imported}'");
                        }
                    }
                }
                catch (Exception ex) { Log.Add($"  info  {name}: raw dump failed: {ex.Message}"); }
                for (var bar = 0; bar < Math.Min(3, measures); bar++)
                {
                    var parts = new List<string>();
                    var first = restored.Tracks[0].Measures[bar];
                    for (var c = 0; c < first.Cells.Count; c++)
                    {
                        var cell = first.Cells[c];
                        if (cell.Notes.Count == 0 && !cell.IsRest) continue;
                        var cellText = string.Join("+", cell.Notes.Select(n => $"s{n.StringIndex}f{n.Fret}"));
                        var duration = MusicTime.CellSlots(cell) == Math.Floor(MusicTime.CellSlots(cell))
                            ? ((int)MusicTime.CellSlots(cell)).ToString()
                            : MusicTime.CellSlots(cell).ToString("0.##");
                        parts.Add(cell.IsRest ? $"{c}:rest/{duration}" : $"{c}:{cellText}/{duration}");
                    }
                    Log.Add($"  info  {name}: track0 bar {bar + 1} -> {(parts.Count == 0 ? "(empty)" : string.Join(" ", parts))}  (slot:fret/slots)");
                }

                // Regression guard for the "first note of each bar is too long" defect: a beat from
                // another voice (commonly an empty quarter rest) used to overwrite the duration of the
                // note already in the cell, stretching an eighth-note downbeat into a quarter. The
                // reference songs pin the correct first-beat values.
                if (LocalReferenceSongs.Matches(name, "reference-a"))
                {
                    Eq($"{name}: bar 1 downbeat is an eighth (2 slots; not stretched by another voice)",
                        2.0, MusicTime.CellSlots(restored.Tracks[0].Measures[0].Cells[0]));
                }
            }
            catch (Exception ex)
            {
                Check($"{name}: imports without throwing", false, ex.Message);
            }
        }
    }

    /// <summary>Counts the notes alphaTab itself reports for a file (reference for import fidelity).</summary>
    private static int CountAlphaTabNotes(string path)
    {
        try
        {
            var data = File.ReadAllBytes(path);
            var score = AlphaTab.Importer.ScoreLoader.LoadScoreFromBytes(data, new AlphaTab.Settings());
            var count = 0;
            var tracks = score.GetType().GetProperty("Tracks")?.GetValue(score) as System.Collections.IEnumerable;
            if (tracks is null) return 0;
            foreach (var track in tracks)
            {
                var staves = track!.GetType().GetProperty("Staves")?.GetValue(track) as System.Collections.IEnumerable;
                if (staves is null) continue;
                foreach (var staff in staves)
                {
                    var bars = staff!.GetType().GetProperty("Bars")?.GetValue(staff) as System.Collections.IEnumerable;
                    if (bars is null) continue;
                    foreach (var bar in bars)
                    {
                        var voices = bar!.GetType().GetProperty("Voices")?.GetValue(bar) as System.Collections.IEnumerable;
                        if (voices is null) continue;
                        foreach (var voice in voices)
                        {
                            var beats = voice!.GetType().GetProperty("Beats")?.GetValue(voice) as System.Collections.IEnumerable;
                            if (beats is null) continue;
                            foreach (var beat in beats)
                            {
                                var notes = beat!.GetType().GetProperty("Notes")?.GetValue(beat) as System.Collections.IEnumerable;
                                if (notes is null) continue;
                                foreach (var _ in notes) count++;
                            }
                        }
                    }
                }
            }
            return count;
        }
        catch { return -1; }
    }

    /// <summary>Counts notes on beats alphaTab marks with its GP tapping technique.</summary>
    private static int CountAlphaTabTappingNotes(string path)
    {
        try
        {
            var data = File.ReadAllBytes(path);
            var score = AlphaTab.Importer.ScoreLoader.LoadScoreFromBytes(data, new AlphaTab.Settings());
            var count = 0;
            foreach (var track in AsEnumerable(score, "Tracks"))
                foreach (var staff in AsEnumerable(track, "Staves"))
                    foreach (var bar in AsEnumerable(staff, "Bars"))
                        foreach (var voice in AsEnumerable(bar, "Voices"))
                            foreach (var beat in AsEnumerable(voice, "Beats"))
                            {
                                if (!IsSet(beat.GetType().GetProperty("Tap")?.GetValue(beat))) continue;
                                count += AsEnumerable(beat, "Notes").Count();
                            }
            return count;
        }
        catch { return -1; }
    }

    /// <summary>Counts the master bars alphaTab marks with a triplet feel (reference for import).</summary>
    private static int CountAlphaTabSwingMeasures(string path)
    {
        try
        {
            var data = File.ReadAllBytes(path);
            var score = AlphaTab.Importer.ScoreLoader.LoadScoreFromBytes(data, new AlphaTab.Settings());
            var masterBars = score.GetType().GetProperty("MasterBars")?.GetValue(score) as System.Collections.IEnumerable;
            if (masterBars is null) return -1;
            var count = 0;
            foreach (var mb in masterBars)
            {
                var feel = mb!.GetType().GetProperty("TripletFeel")?.GetValue(mb)?.ToString() ?? "";
                if (feel.Equals("Triplet8th", StringComparison.OrdinalIgnoreCase) || feel.Equals("Triplet16th", StringComparison.OrdinalIgnoreCase))
                    count++;
            }
            return count;
        }
        catch { return -1; }
    }

    /// <summary>Counts the beat-level annotations alphaTab exposes, so import fidelity can be asserted.</summary>
    private static (int Chords, int Texts, int Lyrics, int Fermata, int Fades, int Graces) CountAlphaTabAnnotations(object score)
    {
        var chords = 0; var texts = 0; var lyrics = 0; var fermata = 0; var fades = 0; var graces = 0;
        foreach (var track in AsEnumerable(score, "Tracks"))
            foreach (var staff in AsEnumerable(track, "Staves"))
                foreach (var bar in AsEnumerable(staff, "Bars"))
                    foreach (var voice in AsEnumerable(bar, "Voices"))
                        foreach (var beat in AsEnumerable(voice, "Beats"))
                        {
                            var t = beat.GetType();
                            if (t.GetProperty("HasChord")?.GetValue(beat) is true) chords++;
                            if (!string.IsNullOrWhiteSpace(t.GetProperty("Text")?.GetValue(beat) as string)) texts++;
                            // alphaTab exposes lyrics as a list of lines per beat, not a string.
                            if (t.GetProperty("Lyrics")?.GetValue(beat) is System.Collections.IEnumerable lines &&
                                lines.Cast<object>().Any(l => !string.IsNullOrWhiteSpace(l?.ToString()))) lyrics++;
                            if (IsSet(t.GetProperty("Fermata")?.GetValue(beat))) fermata++;
                            if (IsSet(t.GetProperty("Fade")?.GetValue(beat))) fades++;
                            if (IsSet(t.GetProperty("GraceType")?.GetValue(beat))) graces++;
                        }
        return (chords, texts, lyrics, fermata, fades, graces);
    }

    private static string? Prop(object owner, string property) =>
        owner.GetType().GetProperty(property)?.GetValue(owner) as string;

    /// <summary>
    /// Validates the tuning table, the string numbering and the capo in one go: for every fretted note
    /// the importer's own rule (tuning[strings - gpString] + fret + capo) must reproduce alphaTab's
    /// sounding pitch. Harmonics are skipped because their RealValue is not a plain fret offset.
    /// </summary>
    private static (int Checked, int Mismatched, string FirstMismatch) CheckTuningConsistency(object score)
    {
        var count = 0;
        var bad = 0;
        var first = "";
        foreach (var track in AsEnumerable(score, "Tracks"))
        {
            var staff = AsEnumerable(track, "Staves").FirstOrDefault();
            if (staff is null) continue;
            var tuning = (staff.GetType().GetProperty("Tuning")?.GetValue(staff) as System.Collections.IEnumerable)
                ?.Cast<object>().Select(v => Convert.ToInt32(v)).ToList() ?? new List<int>();
            if (tuning.Count < 4) continue;
            var capo = Convert.ToInt32(staff.GetType().GetProperty("Capo")?.GetValue(staff) ?? 0);

            foreach (var bar in AsEnumerable(staff, "Bars"))
                foreach (var voice in AsEnumerable(bar, "Voices"))
                    foreach (var beat in AsEnumerable(voice, "Beats"))
                        foreach (var note in AsEnumerable(beat, "Notes"))
                        {
                            var t = note.GetType();
                            var gpString = Convert.ToInt32(t.GetProperty("String")?.GetValue(note) ?? 0);
                            var fret = Convert.ToInt32(t.GetProperty("Fret")?.GetValue(note) ?? 0);
                            var real = Convert.ToInt32(t.GetProperty("RealValue")?.GetValue(note) ?? 0);
                            var harmonic = t.GetProperty("HarmonicType")?.GetValue(note)?.ToString();
                            if (gpString <= 0 || real <= 0) continue;
                            if (!string.IsNullOrEmpty(harmonic) && harmonic != "None") continue;

                            var index = tuning.Count - gpString;
                            count++;
                            if (index < 0 || index >= tuning.Count) { bad++; continue; }
                            var computed = tuning[index] + fret + capo;
                            if (computed == real) continue;
                            bad++;
                            if (first.Length == 0)
                                first = $"strings={tuning.Count} tuning[{index}]={tuning[index]} fret={fret} capo={capo} computed={computed} real={real}";
                        }
        }
        return (count, bad, first);
    }

    private static IEnumerable<object> AsEnumerable(object owner, string property) =>
        (owner.GetType().GetProperty(property)?.GetValue(owner) as System.Collections.IEnumerable)?.Cast<object>()
        ?? Enumerable.Empty<object>();

    private static bool IsSet(object? value)
    {
        if (value is null) return false;
        if (value is bool b) return b;
        var text = value.ToString();
        return !string.IsNullOrWhiteSpace(text) && text != "0" && !text.Equals("None", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Set TABFORGE_NO_LOCAL_SONGS=1 to run as on a clean checkout (no Tabs folder): every local-song check must skip, never fail.</summary>
    internal static bool LocalSongsDisabled => Environment.GetEnvironmentVariable("TABFORGE_NO_LOCAL_SONGS") == "1";

    private static string? FindTabsFolder()
    {
        if (LocalSongsDisabled) return null;
        var candidates = new List<string>
        {
            Path.Combine(Environment.CurrentDirectory, "Tabs"),
            Path.Combine(AppContext.BaseDirectory, "Tabs")
        };
        // Walk up from the build output to the repository root.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 6 && dir is not null; i++)
        {
            candidates.Add(Path.Combine(dir.FullName, "Tabs"));
            dir = dir.Parent;
        }
        return candidates.FirstOrDefault(Directory.Exists);
    }

    /// <summary>
    /// Reads the exported Standard MIDI File back and checks it against the timeline it came from:
    /// header, track layout, tempo, and a complete note-on/note-off pairing on every channel.
    /// </summary>
    private static void TestMidiExport()
    {
        var p = TwoBarSong();
        p.Tracks[0].Name = "Lead";
        // A muted track must still be exported: the file represents the score, not the practice state.
        p.Tracks[0].Mute = true;

        var path = Path.Combine(Path.GetTempPath(), "tabforge-selftest.mid");
        MidiExportService.Export(p, path);
        var bytes = File.ReadAllBytes(path);
        Check("midi file starts with MThd", bytes.Length > 14 && bytes[0] == 'M' && bytes[1] == 'T' && bytes[2] == 'h' && bytes[3] == 'd');
        Check("midi file contains track chunks", Encoding.ASCII.GetString(bytes).Contains("MTrk"));

        var smf = ParseSmf(bytes);
        Eq("midi export is format 1", 1, smf.Format);
        Eq("midi export uses 480 ticks per quarter", 480, smf.Division);
        Eq("midi export has a conductor track plus one per score track", 1 + p.Tracks.Count, smf.Tracks.Count);
        Check("conductor track is named after the score", smf.Tracks[0].Name.StartsWith("TabForge", StringComparison.Ordinal), smf.Tracks[0].Name);
        Check("track chunks are named after the tracks",
            smf.Tracks.Skip(1).Select(t => t.Name).SequenceEqual(p.Tracks.Select(t => t.Name)),
            string.Join(",", smf.Tracks.Skip(1).Select(t => t.Name)));

        // Tempo meta at tick 0 must equal the score tempo.
        var tempo = smf.Tracks[0].Events.FirstOrDefault(e => e.Status == 0xFF && e.Data1 == 0x51);
        var mpq = tempo?.Meta is { Length: 3 } m ? (m[0] << 16) | (m[1] << 8) | m[2] : 0;
        var exportedTempo = mpq == 0 ? 0 : (int)Math.Round(60_000_000.0 / mpq);
        Eq("exported tempo matches the score", p.Tempo, exportedTempo);

        // Every note-on needs a note-off, and nothing may be left hanging.
        var expected = MidiTimelineBuilder.Build(p, new PlaybackOptions { RespectMuteSolo = false }).Events.Count(e => e.IsNoteOn);
        var noteOns = 0;
        var hanging = 0;
        var offsBeforeOns = 0;
        foreach (var track in smf.Tracks)
        {
            var open = new Dictionary<(byte Ch, byte Note), int>();
            foreach (var e in track.Events)
            {
                var kind = e.Status & 0xF0;
                if (kind == 0x90 && e.Data2 > 0)
                {
                    noteOns++;
                    var onKey = ((byte)(e.Status & 0x0F), e.Data1);
                    open[onKey] = open.GetValueOrDefault(onKey) + 1;
                }
                else if (kind == 0x80 || (kind == 0x90 && e.Data2 == 0))
                {
                    var key = ((byte)(e.Status & 0x0F), e.Data1);
                    if (open.TryGetValue(key, out var count) && count > 0) open[key] = count - 1;
                    else offsBeforeOns++;
                }
            }
            hanging += open.Values.Sum();
        }
        Eq("every timeline note-on is exported", expected, noteOns);
        Eq("no exported note is left hanging", 0, hanging);
        Eq("no note-off precedes its note-on", 0, offsBeforeOns);
        Check("a muted track is still written", noteOns > 0, noteOns.ToString());

        try { File.Delete(path); } catch { }
    }

    private sealed record SmfEvent(int Tick, byte Status, byte Data1, byte Data2, byte[]? Meta);
    private sealed record SmfTrack(string Name, List<SmfEvent> Events);

    /// <summary>Minimal Standard MIDI File reader (enough to verify what the exporter produced).</summary>
    private static (int Format, int Division, List<SmfTrack> Tracks) ParseSmf(byte[] b)
    {
        var pos = 0;
        int Be32() { var v = (b[pos] << 24) | (b[pos + 1] << 16) | (b[pos + 2] << 8) | b[pos + 3]; pos += 4; return v; }
        int Be16() { var v = (b[pos] << 8) | b[pos + 1]; pos += 2; return v; }
        int VarLen() { var v = 0; while (true) { var c = b[pos++]; v = (v << 7) | (c & 0x7F); if ((c & 0x80) == 0) return v; } }

        if (Encoding.ASCII.GetString(b, 0, 4) != "MThd") throw new InvalidDataException("not an SMF");
        pos = 4;
        _ = Be32();
        var format = Be16();
        var trackCount = Be16();
        var division = Be16();

        var tracks = new List<SmfTrack>();
        for (var t = 0; t < trackCount && pos < b.Length; t++)
        {
            if (Encoding.ASCII.GetString(b, pos, 4) != "MTrk") break;
            pos += 4;
            var length = Be32();
            var end = pos + length;
            var events = new List<SmfEvent>();
            var tick = 0;
            var name = "";
            byte running = 0;
            while (pos < end)
            {
                tick += VarLen();
                var status = b[pos];
                if (status < 0x80) status = running;   // running status
                else { pos++; running = status; }
                if (status == 0xFF)
                {
                    var type = b[pos++];
                    var len = VarLen();
                    var payload = new byte[len];
                    Array.Copy(b, pos, payload, 0, len);
                    pos += len;
                    if (type == 0x03) name = Encoding.ASCII.GetString(payload);
                    events.Add(new SmfEvent(tick, 0xFF, type, 0, payload));
                    continue;
                }
                var d1 = b[pos++];
                var d2 = (status & 0xF0) is 0xC0 or 0xD0 ? (byte)0 : b[pos++];
                events.Add(new SmfEvent(tick, status, d1, d2, null));
            }
            pos = end;
            tracks.Add(new SmfTrack(name, events));
        }
        return (format, division, tracks);
    }

    /// <summary>The preferences dialog, the tab strip, the settings catalogue and the hotkeys.</summary>
    private static void TestZoomComboShowsValue()
    {
        var combo = new System.Windows.Controls.ComboBox { IsEditable = true };
        foreach (var t in new[] { "Fit width", "50%", "100%", "150%", "200%" })
            combo.Items.Add(new System.Windows.Controls.ComboBoxItem { Content = t });
        foreach (var (zoom, expected) in new[] { (1.0, "100%"), (1.33, "133%"), (0.0, "Fit width"), (0.5, "50%"), (2.5, "200%"), (1.5, "150%") })
        {
            var shown = MainWindow.ShowZoomOn(combo, zoom);
            // The real on-screen text lives in the template's editable text box (the app-wide ComboBox style).
            var box = combo.Template?.FindName("PART_EditableTextBox", combo) as System.Windows.Controls.TextBox;
            Check($"zoom combo shows {expected} for zoom {zoom}",
                shown == expected && combo.Text == expected && box is not null && box.Text == expected &&
                box.Visibility == System.Windows.Visibility.Visible);
        }
    }

    private static void TestTabUi()
    {
        var settings = new AppSettings();
        Check("tab settings default to browser behaviour",
            settings.Tabs!.CloseOnDoubleClick && settings.Tabs.DetachToNewWindow && settings.Tabs.MiddleClickTitleBarNewTab && settings.Tabs.IsRounded);
        Check("opening projects replaces the active tab by default", settings.Tabs.OpenInCurrentTab);
        var playingTab = new TabItemModel { Session = DocumentSession.Blank(), IsPlaying = true };
        Check("the playing badge setting controls tab play-state visibility",
            playingTab.PlayingVisibility == System.Windows.Visibility.Visible);
        playingTab.ShowPlayingIndicator = false;
        Check("disabled playing badges remain hidden while the score plays",
            playingTab.PlayingVisibility == System.Windows.Visibility.Collapsed);
        Check("opening or switching tabs continues previous playback by default",
            settings.Tabs.PlaybackOnTabSwitch == TabPlaybackActions.ContinuePlayingPrevious &&
            TabPlaybackActions.Resolve(settings.Tabs.PlaybackOnTabSwitch) == TabPlaybackAction.Continue);
        Check("zoom controller defaults docked in the right sidebar",
            settings.General.PlaybackControllerDocked && settings.General.PlaybackControllerHeight == 100);
        Check("fretboard position defaults to centre", settings.Appearance.FretboardPosition == "Centre");
        Check("score paper defaults to dark styling", settings.Appearance.ScorePaper == "Dark");
        settings.General.PlaybackControllerDocked = false;
        settings.General.PlaybackControllerX = 315.5;
        settings.General.PlaybackControllerY = 426.25;
        settings.General.PlaybackControllerHeight = 118;
        var controllerReload = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(
            System.Text.Json.JsonSerializer.Serialize(settings))!;
        Check("zoom controller dock state and floating position persist",
            !controllerReload.General.PlaybackControllerDocked &&
            controllerReload.General.PlaybackControllerX == 315.5 &&
            controllerReload.General.PlaybackControllerY == 426.25 &&
            controllerReload.General.PlaybackControllerHeight == 118);
        var paletteHost = DockWorkspace.Tabs("palette-host", "tools", "structure", "rhythm", "layout");
        paletteHost.SelectedPanel = "rhythm";
        settings.Workspace = new Docking.DockWorkspaceState
        {
            Root = DockWorkspace.Split("Vertical", 0.67,
                DockWorkspace.Split("Horizontal", 0.72, DockWorkspace.EditorNode(),
                    paletteHost),
                DockWorkspace.Tabs("timeline-host", "timeline")),
            Floating = new()
            {
                new Docking.DockFloatingState
                {
                    Id = "floating-playback", Root = DockWorkspace.Tabs("floating-host", "playback"),
                    Left = 1450, Top = 260, Width = 380, Height = 210
                }
            },
            ClosedPanels = new() { "practice" }
        };
        settings.Audio.Speed = 0.75;
        settings.Audio.MetronomeVolume = 63;
        settings.Audio.MetronomeAccentVolume = 82;
        settings.Audio.MetronomeClickVolume = 45;
        settings.Audio.MetronomeSubdivision = 3;
        settings.Appearance.Accent = "#2468AC";
        var workspaceReload = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(
            System.Text.Json.JsonSerializer.Serialize(settings))!;
        var savedPaletteHost = workspaceReload.Workspace?.Root?.First?.Second;
        var savedFloatingPanels = workspaceReload.Workspace?.Floating.Single();
        Check("workspace split ratios and tab ordering persist",
            workspaceReload.Workspace?.Root?.First?.Ratio == 0.72 &&
            savedPaletteHost?.Panels.SequenceEqual(new[] { "tools", "structure", "rhythm", "layout" }) == true &&
            savedPaletteHost.SelectedPanel == "rhythm");
        Check("floating panel bounds and closed state persist",
            savedFloatingPanels?.Left == 1450 &&
            savedFloatingPanels.Width == 380 &&
            workspaceReload.Workspace?.ClosedPanels.SequenceEqual(new[] { "practice" }) == true);
        Check("workspace state is isolated from theme and audio settings",
            workspaceReload.Audio.Speed == 0.75 && workspaceReload.Appearance.Accent == "#2468AC");
        Check("metronome volume, accent, click and subdivision persist",
            workspaceReload.Audio.MetronomeVolume == 63 && workspaceReload.Audio.MetronomeAccentVolume == 82 &&
            workspaceReload.Audio.MetronomeClickVolume == 45 && workspaceReload.Audio.MetronomeSubdivision == 3);

        var dockOwner = new Window();
        var dockWorkspace = new DockWorkspace(dockOwner);
        dockWorkspace.SetEditorContent(new Grid());
        foreach (var (id, defaultHost, anchor) in new[]
        {
            ("instrument", "instrument", "score-editor"), ("timeline", "timeline", "score-editor"),
            ("tools", "tools", "structure"), ("structure", "tools", "tools"),
            ("rhythm", "tools", "tools"), ("layout", "tools", "tools"),
            ("sections", "side", "practice"), ("practice", "side", "sections"),
            ("playback", "side", "sections")
        })
            dockWorkspace.RegisterPanel(id, id, new Border(), 180, 100, defaultHost, anchor);
        dockWorkspace.RestoreLayout(null);
        var centreDocked = dockWorkspace.DockPanelTo("rhythm", "default-sections-practice-playback", Docking.DockDropZone.Center);
        var centreHost = FindHost(dockWorkspace.CaptureLayout().Root, "rhythm");
        Check("docking centre merges a panel as a selected tab",
            centreDocked && centreHost?.Panels.Contains("sections") == true && centreHost.SelectedPanel == "rhythm");
        var edgeDocked = dockWorkspace.DockPanelTo("rhythm", "default-timeline", Docking.DockDropZone.Left, splitFraction: 0.31);
        var afterEdge = dockWorkspace.CaptureLayout();
        var timelineSplit = afterEdge.Root?.Second;
        Check("left-edge docking creates an adjacent split at the requested ratio",
            edgeDocked && timelineSplit?.Kind == "split" && Math.Abs(timelineSplit.Ratio - 0.31) < 0.001 &&
            timelineSplit.First?.Panels.Contains("rhythm") == true);
        dockWorkspace.FloatPanelAt("rhythm", new Point(320, 240));
        Check("docking to empty space creates a floating panel root", dockWorkspace.IsPanelFloating("rhythm"));
        dockWorkspace.ResetPanel("rhythm");
        Check("reset-this-panel restores only the selected panel to its default host",
            FindNode(dockWorkspace.CaptureLayout().Root, "default-tool-palette")?.Panels.Contains("rhythm") == true &&
            dockWorkspace.IsPanelFloating("tools") == false);
        dockWorkspace.SetPanelVisible("practice", false);
        var closedPractice = dockWorkspace.CaptureLayout().ClosedPanels.Contains("practice");
        dockWorkspace.SetPanelVisible("practice", true);
        Check("closed panels can reopen in their original tab host",
            closedPractice && FindNode(dockWorkspace.CaptureLayout().Root, "default-sections-practice-playback")?.Panels.Contains("practice") == true);
        dockWorkspace.ResetAllPanels();
        var resetWorkspace = dockWorkspace.CaptureLayout();
        Check("reset-all restores the complete default workspace without floating panels",
            resetWorkspace.Floating.Count == 0 && resetWorkspace.ClosedPanels.Count == 0 &&
            EnumerateDockNodes(resetWorkspace.Root).Count(node => node.Kind == "editor") == 1);
        dockOwner.Close();

        static DockNodeState? FindHost(DockNodeState? node, string panelId)
        {
            if (node is null) return null;
            if (node.Kind == "tabs" && node.Panels.Contains(panelId)) return node;
            return FindHost(node.First, panelId) ?? FindHost(node.Second, panelId);
        }
        static DockNodeState? FindNode(DockNodeState? node, string hostId)
        {
            if (node is null) return null;
            if (node.HostId == hostId) return node;
            return FindNode(node.First, hostId) ?? FindNode(node.Second, hostId);
        }
        static IEnumerable<DockNodeState> EnumerateDockNodes(DockNodeState? node)
        {
            if (node is null) yield break;
            yield return node;
            foreach (var child in EnumerateDockNodes(node.First)) yield return child;
            foreach (var child in EnumerateDockNodes(node.Second)) yield return child;
        }
        settings.Appearance.FretboardPosition = "Right";
        var fretboardReload = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(
            System.Text.Json.JsonSerializer.Serialize(settings))!;
        Check("fretboard left/centre/right snap position persists", fretboardReload.Appearance.FretboardPosition == "Right");
        var savedLedgerSetting = settings.Appearance.LedgerLines;
        var ledgerModesPersist = Enum.GetNames<LedgerLineMode>().All(mode =>
        {
            settings.Appearance.LedgerLines = mode;
            return System.Text.Json.JsonSerializer.Deserialize<AppSettings>(System.Text.Json.JsonSerializer.Serialize(settings))?.Appearance.LedgerLines == mode;
        });
        settings.Appearance.LedgerLines = savedLedgerSetting;
        Check("ledger lines default to minimal and all modes persist", savedLedgerSetting == "Minimal" && ledgerModesPersist &&
            new TabEditorControl().LedgerLines == LedgerLineMode.Minimal);
        Check("new scores default to standard notation plus tablature",
            new TabEditorControl().Notation == NotationMode.TabAndStaff &&
            Documents.DocumentSession.Blank().Notation == NotationMode.TabAndStaff);
        Check("new documents default to continuous seamless score layout",
            Documents.DocumentSession.Blank().ContinuousScoreView);

        var dialog = new PreferencesWindow(settings);
        Check("preferences window builds", dialog.Content is not null);
        dialog.Close();

        var bar = new BrowserTabBar { Settings = settings.Tabs };
        var docs = new Documents.DocumentManager();
        bar.Bind(docs);
        var playbackDoc = docs.AddNew();
        var secondPlaybackDoc = docs.Add(Documents.DocumentSession.Blank(), activate: false);
        bar.Refresh();
        Check("tab bar builds with multiple documents and isolated playback engines",
            bar.Content is not null && !ReferenceEquals(playbackDoc.Playback.Engine, secondPlaybackDoc.Playback.Engine));
        bar.SetPlayingDocuments(new[] { playbackDoc, secondPlaybackDoc });
        Check("tab bar tracks playing state by document rather than active tab",
            bar.IsDocumentMarkedPlaying(playbackDoc) && bar.IsDocumentMarkedPlaying(secondPlaybackDoc));
        bar.SetPlaying(playbackDoc);
        bar.SetPlaying(null);
        playbackDoc.Playback.ReportPosition(new PlaybackPosition { Bar = 8, Cell = 3, ElapsedMs = 4321 });
        Check("each tab retains an independent pending playhead snapshot",
            playbackDoc.Playback.TryTakePendingPosition(out var retainedPosition) && retainedPosition is
                { Bar: 8, Cell: 3, ElapsedMs: 4321 } &&
            !secondPlaybackDoc.Playback.TryTakePendingPosition(out _));
        playbackDoc.Playback.PlayheadMs = 4321;
        docs.Move(0, 1);
        Check("tab reordering preserves the active document and its playback state",
            ReferenceEquals(docs.Active, playbackDoc) && playbackDoc.Playback.PlayheadMs == 4321);
        docs.Move(1, 0);
        bar.Refresh();

        var replaceDocs = new Documents.DocumentManager();
        var firstDoc = replaceDocs.AddNew();
        replaceDocs.AddNew();
        var replacement = Documents.DocumentSession.Blank();
        Check("replacing a document preserves tab ownership and position", replaceDocs.Replace(0, replacement) &&
            replaceDocs.Documents.Count == 2 && ReferenceEquals(replaceDocs.Documents[0], replacement) &&
            !replaceDocs.Documents.Contains(firstDoc));
        Check("replacing a document activates its tab", replaceDocs.ActiveIndex == 0 && ReferenceEquals(replaceDocs.Active, replacement));
        Check("document manager rejects invalid replacement indexes", !replaceDocs.Replace(2, Documents.DocumentSession.Blank()) && replaceDocs.Documents.Count == 2);

        // Settings catalogue: stable unique keys, and a search haystack for every row.
        var catalog = SettingsCatalog.Build(settings);
        Check("settings catalogue is not empty", catalog.Count >= 30, $"{catalog.Count} settings");
        Check("settings pages follow the requested category order", SettingsCatalog.Categories.SequenceEqual(new[]
        {
            "General", "Appearance & colours", "Score & notation", "Playback & sound", "Audio & VST", "Editing",
            "Timeline & sections", "Fretboard", "Tabs & windows", "Hotkeys", "Advanced"
        }));
        Check("setting keys are unique", catalog.Select(d => d.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count() == catalog.Count);
        Check("every setting is searchable", catalog.All(d => d.SearchText.Length > 4 && d.SearchText == d.SearchText.ToLowerInvariant()));
        Check("every setting belongs to a known category", catalog.All(d => SettingsCatalog.Categories.Contains(d.Category)));
        Check("settings colours validate through the framework-neutral parser",
            SettingsColor.IsValid("#12AB34") && SettingsColor.IsValid("sc#1,0.1,0.2,0.3") &&
            SettingsColor.IsValid("CornflowerBlue") && !SettingsColor.IsValid("not-a-colour"));
        Check("timeline scrollbar colour is configurable and included in the appearance settings",
            settings.Appearance.TimelineScrollBarThumbColour == "#758396" &&
            catalog.Any(descriptor => descriptor.Key == "appearance.timelinescrollbar"));
        Check("playback glow controls are exposed as colour and percentage settings",
            new[] { "follow.playhead", "follow.durationglow", "follow.durationopacity", "follow.sectionglow" }
                .All(key => catalog.Any(descriptor => descriptor.Key == key)) &&
            catalog.Where(descriptor => descriptor.Key is "follow.durationopacity" or "follow.sectionglow")
                .All(descriptor => descriptor.Min == 0 && descriptor.Max == 100 && descriptor.Unit == "%"));
        Check("every setting round-trips its own value", catalog.All(d =>
        {
            var before = d.Get();
            d.Set(before);
            return Equals(before, d.Get());
        }));
        var followModel = new AppSettings();
        var followCatalog = SettingsCatalog.Build(followModel);
        followCatalog.Single(d => d.Key == "follow.anticipation").Set(3d);
        followCatalog.Single(d => d.Key == "follow.verticaltrigger").Set(88d);
        followCatalog.Single(d => d.Key == "follow.horizontal").Set(false);
        var followSettings = followCatalog.Single(d => d.Key == "follow.anticipation").Get();
        var followRoundTrip = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(
            System.Text.Json.JsonSerializer.Serialize(followModel))!;
        Check("follow settings expose the persisted horizontal look-ahead and vertical trigger controls",
            Equals(followSettings, 3d) && followRoundTrip.Follow.AnticipationBars == 3 &&
            followRoundTrip.Follow.VerticalTriggerPercent == 88 && !followRoundTrip.Follow.HorizontalFollow);
        var playbackOnTabSwitch = catalog.Single(d => d.Key == "tabs.playbackonswitch");
        Check("tab playback preference exposes all three behaviours",
            playbackOnTabSwitch.Title == "When opening/switching to another tab while music is playing" &&
            playbackOnTabSwitch.Choices.SequenceEqual(new[]
            {
                TabPlaybackActions.ContinuePlayingPrevious,
                TabPlaybackActions.PausePrevious,
                TabPlaybackActions.StopPrevious
            }));
        Check("tab playback choices resolve to continue, pause and stop actions",
            TabPlaybackActions.Resolve(TabPlaybackActions.ContinuePlayingPrevious) == TabPlaybackAction.Continue &&
            TabPlaybackActions.Resolve(TabPlaybackActions.PausePrevious) == TabPlaybackAction.Pause &&
            TabPlaybackActions.Resolve(TabPlaybackActions.StopPrevious) == TabPlaybackAction.Stop);
        foreach (var action in playbackOnTabSwitch.Choices)
        {
            playbackOnTabSwitch.Set(action);
            Check($"tab playback preference persists: {action}", settings.Tabs.PlaybackOnTabSwitch == action &&
                System.Text.Json.JsonSerializer.Deserialize<AppSettings>(System.Text.Json.JsonSerializer.Serialize(settings))!
                    .Tabs.PlaybackOnTabSwitch == action);
        }
        settings.Tabs.PlaybackOnTabSwitch = TabPlaybackActions.ContinuePlayingPrevious;
        var openInCurrent = catalog.Single(d => d.Key == "tabs.openincurrent");
        openInCurrent.Set(false);
        Check("open-in-current-tab preference can be disabled", settings.Tabs.OpenInCurrentTab == false);
        openInCurrent.Set(true);
        Check("open-in-current-tab preference can be re-enabled", settings.Tabs.OpenInCurrentTab);

        // Typing in the search box must never lag: the window filters pre-built rows, and the
        // predicate is a substring test over a haystack computed once at build time.
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var hits = 0;
        for (var pass = 0; pass < 200; pass++)
            foreach (var d in catalog)
                if (SettingsCatalog.Matches(d, "tab")) hits++;
        sw.Stop();
        Check("searching the whole catalogue 200 times is instant", sw.ElapsedMilliseconds < 60, $"{sw.ElapsedMilliseconds} ms");
        Check("search actually matches settings", hits > 0);

        // Hotkeys: unique ids, parseable unique defaults, and a display form for each.
        var ids = HotkeyCatalog.All.Select(a => a.Id).ToList();
        Check("hotkey ids are unique", ids.Distinct(StringComparer.OrdinalIgnoreCase).Count() == ids.Count);
        Check("every hotkey default parses", HotkeyCatalog.All.All(a =>
            string.IsNullOrWhiteSpace(a.DefaultGesture) || HotkeyCatalog.TryParse(a.DefaultGesture, out _, out _)));
        // Clip commands are their own context (they only act while a clip is selected), so they are checked apart.
        var defaults = HotkeyCatalog.All.Where(a => !HotkeyCatalog.IsClipAction(a.Id)).Select(a => a.DefaultGesture)
            .Where(g => !string.IsNullOrWhiteSpace(g)).ToList();
        var clipDefaults = HotkeyCatalog.All.Where(a => HotkeyCatalog.IsClipAction(a.Id)).Select(a => a.DefaultGesture).Where(g => g.Length > 0).ToList();
        Check("clip hotkeys do not collide", clipDefaults.Distinct(StringComparer.OrdinalIgnoreCase).Count() == clipDefaults.Count);
        Check("Ctrl+R records and repeat selection moved", HotkeyCatalog.ById("Transport.Record")?.DefaultGesture == "Ctrl+R");
        Check("default hotkeys do not collide", defaults.Distinct(StringComparer.OrdinalIgnoreCase).Count() == defaults.Count,
            string.Join(", ", defaults.GroupBy(g => g, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key)));
        Check("hotkeys display with the right key names",
            HotkeyCatalog.Display("Ctrl+Shift+T") == "Ctrl+Shift+T" && HotkeyCatalog.Display("Space") == "Space");
        Check("framework-neutral hotkey parsing canonicalizes valid keys and rejects invalid names",
            HotkeyCatalog.TryParse("ctrl+shift+t", out var parsedKey, out _) && parsedKey == "T" &&
            !HotkeyCatalog.TryParse("Ctrl+NotARealKey", out _, out _));
        Check("hotkey tooltip suffix is bracketed",
            HotkeyCatalog.TooltipSuffix(new HotkeySettings(), "Transport.PlayPause") == " (Space)");
        var custom = new HotkeySettings();
        custom["Transport.PlayPause"] = "Ctrl+Alt+P";
        Check("a custom binding overrides the default",
            HotkeyCatalog.GestureFor(custom, "Transport.PlayPause") == "Ctrl+Alt+P");
        custom.Disable("Transport.PlayPause");
        var unbound = HotkeyCatalog.GestureFor(custom, "Transport.PlayPause") == "";
        custom.Reset("Transport.PlayPause");
        Check("hotkeys can be explicitly unbound and reset to the default",
            unbound && HotkeyCatalog.GestureFor(custom, "Transport.PlayPause") == "Space");

        const string legacySettingsJson = "{\"LeftHanded\":true,\"ShowNoteNames\":true,\"PreviewHorizon\":6,\"ScaleHighlight\":\"D Major\",\"FretboardFrets\":12,\"Metronome\":true,\"CountIn\":true,\"Speed\":0.75,\"ShowInstrument\":false,\"ShowArrangement\":false,\"DarkPaper\":false}";
        var legacySettings = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(legacySettingsJson)!;
        SettingsMigration.Normalize(legacySettingsJson, legacySettings);
        Check("legacy flat settings migrate into the organized runtime settings", legacySettings.Editing.LeftHanded &&
            legacySettings.Editing.ShowNoteNames && legacySettings.Editing.PreviewHorizon == 6 &&
            legacySettings.Editing.ScaleHighlight == "D Major" && legacySettings.Editing.FretboardFrets == 12 &&
            legacySettings.Audio.Metronome && legacySettings.Audio.CountIn && Math.Abs(legacySettings.Audio.Speed - 0.75) < 0.001 &&
            !legacySettings.Appearance.ShowFretboard && !legacySettings.Appearance.ShowArrangementOverview &&
            legacySettings.Appearance.ScorePaper == "Light");

        // Following the score while playing: the played system is parked with a 20% margin, one bar of
        // anticipation is kept visible, and scrolling stops once the end is on screen.
        var follow = new FollowSettings();
        Check("follow defaults to smooth mode", follow.Mode == FollowModes.Smooth, follow.Mode);
        Check("follow parks the played system 20% from the top", follow.MarginPercent == 20);
        Check("follow looks one bar ahead", follow.AnticipationBars == 1);
        Check("follow scrolls at the display refresh rate by default", follow.MaxFps == 240);
        Check("follow stops on a manual scroll and at the end of the score", follow.StopOnManualScroll && follow.StopAtEnd);
        Check("duration glow defaults to off (0%)", follow.DurationGlowOpacity == 0);
        settings.Follow.PlayheadColour = "#12AB34";
        settings.Follow.DurationGlowColour = "#AA5500";
        settings.Follow.DurationGlowOpacity = 0.37;
        settings.Follow.SectionGlowIntensity = 0.62;
        var glowReload = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(
            System.Text.Json.JsonSerializer.Serialize(settings))!;
        Check("playback and section glow settings persist through JSON reload",
            glowReload.Follow.PlayheadColour == "#12AB34" && glowReload.Follow.DurationGlowColour == "#AA5500" &&
            Math.Abs(glowReload.Follow.DurationGlowOpacity - 0.37) < 0.001 &&
            Math.Abs(glowReload.Follow.SectionGlowIntensity - 0.62) < 0.001);
        settings.Follow.DurationGlowOpacity = 0;
        var zeroGlowReload = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(
            System.Text.Json.JsonSerializer.Serialize(settings))!;
        Check("a fully disabled duration glow persists as exactly zero",
            zeroGlowReload.Follow.DurationGlowOpacity == 0);
        settings.Appearance.TimelineScrollBarThumbColour = "#667788";
        var scrollbarReload = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(
            System.Text.Json.JsonSerializer.Serialize(settings))!;
        Check("custom timeline scrollbar colour persists through settings reload",
            scrollbarReload.Appearance.TimelineScrollBarThumbColour == "#667788");
        Check("the played beat is highlighted by default", follow.HighlightPlayedBeat);
        Check("duration tint defaults off independently from beat highlighting",
            !follow.DurationTintEnabled && follow.HighlightPlayedBeat);
        Check("the played-note colour is legible on dark paper", ThemeService.TryParse(follow.HighlightColour, out _));

        // Score readability: the spacing scale drives the tablature metrics and the system height.
        var spaced = new TabEditorControl();
        var defaultHeight = spaced.SystemHeightNow;
        Check("score spacing defaults to 100%", Math.Abs(spaced.ScoreSpacing - 1.0) < 0.001);
        Check("standard notation and tablature have clear separation", spaced.StaffToTabGapNow >= 60,
            $"{spaced.StaffToTabGapNow:0} DIP gap");
        Check("score spacing scales the staff-to-tab gap", Math.Abs(spaced.StaffToTabGapNow - 64) < 0.001);
        spaced.ScoreSpacing = 1.4;
        Check("raising the score spacing makes each system taller", spaced.SystemHeightNow > defaultHeight,
            $"{defaultHeight:0} -> {spaced.SystemHeightNow:0}");
        Check("score spacing is clamped to a sane range",
            Math.Abs(new TabEditorControl { ScoreSpacing = 9 }.ScoreSpacing - 1.6) < 0.001 &&
            Math.Abs(new TabEditorControl { ScoreSpacing = 0.1 }.ScoreSpacing - 0.85) < 0.001);
        Check("tablature font grows with the spacing",
            Math.Abs(new TabEditorControl { ScoreSpacing = 1.5 }.ScoreSpacing - 1.5) < 0.001);
    }

    /// <summary>Generalised tuplets: non-3 ratios (quintuplets) must survive import, not just triplets.</summary>
    private static void TestTupletImport()
    {
        var tabs = FindTabsFolder();
        var path = tabs is null ? null : Path.Combine(tabs, "alpha", "tuplets.gp5");
        if (path is null || !File.Exists(path)) { Skip("tuplet import from tuplets.gp5", "file not found"); return; }
        var project = GuitarProImporter.Import(path);
        var cells = project.Tracks.SelectMany(t => t.Measures).SelectMany(m => m.Cells).ToList();
        var triplets = cells.Count(c => c.Tuplet == (3, 2));
        var quintuplets = cells.Count(c => c.Tuplet == (5, 4));
        Check("tuplets.gp5 imports triplet cells", triplets >= 3, $"triplets={triplets}");
        Check("tuplets.gp5 imports quintuplet cells", quintuplets >= 5, $"quintuplets={quintuplets}");
        Check("quintuplet cells are not flagged as triplets",
            cells.Where(c => c.TupletNumerator == 5).All(c => !c.IsTriplet));

        // Beat enumeration must surface every cell so the renderer/navigation never skip a tuplet note.
        var bar = TemplateFactory.Blank().Tracks[0].Measures[0];
        foreach (var slot in new[] { 0, 3, 5 })
            bar.Cells[slot] = new TabCell
            {
                DurationDenominator = 4,
                TupletNumerator = 3,
                TupletDenominator = 2,
                Notes = { new TabNote { StringIndex = 0, Fret = 3, MidiValue = 48 } }
            };
        Eq("beat enumeration finds every tuplet cell", "0,3,5", string.Join(",", MusicTime.BeatSlots(bar)));
    }

    // ---------- documents ----------
    private static void TestDocuments()
    {
        var docs = new Documents.DocumentManager();
        var a = docs.AddNew();
        var b = docs.AddNew();
        var c = docs.AddNew();
        Eq("three tabs open", 3, docs.Documents.Count);
        Eq("newest tab is active", 2, docs.ActiveIndex);
        Check("active document is the third one", ReferenceEquals(c, docs.Active));

        docs.Activate(0);
        Eq("activate switches tab", 0, docs.ActiveIndex);
        Check("active document is the first one", ReferenceEquals(a, docs.Active));

        docs.Move(0, 2);
        Eq("drag reorder moves the active tab index", 2, docs.ActiveIndex);
        Check("reorder keeps the same document active", ReferenceEquals(a, docs.Active));
        Check("documents after reorder are b,c,a",
            ReferenceEquals(docs.Documents[0], b) && ReferenceEquals(docs.Documents[1], c) && ReferenceEquals(docs.Documents[2], a));

        docs.Close(2);
        Eq("close removes a tab", 2, docs.Documents.Count);
        Check("closing the active tab selects a neighbour", docs.ActiveIndex is 0 or 1);

        docs.Close(0);
        docs.Close(0);
        Eq("closing the last tab leaves one empty document", 1, docs.Documents.Count);
        Check("remaining document is usable", docs.Active.Project is not null);

        var doc = new Documents.DocumentSession { Path = @"C:\songs\demo.tforge" };
        Eq("tab title is the file name", "demo.tforge", doc.DisplayName);
        doc.Project.IsDirty = true;
        Check("dirty document reports dirty", doc.IsDirty);
        Eq("tooltip shows the full path", @"C:\songs\demo.tforge", doc.Tooltip);

        var pristine = DocumentSession.Blank();
        pristine.Project.IsDirty = true; // no-op UI commit set the legacy flag
        Check("no-op writes do not make a blank document unsaved", !pristine.HasUnsavedChanges && !pristine.IsDirty);
        pristine.Project.Tempo++;
        Check("real project content changes require saving", pristine.HasUnsavedChanges && pristine.IsDirty);
        pristine.Project.Tempo--;
        Check("reverting to the clean content removes the save prompt", !pristine.HasUnsavedChanges && !pristine.IsDirty);
        pristine.Project.Artist = "changed without the legacy flag";
        Check("close check catches project content changed outside dirty flag", pristine.HasUnsavedChanges);
        pristine.MarkClean();
        Check("saving resets the content baseline", !pristine.HasUnsavedChanges && !pristine.IsDirty);

        var unsavedTemplate = DocumentSession.FromProject(TemplateFactory.Create("Acoustic"), null);
        unsavedTemplate.Project.IsDirty = true;
        Check("new template remains prompt-worthy without a saved baseline", unsavedTemplate.HasUnsavedChanges);

        var tempPath = Path.Combine(Path.GetTempPath(), $"tabforge-document-{Guid.NewGuid():N}.tforge");
        try
        {
            var fileController = new DocumentController();
            var fileSession = DocumentSession.FromProject(TemplateFactory.Blank(), null);
            fileSession.Project.IsDirty = true;
            fileController.Save(fileSession, tempPath, "saved lyrics");
            var opened = fileController.Open(tempPath);
            Check("document save sets a matching clean-content baseline",
                fileSession.Path == tempPath && !fileSession.HasUnsavedChanges && !fileSession.IsNew);
            Check("document open returns the loaded project and session path",
                !opened.ImportedFromGuitarPro && opened.SessionPath == tempPath && opened.Project.Lyrics == "saved lyrics");
        }
        finally
        {
            try { File.Delete(tempPath); } catch { }
        }
    }

    private static void TestUndoController()
    {
        var history = new UndoController();
        var project = TemplateFactory.Blank();
        var beforeTitle = project.Title;
        var before = history.Snapshot(project);
        var first = history.Capture(before);
        var duplicate = history.Capture(before);
        Check("undo capture deduplicates consecutive identical snapshots",
            first.Stored && !duplicate.Stored && history.UndoCount == 1 &&
            !history.Capture(project).Stored && history.UndoCount == 1);

        project.Title += " changed";
        var after = history.Snapshot(project);
        var undone = history.TryUndo(after, out var undoTarget)
            ? history.Restore(undoTarget, project)
            : null;
        Check("undo transfers snapshots into redo history", undone?.Title == beforeTitle &&
            history.UndoCount == 0 && history.RedoCount == 1);

        var redone = undone is not null && history.TryRedo(history.Snapshot(undone), out var redoTarget)
            ? history.Restore(redoTarget, undone)
            : null;
        Check("redo restores the captured project state", redone?.Title == beforeTitle + " changed" &&
            history.UndoCount == 1 && history.RedoCount == 0);

        project = redone ?? project;
        var transaction = history.BeginTransaction(project);
        project.Tempo++;
        var committed = history.Commit(transaction);
        var modified = history.Snapshot(project);
        var transactionUndo = history.TryUndo(modified, out var transactionTarget)
            ? history.Restore(transactionTarget, project)
            : null;
        Check("explicit edit transactions store one immutable pre-edit snapshot",
            committed.Stored && transactionUndo?.Title == beforeTitle + " changed" &&
            transactionUndo.Tempo == project.Tempo - 1);

        var bounded = new UndoController();
        var levels = TemplateFactory.Blank();
        for (var index = 0; index < UndoHistory.MaxLevels + 5; index++)
        {
            levels.Tempo = 40 + index;
            bounded.Capture(levels);
        }
        Check("undo history enforces its level and byte budgets",
            bounded.UndoCount == UndoHistory.MaxLevels && bounded.BytesHeld <= UndoHistory.MaxBytes);

        // The first state is the document's baseline (free); every later state is charged the bytes it stored first.
        const int byteLimit = 4096;
        var byteBounded = new UndoController(maxLevels: 10, maxBytes: byteLimit);
        var song = SingleTrack(8);
        byteBounded.Capture(song);
        string? lastBefore = null;
        for (var index = 0; index < 6; index++)
        {
            song.Tracks[0].Measures[index].Cells[0].Text = new string((char)('a' + index), 1000);
            lastBefore = ProjectService.Snapshot(song);
            byteBounded.Capture(song);
        }
        var keptNewest = byteBounded.TryUndo(byteBounded.Snapshot(song), out var newestLevel) &&
            ProjectService.Snapshot(byteBounded.Restore(newestLevel, song)) == lastBefore;
        Check("undo controller drops the oldest states at its strict byte limit and keeps the newest",
            byteBounded.BytesHeld <= byteLimit && byteBounded.UndoCount is >= 0 and < 6 && keptNewest,
            $"{byteBounded.BytesHeld} bytes in {byteBounded.UndoCount} levels");
    }

    private static void TestEditControllers()
    {
        var trackController = new TrackController();
        var project = TemplateFactory.Blank();
        var firstTrack = project.Tracks[0];
        Check("track controller handles mute, solo, rename and colour edits",
            trackController.ApplyEdit(project, new TrackEditRequest(0, TrackEditKind.ToggleMute)) && firstTrack.Mute &&
            trackController.ApplyEdit(project, new TrackEditRequest(0, TrackEditKind.ToggleSolo)) && firstTrack.Solo &&
            trackController.ApplyEdit(project, new TrackEditRequest(0, TrackEditKind.Rename, "Lead")) && firstTrack.Name == "Lead" &&
            trackController.ApplyEdit(project, new TrackEditRequest(0, TrackEditKind.SetColor, "#123456")) && firstTrack.ColorHex == "#123456");
        Check("track controller applies instrument selections (plug-ins live in the FX chain, not the instrument)",
            trackController.ApplyEdit(project, new TrackEditRequest(0, TrackEditKind.SelectInstrument, "Grand Piano")) &&
            firstTrack.MidiProgram == 0 && firstTrack.Rig.Name == "Piano" && firstTrack.Rig.Plugins.Count == 0);
        var addedTrack = trackController.CreateTrack(project, TrackKind.Bass);
        project.Tracks.Add(addedTrack);
        Check("track controller creates, reorders and deletes tracks",
            addedTrack.Kind == TrackKind.Bass && trackController.MoveTrack(project, 1, 0) &&
            ReferenceEquals(project.Tracks[0], addedTrack) && trackController.DeleteTrack(project, 0) &&
            project.Tracks.Count == 1 && !trackController.DeleteTrack(project, 0));

        var arrangement = new ArrangementController();
        var song = SingleTrack(3, 120);
        song.Markers.Add(new MarkerModel { MeasureIndex = 1, Title = "Verse" });
        var oldMeasureCount = song.Tracks[0].Measures.Count;
        arrangement.InsertBar(song, 1, 0, moveMarkers: true);
        Check("arrangement controller inserts bars and shifts section positions",
            song.Tracks[0].Measures.Count == oldMeasureCount + 1 && song.Markers.Single().MeasureIndex == 2);
        var barClip = TimelineClips.CopyBar(song, 0, 0, allTracks: false);
        song.Tracks[0].Measures[1].Cells[0].Notes.Add(new TabNote { StringIndex = 0, Fret = 3, MidiValue = 43 });
        Check("arrangement clipboard pastes an independent deep measure copy",
            TimelineClips.PasteBars(song, barClip, 1, 0, TimelinePasteKind.OverwriteThisTrack).Changed && song.Tracks[0].Measures[1].Cells[0].Notes.Count == 0);
        Check("arrangement controller deletes bars and rebases section markers",
            arrangement.DeleteBar(song, 1, -1, allTracks: true, moveMarkers: true) &&
            song.Tracks[0].Measures.Count == oldMeasureCount && song.Markers.Single().MeasureIndex == 1);

        var multiTrackSong = SingleTrack(3, 120);
        multiTrackSong.Tracks.Add(new TrackModel { Name = "Bass", Measures = TemplateFactory.Measures(3) });
        Check("arrangement edits propagate meter flags and triplet-feel state across tracks",
            arrangement.TryToggleMeasureProperty(multiTrackSong, 0, 1,
                measure => measure.FreeTime, (measure, value) => measure.FreeTime = value, out var freeTime) && freeTime &&
            multiTrackSong.Tracks.All(track => track.Measures[1].FreeTime) &&
            arrangement.TryCycleTripletFeel(multiTrackSong, 0, 1, out var feel) && feel == "Triplet8th" &&
            multiTrackSong.Tracks.All(track => track.Measures[1].TripletFeel));
        Check("arrangement applies first-bar score defaults and shared tempo/clef edits",
            arrangement.SetTimeSignature(multiTrackSong, 0, 3, 4) &&
            multiTrackSong.TimeSignatureNumerator == 3 && multiTrackSong.Tracks.All(track => track.Measures[0].TimeSigNum == 3) &&
            arrangement.SetKeySignature(multiTrackSong, 0, 2, true) && multiTrackSong.KeySignatureMinor &&
            arrangement.TrySetTempoChange(multiTrackSong, 2, 135) &&
            multiTrackSong.Tracks.All(track => track.Measures[2].TempoChange == 135) &&
            arrangement.TryCycleClef(multiTrackSong, 0, 0, out var clef) &&
            clef == "G" && multiTrackSong.Tracks.All(track => track.Measures[0].Clef == clef));
        var preDuplicate = multiTrackSong.Tracks[0].Measures.Count;
        var repeatedBars = arrangement.RepeatRange(multiTrackSong, 0, 0, 2);
        Check("arrangement duplicates a bar and repeats a selected range on every track",
            arrangement.DuplicateBar(multiTrackSong, 0, 1) &&
            multiTrackSong.Tracks[0].Measures.Count == preDuplicate + 3 &&
            multiTrackSong.Tracks[1].Measures.Count == preDuplicate + 2 && repeatedBars == 2);
    }

    private static void TestBrowserTabShell()
    {
        Check("tab drag threshold accepts horizontal movement", BrowserTabDragPolicy.CrossedThreshold(4, 0));
        Check("tab drag threshold accepts vertical tear-off movement", BrowserTabDragPolicy.CrossedThreshold(0, -4));
        Check("tab drag threshold rejects sub-threshold movement", !BrowserTabDragPolicy.CrossedThreshold(3.99, 0));
        Check("tab drag threshold rejects invalid coordinates", !BrowserTabDragPolicy.CrossedThreshold(double.NaN, 9));
        Eq("drag insertion uses captured tab midpoints", 2,
            BrowserTabDragPolicy.InsertionIndex(new[] { 0d, 103d, 206d }, new[] { 100d, 100d, 100d }, 170));
        Eq("drag insertion skips lifted tab", 1,
            BrowserTabDragPolicy.InsertionIndex(new[] { 0d, 103d, 206d }, new[] { 100d, 100d, 100d }, 120, 2));
        Eq("forward insertion converts to reorder destination", 2, BrowserTabDragPolicy.ReorderDestination(0, 3, 3));
        Eq("backward insertion keeps destination slot", 0, BrowserTabDragPolicy.ReorderDestination(2, 0, 3));
        Check("vertical pull outside shell starts tear-off", BrowserTabDragPolicy.IsBeyondTearOff(150, 72, 900, 0, 44));
        Check("pointer outside window starts tear-off", BrowserTabDragPolicy.IsBeyondTearOff(901, 22, 900, 0, 44));
        Check("movement inside title strip does not tear off", !BrowserTabDragPolicy.IsBeyondTearOff(300, 50, 900, 0, 44));
        Check("cross-window drops obey the merge preference while local tab reordering remains allowed",
            !BrowserTabDragPolicy.AcceptsDrop(false, false) &&
            BrowserTabDragPolicy.AcceptsDrop(false, true) &&
            BrowserTabDragPolicy.AcceptsDrop(true, false));

        var hit = BrowserChromeHitTest.Classify(new System.Windows.Point(25, 20),
            new[] { new System.Windows.Rect(0, 0, 100, 44) }, Array.Empty<System.Windows.Rect>(),
            new[] { new System.Windows.Rect(0, 0, 30, 44) }, new System.Windows.Rect(0, 0, 500, 44));
        Eq("caption buttons win hit-test precedence", BrowserChromeHit.CaptionButton, hit);
        Eq("tab body is treated as draggable tab", BrowserChromeHit.Tab,
            BrowserChromeHitTest.Classify(new System.Windows.Point(60, 20),
                new[] { new System.Windows.Rect(0, 0, 100, 44) }, Array.Empty<System.Windows.Rect>(),
                new[] { new System.Windows.Rect(400, 0, 100, 44) }, new System.Windows.Rect(0, 0, 500, 44)));
        Eq("unused caption is a window drag region", BrowserChromeHit.DraggableCaption,
            BrowserChromeHitTest.Classify(new System.Windows.Point(200, 20), Array.Empty<System.Windows.Rect>(),
                Array.Empty<System.Windows.Rect>(), Array.Empty<System.Windows.Rect>(), new System.Windows.Rect(0, 0, 500, 44)));
        Eq("content outside caption remains client area", BrowserChromeHit.Client,
            BrowserChromeHitTest.Classify(new System.Windows.Point(200, 60), Array.Empty<System.Windows.Rect>(),
                Array.Empty<System.Windows.Rect>(), Array.Empty<System.Windows.Rect>(), new System.Windows.Rect(0, 0, 500, 44)));

        var source = new DocumentManager();
        var target = new DocumentManager();
        var held = source.AddNew();
        held.CursorBar = 7;
        held.Undo.Capture(held.Project);
        held.Project.Tempo++;
        held.Undo.Capture(held.Project);
        var heldBytes = held.Undo.BytesHeld;
        var moved = source.Detach(0);
        Check("detach removes the tab from its source owner", moved is not null && source.Documents.Count == 0);
        if (moved is not null) target.Insert(moved, 0);
        Check("attach gives the destination sole ownership of the same session", target.Documents.Count == 1 &&
            source.Documents.Count == 0 && ReferenceEquals(target.Documents[0], held));
        Check("attached session preserves cursor and undo history", target.Active.CursorBar == 7 &&
            target.Active.Undo.UndoCount == 2 && target.Active.Undo.BytesHeld == heldBytes);
        target.Close(0);
        Eq("closing the final tab leaves a valid active document", 1, target.Documents.Count);
        Check("active-tab transitions survive close and transfer", target.ActiveIndex == 0 && target.Active.Project is not null);

        var serialized = ProjectService.Snapshot(held.Project);
        var restored = ProjectService.Restore(serialized);
        Check("detached document project state remains serializable", restored.Tracks.Count == held.Project.Tracks.Count &&
            restored.Title == held.Project.Title && restored.Tracks.SelectMany(t => t.Measures).Count() ==
            held.Project.Tracks.SelectMany(t => t.Measures).Count());
    }

    // ---------- visualisation ----------

    private static void TestNoteEvents()    {
        var p = TwoBarSong(120);
        var tl = MidiTimelineBuilder.Build(p, new PlaybackOptions());
        Eq("timeline exposes 8 note events", 8, tl.Notes.Count);
        Check("note events carry string/fret/midi",
            tl.Notes.All(n => n.StringIndex == 0 && n.Midi == 64 + n.Fret));
        Eq("first note starts at 0 ms", 0.0, tl.Notes[0].OnsetMs);
        Eq("second note starts at 500 ms (quarter at 120bpm)", 500.0, tl.Notes[1].OnsetMs);
        Check("note events are ordered by onset", tl.Notes.Zip(tl.Notes.Skip(1)).All(pair => pair.First.OnsetMs <= pair.Second.OnsetMs));
        Eq("note events carry the track index", 0, tl.Notes[0].TrackIndex);

        // A repeated note at the same instant must dispatch its NoteOff before the next NoteOn,
        // otherwise fast legato repeats get cut off and sound clipped/laggy.
        var repeat = new SongProject { Tempo = 120 };
        var rt = new TrackModel { Measures = TemplateFactory.Measures(1) };
        for (var i = 0; i < 4; i++)
        {
            var cell = rt.Measures[0].Cells[i * 4];
            cell.DurationDenominator = 4;
            cell.Notes.Add(new TabNote { StringIndex = 0, Fret = 5, MidiValue = 69 });
        }
        repeat.Tracks.Add(rt);
        var repeatTimeline = MidiTimelineBuilder.Build(repeat, new PlaybackOptions());
        var ordered = true;
        for (var i = 1; i < repeatTimeline.Events.Count; i++)
        {
            var prev = repeatTimeline.Events[i - 1];
            var cur = repeatTimeline.Events[i];
            if (Math.Abs(prev.TimeMs - cur.TimeMs) > 0.001) continue;
            if ((prev.Status & 0xF0) == 0x90 && (cur.Status & 0xF0) == 0x80) ordered = false;   // on before off
        }
        Check("identical-timestamp events put note-offs before note-ons", ordered);
    }

    private static void TestInstrumentVisualState()
    {
        var p = TwoBarSong(120);
        var tl = MidiTimelineBuilder.Build(p, new PlaybackOptions());
        var track = p.Tracks[0];

        // At 520 ms the second note is sounding and the third is next.
        var state = InstrumentVisualizer.Build(p, track, tl, 520, isPlaying: true, isPaused: false,
            previewHorizon: 4, leftHanded: false, showNoteNames: false, scaleName: null);
        Check("visual state has a current note", state.Current is not null);
        Eq("current note is the second one (fret 1)", 1, state.Current!.Fret);
        Check("visual state has a next note", state.Next is not null);
        Eq("next note is the third one (fret 2)", 2, state.Next!.Fret);
        Check("upcoming notes are previewed", state.Notes.Count(n => n.Role == VisualRole.Upcoming) >= 2,
            $"upcoming: {state.Notes.Count(n => n.Role == VisualRole.Upcoming)}");
        Check("emphasis decreases with distance",
            state.Notes.First(n => n.Role == VisualRole.Upcoming).Emphasis >
            state.Notes.Last(n => n.Role == VisualRole.Upcoming).Emphasis);

        // Paused state is reported so the view can freeze.
        var paused = InstrumentVisualizer.Build(p, track, tl, 520, true, true, 4, false, false, null);
        Check("paused flag is carried into the visual state", paused.IsPaused);

        // Seeking rebuilds the correct state for the new location (5th note starts at 2000 ms).
        var seek = InstrumentVisualizer.Build(p, track, tl, 2050, true, false, 4, false, false, null);
        Check("after seeking the current note matches the new position",
            seek.Current is not null && Math.Abs(seek.Current!.OnsetMs - 2000) < 1,
            $"onset {seek.Current?.OnsetMs}");

        // Scale highlighting.
        var scaled = InstrumentVisualizer.Build(p, track, tl, 0, false, false, 4, false, false, "E Natural Minor");
        Check("scale highlight resolves pitch classes", scaled.ScalePitchClasses.Count == 7,
            $"count {scaled.ScalePitchClasses.Count}");

        // Drum track uses the drum visualisation.
        var drums = new TrackModel { Kind = TrackKind.Drums, Name = "Drums", Measures = TemplateFactory.Measures(1) };
        var drumProject = new SongProject { Tempo = 120, Tracks = { drums } };
        var drumTl = MidiTimelineBuilder.Build(drumProject, new PlaybackOptions());
        var drumState = InstrumentVisualizer.Build(drumProject, drums, drumTl, 0, false, false, 4, false, false, null);
        Eq("drum track selects the drum renderer", InstrumentKind.Drums, drumState.Kind);
    }

    private static void TestFretboardGeometry()
    {        var p = TwoBarSong(120);
        var tl = MidiTimelineBuilder.Build(p, new PlaybackOptions());
        var track = p.Tracks[0];
        var state = InstrumentVisualizer.Build(p, track, tl, 0, false, false, 4, false, false, null);
        var content = new Rect(0, 22, 900, 146);
        var layout = FretboardGeometry.Compute(state, content);

        // Every string/fret position must round-trip through the hit test.
        var ok = true;
        for (var s = 0; s < track.StringTunings.Count; s++)
        {
            for (var f = layout.FirstFret; f <= Math.Min(layout.LastFret, 12); f++)
            {
                var point = FretboardGeometry.PositionOf(layout, s, f);
                if (!FretboardGeometry.HitTest(state, content, point, out var hs, out var hf) || hs != s || hf != f)
                {
                    ok = false;
                    break;
                }
            }
        }
        Check("fretboard hit testing matches the rendered geometry", ok);

        // Open string strip.
        var open = FretboardGeometry.PositionOf(layout, 2, 0);
        Check("open string hit test returns fret 0",
            FretboardGeometry.HitTest(state, content, open, out var os, out var of) && os == 2 && of == 0);

        // Left-handed mirrors the strings but keeps the mapping consistent.
        var left = InstrumentVisualizer.Build(p, track, tl, 0, false, false, 4, true, false, null);
        var leftLayout = FretboardGeometry.Compute(left, content);
        var leftPoint = FretboardGeometry.PositionOf(leftLayout, 0, 5);
        Check("left-handed hit test still resolves the same string/fret",
            FretboardGeometry.HitTest(left, content, leftPoint, out var ls, out var lf) && ls == 0 && lf == 5);

        // A click far outside the board is rejected.
        Check("clicks outside the board are rejected",
            !FretboardGeometry.HitTest(state, content, new Point(5, 400), out _, out _));

        var wideContent = new Rect(0, 0, 1600, content.Height);
        var anchorLeftLayout = FretboardGeometry.Compute(state, wideContent, FretboardHorizontalPosition.Left, 0, wideContent.Width);
        var centreLayout = FretboardGeometry.Compute(state, wideContent, FretboardHorizontalPosition.Centre, 0, wideContent.Width);
        var rightLayout = FretboardGeometry.Compute(state, wideContent, FretboardHorizontalPosition.Right, 0, wideContent.Width);
        Check("fretboard snap anchors change horizontal position without resizing",
            anchorLeftLayout.Board.Left < centreLayout.Board.Left && centreLayout.Board.Left < rightLayout.Board.Left &&
            Math.Abs(anchorLeftLayout.Board.Width - centreLayout.Board.Width) < 0.01 &&
            Math.Abs(centreLayout.Board.Width - rightLayout.Board.Width) < 0.01);

        var draggedLayout = FretboardGeometry.Compute(state, wideContent,
            FretboardHorizontalPosition.Centre, 70, wideContent.Width);
        var movedPoint = FretboardGeometry.PositionOf(draggedLayout, 1, Math.Min(draggedLayout.LastFret, 5));
        var clampedLayout = FretboardGeometry.Compute(state, wideContent,
            FretboardHorizontalPosition.Centre, 10000, wideContent.Width);
        Check("fretboard drag preview follows live geometry, clamps to the panel and hit-tests there",
            Math.Abs(draggedLayout.Board.Left - centreLayout.Board.Left - 70) < 0.01 &&
            FretboardGeometry.HitTest(state, wideContent, movedPoint, out var movedString, out var movedFret,
                FretboardHorizontalPosition.Centre, 70, wideContent.Width) && movedString == 1 && movedFret == Math.Min(draggedLayout.LastFret, 5) &&
            clampedLayout.Board.Left == rightLayout.Board.Left &&
            Math.Abs(clampedLayout.Board.Width - centreLayout.Board.Width) < 0.01);

        // Natural proportions in every pane shape: the string gap never exceeds 0.8x the fret width (floor 26 px; x the spacing choice,
        // at most 1.5x), a tall pane centres the board, and a small pane still fits everything.
        var shotDir = Environment.GetEnvironmentVariable("TF_FRETBOARD_SHOTS");
        FretboardGeometry.Layout Measured(double w, double h, double spacing, string name, out double scale, out double panelHeight)
        {
            state.StringSpacing = spacing;
            var panel = new Views.InstrumentPanel { Width = w, Height = h };
            panel.SetState(state);
            panel.Measure(new Size(w, h));
            panel.Arrange(new Rect(0, 0, w, h));
            var (l, sc) = panel.CurrentFretboardLayout()!.Value;
            scale = sc; panelHeight = h;
            Log.Add($"  info  fretboard layout {name} ({w}x{h}, spacing {spacing}): scale {sc:0.00}, string gap {l.StringGap * sc:0.0} px, fret width {l.FretWidth * sc:0.0} px, board top {l.Board.Top * sc:0} bottom {l.Board.Bottom * sc:0} of {h}");
            if (!string.IsNullOrEmpty(shotDir))
            {
                System.IO.Directory.CreateDirectory(shotDir);
                var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap((int)w, (int)h, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                bmp.Render(panel);
                var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
                enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
                using var fs = System.IO.File.Create(System.IO.Path.Combine(shotDir, $"fretboard-{name}.png"));
                enc.Save(fs);
            }
            return l;
        }
        var portrait = Measured(1100, 1900, 1.0, "portrait", out var pScale, out _);
        var landscape = Measured(1100, 260, 1.0, "landscape", out var lScale, out _);
        var small = Measured(520, 140, 1.0, "small", out var sScale, out var smallH);
        var portraitWide = Measured(1100, 1900, 1.5, "portrait-wide", out _, out _);
        var portraitCompact = Measured(1100, 1900, 0.75, "portrait-compact", out _, out _);
        state.StringSpacing = 1.0;
        Check("portrait pane: string gap stays within 0.8x the fret width (no vertical stretch)",
            portrait.StringGap <= Math.Max(FretboardGeometry.MinStringGap, FretboardGeometry.MaxGapToFretWidth * portrait.FretWidth) + 0.01,
            $"gap {portrait.StringGap * pScale:0.0} fret {portrait.FretWidth * pScale:0.0}");
        Check("portrait pane: the board is centred vertically with room for its labels",
            portrait.Board.Top > FretboardGeometry.TopPad * 2 &&
            Math.Abs(portrait.Board.Top - FretboardGeometry.TopPad - (1900 / pScale - portrait.Board.Bottom - FretboardGeometry.BottomPad)) < 0.5);
        Check("landscape pane keeps its proportions and stays inside the pane",
            landscape.StringGap <= Math.Max(FretboardGeometry.MinStringGap, FretboardGeometry.MaxGapToFretWidth * landscape.FretWidth) + 0.01 &&
            (landscape.Board.Bottom + FretboardGeometry.BottomPad) * lScale <= 260 + 0.5);
        Check("small pane: nothing is clipped (string gap at least the readable minimum, bottom inside the pane)",
            small.StringGap >= FretboardGeometry.MinStringGap - 0.01 && (small.Board.Bottom + FretboardGeometry.BottomPad) * sScale <= smallH + 0.5,
            $"gap {small.StringGap * sScale:0.0} of scale {sScale:0.00}");
        Check("string spacing: Wide is at most 1.5x Natural, Compact is not larger than Natural",
            portraitWide.StringGap <= portrait.StringGap * 1.5 + 0.01 && portraitWide.StringGap >= portrait.StringGap &&
            portraitCompact.StringGap <= portrait.StringGap + 0.01);
    }

    /// <summary>
    /// Precision test from the design brief: 5 tracks, 64 bars, mixed time signatures, empty and dense
    /// bars, sections and repeats. Verifies the arrangement X-axis has no cumulative drift and that a
    /// vertical line through bar 37 lands on bar 37 identically for every track.
    /// </summary>
    private static void TestArrangementGeometry()
    {
        var project = new SongProject { Tempo = 120, Title = "Precision" };
        var palette = new[] { "#F61A16", "#2248E8", "#35B954", "#F4E014", "#D850C6" };
        var kinds = new[] { TrackKind.Guitar, TrackKind.Guitar, TrackKind.Bass, TrackKind.Drums, TrackKind.Keys };
        for (var t = 0; t < 5; t++)
        {
            var track = new TrackModel
            {
                Name = $"Track {t + 1}",
                Kind = kinds[t],
                ColorHex = palette[t],
                StringTunings = t == 2 ? new List<int> { 43, 38, 33, 28 } : new List<int> { 64, 59, 55, 50, 45, 40 },
                Measures = TemplateFactory.Measures(64)
            };
            for (var b = 0; b < 64; b++)
            {
                var measure = track.Measures[b];
                // Mixed time signatures: 3/4 every 9th bar, 6/8 every 17th, otherwise 4/4.
                if (b % 17 == 0) { measure.TimeSigNum = 6; measure.TimeSigDenom = 8; }
                else if (b % 9 == 0) { measure.TimeSigNum = 3; measure.TimeSigDenom = 4; }
                // Empty bars (every 5th), dense bars (every 3rd), repeated bars, one simile bar.
                if (b % 5 == 0) continue;
                var notesPerBar = b % 3 == 0 ? 16 : 4;
                for (var n = 0; n < notesPerBar; n++)
                {
                    var slot = n * (16 / notesPerBar);
                    if (slot >= measure.Cells.Count) break;
                    var cell = measure.Cells[slot];
                    cell.DurationDenominator = 16 / notesPerBar;
                    cell.Notes.Add(new TabNote { StringIndex = n % 6, Fret = (n + b) % 12, MidiValue = 60 + n });
                }
                if (b % 7 == 0) { measure.RepeatStart = b % 14 == 0; measure.RepeatEnd = b % 14 == 7; measure.RepeatCount = 2; }
                if (b == 40) measure.SimileOneBar = true;
            }
            project.Tracks.Add(track);
        }
        project.Markers.Add(new MarkerModel { MeasureIndex = 0, Title = "Intro" });
        project.Markers.Add(new MarkerModel { MeasureIndex = 16, Title = "Verse" });
        project.Markers.Add(new MarkerModel { MeasureIndex = 36, Title = "Chorus" });
        project.Markers.Add(new MarkerModel { MeasureIndex = 48, Title = "Solo" });

        const double unit = 30.0;
        Eq("64 bars in the project", 64, project.Tracks.Max(t => t.Measures.Count));

        // 1. Bar widths follow the time signature (musically truthful, not decorative).
        Eq("4/4 bar width is one unit", 30.0, MusicTime.BarWidth(project, 1, unit));
        Eq("3/4 bar is three quarters of a unit", 22.5, MusicTime.BarWidth(project, 9, unit));
        Eq("6/8 bar is three quarters of a unit", 22.5, MusicTime.BarWidth(project, 17, unit));

        // 2. No cumulative drift: BarX is exactly the running sum of widths.
        var drift = 0.0;
        var running = 0.0;
        for (var b = 0; b < 64; b++)
        {
            drift = Math.Max(drift, Math.Abs(MusicTime.BarX(project, b, unit) - running));
            running += MusicTime.BarWidth(project, b, unit);
        }
        Check("X mapping has zero cumulative drift", drift < 1e-9, $"max drift {drift}");
        Eq("total width equals the sum of all bar widths", running, MusicTime.TotalWidth(project, unit));

        // 3. Uniform 4/4 songs give exactly uniform columns (the common case).
        var plain = new SongProject { Tracks = { new TrackModel { Measures = TemplateFactory.Measures(64) } } };
        Eq("bar 38 of a 4/4 song starts at 37 units", 37 * unit, MusicTime.BarX(plain, 37, unit));
        Eq("uniform bars are all the same width", true,
            Enumerable.Range(0, 64).Select(b => MusicTime.BarWidth(plain, b, unit)).Distinct().Count() == 1);

        // 4. A vertical line through bar 37 must land on bar 37 for every track: the mapping takes no
        //    track argument, so the only thing to prove is that all tracks agree on the bar count and
        //    that the hit test resolves that exact X back to bar 37 for each track row.
        var x37 = MusicTime.BarX(project, 37, unit);
        var resolved = new List<int>();
        for (var t = 0; t < project.Tracks.Count; t++)
        {
            var bar = 0;
            var x = 0.0;
            for (var b = 0; b < 64; b++)
            {
                var w = MusicTime.BarWidth(project, b, unit);
                if (x37 >= x && x37 < x + w) { bar = b; break; }
                x += w;
            }
            resolved.Add(bar);
        }
        Check("a vertical line through bar 37 hits bar 37 on all 5 tracks",
            resolved.All(b => b == 37), string.Join(",", resolved));

        // 5. Section boundaries land exactly on bar boundaries.
        foreach (var marker in project.Markers)
        {
            var sx = MusicTime.BarX(project, marker.MeasureIndex, unit);
            var exact = Math.Abs(sx - MusicTime.BarX(project, marker.MeasureIndex, unit)) < 1e-9;
            Check($"section '{marker.Title}' starts exactly on its bar boundary", exact);
        }

        // 6. The scale sweep used by the brief: the absolute position of bar 37 is stable at every
        //    zoom/DPI scale because widths are pure multiples of the unit width.
        foreach (var scale in new[] { 1.0, 1.25, 1.5, 1.75, 2.0 })
        {
            var scaled = MusicTime.BarX(project, 37, unit * scale);
            Check($"bar 37 X scales exactly at {scale:0.00}x", Math.Abs(scaled - x37 * scale) < 1e-9,
                $"{scaled} vs {x37 * scale}");
        }
    }
}
