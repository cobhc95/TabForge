using System.Windows;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Views;

/// <summary>
/// What every pane controller of the main window (layouts, zoom, tool palette, instrument panel, mixer windows) needs from it;
/// the controller's own host interface adds the rest.
/// </summary>
internal interface IPaneHost
{
    /// <summary>The window: owner of dialogs, theme resources, dispatcher, size and state.</summary>
    Window Window { get; }
    AppSettings Settings { get; }
    TabEditorControl Editor { get; }
    SongProject Project { get; }
    TrackModel? SelectedTrack { get; }
    void SaveSettings();
    void SetStatus(string text);
}
