using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using TabForge.Audio;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Playback;
using TabForge.Avalonia.Transport;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Features.Recording.Rules;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the recording wiring of batch 1: the first (dock-built) arrangement's arm toggle, armed-row input and monitor edits and input meter
// reach the engine; Stop/Space/Esc end a recording; a refused start leaves the recording off and shows the status; the Record button
//   shows the REC state.
// Does not own: the recording itself and its takes (TransportRecordingTests, MidiTakeCaptureTests), the live overlay or lanes (later batches),
//   the pointer geometry (TimelineInputController, ArrangementClicks).
// Tests: this class.
public class RecordingBatchOneTests
{
    private const int ArmedRow = 2;

    /// <summary>A window over the demo song with a fake engine client (no process) and a fake playback engine; the client is running for the
    /// arm/record booking, and the sessions create a real session object per document so Transport Record has one.</summary>
    private static (MainWindow Window, SongDocument Song, AudioEngineClient Client, AvaloniaPlaybackSessions Sessions) OpenWindow()
    {
        var client = AvaloniaPlaybackComposition.CreateEngineClient(new TabForge.Plugins.PluginTrust());
        client.AttachFakeForTest();
        var playbackEngine = new AvaloniaPlaybackSessionTests.FakeEngine();
        var sessions = new AvaloniaPlaybackSessions(new AvaloniaPlaybackSessionTests.FakeUiThread(), () => playbackEngine) { Engine = client };
        var song = new SongDocument(() => null) { Project = DemoSongFactory.Create() };
        var window = new MainWindow(first: song, playback: sessions);
        window.Show();
        window.UpdateLayout();
        return (window, song, client, sessions);
    }

    private static ArrangementView FirstArrangement(MainWindow window) => window.GetLogicalDescendants().OfType<ArrangementView>().First();

    private static void ClickInMain(MainWindow main, ArrangementView arrangement, Rect cell)
    {
        var point = arrangement.TranslatePoint(cell.Center, main) ?? throw new InvalidOperationException("The arrangement is not in the main window.");
        main.MouseDown(point, MouseButton.Left);
        main.MouseUp(point, MouseButton.Left);
    }

    private static void ClickCell(MainWindow main, ArrangementView arrangement, int row, TrackRowCells.Cell cell) =>
        ClickInMain(main, arrangement, ArrangementClicks.CellInWindow(arrangement, row, cell));

    private static void PressKey(MainWindow main, Key key, KeyModifiers modifiers = KeyModifiers.None)
    {
        var score = main.GetLogicalDescendants().OfType<ScorePreviewView>().Single();
        score.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers, Source = score });
    }

    private static TransportBarView.TransportButton RecordButton(MainWindow main) =>
        main.GetVisualDescendants().OfType<TransportBarView.TransportButton>().First(b => b.Item.CommandId == MainWindowTransportRecord.CommandId);

    private static StatusNoticeBarView Notice(MainWindow main) =>
        // All notifications appear on the single top bar (N1): the first visible notice is the top bar; the bottom rows stay hidden tombstones.
        main.GetLogicalDescendants().OfType<StatusNoticeBarView>().First(v => v.IsVisible);

    private static string NoticeText(MainWindow main) =>
        Notice(main).GetLogicalDescendants().OfType<TextBlock>().First(t => t.Name == "NoticeText").Text ?? "";

    [AvaloniaFact]
    public void FirstArrangementArmAndInputEditsReachTheEngineAndTheMeterIsWired()
    {
        var (window, song, client, sessions) = OpenWindow();
        var arrangement = FirstArrangement(window);
        var track = song.Project.Tracks[ArmedRow];
        var sent = new List<(int Slot, bool Armed, int Mode, bool Monitor)>();
        client.ArmSentForTest = (slot, armed, mode, monitor) => sent.Add((slot, armed, mode, monitor));

        // The record hooks attach to the arrangement the dock built before the window registered them: the meter source is set on that pane.
        Assert.NotNull(arrangement.ArmedRows.InputLevelOf);

        ClickCell(window, arrangement, ArmedRow, TrackRowCells.Cell.Arm);
        window.UpdateLayout();

        var slot = client.SlotOf(track);
        Assert.True(slot >= 0);
        Assert.Contains(sent, a => a.Slot == slot && a.Armed && a.Mode == AudioInputs.WireMode(track.AudioInput) && a.Monitor);

        // The monitor toggle and the input picker of the armed sub-row both sync the engine.
        sent.Clear();
        var monitor = arrangement.ArmedRows.Children.OfType<ToggleButton>().Single();
        monitor.IsChecked = false;
        Assert.Contains(sent, a => a.Slot == slot && a.Armed && !a.Monitor);

        sent.Clear();
        var picker = arrangement.ArmedRows.Children.OfType<ComboBox>().Single();
        picker.SelectedItem = picker.ItemsSource!.Cast<AudioInputChoice>().First(c => c.Id == AudioInputs.Input2);
        Assert.Contains(sent, a => a.Slot == slot && a.Armed && a.Mode == AudioInputs.WireMode(AudioInputs.Input2));

        // A layout reset re-lays the dock but reuses the one arrangement pane (Perf E2b), so its record hooks stay attached.
        sent.Clear();
        window.DockWorkspace.ResetAllPanels();
        window.UpdateLayout();
        var later = FirstArrangement(window);
        Assert.Same(arrangement, later);
        Assert.NotNull(later.ArmedRows.InputLevelOf);
        Assert.True(track.RecordArm);
        ClickCell(window, later, ArmedRow, TrackRowCells.Cell.Arm);
        Assert.Contains(sent, a => a.Slot == slot && !a.Armed);

        song.MarkClean();
        window.Close();
        sessions.Dispose();
        client.Dispose();
    }

    [AvaloniaFact]
    public void SpaceEndsARecordingAndRestoresTheRecordButton()
    {
        var (window, song, client, sessions) = OpenWindow();
        var arrangement = FirstArrangement(window);
        var session = sessions.For(song)!;
        song.CursorBar = 1;
        song.CursorCell = 0;

        ClickCell(window, arrangement, ArmedRow, TrackRowCells.Cell.Arm);
        PressKey(window, Key.R, KeyModifiers.Control);
        Assert.True(window.RecordingHooks.IsRecording);
        Assert.True(RecordButton(window).Lit);

        PressKey(window, Key.Space);

        Assert.False(window.RecordingHooks.IsRecording);
        Assert.False(session.IsPlaying);
        Assert.False(RecordButton(window).Lit);
        Assert.Equal(1, song.CursorBar);

        song.MarkClean();
        window.Close();
        sessions.Dispose();
        client.Dispose();
    }

    [AvaloniaFact]
    public void SpaceEndsARecordingWithTheInputPickerFocused()
    {
        var (window, song, client, sessions) = OpenWindow();
        var arrangement = FirstArrangement(window);
        var session = sessions.For(song)!;
        ClickCell(window, arrangement, ArmedRow, TrackRowCells.Cell.Arm);
        PressKey(window, Key.R, KeyModifiers.Control);
        Assert.True(window.RecordingHooks.IsRecording);

        // WPF blocks Space for text inputs only: a focused combo box never keeps it from ending the recording.
        var picker = arrangement.ArmedRows.Children.OfType<ComboBox>().Single();
        picker.Focus();
        window.UpdateLayout();
        Assert.True(picker.IsFocused);

        PressKey(window, Key.Space);

        Assert.False(window.RecordingHooks.IsRecording);
        Assert.False(session.IsPlaying);
        Assert.False(RecordButton(window).Lit);

        song.MarkClean();
        window.Close();
        sessions.Dispose();
        client.Dispose();
    }

    [AvaloniaFact]
    public void StopWhileRecordingEndsItAndLeavesTheTransportStopped()
    {
        var (window, song, client, sessions) = OpenWindow();
        var arrangement = FirstArrangement(window);
        var session = sessions.For(song)!;
        ClickCell(window, arrangement, ArmedRow, TrackRowCells.Cell.Arm);
        PressKey(window, Key.R, KeyModifiers.Control);
        Assert.True(window.RecordingHooks.IsRecording);

        PressKey(window, Key.OemPeriod, KeyModifiers.Control);   // Transport.Stop (Ctrl+OemPeriod)

        Assert.False(window.RecordingHooks.IsRecording);
        Assert.False(session.IsPlaying);
        Assert.False(RecordButton(window).Lit);

        song.MarkClean();
        window.Close();
        sessions.Dispose();
        client.Dispose();
    }

    [AvaloniaFact]
    public void EscapeEndsARecordingAndStopsThePlayback()
    {
        var (window, song, client, sessions) = OpenWindow();
        var arrangement = FirstArrangement(window);
        var session = sessions.For(song)!;
        ClickCell(window, arrangement, ArmedRow, TrackRowCells.Cell.Arm);
        PressKey(window, Key.R, KeyModifiers.Control);
        Assert.True(window.RecordingHooks.IsRecording);

        PressKey(window, Key.Escape);

        Assert.False(window.RecordingHooks.IsRecording);
        Assert.False(session.IsPlaying);
        Assert.False(RecordButton(window).Lit);

        song.MarkClean();
        window.Close();
        sessions.Dispose();
        client.Dispose();
    }

    [AvaloniaFact]
    public void RecordWithNothingArmedLeavesItNotRecordingAndShowsTheWpfStatus()
    {
        var (window, song, client, sessions) = OpenWindow();
        var session = sessions.For(song)!;

        PressKey(window, Key.R, KeyModifiers.Control);

        Assert.False(window.RecordingHooks.IsRecording);
        Assert.False(session.IsPlaying);
        Assert.False(RecordButton(window).Lit);
        Assert.Equal(TransportRecording.NothingArmedText, NoticeText(window));

        song.MarkClean();
        window.Close();
        sessions.Dispose();
        client.Dispose();
    }

    [Fact]
    public void RecordWithOnlyAMidiTrackAndNoMidiDeviceRefusesTheStart()
    {
        var client = new AudioEngineClient(new TabForge.Plugins.PluginTrust());
        var recording = new TransportRecording(client, new MidiInputHub(new SilentMidiInput()).CreateClient());
        var song = new SongDocument(() => null);
        song.Project.Tracks.Add(new TrackModel { AudioInput = AudioInputs.Midi, RecordArm = true });

        var status = recording.Toggle(song.Project.Tracks, "unused");

        Assert.Equal(TransportRecording.NoMidiDeviceText, status);
        Assert.False(recording.IsRecording);
        recording.Dispose();
        client.Dispose();
    }

    [AvaloniaFact]
    public void TheRecordButtonShowsAndClearsTheRecordingState()
    {
        var bar = new TransportBarView();
        var button = bar.GetVisualDescendants().OfType<TransportBarView.TransportButton>().First(b => b.Item.CommandId == MainWindowTransportRecord.CommandId);

        Assert.False(bar.Recording);
        Assert.False(button.Lit);
        bar.ShowRecording(true);
        Assert.True(bar.Recording);
        Assert.True(button.Lit);
        Assert.NotNull(button.GlowAccent(light: false));
        bar.ShowRecording(false);
        Assert.False(button.Lit);
    }

    /// <summary>A MIDI input whose device never opens, as a machine with no MIDI input (or one another program holds).</summary>
    private sealed class SilentMidiInput : IMidiInputSource
    {
        public event Action<int, int, int, long>? Message { add { } remove { } }
        public bool IsOpen => false;
        public int Open() => 0;
        public void Close() { }
        public void Dispose() { }
    }
}
