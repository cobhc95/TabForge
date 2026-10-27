using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the UI-responsiveness architecture guard. A high-frequency handler (slider ValueChanged, pointer move/wheel, scroll, size/bounds change,
//   render-frame callback, sub-100 ms DispatcherTimer Tick) must not call the full settings save or sync the whole project to the engine, and views
//   must not block the UI thread on a task (.Result, .Wait(), GetAwaiter().GetResult()). Shrink-only baseline in UiResponsivenessBaseline.txt.
// Does not own: the quiet save (MainWindowSettings.SaveSettingsQuietly / SaveSettingsDeferred) or the frame coalescer (TabForge.Platform.NextFrameCoalescer).
// Tests: itself. The scanner is a text scan of handler bodies (lambdas, methods subscribed by name, overrides), one call level deep.
public sealed class UiResponsivenessGuardTests
{
    private const string WriteBaselineEnvVar = "TABFORGE_ARCH_WRITE_BASELINE";

    private static readonly Regex SubscribePattern = new(
        @"\.\s*(ValueChanged|PointerMoved|PointerWheelChanged|ScrollChanged|SizeChanged|LayoutUpdated|Tick|PropertyChanged)\s*\+=\s*", RegexOptions.Compiled);
    private static readonly Regex FramePattern = new(@"\b(RequestAnimationFrame|BoundsProperty\s*\)\s*\.\s*Subscribe)\s*(?:<[^>]*>)?\s*\(", RegexOptions.Compiled);
    private static readonly Regex OverridePattern = new(
        @"\boverride\s+(?:void|Size)\s+(OnPointerMoved|OnPointerWheelChanged|OnSizeChanged|OnValueChanged|OnScrollChanged|ArrangeOverride)\s*\(", RegexOptions.Compiled);
    private static readonly Regex FastIntervalPattern = new(
        @"FromMilliseconds\s*\(\s*\d{1,2}(?:\.\d+)?\s*\)|FromSeconds\s*\(\s*0?\.0\d*\s*\)|FromTicks", RegexOptions.Compiled);

    private static readonly Regex FullSavePattern = new(@"\bSaveSettings\s*\(", RegexOptions.Compiled);
    private static readonly Regex EngineSyncPattern = new(
        @"\bSyncAudioEngine\s*\(|\bAvaloniaEngineRouting\s*\.\s*Apply\s*\(|\.\s*Sync\s*\(\s*(?:[\w.]*\.)?[Pp]roject\b", RegexOptions.Compiled);
    private static readonly Regex DeferWrapperPattern = new(
        @"\b(?:Post|InvokeAsync|Run|Soon|Deferred|Debounce\w*|Coalesc\w*|Schedule\w*|SaveSettingsDeferred)\s*\(", RegexOptions.Compiled);
    // A preview write (the live copy into the settings and the notice to every consumer) is full-save weight: hot handlers queue it, they never run it.
    private static readonly Regex LiveWritePattern = new(@"\bWriteLive\s*\(|\bPreferencesCatalog\s*\.\s*Copy\s*\(|\bRaisePreviewed\s*\(", RegexOptions.Compiled);
    private static readonly Regex CallPattern = new(@"(?<![.\w])([A-Z]\w*)\s*\(", RegexOptions.Compiled);
    private const int CallDepth = 2;
    // Any .Result blocks on a task, except these receivers, listed one by one: each is a non-Task type's own Result (SavePromptWindow and AvaloniaMessageBox answer
    // properties as ask, box and window; the MixTablePresenter.Result record type).
    private static readonly HashSet<string> NonTaskResultReceivers = new(StringComparer.Ordinal) { "ask", "box", "window", "MixTablePresenter" };
    private static readonly Regex ReceiverPattern = new(@"(\w+)\s*$", RegexOptions.Compiled);

    private static bool IsNonTaskResult(string s, Match hit)
    {
        if (!hit.Value.TrimStart('.', ' ', '	').StartsWith("Result", StringComparison.Ordinal)) return false;
        var before = s.Substring(Math.Max(0, hit.Index - 60), Math.Min(60, hit.Index));
        return ReceiverPattern.Match(before) is { Success: true } receiver && NonTaskResultReceivers.Contains(receiver.Groups[1].Value);
    }

    private static readonly Regex BlockingWaitPattern = new(
        @"\.\s*Result\b(?!\s*[=(])|\.\s*Wait\s*\([^)]*\)|\bWait(?:All|Any)\s*\(|\.\s*GetAwaiter\s*\(\s*\)\s*\.\s*GetResult\s*\(\s*\)", RegexOptions.Compiled);

    private const string HotFix =
        "Fix (hot handlers): in a slider/resize/pointer/frame handler do only the cheap part (store the value, repaint). Save with MainWindowSettings.SaveSettingsQuietly() "
        + "(or SaveSettingsDeferred()), and push to the engine through a TabForge.Platform.NextFrameCoalescer (call Request() in the handler; the sync runs once next frame). "
        + "Reference patterns: NextFrameCoalescer, SliderChangeCoalescer, SyncLevelSoon in LiveInputSession.\n"
        + "Fix (blocking waits): never wait on a task on the UI thread (.Result / .Wait() / GetAwaiter().GetResult() freeze the window and can deadlock); make the method async and await it.\n"
        + "Only if it is truly safe, add the file to UiResponsivenessBaseline.txt with a reason.";

    private static string BaselinePath(string repo) =>
        Path.Combine(repo, "Source", "tests", "TabForge.Avalonia.Tests", "UiResponsivenessBaseline.txt");

    private static bool IsAvalonia(string rel) => rel.Contains("src/TabForge.Avalonia/", StringComparison.Ordinal);

    private static bool IsUiSource(string rel) =>
        !rel.Contains("/SelfTests/", StringComparison.Ordinal)
        && (IsAvalonia(rel) || (rel.Contains("src/TabForge.Core/", StringComparison.Ordinal) && rel.Contains("Presenter", StringComparison.Ordinal)));

    private static int Match(string s, int open, char o, char c)
    {
        int depth = 0;
        for (int i = open; i < s.Length; i++)
        {
            if (s[i] == o) depth++;
            else if (s[i] == c && --depth == 0) return i;
        }
        return s.Length - 1;
    }

    /// <summary>The body span of the lambda attached at <paramref name="at"/>, or null with <paramref name="methodName"/> set when a method is named.</summary>
    private static (int Start, int End)? HandlerAt(string s, int at, out string? methodName)
    {
        methodName = null;
        int i = at;
        while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        int semi = s.IndexOf(';', i);
        if (semi < 0) semi = s.Length - 1;
        int arrow = s.IndexOf("=>", i, StringComparison.Ordinal);
        if (arrow >= 0)
        {
            int b = arrow + 2;
            while (b < s.Length && char.IsWhiteSpace(s[b])) b++;
            if (b < s.Length && s[b] == '{') return (b, Match(s, b, '{', '}'));
            if (arrow < semi) return (b, semi);
        }
        var m = Regex.Match(s.Substring(i, Math.Min(160, s.Length - i)), @"^(?:this\s*\.\s*)?(?:new\s+[\w.]+(?:<[^>]*>)?\s*\(\s*)?([A-Za-z_]\w*)\s*[;)]");
        if (m.Success) methodName = m.Groups[1].Value;
        return null;
    }

    private static IEnumerable<(int Start, int End)> MethodBodies(string s, string name)
    {
        foreach (Match m in Regex.Matches(s, @"\b(?:void|Task|bool)\s+" + Regex.Escape(name) + @"\s*\("))
        {
            int j = Match(s, m.Index + m.Length - 1, '(', ')') + 1;
            while (j < s.Length && char.IsWhiteSpace(s[j])) j++;
            if (j < s.Length && s[j] == '{') yield return (j, Match(s, j, '{', '}'));
        }
    }

    private static IEnumerable<(string Rule, int Index)> HotHits(string s, int start, int end)
    {
        var defer = new List<(int, int)>();
        foreach (Match m in DeferWrapperPattern.Matches(s, start))
        {
            if (m.Index >= end) break;
            defer.Add((m.Index, Match(s, m.Index + m.Length - 1, '(', ')')));
        }
        foreach (var (rule, rx) in new[] { ("hot-save", FullSavePattern), ("hot-sync", EngineSyncPattern), ("hot-live", LiveWritePattern) })
        {
            foreach (Match m in rx.Matches(s, start))
            {
                if (m.Index >= end) break;
                if (defer.Any(d => m.Index > d.Item1 && m.Index < d.Item2)) continue;
                yield return (rule, m.Index);
            }
        }
    }

    [Fact]
    public void UiThreadStaysResponsive()
    {
        var repo = ArchitectureScan.RepoRoot();
        var files = ArchitectureScan.SourceFiles(repo, f => !IsUiSource(ArchitectureScan.Relative(repo, f)));
        Assert.True(files.Count > 50, "UiResponsiveness guard scanned too few source files");
        var texts = files.ToDictionary(f => ArchitectureScan.Relative(repo, f), f => ArchitectureScan.Strip(ArchitectureScan.ReadText(f), stripStrings: true), StringComparer.Ordinal);
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var lines = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        ScanTexts(texts, counts, lines);
        ArchitectureScan.AssertCountRatchet(counts, lines, BaselinePath(repo), WriteBaselineEnvVar, "UiResponsiveness\n" + HotFix + "\n");
    }

    [Theory]
    [InlineData("void Wire(){ s.ValueChanged += (a, b) => { settings.SaveSettings(); }; }", "hot-save")]
    [InlineData("void Wire(){ s.PointerMoved += Moved; } void Moved(object o, E e){ SyncAudioEngine(); }", "hot-sync")]
    [InlineData("protected override void OnPointerWheelChanged(E e){ AvaloniaEngineRouting.Apply(p); }", "hot-sync")]
    [InlineData("void Wire(){ v.SizeChanged += (a, b) => { x.SaveSettings(); }; }", "hot-save")]
    [InlineData("void Go(){ var t = Load(); t.GetAwaiter().GetResult(); }", "block-wait")]
    [InlineData("void Go(){ task.Wait(50); }", "block-wait")]
    [InlineData("void Go(){ Task.WaitAll(a, b); }", "block-wait")]
    [InlineData("void Go(){ var r = op.Result; }", "block-wait")]
    [InlineData("void Go(){ var r = window.Task.Result; }", "block-wait")]
    [InlineData("void Wire(){ s.ValueChanged += (a, b) => { MarkChanged(); }; } void MarkChanged(){ WriteLive(w); }", "hot-live")]
    [InlineData("void Wire(){ s.ValueChanged += Moved; } void Moved(object o, E e){ Mark(); } void Mark(){ Inner(); } void Inner(){ RaisePreviewed(); }", "hot-live")]
    public void ScannerFlagsKnownBadShapes(string source, string rule)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        ScanTexts(new Dictionary<string, string> { ["Source/src/TabForge.Avalonia/Sample.cs"] = source }, counts, new Dictionary<string, List<int>>());
        Assert.Contains(counts.Keys, k => k.StartsWith(rule + ":", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("void Wire(){ s.ValueChanged += (a, b) => { settings.SaveSettingsQuietly(); sync.Request(); }; }")]
    [InlineData("void Wire(){ s.ValueChanged += (a, b) => { ui.Post(() => settings.SaveSettings()); }; }")]
    [InlineData("void Wire(){ b.Click += (a, e) => { settings.SaveSettings(); }; }")]
    public void ScannerAcceptsTheCorrectPatterns(string source)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        ScanTexts(new Dictionary<string, string> { ["Source/src/TabForge.Avalonia/Sample.cs"] = source }, counts, new Dictionary<string, List<int>>());
        Assert.Empty(counts);
    }

    private static void ScanTexts(Dictionary<string, string> texts, Dictionary<string, int> counts, Dictionary<string, List<int>> lines)
    {
        var seen = new HashSet<(string, int)>();

        void Report(string file, string rule, int index)
        {
            if (!seen.Add((file, index))) return;
            var key = rule + ":" + file;
            counts[key] = counts.GetValueOrDefault(key) + 1;
            if (!lines.TryGetValue(key, out var l)) lines[key] = l = new List<int>();
            l.Add(ArchitectureScan.LineOf(texts[file], index));
        }

        // A handler's own calls are followed into the same file's methods, CallDepth levels deep (lambda -> MarkChanged -> WriteLive).
        void ScanRange(string file, int start, int end, int depth = CallDepth)
        {
            foreach (var (rule, idx) in HotHits(texts[file], start, end)) Report(file, rule, idx);
            if (depth == 0) return;
            var s = texts[file];
            foreach (Match call in CallPattern.Matches(s.Substring(start, end - start + 1)))
                foreach (var (a, b) in MethodBodies(s, call.Groups[1].Value))
                    if (a < start || a > end) ScanRange(file, a, b, depth - 1);
        }

        void ScanNamed(string name)
        {
            foreach (var f in texts.Keys)
                foreach (var (a, b) in MethodBodies(texts[f], name)) ScanRange(f, a, b);
        }

        void ScanHandler(string file, string s, int at)
        {
            var lam = HandlerAt(s, at, out var name);
            if (lam is not null) ScanRange(file, lam.Value.Start, lam.Value.End);
            else if (name is not null) ScanNamed(name);
        }

        foreach (var (file, s) in texts)
        {
            bool fastTimer = FastIntervalPattern.IsMatch(s);
            foreach (Match m in SubscribePattern.Matches(s))
            {
                var ev = m.Groups[1].Value;
                if (ev == "Tick" && !fastTimer) continue;
                if (ev == "PropertyChanged" && !s.AsSpan(m.Index, Math.Min(400, s.Length - m.Index)).Contains("Bounds".AsSpan(), StringComparison.Ordinal)) continue;
                ScanHandler(file, s, m.Index + m.Length);
            }
            foreach (Match m in FramePattern.Matches(s))
                ScanHandler(file, s, m.Index + m.Length);
            foreach (Match m in OverridePattern.Matches(s))
            {
                int j = Match(s, m.Index + m.Length - 1, '(', ')') + 1;
                while (j < s.Length && char.IsWhiteSpace(s[j])) j++;
                if (j < s.Length && s[j] == '{') ScanRange(file, j, Match(s, j, '{', '}'));
            }
            if (IsAvalonia(file))
            {
                foreach (Match m in BlockingWaitPattern.Matches(s))
                    if (!IsNonTaskResult(s, m)) Report(file, "block-wait", m.Index);
            }
        }
    }
}
