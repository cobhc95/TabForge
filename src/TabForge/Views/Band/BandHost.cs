using System.Windows;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Services;
using TabForge.Shell;
using TabForge.Visualization;

namespace TabForge.Views.Band;

// Owns: the Band view's window (IBandViewHost): the pane basics forwarded to the window's own IPaneHost, the active song, the instrument options,
//   the cursor a lane click sets, the track move of the order sync and the Band menu's score zoom and settings items.
// Does not own: the window, the zoom, the settings dialog or the song's track order (it only calls what the window hands it); the view itself (BandViewController).
// Tests: TestBandHostForwards.
internal sealed class BandHost : IBandViewHost
{
    private readonly IPaneHost _pane;
    private readonly DocumentManager _documents;
    private readonly AppOptions _options;
    private readonly Func<(bool LeftHanded, bool ShowNoteNames, string? Scale, int Horizon)> _instrument;
    private readonly Action<int, int> _moveTrack;
    private readonly Action<int> _selectTrack;
    private readonly Func<ScoreZoomController> _zoom;
    private readonly Action<string, string?> _openSettings;

    public BandHost(IPaneHost pane, DocumentManager documents, AppOptions options,
        Func<(bool LeftHanded, bool ShowNoteNames, string? Scale, int Horizon)> instrument,
        Action<int, int> moveTrack, Action<int> selectTrack, Func<ScoreZoomController> zoom, Action<string, string?> openSettings)
    {
        _pane = pane; _documents = documents; _options = options; _instrument = instrument;
        _moveTrack = moveTrack; _selectTrack = selectTrack; _zoom = zoom; _openSettings = openSettings;
    }

    public Window Window => _pane.Window;
    public AppSettings Settings => _pane.Settings;
    public TabEditorControl Editor => _pane.Editor;
    public SongProject Project => _pane.Project;
    public TrackModel? SelectedTrack => _pane.SelectedTrack;
    public void SaveSettings() => _pane.SaveSettings();
    public void SetStatus(string text) => _pane.SetStatus(text);

    public DocumentSession ActiveDocument => _documents.Active;
    public (bool LeftHanded, bool ShowNoteNames, string? Scale, int Horizon) InstrumentOptions => _instrument();
    public VisualOptions? Visual => _options.Visual;
    public void MoveSongTrack(int from, int to) => _moveTrack(from, to);

    public void ShowCursor(int trackIndex, int bar, int cell)
    {
        _selectTrack(trackIndex);
        _pane.Editor.SetPosition(bar, cell, 0);
    }

    public void RunScoreMenu(MenuSpec spec)
    {
        switch (spec.Id)
        {
            case ScoreMenus.ZoomInId: _zoom().ZoomBy(1); break;
            case ScoreMenus.ZoomOutId: _zoom().ZoomBy(-1); break;
            case ScoreMenus.FitWidthId: _zoom().ApplyZoomText("Fit width"); break;
            case BandMenus.SettingsId: _openSettings(SettingsCatalog.Timeline, BandMenus.SettingsRow); break;
        }
    }
}
