using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using Microsoft.Win32;
using TabForge.Controllers;
using TabForge.Docking;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Plugins;
using TabForge.Presets;
using TabForge.Services;
using TabForge.Shell;
using TabForge.Views;
using TabForge.Visualization;

namespace TabForge;

// MainWindow, window-level keyboard handling and closing.
public partial class MainWindow
{
    // ---------- keyboard ----------
    // Global routing: numpad + tab keys work even when the mixer grid or
    // buttons have focus. Text editing (TextBox) is never stolen.

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var mods = Keyboard.Modifiers;
        var inText = IsEditableTextInput(e.OriginalSource) || IsEditableTextInput(Keyboard.FocusedElement);
        var gesture = WpfHotkeyGestureAdapter.FromEvent(e);

        // Shift+F10 / the Menu key on the score: the keyboard way to its context menu (F10 arrives as a "system" key).
        var rawKey = e.Key == Key.System ? e.SystemKey : e.Key;
        if (!inText && ScoreScroll.IsKeyboardFocusWithin && ((rawKey == Key.Apps && mods == ModifierKeys.None) || (rawKey == Key.F10 && mods == ModifierKeys.Shift)))
        {
            if (Editor.RequestContextMenuAtCaret()) e.Handled = true;
            return;
        }

        // Route Space at the window level rather than relying on whichever editor/control has focus.
        // Read-only labels are not text-entry fields; an actual editable field keeps normal spaces.
        if (!inText && e.Key == Key.Space && (mods == ModifierKeys.None || mods == ModifierKeys.Shift))
        {
            if (!e.IsRepeat)
            {
                if (IsRecording) ToggleRecording();   // Space ends a recording (and playback, if Record started it)
                else if (_hotkeyMap.TryGetValue(gesture, out var spaceAction) && RunHotkey(spaceAction)) { }
                else if (mods == ModifierKeys.Shift) PlayFromStart();
                else TogglePlayback();
            }
            e.Handled = true;
            return;
        }

        // Standard zoom shortcuts take priority over the editor's legacy Ctrl+plus/minus
        // insert/delete-beat bindings. Shift is accepted for the main keyboard's '+' key.
        if (!inText && mods.HasFlag(ModifierKeys.Control) && !mods.HasFlag(ModifierKeys.Alt)
            && e.Key is Key.OemPlus or Key.Add or Key.OemMinus or Key.Subtract)
        {
            ZoomBy(e.Key is Key.OemPlus or Key.Add ? 1 : -1);
            e.Handled = true;
            return;
        }

        // A selected clip (or a clicked clip lane) owns its keys first: Delete, arrows, Esc, Ctrl+C/X/V/D...
        if (!inText && ClipContextActive && _clipHotkeyMap.TryGetValue(gesture, out var clipAction) && RunClipHotkey(clipAction))
        {
            e.Handled = true;
            return;
        }

        if (!inText && e.Key == Key.Escape)
        {
            if (IsRecording)
            {
                ToggleRecording();   // Esc ends the recording, stops playback and returns to where it began
                e.Handled = true;
                return;
            }
            // First Esc clears a selected area; the loop button stays on and loops the song again.
            if (_selection.HasRange || _loopHasArea || Editor.HasSelection)
            {
                _selection.Clear(SelectionOrigin.Command);   // clears score, timeline and area together
                if (Editor.HasSelection) Editor.ClearSelection();
                RefreshArrangementSelection();
                StatusText.Text = _loop ? "Selection cleared - looping the song" : "Selection cleared";
                e.Handled = true;
                return;
            }
            if (_midi.IsPlaying || _isPlayingVisual)
            {
                StopPlayback();
            }
            else
            {
                Editor.PlaybackActive = false;
                Editor.ClearPlayhead();
                Playhead.SetGeometry(null);
                Playhead.SetDurationGeometry(null);
                Playback.ClearPending();
                _playheadBar = -1;
                SyncArrangementPlayhead();
            }
            Editor.ClearSelection();
            RefreshToolsPalette();
            e.Handled = true;
            return;
        }

        // While a text box has focus only the universally-expected application shortcuts are kept
        // (file/print/tab). Everything else belongs to the text box.
        if (inText)
        {
            if (_hotkeyMap.TryGetValue(gesture, out var textAction)
                && HotkeyCatalog.AllowsInTextBox(textAction)
                && RunHotkey(textAction))
                e.Handled = true;
            return;
        }

        // A focused grid owns the arrow keys.
        var gridOwnsArrows = TrackMixerGrid.IsKeyboardFocusWithin || MarkerList.IsKeyboardFocusWithin
            || ChordResultList.IsKeyboardFocusWithin ;
        var isArrow = e.Key is Key.Left or Key.Right or Key.Up or Key.Down;
        if (isArrow && gridOwnsArrows && mods == ModifierKeys.None) return;

        // A bound Ctrl / Alt chord, then the editor's note entry and score navigation, then the other bindings (the map is
        // rebuilt whenever the hotkey settings change). Alt chords arrive as Key.System; the router reads the real key.
        var target = WindowKeyRouter.Dispatch(e.Key, e.SystemKey, mods, Editor, _hotkeyMap, RunHotkey);
        if (target == WindowKeyRouter.Target.Editor)
        {
            if (!Keyboard.IsKeyToggled(Key.NumLock) && rawKey is Key.Insert or Key.End or Key.Down or Key.Next or Key.Left or Key.Clear or Key.Right or Key.Home or Key.Up or Key.Prior)
                StatusText.Text = "Note entered — turn NumLock ON to use the numeric keypad";
            e.Handled = true;
        }
        else if (target == WindowKeyRouter.Target.Hotkey) e.Handled = true;
    }

    private static bool IsEditableTextInput(object? source)
    {
        var element = source as DependencyObject;
        while (element is not null)
        {
            if (element is TextBoxBase textBox) return !textBox.IsReadOnly;
            if (element is PasswordBox) return true;
            element = VisualTreeHelper.GetParent(element);
        }
        return false;
    }

    /// <summary>Moves the track selection by <paramref name="delta"/> (Ctrl+Shift+Up/Down).</summary>
    private void SelectTrack(int delta)
    {
        var count = _project.Tracks.Count;
        if (count == 0) return;
        var index = Math.Clamp(TrackMixerGrid.SelectedIndex + delta, 0, count - 1);
        TrackMixerGrid.SelectedIndex = index;
        ScrollToCursor();
        Editor.Focus();
    }

    private MessageBoxResult ShowSaveChangesConfirmation(string message)
    {
        var dialog = new ThemedConfirmDialog("TabForge", message) { Owner = this };
        return DialogHost.ShowModal(dialog) == true ? dialog.Result : MessageBoxResult.Cancel;
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        // Never waits here: a running or requested save cancels this close and closes again when it is done (MainWindow.File.cs).
        // Closing only decides (it can be cancelled here or by any other handler). The window's playback, observers and attachments to
        // longer-lived objects are released in Closed (ReleaseWindowResources), so a cancelled close leaves the window fully working.
        if (!ConfirmWindowClose()) e.Cancel = true;
    }

    private void SetPlayIcon(bool playing)
    {
        PlayIcon.Icon = playing ? "pause" : "play";
        RefreshPlayingIndicators();
    }

    private void RefreshPlayingIndicators() => Tabs.SetPlayingDocuments(
        _documents.Documents.Where(session => session.Playback.Engine.IsPlaying && !session.Playback.Engine.IsPaused));

    private void ZoomInButton_Click(object sender, RoutedEventArgs e) => ZoomBy(1);
    private void ZoomOutButton_Click(object sender, RoutedEventArgs e) => ZoomBy(-1);

    // The instant flag: the exact content check (serialises the whole song) runs after undo/redo and
    // before closing, not on every edit.
    /// <summary>What an edit needs redrawn; <see cref="CommitEdit"/> marks the song changed and applies it.</summary>
    [Flags]
    private enum EditRefresh
    {
        None = 0, Score = 1, Repaint = 2, Arrangement = 4, TimelineGeometry = 8,
        Palette = 16, Status = 32, Instrument = 64, Markers = 128,
    }

    /// <summary>The one ending for a model edit made from the window: dirty flag, the requested refreshes, title.</summary>
    private void CommitEdit(EditRefresh refresh)
    {
        _project.IsDirty = true;
        _project.MarkTimelineChanged();   // A5-08: every window edit ends here; timing caches rebuild once
        RefreshAfterEdit(refresh);
    }

    /// <summary>The view's half of an edit that already went through <see cref="DocumentEdits"/> (which marked the song changed): redraws only.</summary>
    private void RefreshAfterEdit(EditRefresh refresh)
    {
        if (refresh.HasFlag(EditRefresh.Status)) RefreshStatus();
        if (refresh.HasFlag(EditRefresh.Markers)) RefreshMarkers();
        if (refresh.HasFlag(EditRefresh.Arrangement)) RefreshArrangement();
        if (refresh.HasFlag(EditRefresh.Score)) Editor.InvalidateScoreLayout();
        else if (refresh.HasFlag(EditRefresh.Repaint)) Editor.InvalidateVisual();
        if (refresh.HasFlag(EditRefresh.TimelineGeometry)) RefreshTimelineOverviewGeometry();
        if (refresh.HasFlag(EditRefresh.Instrument)) RefreshInstrument();
        if (refresh.HasFlag(EditRefresh.Palette)) RefreshToolsPalette();
        UpdateTitle();
    }

    private void UpdateTitle() => Title = $"TabForge - {_project.Title}{(_project.IsDirty ? " *" : "")}{DegradedTitleSuffix}";

    private static string SanitizeFileName(string text) => TabForge.Audio.Contracts.SafeFileNames.SafeFileName(text, "Untitled");
}
