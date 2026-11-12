using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using TabForge.Avalonia.Views;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the characterization of what the window's composition root wires, so a refactor of MainWindow.Compose* cannot drop or double a piece
//   silently: every command id the window registers (without and with a settings file), the listener counts on the arrangement's events the
//   composition hooks, and the number of tab-switch (ActiveChanged) subscribers. Golden values in main-window-composition.txt (regenerate only
//   with TABFORGE_COMPOSITION_GOLDEN_WRITE=1 after a deliberate change, with the reason in the commit message).
// Does not own: what each command does (its own test class) or the menu tree (MainWindowMenuTests).
// Tests: MainWindow composition (MainWindow.Compose*.cs).
public sealed class MainWindowCompositionTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "tf-compose-" + Guid.NewGuid().ToString("N"));

    public MainWindowCompositionTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { /* swallow: a temp folder left behind is harmless */ }
    }

    private static string GoldenPath => Path.Combine(ArchitectureScan.RepoRoot(), "Source", "tests", "TabForge.Avalonia.Tests", "MainWindow", "main-window-composition.txt");

    private static IEnumerable<string> Keys(object table, string field) =>
        ((IDictionary)typeof(MainWindowCommands).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(table)!).Keys.Cast<string>();

    private static int Listeners(object owner, string eventName)
    {
        for (var type = owner.GetType(); type is not null; type = type.BaseType)
            if (type.GetField(eventName, BindingFlags.Instance | BindingFlags.NonPublic) is { } field)
                return (field.GetValue(owner) as Delegate)?.GetInvocationList().Length ?? 0;
        throw new InvalidOperationException($"no event field {eventName} on {owner.GetType().Name}");
    }

    private static IEnumerable<string> Describe(string label, MainWindow window)
    {
        foreach (var id in Keys(window.CommandTable, "_handlers").Concat(Keys(window.CommandTable, "_argumentHandlers")).Distinct().OrderBy(id => id, StringComparer.Ordinal))
            yield return $"{label}|command|{id}";
        yield return $"{label}|ActiveChanged|{Listeners(window.Songs, nameof(MainWindowTabs.ActiveChanged))}";
        var arrangement = window.GetVisualDescendants().OfType<ArrangementView>().FirstOrDefault();
        if (arrangement is null) { yield return $"{label}|arrangement|none"; yield break; }
        yield return $"{label}|Input.TrackArmToggled|{Listeners(arrangement.Input, "TrackArmToggled")}";
        yield return $"{label}|Input.SectionMoved|{Listeners(arrangement.Input, "SectionMoved")}";
        yield return $"{label}|Timeline.ClipEditLanded|{Listeners(arrangement.Timeline, "ClipEditLanded")}";
        yield return $"{label}|Timeline.ClipNotice|{Listeners(arrangement.Timeline, "ClipNotice")}";
        yield return $"{label}|Timeline.MediaApprovalRequested|{Listeners(arrangement.Timeline, "MediaApprovalRequested")}";
        yield return $"{label}|Timeline.MixMarkClicked|{Listeners(arrangement.Timeline, "MixMarkClicked")}";
        yield return $"{label}|TrackRowsCountChanged|{Listeners(arrangement, "TrackRowsCountChanged")}";
        yield return $"{label}|TrackArmChanged|{Listeners(arrangement, "TrackArmChanged")}";
    }

    [AvaloniaFact]
    public void TheWindowWiresTheSameCommandsAndHooks()
    {
        var actual = new List<string>();
        var bare = new MainWindow { Width = 1600, Height = 1000 };
        bare.Show();
        actual.AddRange(Describe("bare", bare));
        bare.Close();
        using (var settings = MainWindowSettings.Open(Path.Combine(_folder, "settings.json")))
        {
            var withSettings = new MainWindow(settings) { Width = 1600, Height = 1000 };
            withSettings.Show();
            actual.AddRange(Describe("settings", withSettings));
            withSettings.Close();
        }
        if (Environment.GetEnvironmentVariable("TABFORGE_COMPOSITION_GOLDEN_WRITE") == "1")
        {
            File.WriteAllLines(GoldenPath, actual);
            return;
        }
        var golden = File.ReadAllLines(GoldenPath).Where(l => l.Length > 0).ToList();
        var missing = golden.Except(actual).ToList();
        var added = actual.Except(golden).ToList();
        Assert.True(missing.Count == 0 && added.Count == 0, "missing:\n" + string.Join("\n", missing) + "\nadded:\n" + string.Join("\n", added));
    }
}
