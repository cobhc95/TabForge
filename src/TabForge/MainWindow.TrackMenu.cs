using System.Windows;
using TabForge.Controllers;
using TabForge.Documents;
using TabForge.Services;
using TabForge.Views;

namespace TabForge;

// MainWindow: the track row's right-click menu and the track-row hotkeys (Ctrl+C/X/V/D, Delete while a track row has the focus).
// The behaviour is TrackClipboardFlow; the menu contents are TrackRowMenus; this is the window's wiring and refresh around them.
public partial class MainWindow
{
    private TrackClipboardFlow? _trackFlow;

    private TrackClipboardFlow TrackFlow => _trackFlow ??= new TrackClipboardFlow(new TrackClipboardHost(this), ClipboardService.Shared, _trackController);

    private void HookTrackRowMenu() => Arrangement.TrackRowMenuRequested += (_, index) => ShowTrackRowMenu(index);

    /// <summary>True while a track row of the arrangement's list, or the mixer table, has the keyboard focus.</summary>
    private bool TrackListFocused => Arrangement.TrackRowFocused || TrackMixerGrid.IsKeyboardFocusWithin;

    /// <summary>The track-row command a key runs right now (null: the key keeps its score or timeline meaning).</summary>
    private bool TryRunTrackRowHotkey(string gesture) =>
        TrackClipboardFlow.Route(TrackListFocused, ClipContextActive, _hotkeys.TrackRow, gesture) is { } id
        && TrackFlow.RunHotkey(id, TrackMixerGrid.SelectedIndex);

    internal System.Windows.Controls.ContextMenu TrackRowMenu(int index)
    {
        var track = _project.Tracks[index];
        var state = new TrackRowMenuState(track.IsAudio, TrackFlow.CanPaste, _project.Tracks.Count > 1, track.ColorHex);
        return SpecMenus.New("Track options", TrackRowMenus.Build(state, MenuKey), spec => RunTrackRowCommand(spec, index), Arrangement);
    }

    private void ShowTrackRowMenu(int index)
    {
        if (index < 0 || index >= _project.Tracks.Count) return;
        SpecMenus.Open(TrackRowMenu(index), Arrangement, null, false);
    }

    private void RunTrackRowCommand(MenuSpec spec, int index)
    {
        switch (spec.Id)
        {
            case TrackRowMenus.Rename: Arrangement.BeginTrackRename(index); break;
            case TrackRowMenus.Colour when spec.Arg is { } hex: Arrangement.ApplyTrackColour(index, hex); break;
            case TrackRowMenus.Properties: TrackMixerGrid.SelectedIndex = index; TrackProps_Click(this, new RoutedEventArgs()); break;
            case TrackRowMenus.Convert: ConvertAudioTrack(index); break;
            case { } id: TrackFlow.RunHotkey(id, index); break;
        }
    }

    private sealed class TrackClipboardHost : ITrackClipboardHost
    {
        private readonly MainWindow _window;
        public TrackClipboardHost(MainWindow window) => _window = window;
        public DocumentSession Document => _window.Doc;
        public bool ConfirmDeleteTrack(string trackName) => DeleteTrackPrompt.Ask(_window, trackName);
        public bool ConfirmConvertToAudio(string trackName) => ConvertTrackPrompts.AskToAudio(_window, trackName);
        public void SetStatus(string text) => _window.StatusText.Text = text;

        public void TracksChanged(int selectIndex, string status)
        {
            _window.FinishTrackListChange(selectIndex); _window.Editor.InvalidateScoreLayout();
            _window.StatusText.Text = status;
        }
    }
}
