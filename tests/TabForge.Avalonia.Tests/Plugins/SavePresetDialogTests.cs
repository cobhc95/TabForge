using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Presenters;
using Xunit;
using static TabForge.Avalonia.Tests.DialogTestKit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the shared save-preset dialog checks (new name saves, existing name shows the replace note and asks,
/// built-in rows fill the copy suggestion and cannot be saved, empty and invalid names disable OK with an inline
/// message), the proof that every converted caller passes its own preset list, and the guard that fails when a raw
/// single-text-box Save preset prompt is created outside the shared component.
/// Does not own: the dialog or the callers (Views.PluginPresetWindow, the five converted call sites).
/// Tests: this class.
/// </summary>
public class SavePresetDialogTests
{
    private static Window Owner()
    {
        var owner = new Window();
        owner.Show();
        return owner;
    }

    private static ComboBox Box(Window window) => window.GetLogicalDescendants().OfType<ComboBox>().Single();

    private static Button Ok(Window window) =>
        window.GetLogicalDescendants().OfType<Button>().Single(b => b.Content as string == "OK");

    private static TextBlock Note(Window window) =>
        window.GetLogicalDescendants().OfType<TextBlock>().Single(t => t.Name == "NoteText");

    private static PluginPresetWindow NewDialog(string initial, IReadOnlyList<string> users,
        IReadOnlyList<string>? builtIns = null, Func<string, string?>? validator = null) =>
        new(new AvaloniaDialogHost(), "Save preset", "Preset name:", initial, users, builtIns, validator, null);

    [AvaloniaFact]
    public async System.Threading.Tasks.Task NewNameSavesTrimmedWithoutNote()
    {
        var owner = Owner();
        var window = NewDialog("", new[] { "Warm" }, null, PresetRules.Error);
        var pending = window.ShowDialog<string?>(owner);
        Assert.False(Ok(window).IsEnabled);   // empty disables OK
        Box(window).Text = "  Bright ";
        Assert.True(Ok(window).IsEnabled);
        Assert.False(Note(window).IsVisible);
        Click(Ok(window));
        Assert.Equal("Bright", await pending);
        owner.Close();
    }

    [AvaloniaFact]
    public async System.Threading.Tasks.Task ExistingNameShowsReplaceNoteAndSaves()
    {
        // The new path shows the inline replace note and closes with the name; the caller asks the replace
        // question itself (its store semantics are unchanged), so no modal opens here.
        var owner = Owner();
        var window = NewDialog("", new[] { "Warm", "Bright" }, null, PresetRules.Error);
        var pending = window.ShowDialog<string?>(owner);
        Box(window).Text = "warm";
        Assert.True(Ok(window).IsEnabled);
        Assert.True(Note(window).IsVisible);
        Assert.Equal(PluginPresetNamePresenter.ReplaceNote("Warm"), Note(window).Text);
        Click(Ok(window));
        Assert.Equal("warm", await pending);
        owner.Close();
    }

    [AvaloniaFact]
    public void BuiltInRowPickFillsCopySuggestion()
    {
        var window = NewDialog("", new[] { "Warm" }, new[] { "Factory" });
        window.Show();
        Assert.Contains("Factory" + PluginPresetNamePresenter.BuiltInSuffix, Box(window).Items.Cast<string>());
        Box(window).SelectedItem = "Factory" + PluginPresetNamePresenter.BuiltInSuffix;
        Assert.Equal(PresetRules.CopyName("Factory", new[] { "Warm" }), Box(window).Text);
        Assert.True(Ok(window).IsEnabled);
        window.Close();
    }

    [AvaloniaFact]
    public void TypingBuiltInNameShowsErrorAndDisablesOk()
    {
        var window = NewDialog("", new[] { "Warm" }, new[] { "Factory" });
        window.Show();
        Box(window).Text = "factory";
        Assert.False(Ok(window).IsEnabled);
        Assert.True(Note(window).IsVisible);
        Assert.Equal(PluginPresetNamePresenter.BuiltInError, Note(window).Text);
        window.Close();
    }

    [AvaloniaFact]
    public void InvalidNameShowsMessageAndDisablesOk()
    {
        var window = NewDialog("", new[] { "Warm" }, null, PresetRules.Error);
        window.Show();
        Box(window).Text = "a/b";
        Assert.False(Ok(window).IsEnabled);
        Assert.True(Note(window).IsVisible);
        Assert.Equal(PresetRules.Error("a/b"), Note(window).Text);
        window.Close();
    }

    public static IEnumerable<object[]> Callers() => new[]
    {
        // Every converted caller routes through the shared component and passes its own store list.
        new object[] { "Source/src/TabForge.Avalonia/Views/Midi/MidiPresetBars.cs", "SavePresetName", "_listStore.Names()" },
        new object[] { "Source/src/TabForge.Avalonia/Views/Midi/MidiPresetBars.cs", "SavePresetName", "store.Names()" },
        new object[] { "Source/src/TabForge.Avalonia/Views/Articulations/ArticulationsPane.Files.cs", "SavePresetName", "_store.Names()" },
        new object[] { "Source/src/TabForge.Avalonia/Views/Fx/FxContainerPresets.cs", "SavePresetName", "_store.Names()" },
        new object[] { "Source/src/TabForge.Avalonia/Views/CurveSidePanel.cs", "PluginPresetWindow", "_presets.List" },
    };

    [Theory]
    [MemberData(nameof(Callers))]
    public void ConvertedCallerPassesItsPresetList(string relative, string shared, string list)
    {
        var text = File.ReadAllText(Path.Combine(TestRepo.CheckoutRoot(), relative.Replace('/', Path.DirectorySeparatorChar)));
        Assert.Contains(shared, text);
        Assert.Contains(list, text);
        Assert.DoesNotContain(".Prompt(", text);
        Assert.DoesNotContain("new TextPromptWindow(", text);
    }

    [Fact]
    public void NoRawSavePresetPromptOutsideTheSharedComponent()
    {
        var repo = TestRepo.CheckoutRoot();
        var problems = new List<string>();
        foreach (var file in ArchitectureScan.SourceFiles(repo))
        {
            var stripped = ArchitectureScan.Strip(ArchitectureScan.ReadText(file), stripStrings: false);
            var consts = ConstStrings(stripped);
            foreach (var index in Occurrences(stripped, "new TextPromptWindow("))
                if (ArgText(stripped, index + "new TextPromptWindow".Length) is { } args && Substitute(args, consts).IndexOf("preset", StringComparison.OrdinalIgnoreCase) >= 0)
                    problems.Add($"{ArchitectureScan.Relative(repo, file)}:{ArchitectureScan.LineOf(stripped, index)}: TextPromptWindow with a preset name");
            foreach (var index in Occurrences(stripped, ".Prompt("))
                if (ArgText(stripped, index + ".Prompt".Length) is { } args && StringLiterals(args).Any(l => l.IndexOf("preset", StringComparison.OrdinalIgnoreCase) >= 0))
                    problems.Add($"{ArchitectureScan.Relative(repo, file)}:{ArchitectureScan.LineOf(stripped, index)}: raw .Prompt with a preset name (use FxChainDialogs.SavePresetName)");
        }
        Assert.True(problems.Count == 0, "raw single-text-box Save preset prompt outside PluginPresetWindow:\n" + string.Join("\n", problems));
    }

    private static List<int> Occurrences(string text, string token)
    {
        var list = new List<int>();
        for (var i = text.IndexOf(token, StringComparison.Ordinal); i >= 0; i = text.IndexOf(token, i + 1, StringComparison.Ordinal))
        {
            var before = i == 0 ? ' ' : text[i - 1];
            if (char.IsLetterOrDigit(before) || before == '_') continue;   // PluginPresetNamePresenter.Prompt( is not the dialog seam
            list.Add(i);
        }
        return list;
    }

    /// <summary>The balanced argument list starting at the opening paren at <paramref name="paren"/>.</summary>
    private static string? ArgText(string text, int paren)
    {
        var i = paren;
        while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
        if (i >= text.Length || text[i] != '(') return null;
        var depth = 0;
        var start = i;
        for (; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '"' || c == '\'')
            {
                var quote = c;
                i++;
                while (i < text.Length && text[i] != quote)
                    i += text[i] == '\\' ? 2 : 1;
            }
            else if (c == '(') depth++;
            else if (c == ')')
            {
                if (--depth == 0) return text.Substring(start, i - start + 1);
            }
        }
        return null;
    }

    private static Dictionary<string, string> ConstStrings(string text)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(text, @"const\s+string\s+([^;]*);"))
            foreach (var part in m.Groups[1].Value.Split(','))
            {
                var mm = Regex.Match(part, @"(\w+)\s*=\s*""((?:[^""\\]|\\.)*)""");
                if (mm.Success) map[mm.Groups[1].Value] = mm.Groups[2].Value;
            }
        return map;
    }

    private static string Substitute(string args, Dictionary<string, string> consts)
    {
        foreach (var (name, value) in consts)
            args = Regex.Replace(args, $@"\b{Regex.Escape(name)}\b", value);
        return args;
    }

    private static List<string> StringLiterals(string args)
    {
        var list = new List<string>();
        foreach (Match m in Regex.Matches(args, @"""((?:[^""\\]|\\.)*)"""))
            list.Add(m.Groups[1].Value);
        return list;
    }
}
