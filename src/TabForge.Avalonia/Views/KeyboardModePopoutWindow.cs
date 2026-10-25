using System;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Transport;
using TabForge.Documents;
using TabForge.KeyboardMode;
using TabForge.Models;
using TabForge.Presenters;

namespace TabForge.Avalonia.Views;

// Owns: the Keyboard mode pop-out: the keyboard view in its own window, with a full screen button. F11 toggles full screen and Esc leaves it.
//   With a Learn engine the bar drives the shared engine (the pane and the pop-out stay in sync: wait, hands, speed, loop, track, MIDI, look-ahead);
//   the strip feed is the engine's run. Without one the window shows the state it is given (ShowPlayAlong).
// Does not own: the texts and key rules (KeyboardModePopoutPresenter), the control bar (KeyboardModeBar), the falling-notes view (KeyboardStripView in
//   KeysHost), the session or MIDI: the window shows the state the engine holds.
// The preview has no Keyboard mode, so it opens this window with Keyboard mode on (KeyboardModePopoutPresenter.CanOpen).
// Tests: TabForge.Avalonia.Tests.KeyboardModePopoutWindowTests, TabForge.Avalonia.Tests.KeyboardLearnEngineTests.
public partial class KeyboardModePopoutWindow : Window
{
    public KeyboardModePopoutWindow()
    {
        InitializeComponent();
        ShowInTaskbar = false;
        Title = KeyboardModePopoutPresenter.Title;
        AutomationProperties.SetName(this, "Keyboard mode keyboard view");
        AutomationProperties.SetName(FullScreenButton, "Toggle full screen");
        ToolTip.SetTip(FullScreenButton, KeyboardModePopoutPresenter.FullScreenTooltip);
        FullScreenButton.Click += (_, _) => ToggleFullScreen();
        Closed += (_, _) => { _binding?.Dispose(); _binding = null; _engine?.SetPopoutOpen(false); };
        Bar = new KeyboardModeBar(BarPanel);
        BarPanel.Children.Remove(FullScreenButton);
        FullScreenButton.Margin = new Thickness(12, 1, 1, 1);
        BarPanel.Children.Add(FullScreenButton);
        Bar.Drive(Strip);
        KeysHost.Children.Add(Strip);
        SyncFullScreenText();
    }

    public KeyboardModeBar Bar { get; }

    public KeyboardStripView Strip { get; } = new();

    /// <summary>Shows a play-along state: the notes, judge, score and feedback of <paramref name="state"/> at its moment, the song progress, and the track on the bar.</summary>
    public void ShowPlayAlong(KeyboardPlayAlongStub state, string trackName)
    {
        Strip.Source = state.Source;
        Strip.Judge = state.Judge;
        Strip.Score = state.Score;
        Strip.Feedback = state.Feedback;
        Strip.Position = new FixedKeyboardSongPosition(state.NowMs);
        Strip.Progress = state.Timeline.TotalMs > 0 ? state.NowMs / state.Timeline.TotalMs : 0;
        Bar.SetTracks(new[] { trackName }, 0);
        Bar.HasFingers = state.Source.HasFingers;
        Strip.InvalidateVisual();
    }

    private KeyboardSessionBinding? _binding;
    private KeyboardLearnEngine? _engine;

    /// <summary>Follows <paramref name="session"/> of <paramref name="document"/> through the shared <paramref name="engine"/> (null unbinds):
    /// this bar and the docked pane's bar drive the same run and stay in sync.</summary>
    public void BindPlayback(AvaloniaPlaybackSession? session, SongDocument document, KeyboardLearnEngine engine)
    {
        _binding?.Dispose();
        _binding = null;
        _engine?.SetPopoutOpen(false);
        _engine = engine;
        Bar.SetTracks(document.Project.Tracks.Select(t => t.Name), Math.Max(0, document.TrackIndex));
        if (session is not null)
        {
            engine.SetPopoutOpen(true);
            _binding = new KeyboardSessionBinding(Strip, Bar, session, () => document, engine);
        }
        else
        {
            engine.SetPopoutOpen(false);
        }
    }

    /// <summary>True while the window covers the screen.</summary>
    public bool IsFullScreen => WindowState == WindowState.FullScreen;

    public void ToggleFullScreen() => WindowState = IsFullScreen ? WindowState.Normal : WindowState.FullScreen;

    // The button text follows the window state, however it changed (the button, F11 or the title bar).
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == WindowStateProperty && FullScreenButton is not null) SyncFullScreenText();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (KeyboardModePopoutPresenter.TogglesFullScreen(e.Key == Key.F11, e.Key == Key.Escape, IsFullScreen))
        {
            ToggleFullScreen();
            e.Handled = true;
        }
    }

    private void SyncFullScreenText() => FullScreenButton.Content = KeyboardModePopoutPresenter.FullScreenButtonText(IsFullScreen);
}
