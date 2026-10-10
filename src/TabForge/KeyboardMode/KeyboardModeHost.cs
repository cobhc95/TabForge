using System.Windows;
using TabForge.Audio;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Services;
using TabForge.Views;

namespace TabForge.KeyboardMode;

// Owns: the Keyboard mode pane's window (IKeyboardModeHost): the pane basics forwarded to the window's own IPaneHost and the active document.
// Does not own: the window or the song; it only calls what the window hands it.
// Tests: TestKeyboardModeNoteStream.
internal sealed class KeyboardModeHost : IKeyboardModeHost, IKeyboardModeWaitHost
{
    private readonly IPaneHost _pane;
    private readonly Func<DocumentSession> _document;

    private readonly Action? _togglePlayback;

    /// <param name="togglePlayback">The window's Play / Pause handler: wait mode pauses and resumes through it, so the follow, clock and play icon stay as for a hand-made pause.</param>
    public KeyboardModeHost(IPaneHost pane, Func<DocumentSession> document, AudioEngineClient engine, Action? togglePlayback = null) { _pane = pane; _document = document; Engine = engine; _togglePlayback = togglePlayback; }

    public bool IsPlaying => ActiveDocument.Playback.Engine is { IsPlaying: true, IsPaused: false };
    public void Pause() { if (IsPlaying) _togglePlayback?.Invoke(); }
    public void Resume() { if (ActiveDocument.Playback.Engine is { IsPlaying: true, IsPaused: true }) _togglePlayback?.Invoke(); }

    public Window Window => _pane.Window;
    public AppSettings Settings => _pane.Settings;
    public TabEditorControl Editor => _pane.Editor;
    public SongProject Project => _pane.Project;
    public TrackModel? SelectedTrack => _pane.SelectedTrack;
    public void SaveSettings() => _pane.SaveSettings();
    public void SetStatus(string text) => _pane.SetStatus(text);
    public DocumentSession ActiveDocument => _document();
    public AudioEngineClient Engine { get; }
}
