using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using TabForge.Avalonia.Views;
using TabForge.Platform.Theme;
using TabForge.Presenters;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>What one rendered shot is: its name, theme, size, group (window or states), PNG (relative to the output folder) and how many items each check looked at.</summary>
internal sealed record SweepShot(string Name, string Theme, string Size, string Group, string Png, Dictionary<string, int> Checked, string? Error);

/// <summary>What one sweep case leaves behind: its shot and its findings.</summary>
internal sealed record SweepCase(SweepShot Shot, List<SweepFinding> Findings);

/// <summary>
/// Owns: the screenshot sweep, opt-in with TABFORGE_SHOTS=&lt;output folder&gt; (otherwise every case returns at once). It renders the parity windows
/// (ParityShotMap) and the Paste special, bar-range and empty-song windows in both themes (the main windows also at 1600x900 and 1024x640),
/// and a rest/hover/pressed/focus/disabled strip for each control kind in both themes; runs ShotAnalysis over each PNG; and writes
/// findings.json, findings.md, index.html (a contact sheet with a findings filter) and the release set (release-shots.txt) into the folder.
/// Does not own: the window twins (ParityShotMap) or the checks (ShotAnalysis). Fails on a render exception only (recorded as a finding too); findings never fail it.
/// Re-run: set TABFORGE_SHOTS and TABFORGE_REPO, then dotnet test --filter "FullyQualifiedName~ShotSweepTests".
/// </summary>
public class ShotSweepTests
{
    private static string? OutDir => Environment.GetEnvironmentVariable("TABFORGE_SHOTS") is { Length: > 0 } dir ? dir : null;

    private static readonly string[] Themes = { "dark", "light" };
    private static readonly string[] MainLike = { "win-default", "main-empty", "play-playhead", "play-band", "play-learn", "win-Mixer", "win-Preferences" };
    private static readonly string[] States = { "rest", "hover", "pressed", "focus", "disabled" };

    private static readonly Dictionary<string, Func<ParityCapture>> Extras = new()
    {
        ["main-empty"] = () => new ParityCapture(new MainWindow(null, null, MainWindowTabs.NewSong()) { Width = 1600, Height = 1000 }),
        ["paste-special-bars"] = () => new ParityCapture(new PasteSpecialWindow(bars: true)),
        ["paste-special-notes"] = () => new ParityCapture(new PasteSpecialWindow(bars: false)),
        ["paste-special-range"] = () => new ParityCapture(new PasteSpecialWindow(new RangePasteRequest(new RangePasteOptions()))),
        ["delete-bars-clear"] = () => new ParityCapture(new BarRangePromptWindow("Bars 12-14 are selected. What should Delete do?", BarRangeAction.Clear, true, _ => "")),
        ["delete-bars-remove"] = () => new ParityCapture(new BarRangePromptWindow("Bars 12-14 are selected. What should Delete do?", BarRangeAction.Remove, false, id => id == "Range.Remove" ? "Shift+Del" : "")),
    };

    public static IEnumerable<object[]> Windows()
    {
        var names = ParityShotMap.Entries.Where(e => e.Value.Build is not null).Select(e => e.Key).Concat(Extras.Keys).Distinct().ToList();
        foreach (var name in names)
            foreach (var theme in Themes)
                foreach (var size in MainLike.Contains(name) ? new[] { "native", "1600x900", "1024x640" } : new[] { "native" })
                    yield return new object[] { name, theme, size };
    }

    public static IEnumerable<object[]> Kinds()
    {
        var kinds = new List<string> { "Button", "ToggleButton", "CheckBox", "RadioButton", "ComboBox", "TextBox", "Slider", "ListBoxItem", "TabItem" };
        foreach (var c in new[] { "Button", "AccentButton", "PrefsActionButton", "PrefsActionSecondary", "PrefsActionReset", "StepButton" }) kinds.Add("Button." + c);
        foreach (var c in new[] { "MixerToggle", "PrefsMoreOptions" }) kinds.Add("ToggleButton." + c);
        foreach (var look in LookTable.Looks.Values.Where(l => l.Kind is "Button" or "ToggleButton")) kinds.Add($"{look.Kind}.{look.Name}");
        foreach (var kind in kinds.Distinct())
            foreach (var theme in Themes) yield return new object[] { kind, theme };
    }

    [AvaloniaTheory]
    [MemberData(nameof(Windows))]
    public void SweepWindow(string name, string theme, string size)
    {
        if (OutDir is not { } outDir) return;
        var light = theme == "light";
        using var themeScope = DialogTestKit.UseTheme(light);
        global::TabForge.Avalonia.Platform.ToolIconArt.PreloadAll();   // the app preloads the tool SVGs after its first frame; a shot is taken without that idle step
        var png = Path.Combine(Directory.CreateDirectory(Path.Combine(outDir, "windows")).FullName, $"{name}-{theme}-{size}.png");
        Window? window = null;
        try
        {
            var capture = (ParityShotMap.Entries.TryGetValue(name, out var entry) && entry.Build is { } build ? build : Extras[name])();
            window = capture.Window;
            if (size != "native")
            {
                var parts = size.Split('x');
                window.SizeToContent = SizeToContent.Manual;
                window.Width = int.Parse(parts[0]);
                window.Height = int.Parse(parts[1]);
            }
            window.Show();
            DialogTestKit.RunQueuedUiWork();
            global::Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            var part = capture.Part?.Invoke(window);
            Visual root = window;
            if (part is null)
            {
                using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("The window rendered no frame.");
                frame.Save(png);
            }
            else
            {
                root = part;
                var pixels = new PixelSize(Math.Max(1, (int)Math.Ceiling(part.Bounds.Width)), Math.Max(1, (int)Math.Ceiling(part.Bounds.Height)));
                using var bitmap = new RenderTargetBitmap(pixels, new Vector(96, 96));
                bitmap.Render(part);
                bitmap.Save(png);
            }
            var counts = new Dictionary<string, int>();
            var findings = ShotAnalysis.Run(root, ShotPixels.Load(png), light, true, name, theme, size, "rest", png, counts);
            Save(outDir, new SweepCase(new SweepShot(name, theme, size, "window", Rel(outDir, png), counts, null), findings));
        }
        catch (Exception ex) when (ex is not Xunit.Sdk.XunitException)
        {
            Save(outDir, new SweepCase(new SweepShot(name, theme, size, "window", "", new Dictionary<string, int>(), ex.GetType().Name + ": " + ex.Message),
                new List<SweepFinding> { new("render-failure", "High", name, theme, size, "rest", ex.GetType().Name + ": " + ex.Message, png) }));
        }
        finally
        {
            window?.Close();
        }
    }

    [AvaloniaTheory]
    [MemberData(nameof(Kinds))]
    public void SweepControlStates(string kind, string theme)
    {
        if (OutDir is not { } outDir) return;
        var light = theme == "light";
        using var themeScope = DialogTestKit.UseTheme(light);
        global::TabForge.Avalonia.Platform.ToolIconArt.PreloadAll();   // the app preloads the tool SVGs after its first frame; a shot is taken without that idle step
        var png = Path.Combine(Directory.CreateDirectory(Path.Combine(outDir, "states")).FullName, $"{kind}-{theme}.png");
        var shotName = "states:" + kind;
        var window = new Window { SizeToContent = SizeToContent.WidthAndHeight };
        try
        {
            var controls = States.Select(_ => Make(kind)).ToList();
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(14), Spacing = 16 };
            for (var i = 0; i < controls.Count; i++)
            {
                var cell = new StackPanel { Spacing = 4 };
                cell.Children.Add(controls[i]);
                cell.Children.Add(new TextBlock { Text = States[i], FontSize = 11, Opacity = 0.9 });
                row.Children.Add(cell);
            }
            window.Content = row;
            window.Show();
            DialogTestKit.RunQueuedUiWork();
            ((IPseudoClasses)controls[1].Classes).Set(":pointerover", true);
            ((IPseudoClasses)controls[2].Classes).Set(":pointerover", true);
            ((IPseudoClasses)controls[2].Classes).Set(":pressed", true);
            controls[3].Focus(NavigationMethod.Tab);
            ((IPseudoClasses)controls[3].Classes).Set(":focus-visible", true);
            controls[4].IsEnabled = false;
            window.UpdateLayout();
            DialogTestKit.RunQueuedUiWork();
            global::Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();   // the state changes above reach the frame only after a render tick
            using (var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("The window rendered no frame.")) frame.Save(png);
            var px = ShotPixels.Load(png);
            var counts = new Dictionary<string, int>();
            var findings = ShotAnalysis.Run(window, px, light, false, shotName, theme, "strip", "all", png, counts);
            // Exempt: the close caption button holds a glyph that its look draws white on the fixed red hover (CaptionButtons); the strip's "Cancel" text is only a stand-in.
            if (kind == "Button.ChromeCloseButton") findings.RemoveAll(f => f.Check == "text-contrast");
            var rects = controls.Select(c => ShotAnalysis.RectIn(c, window) ?? default).ToList();
            void Compare(int state, string check, string severity, string message)
            {
                counts[check] = counts.GetValueOrDefault(check) + 1;
                if (ShotPixels.Differing(px, rects[0], px, rects[state]) == 0)
                    findings.Add(new SweepFinding(check, severity, shotName, theme, "strip", States[state], $"{kind}: {message}", png));
            }
            var pointerless = kind.StartsWith("Slider", StringComparison.Ordinal);
            var noPress = pointerless || kind is "TextBox" or "ComboBox";
            if (!pointerless) Compare(1, "missing-hover", "Medium", "hover looks the same as rest");
            if (!noPress) Compare(2, "missing-pressed", "Medium", "pressed looks the same as rest");
            Compare(3, "missing-focus", "Low", "focus looks the same as rest (no focus indicator)");
            Compare(4, "missing-disabled", "Medium", "disabled looks the same as rest");
            Save(outDir, new SweepCase(new SweepShot(kind, theme, "strip", "states", Rel(outDir, png), counts, null), findings));
        }
        catch (Exception ex) when (ex is not Xunit.Sdk.XunitException)
        {
            Save(outDir, new SweepCase(new SweepShot(kind, theme, "strip", "states", "", new Dictionary<string, int>(), ex.GetType().Name + ": " + ex.Message),
                new List<SweepFinding> { new("render-failure", "High", shotName, theme, "strip", "all", ex.GetType().Name + ": " + ex.Message, png) }));
        }
        finally
        {
            window.Close();
        }
    }

    private static Control Make(string kind)
    {
        var parts = kind.Split('.');
        Control control = parts[0] switch
        {
            "ToggleButton" => new ToggleButton { Content = "Cancel", Width = 140, Height = 34, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center },
            "CheckBox" => new CheckBox { Content = "Option", Width = 140 },
            "RadioButton" => new RadioButton { Content = "Option", Width = 140 },
            "ComboBox" => new ComboBox { ItemsSource = new[] { "Option", "Other" }, SelectedIndex = 0, Width = 140, Height = 34 },
            "TextBox" => new TextBox { Text = "Text", Width = 140 },
            "Slider" => new Slider { Minimum = 0, Maximum = 100, Value = 40, Width = 140 },
            "ListBoxItem" => new ListBoxItem { Content = "Item", Width = 140 },
            "TabItem" => new TabItem { Header = "Tab", Width = 140 },
            _ => new Button { Content = "Cancel", Width = 140, Height = 34, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center },
        };
        if (parts.Length > 1) control.Classes.Add(parts[1]);
        if (parts.Length > 1 && parts[1] == "PrefsPrimaryButton") control.Classes.AddRange(new[] { "PrefsActionButton", "AccentButton" });   // the Settings OK button wears these with its look, as the app builds it
        return control;
    }

    private static string Rel(string outDir, string path) => Path.GetRelativePath(outDir, path).Replace('\\', '/');

    private static void Save(string outDir, SweepCase result)
    {
        var data = Directory.CreateDirectory(Path.Combine(outDir, "data")).FullName;
        var s = result.Shot;
        File.WriteAllText(Path.Combine(data, $"{s.Group}-{s.Name.Replace(':', '_')}-{s.Theme}-{s.Size}.json"), JsonSerializer.Serialize(result));
        WriteReport(outDir);
    }

    private static readonly string[] SeverityOrder = { "High", "Medium", "Low" };

    // Rebuilt from every case file after each case, so a partial run still leaves a readable report.
    private static void WriteReport(string outDir)
    {
        var cases = Directory.GetFiles(Path.Combine(outDir, "data"), "*.json").Select(f => JsonSerializer.Deserialize<SweepCase>(File.ReadAllText(f))!).ToList();
        var findings = cases.SelectMany(c => c.Findings)
            .OrderBy(f => Array.IndexOf(SeverityOrder, f.Severity)).ThenBy(f => f.Check, StringComparer.Ordinal).ThenBy(f => f.Shot, StringComparer.Ordinal).ThenBy(f => f.Theme).ToList();
        File.WriteAllText(Path.Combine(outDir, "findings.json"), JsonSerializer.Serialize(findings, new JsonSerializerOptions { WriteIndented = true }));

        var md = new StringBuilder("# Screenshot sweep findings\n\n");
        md.Append($"{cases.Count} shots, {findings.Count} findings. Ranked High, Medium, Low; each line names the PNG.\n\n## Counts by check\n\n| check | looked at | High | Medium | Low |\n|---|---|---|---|---|\n");
        var checks = findings.Select(f => f.Check).Concat(cases.SelectMany(c => c.Shot.Checked.Keys)).Distinct().OrderBy(c => c, StringComparer.Ordinal);
        foreach (var check in checks)
            md.Append($"| {check} | {cases.Sum(c => c.Shot.Checked.GetValueOrDefault(check))} | {findings.Count(f => f.Check == check && f.Severity == "High")} | {findings.Count(f => f.Check == check && f.Severity == "Medium")} | {findings.Count(f => f.Check == check && f.Severity == "Low")} |\n");
        foreach (var severity in SeverityOrder)
        {
            var group = findings.Where(f => f.Severity == severity).ToList();
            if (group.Count == 0) continue;
            md.Append($"\n## {severity} ({group.Count})\n\n");
            foreach (var f in group) md.Append($"- [{f.Check}] {f.Shot} / {f.Theme} / {f.Size} / {f.State}: {f.Detail}  \n  {f.Png}\n");
        }
        File.WriteAllText(Path.Combine(outDir, "findings.md"), md.ToString());

        var html = new StringBuilder("<!doctype html><meta charset=utf-8><title>TabForge screenshot sweep</title><style>body{font:13px sans-serif;background:#222;color:#ddd}.c{display:inline-block;margin:6px;padding:4px;background:#333;vertical-align:top}.c img{display:block;max-width:360px;max-height:240px;background:#888}.bad{outline:2px solid #e55}.d{color:#f99;font-size:11px}</style>");
        html.Append("<h2>TabForge screenshot sweep</h2><input id=q placeholder=filter> <select id=t><option value=''>both themes<option>dark<option>light</select> <label><input type=checkbox id=f> findings only</label> <a href=findings.md>findings.md</a><div id=g>");
        foreach (var c in cases.OrderBy(c => c.Shot.Group).ThenBy(c => c.Shot.Name, StringComparer.Ordinal).ThenBy(c => c.Shot.Theme))
        {
            var s = c.Shot;
            html.Append($"<div class='c{(c.Findings.Count > 0 ? " bad" : "")}' data-t='{s.Theme}' data-n='{WebUtility.HtmlEncode((s.Group + " " + s.Name + " " + s.Size).ToLowerInvariant())}' data-f='{c.Findings.Count}'>");
            if (s.Png.Length > 0) html.Append($"<a href='{WebUtility.HtmlEncode(s.Png)}'><img loading=lazy src='{WebUtility.HtmlEncode(s.Png)}'></a>");
            html.Append($"<b>{WebUtility.HtmlEncode(s.Name)}</b> {s.Theme} {s.Size} <span class=d>{c.Findings.Count} findings</span>");
            foreach (var f in c.Findings.Take(4)) html.Append($"<div class=d>{WebUtility.HtmlEncode(f.Severity + " " + f.Check + ": " + f.Detail)}</div>");
            html.Append("</div>");
        }
        html.Append("</div><script>const q=document.getElementById('q'),t=document.getElementById('t'),f=document.getElementById('f');function a(){for(const e of document.querySelectorAll('.c'))e.style.display=(e.dataset.n.includes(q.value.toLowerCase())&&(!t.value||e.dataset.t==t.value)&&(!f.checked||e.dataset.f>0))?'':'none'}q.oninput=t.onchange=f.onchange=a</script>");
        File.WriteAllText(Path.Combine(outDir, "index.html"), html.ToString());

        var list = Path.Combine(TestRepo.CheckoutRoot(), "Source", "tests", "TabForge.Avalonia.Tests", "release-shots.txt");
        if (!File.Exists(list)) return;
        var release = Directory.CreateDirectory(Path.Combine(outDir, "release")).FullName;
        foreach (var line in File.ReadAllLines(list).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#')))
            foreach (var c in cases.Where(c => c.Shot.Png.Length > 0 && $"{c.Shot.Name}-{c.Shot.Theme}-{c.Shot.Size}" == line))
                File.Copy(Path.Combine(outDir, c.Shot.Png), Path.Combine(release, line + ".png"), true);
    }
}
