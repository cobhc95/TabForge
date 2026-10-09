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
    private static HashSet<string> _required = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Requirements that turn skips into failures ("ci" is the one alias every gate uses: gp-fixtures, synthetic-fixtures, source-hygiene,
    /// installer-parity, fuzz; unknown names fail; groups and their minimum check counts are defined in SelfTestRequirements.cs): <c>--selftest &lt;log&gt; --require gp-fixtures[,all]</c> or the
    /// TABFORGE_SELFTEST_REQUIRE environment variable (comma/semicolon separated). "gp-fixtures" demands the
    /// synthetic Guitar Pro round trip ran; "synthetic-fixtures" that the synthetic fixture group ran; "source-hygiene" and "installer-parity" demand the repository-source checks
    /// (control characters, single plug-in factory; installer file associations) ran; "all" makes every skip a failure. Exit code stays 0 = pass, non-zero = fail.
    /// </summary>
    private static List<string> _unknownRequired = new();

    private static HashSet<string> ParseRequirements()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var values = new List<string> { Environment.GetEnvironmentVariable("TABFORGE_SELFTEST_REQUIRE") ?? "" };
        var args = Environment.GetCommandLineArgs();
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i].Equals("--require", StringComparison.OrdinalIgnoreCase)) values.Add(args[i + 1]);
        var raw = values.SelectMany(v => v.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        set = NormalizeRequirements(raw, out _unknownRequired);   // "ci" is expanded; unknown names are reported by ReportRequirements
        return set;
    }

    public static int Run(string outputPath)
    {
        Log.Clear(); _pass = 0; _fail = 0; _skip = 0; GroupStates.Clear();
        // The last test window to close must not make the application begin its shutdown (after that no window can load its XAML any more);
        // App.OnStartup ends the process explicitly with the exit code.
        try { if (Application.Current is { } app) app.ShutdownMode = ShutdownMode.OnExplicitShutdown; } catch (InvalidOperationException) { }
        _required = ParseRequirements();
        _areas = ParseAreas();
        _only = ParseOnly(Environment.GetCommandLineArgs()); _skippedByOnly = 0;
        if (!ValidateOnly()) return FinishRun(outputPath, "");   // an unknown --only name stops the run before any test executes
        if (_areas is not null) Log.Add($"  info  areas: core + {string.Join(", ", _areas.OrderBy(x => x))}");
        if (_required.Count > 0) Log.Add($"  info  required: {string.Join(", ", _required.OrderBy(x => x))}");
        RunFullSuite();   // empty in the basic build
        Section("Basic set: smoke");
        Guard(TestModelRoundTrip);
        Guard(TestProjectRoundtrip);
        Guard(TestEditorEntry);
        Guard(TestEverySettingIsWired);
        Guard(TestFretMarkerSize);
        Guard(TestHeadlessDeviceReconfigure);
        Section("Basic set: essential actions");
        Guard(TestEssentialStartNoFile);
        Guard(TestEssentialStartupFiles);
        Guard(TestEssentialSecondFileAndTabs);
        Guard(TestEssentialCloseTabUnsaved);
        Guard(TestEssentialSaveAndReopen);
        Guard(TestEssentialAutosaveAndRecovery);
        Guard(TestEssentialNoteEditing);
        Guard(TestEssentialSelectionCopyPaste);
        Guard(TestEssentialTracks);
        Guard(TestEssentialTimelineEdits);
        Guard(TestEssentialClips);
        Guard(TestEssentialPlayback);
        Guard(TestEssentialWindowsAndPrompts);
        Guard(TestEssentialExports);
        Section("Basic set: repository hygiene");
        Guard(TestSourceControlCharacters);
        Guard(TestInstallerAssociationParity);
        Guard(TestPublicDocsConsistency);
        Guard(TestFeatureMapInSync);
        Guard(TestRequireArgumentStrings);
        Guard(TestRequiredGroupGate);
        Guard(TestOnlyOption);
        Guard(TestDebuggingDocInSync);
        Guard(TestStartHereAndRecipesInSync);
        Guard(TestDocImagesAreReferenced);
        Guard(TestLooseSoundTouchAndLicenseTexts);
        Guard(TestHotkeyIdsDocumented);
        Guard(TestNoMojibakeInSources);
        Guard(TestFindCommand);
        Section("Basic set: architecture (layering)");
        GuardGroup("architecture", TestArchitectureLayering);
        GuardGroup("architecture", TestArchitectureDocumentOperations);
        GuardGroup("architecture", TestArchitectureGuards);
        GuardGroup("architecture", TestEveryTestHasAnArea);

#if DEBUG
        var performance = RenderPerformance.Snapshot;
        Log.Add($"  PERF  DEBUG latest scopes: arrangement {performance.ArrangementRenderMs:0.###} ms / {performance.ArrangementRenderBytes:N0} B; " +
                $"score {performance.ScoreRenderMs:0.###} ms / {performance.ScoreRenderBytes:N0} B; " +
                $"drag {performance.DragFrameMs:0.###} ms / {performance.DragFrameBytes:N0} B; " +
                $"score-layout rebuild {performance.ScoreLayoutRebuildMs:0.###} ms / {performance.ScoreLayoutRebuildBytes:N0} B");
#endif

        ReportRequirements(RequiredGroupsFullyIncluded(_required), _unknownRequired);   // required groups ran with enough checks; unknown --require names fail
        ReportReleaseGate();
        var summary = $"TabForge self-test: {_pass} passed, {_fail} failed" + (_skip > 0 ? $", {_skip} skipped" : "");
        if (_skippedByArea > 0) summary += $" ({_skippedByArea} test groups outside the selected areas not run)";
        if (_skippedByOnly > 0) summary += $" ({_skippedByOnly} tests not named by --only not run)";
        return FinishRun(outputPath, summary);
    }

    private static int FinishRun(string outputPath, string summary)
    {
        if (summary.Length == 0) summary = $"TabForge self-test: {_pass} passed, {_fail} failed";
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
    private static readonly Dictionary<string, string> AreaOf = BuildAreaOf();

    private static Dictionary<string, string> BuildAreaOf()
    {
        var areas = new Dictionary<string, string>(StringComparer.Ordinal)
        {
        ["TestArchitectureDocumentOperations"] = "architecture", ["TestArchitectureGuards"] = "architecture", ["TestArchitectureLayering"] = "architecture", ["TestEveryTestHasAnArea"] = "architecture",
        ["TestDocImagesAreReferenced"] = "hygiene", ["TestDebuggingDocInSync"] = "hygiene", ["TestFeatureMapInSync"] = "hygiene", ["TestInstallerAssociationParity"] = "hygiene", ["TestLooseSoundTouchAndLicenseTexts"] = "hygiene",
        ["TestHotkeyIdsDocumented"] = "hygiene", ["TestNoMojibakeInSources"] = "hygiene", ["TestFindCommand"] = "hygiene",
        ["TestOnlyOption"] = "hygiene", ["TestPublicDocsConsistency"] = "hygiene", ["TestRequireArgumentStrings"] = "hygiene", ["TestRequiredGroupGate"] = "hygiene",
        ["TestSourceControlCharacters"] = "hygiene", ["TestStartHereAndRecipesInSync"] = "hygiene", ["TestEditorEntry"] = "smoke", ["TestEverySettingIsWired"] = "smoke", ["TestFretMarkerSize"] = "smoke",
        ["TestEssentialStartNoFile"] = "smoke",
        ["TestEssentialStartupFiles"] = "smoke",
        ["TestEssentialSecondFileAndTabs"] = "smoke",
        ["TestEssentialCloseTabUnsaved"] = "smoke",
        ["TestEssentialSaveAndReopen"] = "smoke",
        ["TestEssentialAutosaveAndRecovery"] = "smoke",
        ["TestEssentialNoteEditing"] = "smoke",
        ["TestEssentialSelectionCopyPaste"] = "smoke",
        ["TestEssentialTracks"] = "smoke",
        ["TestEssentialTimelineEdits"] = "smoke",
        ["TestEssentialClips"] = "smoke",
        ["TestEssentialPlayback"] = "smoke",
        ["TestEssentialWindowsAndPrompts"] = "smoke",
        ["TestEssentialExports"] = "smoke",
        ["TestSingleInstanceProcessHandover"] = "release",
        ["TestEngineSyncDeferredRequests"] = "engine",
        ["TestScoreLayoutPartial"] = "ui", ["TestPlaybackScheduleReuse"] = "playback",
        ["TestMenuPopupWarmup"] = "ui", ["TestToolbarZoomAndSpeed"] = "ui", ["TestToolbarZoomSpeedNarrow"] = "ui", ["TestMixPointsSurviveSeek"] = "playback",
        ["TestTimelineSongTimeRepeatGrowth"] = "recording",
        ["TestSectionColourEditing"] = "ui",
        ["TestLongAudioClipGrowthPlayback"] = "recording", ["TestAudioGrowthReservation"] = "recording",
        ["TestHeadlessDeviceReconfigure"] = "smoke", ["TestModelRoundTrip"] = "smoke", ["TestProjectRoundtrip"] = "smoke",
        };
        AddFullSuiteAreas(areas);
        return areas;
    }

    /// <summary>Full-suite registrations (tests/full-suite); not compiled into normal builds.</summary>
    static partial void RunFullSuite();

    /// <summary>Adds the areas of the full-suite tests (tests/full-suite); not compiled into normal builds.</summary>
    static partial void AddFullSuiteAreas(Dictionary<string, string> areas);

    private static HashSet<string>? _areas;   // null: every area
    private static bool _basicOnly;   // --areas basic: untagged core tests do not run
    /// <summary>The areas that make up the basic set (the tests compiled into every build): hygiene checks, architecture checks and a few smoke tests.</summary>
    private static readonly string[] BasicAreas = { "architecture", "hygiene", "smoke" };

    private static HashSet<string>? ParseAreas()
    {
        var args = Environment.GetCommandLineArgs();
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i].Equals("--areas", StringComparison.OrdinalIgnoreCase))
            {
                var set = args[i + 1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (set.Contains("release")) { _basicOnly = true; _releaseTests = new HashSet<string>(ReleaseTestNames, StringComparer.Ordinal); set.UnionWith(BasicAreas); }   // the release gate: basic plus the curated release tests
                if (set.Contains("basic")) { _basicOnly = true; set.UnionWith(BasicAreas); }   // the basic set: only the tests tagged with these areas, no untagged core
                return set.Contains("all") ? null : set;
            }
        return null;
    }

    private static void Guard(Action test, [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(test))] string name = "") => GuardRan(test, name);

    /// <summary>Runs a test inside the area filter and failure containment; false when it threw. (A test outside --areas counts as not thrown.)</summary>
    private static bool GuardRan(Action test, string name)
    {
        var area = AreaOf.TryGetValue(test.Method.Name, out var a) ? a : "core";
        if (_areas is not null && (area != "core" || _basicOnly) && !_areas.Contains(area) && _releaseTests?.Contains(test.Method.Name) != true) { _skippedByArea++; return true; }
        if (OutsideOnly(test.Method.Name)) { _skippedByOnly++; return true; }
        if (_releaseTests?.Contains(test.Method.Name) == true) _releaseRan.Add(test.Method.Name);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var ok = true;
        var windowsBefore = VisibleMainWindows();
        try { test(); }
        catch (Exception ex) { ok = false; Check($"{name} completed without throwing", false, $"{ex.GetType().Name}: {ex.Message}"); }
        var leftOpen = VisibleMainWindows().Count(w => !windowsBefore.Contains(w));
        if (leftOpen > 0) Log.Add($"  WARN  {name} [{area}] left {leftOpen} visible main window(s) open");
        if (watch.ElapsedMilliseconds >= 1000) Log.Add($"  time  {name} [{area}] {watch.Elapsed.TotalSeconds:0.0} s");
        return ok;
    }

    private static int _skippedByArea;

    private static HashSet<MainWindow> VisibleMainWindows() =>
        Application.Current?.Windows.OfType<MainWindow>().Where(w => w.IsVisible).ToHashSet() ?? new HashSet<MainWindow>();

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
