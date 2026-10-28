using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Views.NoteMaps;
using TabForge.Plugins;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the drum map editor's auto-copy on first edit of a built-in map: the first change makes a
///   user copy ("&lt;name&gt; (edited)", unique) carrying the edit, the shipped map stays untouched,
///   later edits reuse the copy, and closing without saving leaves no orphan file.
/// Does not own: the editor window (DrumMapEditorWindow), the checks (DrumMapDraft) or the map files (DrumMapLibrary).
/// Tests: this class.
/// </summary>
[Collection("PluginLibraryRoot")]   // PluginLibrary.RootOverride is process-wide: run alone so no parallel test resets it or reads the scratch folder
public class DrumMapBuiltInAutoCopyTests
{
    [AvaloniaFact]
    public void FirstSendEditMakesAnEditedCopyCarryingTheEdit()
    {
        using var scratch = Scratch();
        var builtIn = DrumMapLibrary.Find("Superior Drummer 3");
        Assert.NotNull(builtIn);
        var window = new DrumMapEditorWindow(builtIn, null);
        window.Show();
        try
        {
            var name = DialogTestKit.Named<TextBox>(window, "NameBox");
            Assert.Equal("Superior Drummer 3", name.Text);
            Assert.True(name.IsEnabled);
            Assert.True(DialogTestKit.Named<Button>(window, "SaveButton").IsEnabled);
            Assert.Contains("first change", DialogTestKit.Named<TextBlock>(window, "HintText").Text);

            var send = DialogTestKit.Named<TextBox>(window, "Send_42");
            Assert.Equal("61", send.Text);
            send.Text = "62";
            DialogTestKit.RunQueuedUiWork();

            Assert.Equal("Superior Drummer 3 (edited)", name.Text);
            var maps = DialogTestKit.Named<ComboBox>(window, "MapBox");
            var selected = Assert.IsType<ComboBoxItem>(maps.SelectedItem);
            Assert.Equal("★ Superior Drummer 3 (edited)", selected.Content);
            Assert.Equal("62", DialogTestKit.Named<TextBox>(window, "Send_42").Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void SavedCopyCarriesTheEditAndTheBuiltInStaysUntouched()
    {
        using var scratch = Scratch();
        var builtIn = DrumMapLibrary.Find("Superior Drummer 3");
        Assert.NotNull(builtIn);
        var window = new DrumMapEditorWindow(builtIn, null);
        window.Show();
        try
        {
            DialogTestKit.Named<TextBox>(window, "Send_42").Text = "62";
            DialogTestKit.RunQueuedUiWork();
            FxTestKit.PressButton(window, "SaveButton");
            DialogTestKit.RunQueuedUiWork();

            Assert.Equal("Superior Drummer 3 (edited)", window.Saved);
            var copy = DrumMapLibrary.Find("Superior Drummer 3 (edited)");
            Assert.NotNull(copy);
            Assert.True(copy.IsUser);
            Assert.Equal(62, DrumMapLibrary.BuildTable("General MIDI", "Superior Drummer 3 (edited)", "")[42]);
            Assert.Equal(61, DrumMapLibrary.BuildTable("General MIDI", "Superior Drummer 3", "")[42]);
            Assert.False(File.Exists(Path.Combine(PluginLibrary.Root, "DrumMaps", "Superior Drummer 3.json")));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void SecondEditReusesTheCopy()
    {
        using var scratch = Scratch();
        var builtIn = DrumMapLibrary.Find("Superior Drummer 3");
        Assert.NotNull(builtIn);
        var window = new DrumMapEditorWindow(builtIn, null);
        window.Show();
        try
        {
            DialogTestKit.Named<TextBox>(window, "Send_42").Text = "62";
            DialogTestKit.RunQueuedUiWork();
            DialogTestKit.Named<TextBox>(window, "Send_36").Text = "38";
            DialogTestKit.RunQueuedUiWork();

            Assert.Equal("Superior Drummer 3 (edited)", DialogTestKit.Named<TextBox>(window, "NameBox").Text);
            var maps = DialogTestKit.Named<ComboBox>(window, "MapBox");
            Assert.Single(maps.Items.OfType<ComboBoxItem>(), i => (i.Content?.ToString() ?? "").Contains("(edited)"));
            FxTestKit.PressButton(window, "SaveButton");
            DialogTestKit.RunQueuedUiWork();

            Assert.Single(DrumMapLibrary.All(), m => m.IsUser && m.Name.StartsWith("Superior Drummer 3 (edited)", StringComparison.Ordinal));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void EditedNameIsUnique()
    {
        using var scratch = Scratch();
        Write(Path.Combine("DrumMaps", "Superior Drummer 3 (edited).json"), "{\"Name\":\"Superior Drummer 3 (edited)\",\"Notes\":{\"36\":\"Kick\"}}");
        DrumMapLibrary.Reload();
        var builtIn = DrumMapLibrary.Find("Superior Drummer 3");
        Assert.NotNull(builtIn);
        var window = new DrumMapEditorWindow(builtIn, null);
        window.Show();
        try
        {
            DialogTestKit.Named<TextBox>(window, "Send_42").Text = "62";
            DialogTestKit.RunQueuedUiWork();

            Assert.Equal("Superior Drummer 3 (edited 2)", DialogTestKit.Named<TextBox>(window, "NameBox").Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ClosingWithoutSavingLeavesNoOrphanCopy()
    {
        using var scratch = Scratch();
        var builtIn = DrumMapLibrary.Find("Superior Drummer 3");
        Assert.NotNull(builtIn);
        var window = new DrumMapEditorWindow(builtIn, null);
        window.Show();
        try
        {
            DialogTestKit.Named<TextBox>(window, "Send_42").Text = "62";
            DialogTestKit.RunQueuedUiWork();
            Assert.Equal("Superior Drummer 3 (edited)", DialogTestKit.Named<TextBox>(window, "NameBox").Text);
        }
        finally { window.Close(); }

        DrumMapLibrary.Reload();
        Assert.Null(DrumMapLibrary.Find("Superior Drummer 3 (edited)"));
        Assert.Empty(Directory.Exists(Path.Combine(PluginLibrary.Root, "DrumMaps"))
            ? Directory.EnumerateFiles(Path.Combine(PluginLibrary.Root, "DrumMaps"), "*.json")
            : Array.Empty<string>());
    }

    private static IDisposable Scratch()
    {
        var scratch = FxTestKit.ScratchLibrary();
        DrumMapLibrary.Reload();
        return new ScratchRoot(scratch);
    }

    private sealed class ScratchRoot : IDisposable
    {
        private readonly IDisposable _scratch;
        public ScratchRoot(IDisposable scratch) => _scratch = scratch;
        public void Dispose() { _scratch.Dispose(); DrumMapLibrary.Reload(); }   // leave no cache pointing at a deleted folder
    }

    private static void Write(string relativePath, string text)
    {
        var path = Path.Combine(PluginLibrary.Root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }
}
