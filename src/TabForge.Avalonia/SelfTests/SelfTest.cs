using System;
using System.Collections.Generic;
using System.Linq;
using TabForge.Services;

namespace TabForge;

// Owns: the self-test runner (TabForge.exe --selftest <log>): option parsing, the Check / Guard harness, the area table and the list of tests that run.
// Does not own: the tests themselves (the folders beside this file), the documents they read, or the process entry (Program calls Run).
// Tests: TestOnlyOption, TestRequiredGroupGate, TestRunnerTimingAndShardOptions, TestEveryTestHasAnArea.
/// <summary>
/// Headless verification suite, no window and no Avalonia application. Run with: <c>TabForge.exe --selftest &lt;log&gt; [--areas basic|release|all|a,b] [--only TestA,TestB]</c>.
/// Writes the report to the log path and returns 0 when nothing failed, 1 when a check failed, 2 when the log could not be written.
/// With no <c>--areas</c> and no <c>--only</c> the basic set runs; <c>release</c> adds the curated release tests; <c>all</c> runs every test.
/// </summary>
public static partial class SelfTest
{
    /// <summary>The first process argument that selects the self-test mode; the second is the log path.</summary>
    public const string Argument = "--selftest";

    private static readonly List<string> Log = new();
    private static int _pass, _fail, _skip;
    private static HashSet<string> _required = new(StringComparer.OrdinalIgnoreCase);
    private static List<string> _unknownRequired = new();
    private static string[] _args = Array.Empty<string>();

    /// <summary>The first <c>--require</c> values plus the TABFORGE_SELFTEST_REQUIRE environment variable (comma / semicolon separated).</summary>
    private static HashSet<string> ParseRequirements()
    {
        var values = new List<string> { Environment.GetEnvironmentVariable("TABFORGE_SELFTEST_REQUIRE") ?? "" };
        for (var i = 0; i < _args.Length - 1; i++)
            if (_args[i].Equals("--require", StringComparison.OrdinalIgnoreCase)) values.Add(_args[i + 1]);
        var raw = values.SelectMany(v => v.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return NormalizeRequirements(raw, out _unknownRequired);   // "ci" is expanded; unknown names are reported by ReportRequirements
    }

    /// <summary>Runs the tests the arguments select. <paramref name="args"/> are the process arguments: <c>--selftest</c>, the log path, then the options.</summary>
    public static int Run(string[] args)
    {
        if (args.Length < 2) return 2;
        _args = args;
        var outputPath = args[1];
        Log.Clear(); _pass = 0; _fail = 0; _skip = 0; GroupStates.Clear();
        _required = ParseRequirements();
        _areas = ParseAreas();
        _only = ParseOnly(_args); _skippedByOnly = 0;
        _coreShard = ParseCoreShard(_args); _skippedByArea = 0;
        var timingPath = OptionValue(_args, "--timing"); _timing = timingPath is null ? null : new List<TimingRow>();
        var wall = System.Diagnostics.Stopwatch.StartNew();
        if (OptionValue(_args, "--merge-group-reports") is { } mergeList) return RunMerge(outputPath, mergeList);   // pure merge: no test runs
        if (!ValidateOnly()) return FinishRun(outputPath, "");   // an unknown --only name stops the run before any test executes
        if (_areas is not null) Log.Add($"  info  areas: core + {string.Join(", ", _areas.OrderBy(x => x))}");
        if (_required.Count > 0) Log.Add($"  info  required: {string.Join(", ", _required.OrderBy(x => x))}");
        RunBasicSet();
        RunReleaseTests();
        ReportRequirements(RequiredGroupsFullyIncluded(_required), _unknownRequired);   // required groups ran with enough checks; unknown --require names fail
        ReportReleaseGate();
        WriteTiming(timingPath, wall.ElapsedMilliseconds);
        WriteGroupReport(OptionValue(_args, "--group-report"));
        var summary = $"TabForge self-test: {_pass} passed, {_fail} failed" + (_skip > 0 ? $", {_skip} skipped" : "");
        if (_skippedByArea > 0) summary += $" ({_skippedByArea} test groups outside the selected areas not run)";
        if (_skippedByOnly > 0) summary += $" ({_skippedByOnly} tests not named by --only not run)";
        return FinishRun(outputPath, summary);
    }

    private static void RunBasicSet()
    {
        Section("Basic set: repository hygiene");
        Guard(TestSourceControlCharacters);
        Guard(TestInstallerAssociationParity);
        Guard(TestPublicDocsConsistency);
        Guard(TestRequireArgumentStrings);
        Guard(TestRequiredGroupGate);
        Guard(TestOnlyOption);
        Guard(TestRunnerTimingAndShardOptions);
        Guard(TestDebuggingDocInSync);
        Guard(TestDocImagesAreReferenced);
        Guard(TestLooseSoundTouchAndLicenseTexts);
        Guard(TestNoMojibakeInSources);
        Section("Basic set: architecture");
        GuardGroup("architecture", TestChildProcessGuardPort);
        GuardGroup("architecture", TestEveryTestHasAnArea);
        GuardGroup("architecture", TestArchitectureFileLineBudget);
        Section("Basic set: engine (no audio device)");
        Guard(TestHeadlessDeviceReconfigure);
        Guard(TestSharedRingProducers);
        Guard(TestEngineExitCleanupOnUi);
        Guard(TestClipChurnWhileStreaming);
        Guard(TestAudioTrackAddDuringPlayback);
        Guard(TestDiskStreamerIdle);
        Guard(TestEngineOwnerTransports);
        Guard(TestEngineOwnerIdLifecycle);
        Guard(TestClapRealPlugin);
        Guard(TestEngineMessagesRoundTrip);
        Guard(TestEngineMessagesGolden);
    }

    private static void RunReleaseTests()
    {
        Section("Release set: saving, recovery and untrusted input");
        Guard(TestPresetFolderStoreNoOverwrite);
        Guard(TestNumberedSiblingCeiling);
        Guard(TestSaveClaimAtomic);
        Guard(TestAutosaveCopies);
        Guard(TestPairSaveEveryStage);
        Guard(TestPairMarkerIsUntrusted);
        Guard(TestDirectTforgeOpenRecovers);
        Guard(TestTforgeCompression);
        Guard(TestScoreClipRejectsUntrustedInput);
        Guard(TestClipboardCopyPasteBytes);
        Guard(TestSettingsChangeDuringSaveStaysPending);
        Guard(TestSettingsBadWorkspaceKeepsOtherSettings);
        Guard(TestRenderOutputFolderRefused);
        Guard(TestPluginRightsNotice);
        Section("Release set: import and malformed input");
        GuardGroup("fuzz", TestMalformedInputFuzz);
        GuardGroup("gp-fixtures", TestSyntheticGuitarProFixture);
        GuardGroup("synthetic-fixtures", TestSyntheticFixtures);
        Guard(TestGp5OwnFilesImport);
        Guard(TestGpifPitchCurves);
        Guard(TestGpRoundTripCounts);
        Guard(TestLegacyGpExport);
        Guard(TestGuitarProImportWorker);
        Guard(TestImportPlausibility);
        Section("Release set: playback and recording");
        Guard(TestPlaybackOrderSpec);
        Guard(TestNoHangingNotes);
        Guard(TestMixPointsSurviveSeek);
        Guard(TestPlaybackEditRefresh);
        Guard(TestSeekDuringPlayback);
        Guard(TestArticulationKeyswitches);
        Guard(TestSongExtent);
        Guard(TestLiveAudioFidelity);
        Guard(TestLiveRecordingLoudness);
        Guard(TestVideoEncoderGolden);
    }

    private static int FinishRun(string outputPath, string summary)
    {
        if (summary.Length == 0) summary = $"TabForge self-test: {_pass} passed, {_fail} failed";
        Log.Add("");
        Log.Add(summary);
        try { DiagnosticFileService.WriteText(outputPath, string.Join(Environment.NewLine, Log)); }
        catch (Exception ex) // Not logged: self-test harness: the failure is recorded as a check result
        {
            Trace.Error(Trace.Ui, "self-test log could not be written: " + ex.Message);
            return 2;
        }
        return _fail == 0 ? 0 : 1;
    }

    private static void Section(string name) { Log.Add(""); Log.Add($"== {name} =="); }

    /// <summary>
    /// Test areas (<c>--areas engine,persistence</c> runs only those; <c>all</c> runs everything). Every registered test has an explicit entry,
    /// which TestEveryTestHasAnArea enforces and which --only uses as the registry of names.
    /// </summary>
    private static readonly Dictionary<string, string> AreaOf = BuildAreaOf();

    private static Dictionary<string, string> BuildAreaOf() => new(StringComparer.Ordinal)
    {
        ["TestSourceControlCharacters"] = "hygiene", ["TestInstallerAssociationParity"] = "hygiene", ["TestPublicDocsConsistency"] = "hygiene", ["TestRequireArgumentStrings"] = "hygiene",
        ["TestRequiredGroupGate"] = "hygiene", ["TestOnlyOption"] = "hygiene", ["TestRunnerTimingAndShardOptions"] = "hygiene", ["TestDebuggingDocInSync"] = "hygiene",
        ["TestDocImagesAreReferenced"] = "hygiene", ["TestLooseSoundTouchAndLicenseTexts"] = "hygiene", ["TestNoMojibakeInSources"] = "hygiene",
        ["TestChildProcessGuardPort"] = "architecture", ["TestEveryTestHasAnArea"] = "architecture", ["TestArchitectureFileLineBudget"] = "architecture",
        ["TestHeadlessDeviceReconfigure"] = "engine", ["TestSharedRingProducers"] = "engine", ["TestEngineExitCleanupOnUi"] = "engine", ["TestClipChurnWhileStreaming"] = "engine",
        ["TestAudioTrackAddDuringPlayback"] = "engine", ["TestDiskStreamerIdle"] = "engine", ["TestEngineOwnerTransports"] = "engine", ["TestEngineOwnerIdLifecycle"] = "engine",
        ["TestEngineMessagesRoundTrip"] = "engine", ["TestEngineMessagesGolden"] = "engine", ["TestClapRealPlugin"] = "engine",
        ["TestPresetFolderStoreNoOverwrite"] = "persistence", ["TestNumberedSiblingCeiling"] = "persistence", ["TestSaveClaimAtomic"] = "persistence", ["TestAutosaveCopies"] = "persistence",
        ["TestPairSaveEveryStage"] = "persistence", ["TestPairMarkerIsUntrusted"] = "persistence", ["TestDirectTforgeOpenRecovers"] = "persistence", ["TestTforgeCompression"] = "persistence",
        ["TestScoreClipRejectsUntrustedInput"] = "persistence", ["TestClipboardCopyPasteBytes"] = "persistence",
        ["TestSettingsChangeDuringSaveStaysPending"] = "settings", ["TestSettingsBadWorkspaceKeepsOtherSettings"] = "settings",
        ["TestRenderOutputFolderRefused"] = "core", ["TestPluginRightsNotice"] = "core",
        ["TestMalformedInputFuzz"] = "fuzz", ["TestSyntheticGuitarProFixture"] = "guitarpro", ["TestSyntheticFixtures"] = "synthetic", ["TestGp5OwnFilesImport"] = "guitarpro",
        ["TestGpifPitchCurves"] = "guitarpro",
        ["TestGpRoundTripCounts"] = "guitarpro", ["TestLegacyGpExport"] = "guitarpro", ["TestGuitarProImportWorker"] = "guitarpro", ["TestImportPlausibility"] = "guitarpro",
        ["TestPlaybackOrderSpec"] = "playback", ["TestNoHangingNotes"] = "playback", ["TestMixPointsSurviveSeek"] = "playback", ["TestArticulationKeyswitches"] = "playback",
        ["TestSongExtent"] = "recording", ["TestLiveAudioFidelity"] = "recording", ["TestLiveRecordingLoudness"] = "recording",
        ["TestVideoEncoderGolden"] = "recording",
        ["TestPlaybackEditRefresh"] = "playback", ["TestSeekDuringPlayback"] = "playback",
    };

    private static HashSet<string>? _areas;   // null: every area
    private static bool _basicOnly;   // --areas basic or release: untagged core tests do not run unless the release set names them
    /// <summary>The areas that make up the basic set (every run): hygiene checks, architecture checks and the engine tests that need no audio device.</summary>
    private static readonly string[] BasicAreas = { "architecture", "hygiene", "engine" };

    private static HashSet<string>? ParseAreas()
    {
        for (var i = 0; i < _args.Length - 1; i++)
            if (_args[i].Equals("--areas", StringComparison.OrdinalIgnoreCase))
            {
                var set = _args[i + 1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (set.Contains("release")) { _basicOnly = true; _releaseTests = new HashSet<string>(ReleaseTestNames, StringComparer.Ordinal); set.UnionWith(BasicAreas); }   // the release gate: basic and the curated release tests
                if (set.Contains("basic")) { _basicOnly = true; set.UnionWith(BasicAreas); }   // the basic set: only the tests tagged with these areas
                return set.Contains("all") ? null : set;
            }
        if (OptionValue(_args, "--only") is null) { _basicOnly = true; return new HashSet<string>(BasicAreas, StringComparer.OrdinalIgnoreCase); }   // no selection runs the basic set
        return null;
    }

    private static void Guard(Action test, [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(test))] string name = "") => GuardRan(test, name);

    /// <summary>Runs a test inside the area filter and failure containment; false when it threw. (A test outside --areas counts as not thrown.)</summary>
    private static bool GuardRan(Action test, string name)
    {
        var nested = _guardDepth > 0;   // a Guard inside a running test belongs to it: the area, core-shard and --only filters judged the outer test already
        var area = AreaOf.TryGetValue(test.Method.Name, out var a) ? a : "core";
        if (!nested && _areas is not null && (area != "core" || _basicOnly) && !_areas.Contains(area) && _releaseTests?.Contains(test.Method.Name) != true) { _skippedByArea++; return true; }
        if (!nested && !RunsCore(_coreShard) && area == "core") { _skippedByArea++; return true; }   // --core-shard other than 0: the core tests run in shard 0 only
        if (!nested && OutsideOnly(test.Method.Name)) { _skippedByOnly++; return true; }
        if (_releaseTests?.Contains(test.Method.Name) == true) _releaseRan.Add(test.Method.Name);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var ok = true;
        var passBefore = _pass; var failBefore = _fail;
        _guardDepth++; try { test(); }
        catch (Exception ex) { ok = false; Check($"{name} completed without throwing", false, $"{ex.GetType().Name}: {ex.Message}"); } finally { _guardDepth--; }   // Not logged: self-test harness: the failure is recorded as a check result
        _timing?.Add(new TimingRow(area, test.Method.Name, watch.ElapsedMilliseconds, _pass + _fail - passBefore - failBefore, _fail - failBefore));
        if (watch.ElapsedMilliseconds >= 1000) Log.Add($"  time  {name} [{area}] {watch.Elapsed.TotalSeconds:0.0} s");
        return ok;
    }

    private static int _skippedByArea, _guardDepth;

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
}
