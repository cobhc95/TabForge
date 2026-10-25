using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Controls.Presenters;
using Avalonia.VisualTree;
using Avalonia.Controls.Templates;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using TabForge.Avalonia.Views;
using TabForge.Menus;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the Avalonia right-click menus built from the Core rows: a row with no Avalonia handler renders disabled, and a row with a handler runs it.
// Does not own: the rows (the Core menus, pinned by SelfTestMenuGolden), the pixels (ParityShotsTests) or the right-click routing of each view.
// Tests: this file.
public class ContextMenuTests
{
    [AvaloniaFact]
    public void RowsWithoutAHandlerRenderDisabled()
    {
        var rows = TrackRowMenus.Build(new TrackRowMenuState(IsAudio: false, CanPaste: true, CanDelete: true, ColourHex: "#2E6FD8"), _ => "");
        var menu = ContextMenuBuilder.Build(rows, ContextMenuHost.NoCommands);

        var leaves = Leaves(menu.Items).ToList();
        Assert.NotEmpty(leaves);
        Assert.All(leaves, item => Assert.False(item.IsEnabled));
    }

    [AvaloniaFact]
    public void RowWithAHandlerRunsIt()
    {
        var ran = false;
        var rows = new[] { new MenuSpec { Id = TrackRowMenus.Copy, Header = "Copy" } };
        var menu = ContextMenuBuilder.Build(rows, new FakeRunner(TrackRowMenus.Copy, () => ran = true));

        var item = Assert.IsType<MenuItem>(menu.Items[0]);
        Assert.True(item.IsEnabled);
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.True(ran);
    }

    [AvaloniaTheory]
    [InlineData(":pointerover")]
    [InlineData(":selected")]
    [InlineData(":open")]
    public void EveryRowKindShowsTheHoverShadeWhileHighlighted(string pseudoClass)
    {
        var rows = new[]
        {
            new MenuSpec { Id = TrackRowMenus.Copy, Header = "Plain" },
            new MenuSpec { Id = TrackRowMenus.Copy, Header = "Checked", Checkable = true, Checked = true },
            new MenuSpec { Header = "Parent", Children = new() { new MenuSpec { Id = TrackRowMenus.Copy, Header = "Child" } } },
            new MenuSpec { Id = TrackRowMenus.Copy, Header = "Last" },
        };
        var menu = ContextMenuBuilder.Build(rows, ContextMenuHost.NoCommands);
        var panel = new Panel { ContextMenu = menu };
        var window = new Window { Content = panel };
        window.Show();
        menu.Open(panel);
        foreach (var item in menu.Items.OfType<MenuItem>())
        {
            ((IPseudoClasses)item.Classes).Set(pseudoClass, true);
            var root = item.GetVisualDescendants().OfType<Border>().First(b => b.Name == "PART_LayoutRoot");
            var brush = Assert.IsAssignableFrom<ISolidColorBrush>(root.Background);
            Assert.NotEqual(Colors.Transparent, brush.Color);
        }
    }

    [AvaloniaFact]
    public void TimelineCopyAndScorePasteUseTheEditCommandIds()
    {
        Assert.Equal("Edit.Copy", ContextMenuRows.CommandIdOf(new MenuSpec { Command = TimelineCommand.CopySelection }));
        Assert.Equal("Edit.Paste", ContextMenuRows.CommandIdOf(new MenuSpec { Id = ScoreMenus.PasteId }));
        Assert.Equal(ContextMenuRows.SectionGoToId, ContextMenuRows.CommandIdOf(new MenuSpec { Command = TimelineCommand.GoToSection }));
    }

    [AvaloniaFact]
    public void ClipMenuRowsRunTheClipCommands()
    {
        var rows = TimelineMenus.Leaves(TimelineMenus.Clip(new ClipMenuState(HasClip: true, IsMidi: false, CanPaste: true, Muted: false), _ => "")).ToList();
        Assert.Equal("Clip.Mute", ContextMenuRows.CommandIdOf(rows.Single(r => r.Command == TimelineCommand.ClipMute)));
        Assert.Equal("Clip.FadeReset", ContextMenuRows.CommandIdOf(rows.Single(r => r.Command == TimelineCommand.ClipFadeReset)));
        Assert.Equal("Clip.Split", ContextMenuRows.CommandIdOf(rows.Single(r => r.Command == TimelineCommand.ClipSplit)));
        Assert.Equal("Clip.Properties", ContextMenuRows.CommandIdOf(rows.Single(r => r.Command == TimelineCommand.ClipProperties)));
    }

    [AvaloniaFact]
    public void EveryClipRowWithAHandlerIsEnabledAndRunsItAndOnlyTheUnimplementedStayDisabled()
    {
        var handled = new[]
        {
            "Clip.Copy", "Clip.Cut", "Clip.Paste", "Clip.Duplicate", "Clip.Split", "Clip.Glue", "Clip.FadeReset", "Clip.Delete", "Clip.Mute", "Clip.Properties",
        };
        var runner = new MultiRunner(handled);
        var rows = TimelineMenus.Leaves(TimelineMenus.Clip(new ClipMenuState(HasClip: true, IsMidi: true, CanPaste: true, Muted: false), _ => "")).ToList();
        var menu = ContextMenuBuilder.Build(rows, runner);
        var items = Leaves(menu.Items).ToList();

        var enabled = items.Where(item => item.IsEnabled).Select(item => item.Header?.ToString()).ToList();
        Assert.Equal(10, enabled.Count);
        Assert.Contains("Split at cursor", enabled);
        Assert.Contains("Properties…", enabled);
        Assert.Contains("Duplicate", enabled);
        Assert.Contains("Delete", enabled);
        Assert.Contains("Glue", enabled);
        Assert.Contains("Mute", enabled);
        var disabled = items.Where(item => !item.IsEnabled).Select(item => item.Header?.ToString()).ToList();
        Assert.Equal(new[] { "Write into the track's notation" }, disabled);   // the only clip row with no Avalonia handler

        foreach (var item in items.Where(item => item.IsEnabled)) item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.Equal(10, runner.Ran.Count);
        Assert.Contains("Clip.Split", runner.Ran);
        Assert.Contains("Clip.Properties", runner.Ran);

        var emptyLane = Leaves(ContextMenuBuilder.Build(
            TimelineMenus.Leaves(TimelineMenus.Clip(new ClipMenuState(HasClip: false, IsMidi: false, CanPaste: true, Muted: false), _ => "")).ToList(), runner).Items)
            .ToList();
        Assert.Contains(emptyLane, item => item.Header?.ToString() == "Add audio file…" && !item.IsEnabled);   // no handler yet
    }

    [AvaloniaFact]
    public void TimelineSettingsDoorsRunTheTimelineSettingsCommand()
    {
        Assert.Equal(TrackListMenus.SettingsId, ContextMenuRows.CommandIdOf(new MenuSpec { Command = TimelineCommand.TimelineSettings }));
        Assert.Equal(TrackListMenus.SettingsId, ContextMenuRows.CommandIdOf(new MenuSpec { Command = TimelineCommand.SectionSettings }));
    }

    [AvaloniaFact]
    public void BarInsertRowsRunAtTheClickedBar()
    {
        Assert.Equal(ContextMenuRows.InsertBarBeforeId, ContextMenuRows.CommandIdOf(new MenuSpec { Command = TimelineCommand.InsertBarBefore }));
        Assert.Equal(ContextMenuRows.InsertBarAfterId, ContextMenuRows.CommandIdOf(new MenuSpec { Command = TimelineCommand.InsertBarAfter }));
        var host = new ContextMenuHost { ClickedBar = 3 };
        Assert.Equal(3, host.ClickedBar);
    }

    [AvaloniaFact]
    public void PasteRowStaysDisabledWithoutAClipboard()
    {
        var host = new ContextMenuHost { Commands = new FakeRunner("Edit.Paste", () => { }) };
        Assert.False(host.CanPaste);
        var menu = ContextMenuBuilder.Build(new[] { new MenuSpec { Command = TimelineCommand.PasteSelection, Header = "Paste", Enabled = host.CanPaste } }, host.Commands);
        Assert.False(Assert.IsType<MenuItem>(menu.Items[0]).IsEnabled);
    }

    /// <summary>A command runner that handles one id.</summary>
    private sealed class FakeRunner : IMenuCommandRunner
    {
        private readonly string _id;
        private readonly Action _run;

        public FakeRunner(string id, Action run) => (_id, _run) = (id, run);

        public bool Handles(string commandId) => commandId == _id;

        public void Run(string commandId, string? argument) => _run();
    }

    /// <summary>A command runner that handles a set of ids and records every run.</summary>
    private sealed class MultiRunner : IMenuCommandRunner
    {
        private readonly HashSet<string> _handled;
        public List<string> Ran { get; } = new();
        public MultiRunner(IEnumerable<string> handled) => _handled = new HashSet<string>(handled, StringComparer.Ordinal);
        public bool Handles(string commandId) => _handled.Contains(commandId);
        public void Run(string commandId, string? argument) => Ran.Add(commandId);
    }

    /// <summary>The menu items that hold no submenu of their own, depth first.</summary>
    private static IEnumerable<MenuItem> Leaves(IEnumerable items)
    {
        foreach (var item in items.OfType<MenuItem>())
        {
            if (item.Items.OfType<MenuItem>().Any())
                foreach (var leaf in Leaves(item.Items)) yield return leaf;
            else yield return item;
        }
    }
}
