using System.Threading.Tasks;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TabForge.Avalonia.Platform;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Presenters;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

public sealed class TabEditingFocusTests
{
    private sealed class Rig
    {
        public readonly Window Window = new() { Width = 1100, Height = 850 };
        public readonly ScorePreviewView Score = new();
        public readonly Button Other = new() { Content = "Other" };
        public readonly TextBox Text = new();
        public readonly TransportBarView Transport = new();
        public readonly MainWindowTabs Tabs;
        public readonly EditorPaneSwitch Pane;
        public readonly MainWindowCommands Commands;

        public Rig()
        {
            Tabs = new MainWindowTabs(new SongDocument(() => null) { Project = DemoSongFactory.Create() },
                _ => Task.FromResult(PromptButton.Cancel), _ => Task.FromResult(false));
            var panes = new MainWindowDocumentPanes(Tabs, Score);
            Pane = new EditorPaneSwitch(new ScrollViewer { Content = Score }, Score, Tabs, panes, null, null, () => Window, _ => { });
            var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*") };
            var controls = new StackPanel { Orientation = global::Avalonia.Layout.Orientation.Horizontal };
            controls.Children.Add(Other);
            controls.Children.Add(Transport);
            grid.Children.Add(controls);
            Grid.SetRow(Text, 1);
            grid.Children.Add(Text);
            Grid.SetRow(Pane.Host, 2);
            grid.Children.Add(Pane.Host);
            Window.Content = grid;
            Commands = new MainWindowCommands(Window, () => { });
            Pane.Register(Commands);
            Window.Show();
            Drain();
        }

        public void Drain() { Window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
    }

    [AvaloniaFact]
    public void OpenTrackAndBothModeSwitchPathsRestoreTabKeyboardFocus()
    {
        var rig = new Rig();
        try
        {
            Assert.True(rig.Score.IsFocused);
            rig.Score.Editor.SelectedString = 4;
            rig.Other.Focus();
            rig.Tabs.Active.TrackIndex = 1;
            rig.Drain();
            Assert.True(rig.Score.IsFocused);
            Assert.Equal(4, rig.Score.Editor.SelectedString);

            rig.Pane.SetView(TrackEditorView.PianoRoll);
            rig.Drain();
            Assert.True(rig.Pane.PianoRoll.Surface.IsFocused);
            rig.Other.Focus();
            rig.Pane.SetView(TrackEditorView.Score);
            rig.Drain();
            Assert.True(rig.Score.IsFocused);
            Assert.Equal(4, rig.Score.Editor.SelectedString);

            rig.Other.Focus();
            rig.Tabs.Open(new SongDocument(() => null) { Project = DemoSongFactory.Create(), CursorString = 3 });
            rig.Drain();
            Assert.True(rig.Score.IsFocused);
            Assert.Equal(3, rig.Score.Editor.SelectedString);
        }
        finally { rig.Window.Close(); }
    }

    [AvaloniaFact]
    public void StaffClicksKeepTheTabStringAndTypedFretsWhileTabClicksChooseTheString()
    {
        var rig = new Rig();
        try
        {
            var probe = rig.Score.Probe;
            rig.Score.Editor.SelectedString = 4;
            var x = probe.Layout.GetLayout(probe.Track!).Systems[0].Measures[1].X + 2;
            void Click(double y)
            {
                var point = rig.Score.TranslatePoint(new Point(x * rig.Score.Zoom, y * rig.Score.Zoom), rig.Window)!.Value;
                rig.Window.MouseDown(point, MouseButton.Left);
                rig.Window.MouseUp(point, MouseButton.Left);
            }
            Click(probe.StaffTop(0) + 2 * probe.StaffGap);
            Assert.True(rig.Score.IsFocused);
            Assert.Equal(1, rig.Score.Editor.SelectedMeasure);
            Assert.Equal(4, rig.Score.Editor.SelectedString);
            rig.Window.KeyPress(Key.D7, RawInputModifiers.None, PhysicalKey.Digit7, "7");
            var cell = rig.Score.Editor.SelectedCell;
            Assert.Contains(probe.Track!.Measures[1].Cells[cell].Notes, note => note.StringIndex == 4 && note.Fret == 7);
            Click(probe.TabTop(0) + 2 * probe.StringGap);
            Assert.Equal(2, rig.Score.Editor.SelectedString);
            Click(probe.StaffTop(0));
            Assert.Equal(2, rig.Score.Editor.SelectedString);
        }
        finally { rig.Window.Close(); }
    }

    [AvaloniaFact]
    public void DirectTransportButtonReturnsKeyboardAndEditingRoutingToTab()
    {
        var rig = new Rig();
        try
        {
            rig.Score.Editor.SelectedString = 4;
            var stop = rig.Transport.GetVisualDescendants().OfType<TransportBarView.TransportButton>().Single(button => button.Item.Name == "Stop playback");
            stop.IsEnabled = true;
            var stopped = false;
            stop.Pressed = () => stopped = true; // direct session buttons do not dispatch a window command
            rig.Tabs.Active.Selection.SetFocus(FocusTarget.TrackRow);
            rig.Other.Focus();
            var point = stop.TranslatePoint(new Point(10, 10), rig.Window)!.Value;
            rig.Window.MouseDown(point, MouseButton.Left);
            rig.Window.MouseUp(point, MouseButton.Left);
            rig.Drain();
            Assert.True(stopped);
            Assert.True(rig.Score.IsFocused);
            Assert.Equal(FocusTarget.ScoreSelection, rig.Tabs.Active.Selection.Focus);
            Assert.Equal(4, rig.Score.Editor.SelectedString);
        }
        finally { rig.Window.Close(); }
    }

    [AvaloniaFact]
    public async Task TransportBarCommandsAndDialogReturnRestoreTabButDoNotStealTextInput()
    {
        var rig = new Rig();
        try
        {
            rig.Score.Editor.SelectedString = 4;
            rig.Commands.Add("Transport.Stop", () => { });
            rig.Commands.Add("Bar.Next", () => rig.Score.Editor.SetPosition(1, 0, rig.Score.Editor.SelectedString));
            foreach (var id in new[] { "Transport.Stop", "Bar.Next" })
            {
                rig.Other.Focus();
                rig.Commands.Run(id);
                rig.Drain();
                Assert.True(rig.Score.IsFocused);
                Assert.Equal(4, rig.Score.Editor.SelectedString);
            }
            var dialogText = new TextBox();
            var dialog = new Window { Content = dialogText };
            var closed = dialog.ShowDialog(rig.Window);
            dialogText.Focus();
            rig.Drain();
            Assert.True(dialogText.IsFocused);
            dialog.Close();
            await closed;
            rig.Drain();
            Assert.True(rig.Score.IsFocused);
            Assert.Equal(4, rig.Score.Editor.SelectedString);

            rig.Text.Focus();
            rig.Pane.RestoreEditingFocus();
            rig.Drain();
            Assert.True(rig.Text.IsFocused);
        }
        finally { rig.Window.Close(); }
    }
}
