using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Avalonia.Views.Fx;
using TabForge.Avalonia.Views.Midi;
using TabForge.Avalonia.Views.Presets;
using TabForge.Plugins;
using TabForge.Presenters;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the organisable row layer over a fake store (built-in plain, a user row's two icon buttons and their automation names, tooltips, hover
///   surface and compact right-aligned layout, refused name, replace question, one delete question, reorder at the ends, the Manage row never a
///   preset) and the one check per call site that its box ends with the Manage row.
/// Does not own: the manager window or the stores' file rules.
/// Tests: this class.
/// </summary>
public class PresetRowsTests
{
    private sealed record Row(string Text, string? Name, bool Manage = false, bool Group = false);

    [AvaloniaFact]
    public void BuiltInRowsArePlainAndUserRowsCarryTheirTools()
    {
        var (window, _, rows, _) = Open();
        Assert.IsType<TextBlock>(rows.View(new Row("Built-in", null)));
        Assert.IsType<Border>(rows.View(new Row("Mine", "Mine")));
        window.Close();
    }

    [AvaloniaFact]
    public void UserRowCarriesTwoIconButtonsNamedForTheActionAndPreset()
    {
        var (window, _, rows, _) = Open();
        var user = rows.View(new Row("Metal", "Metal"));
        var buttons = user.GetLogicalDescendants().OfType<Button>().ToList();
        Assert.Equal(2, buttons.Count);
        Assert.Equal(new[] { "Rename Metal", "Delete Metal" }, buttons.Select(button => AutomationProperties.GetName(button) ?? ""));
        Assert.Equal(new[] { "Rename (F2)", "Delete (Delete)" }, buttons.Select(button => ToolTip.GetTip(button) as string ?? ""));
        Assert.All(buttons, button => Assert.IsType<Path>(button.Content));   // the icon alone: no box behind it
        Assert.All(buttons, button => Assert.Null(button.Cursor));   // the pointer shape never changes on hover
        Assert.DoesNotContain(user.GetLogicalDescendants().OfType<TextBlock>(), text => text.Text is "Rename" or "Delete");
        window.Close();
    }

    [AvaloniaFact]
    public void BuiltInRowCarriesNoButtons()
    {
        var (window, _, rows, _) = Open();
        var builtIn = rows.View(new Row("Built-in", null));
        Assert.Empty(builtIn.GetLogicalDescendants().OfType<Button>());
        Assert.Empty(builtIn.GetLogicalDescendants().OfType<Path>());
        window.Close();
    }

    [AvaloniaFact]
    public void UserRowIconsAreSmallAppearOnHoverAndTurnDangerOrAccentUnderThePointer()
    {
        var (window, _, rows, _) = Open();
        var user = rows.View(new Row("A preset name long enough for the old text buttons to leave it no room", "Long"));
        var host = new Window { Width = 240, Height = 60, HorizontalContentAlignment = HorizontalAlignment.Stretch, Content = user };
        host.Show();
        var buttons = user.GetLogicalDescendants().OfType<Button>().ToList();
        Assert.Equal(2, buttons.Count);
        var tools = Assert.IsType<StackPanel>(buttons[0].Parent);
        // The pencil and bin keep their room but are invisible and ignore the pointer until the pointer is over the row.
        Assert.Equal(0, tools.Opacity);
        Assert.False(tools.IsHitTestVisible);
        Assert.All(buttons, button => Assert.True(button.IsVisible && button.Bounds.Width > 0, "the icon buttons keep their room"));
        Assert.All(buttons, button => Assert.Equal(PresetRowIcons.PresetButtonSize, button.Bounds.Width, 1));
        Assert.All(buttons, button => Assert.Equal(DialogMetrics.PresetIconSize, Assert.IsType<Path>(button.Content).Width));
        var first = buttons[0];
        var second = buttons[1];
        var gap = second.TranslatePoint(default, user)!.Value.X - (first.TranslatePoint(default, user)!.Value.X + first.Bounds.Width);
        Assert.Equal(PresetRowIcons.ButtonGap, gap, 1);
        var right = second.TranslatePoint(new Point(second.Bounds.Width, 0), user)!.Value.X;
        Assert.Equal(user.Bounds.Width - 2, right, 1);   // the row's own 2 DIP right padding
        var name = user.GetLogicalDescendants().OfType<TextBlock>().First(text => text.Text?.Contains("A preset") == true);
        Assert.True(name.Bounds.Width > 110, $"the name gets {name.Bounds.Width:0} DIP");
        // The pointer over the row shows the icons; over the pencil its ink takes the accent, over the bin the danger colour; no box is drawn.
        var pencil = Assert.IsType<Path>(first.Content);
        var bin = Assert.IsType<Path>(second.Content);
        var rest = Assert.IsAssignableFrom<SolidColorBrush>(bin.Stroke).Color;
        host.MouseMove(first.TranslatePoint(new Point(first.Bounds.Width / 2, first.Bounds.Height / 2), host)!.Value);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, tools.Opacity);
        Assert.True(tools.IsHitTestVisible);
        Assert.Equal(AvaloniaPalette.ColorOf(AvaloniaPalette.Get("Accent", pencil.ActualThemeVariant == ThemeVariant.Light)), Assert.IsAssignableFrom<SolidColorBrush>(pencil.Fill).Color);
        host.MouseMove(second.TranslatePoint(new Point(second.Bounds.Width / 2, second.Bounds.Height / 2), host)!.Value);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(AvaloniaPalette.ColorOf(AvaloniaPalette.Get("Danger", bin.ActualThemeVariant == ThemeVariant.Light)), Assert.IsAssignableFrom<SolidColorBrush>(bin.Stroke).Color);
        Assert.NotEqual(rest, Assert.IsAssignableFrom<SolidColorBrush>(bin.Stroke).Color);
        host.MouseMove(new Point(500, 500));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, tools.Opacity);
        host.Close();
        window.Close();
    }

    [AvaloniaFact]
    public async Task RenameRefusesABadNameAndReplacesOnlyWhenConfirmed()
    {
        var (window, _, rows, store) = Open();
        Assert.False(await rows.CommitRenameAsync("A", "A."));
        Assert.Equal(PresetRules.Error("A."), rows.Message);
        Assert.Equal(new[] { "A", "B" }, store.Items);

        rows.Confirm = _ => Task.FromResult(false);
        Assert.False(await rows.CommitRenameAsync("A", "B"));
        Assert.Equal(new[] { "A", "B" }, store.Items);

        rows.Confirm = _ => Task.FromResult(true);
        Assert.True(await rows.CommitRenameAsync("A", "B"));
        Assert.Equal(new[] { "B" }, store.Items);
        window.Close();
    }

    [AvaloniaFact]
    public void DeleteIsAtOnceAndUndoPutsThePresetBackWhereItWas()
    {
        var (window, _, rows, store) = Open();
        store.Items.Add("C");
        Assert.NotNull(rows.Delete("B"));
        Assert.Equal(new[] { "A", "C" }, store.Items);   // no question was asked
        Assert.True(rows.Toast.IsShowing);
        Assert.Equal("Deleted \"B\"", rows.Toast.Text);
        rows.Toast.Undo();
        Assert.Equal(new[] { "A", "B", "C" }, store.Items);
        Assert.False(rows.Toast.IsShowing);
        window.Close();
    }

    [AvaloniaFact]
    public void DroppingAGripOnAnotherRowMovesThePresetThere()
    {
        var (window, _, rows, store) = Open();
        store.Items.Add("C");
        rows.DropOn("C", "A");
        Assert.Equal(new[] { "C", "A", "B" }, store.Items);
        rows.DropOn("C", "B");
        Assert.Equal(new[] { "A", "B", "C" }, store.Items);
        rows.DropOn("A", "A");
        Assert.Equal(new[] { "A", "B", "C" }, store.Items);
        window.Close();
    }

    [AvaloniaFact]
    public void RowsAreGroupedBuiltInThenYoursWithAHeaderOnTheFirstOfEach()
    {
        var (window, combo, rows, _) = Open();
        var items = new List<object> { new Row("Hint", null), new Row("Factory 1", null, Group: true), new Row("Factory 2", null, Group: true), new Row("A", "A"), new Row("B", "B") };
        PresetRowText.AppendManage(items);
        combo.ItemsSource = items;
        Assert.Equal(PresetGroup.BuiltIn, rows.GroupOf(items[1]));
        Assert.Equal(PresetGroup.Yours, rows.GroupOf(items[3]));
        Assert.Equal(PresetGroup.None, rows.GroupOf(items[0]));
        Assert.Equal(PresetGroup.None, rows.GroupOf(items[^1]));
        string[] Headers(object item) => rows.View(item).GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").Where(t => t is "Built-in" or "Yours").ToArray();
        Assert.Equal(new[] { "Built-in" }, Headers(items[1]));
        Assert.Empty(Headers(items[2]));
        Assert.Equal(new[] { "Yours" }, Headers(items[3]));
        Assert.Empty(Headers(items[4]));
        window.Close();
    }

    [AvaloniaFact]
    public void ReorderMovesOnePlaceAndStopsAtTheEnds()
    {
        var (window, _, rows, store) = Open();
        store.Items.Add("C");
        rows.Move("C", -1);
        Assert.Equal(new[] { "A", "C", "B" }, store.Items);
        rows.Move("A", -1);
        Assert.Equal(new[] { "A", "C", "B" }, store.Items);
        rows.Move("B", 1);
        Assert.Equal(new[] { "A", "C", "B" }, store.Items);
        window.Close();
    }

    [AvaloniaFact]
    public void ManageRowIsNeverAPreset()
    {
        var (window, combo, rows, _) = Open();
        var manage = combo.Items.Cast<object>().Last();
        Assert.True(PresetRowText.IsManage(manage));
        Assert.False(rows.IsPreset(manage));
        Assert.True(rows.IsPreset(combo.Items.Cast<object>().First(item => item is Row { Name: not null })));
        window.Close();
    }

    [AvaloniaFact]
    public void FxBarBoxEndsWithTheManageRow()
    {
        using var scratch = FxTestKit.ScratchLibrary();
        var window = FxTestKit.Window(FxTestKit.Track(FxChainWindow.SampleChain()));
        window.Show();
        var box = DialogTestKit.Named<ComboBox>(window, "PresetBox");
        Assert.NotNull(box.ItemTemplate);
        Assert.Equal(PresetRowText.Manage, box.Items.Cast<object>().Last().ToString());
        window.Close();
    }

    [AvaloniaFact]
    public void ContainerBoxEndsWithTheManageRow()
    {
        using var scratch = FxTestKit.ScratchLibrary();
        var owner = new Window { Width = 420, Height = 160 };
        var bar = new StackPanel();
        var presets = new FxContainerPresets(owner, bar, () => null, () => new RigPreset(), _ => { }, _ => { }, _ => true,
            () => Task.CompletedTask, () => new FxChainDialogs());
        presets.Fill();
        owner.Content = bar;
        owner.Show();
        var box = bar.Children.OfType<ComboBox>().Single(combo => combo.Name == "ContainerPresetBox");
        Assert.NotNull(box.ItemTemplate);
        Assert.True(PresetRowText.IsManage(((IEnumerable<object>)box.ItemsSource!).Last()));
        owner.Close();
    }

    [AvaloniaFact]
    public void MidiBoxesEndWithTheManageRow()
    {
        using var scratch = FxTestKit.ScratchLibrary();
        var owner = new Window();
        var bars = new MidiPresetBars(owner, () => new FxChainDialogs(), () => new List<PluginMidiProcessor>(), _ => { }, (_, _) => { });
        bars.Show(new PluginMidiProcessor { Type = "transpose" });
        owner.Content = new StackPanel { Children = { bars.ListBox, bars.ProcessorRow } };
        owner.Show();
        Assert.Equal(PresetRowText.Manage, ((MidiPresetBars.Entry)bars.ListBox.Items.Cast<object>().Last()).Text);
        Assert.Equal(PresetRowText.Manage, ((MidiPresetBars.Entry)bars.ProcessorBox.Items.Cast<object>().Last()).Text);
        owner.Close();
    }

    private static (Window Window, ComboBox Combo, PresetRows Rows, FakeStore Store) Open()
    {
        var store = new FakeStore();
        var window = new Window();
        var combo = new ComboBox();
        var rows = PresetRows.Attach(combo, () => store,
            item => item is Row { Name: not null } row ? row.Name : null,
            () => PresetCatalogKind.FxChain, () => null, isBuiltIn: item => item is Row { Group: true });
        window.Content = combo;
        var items = new List<object> { new Row("Hint", null), new Row("A", "A"), new Row("B", "B") };
        PresetRowText.AppendManage(items);
        combo.ItemsSource = items;
        window.Show();
        return (window, combo, rows, store);
    }

    private sealed class FakeStore : IPresetRestorable
    {
        public List<string> Items { get; } = new() { "A", "B" };
        public event Action? Changed;
        public string FileExtension => ".json";
        public bool Accepts(byte[] bytes) => true;
        public byte[]? Snapshot(string name) => System.Text.Encoding.UTF8.GetBytes(name);
        public void Restore(string name, byte[] bytes, int index) { Items.RemoveAll(existing => existing == name); Items.Insert(Math.Clamp(index, 0, Items.Count), name); Changed?.Invoke(); }
        public IReadOnlyList<string> Names() => Items.ToList();
        public void Rename(string from, string to, bool replace = false)
        {
            if (replace) Items.RemoveAll(name => string.Equals(name, to, StringComparison.OrdinalIgnoreCase) && !string.Equals(name, from, StringComparison.OrdinalIgnoreCase));
            Items[Items.FindIndex(name => string.Equals(name, from, StringComparison.OrdinalIgnoreCase))] = to;
            Changed?.Invoke();
        }
        public void Duplicate(string from, string to) { Items.Insert(Items.IndexOf(from) + 1, to); Changed?.Invoke(); }
        public void Delete(string name) { Items.RemoveAll(existing => string.Equals(existing, name, StringComparison.OrdinalIgnoreCase)); Changed?.Invoke(); }
        public void Move(string name, int index)
        {
            var from = Items.FindIndex(existing => string.Equals(existing, name, StringComparison.OrdinalIgnoreCase));
            if (from < 0) return;
            Items.RemoveAt(from);
            Items.Insert(Math.Clamp(index, 0, Items.Count), name);
            Changed?.Invoke();
        }
    }
}
