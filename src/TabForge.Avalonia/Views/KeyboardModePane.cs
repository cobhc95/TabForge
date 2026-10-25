using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using System;
using System.Linq;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Transport;
using TabForge.Documents;
using TabForge.Models;

namespace TabForge.Avalonia.Views;

// Owns: the docked Keyboard mode: a header row ("Keyboard view", Pop out, close), the control bar and the falling-notes strip, in the dock's "learn" pane.
//   With a Learn engine the bar drives the engine and the strip shows its run (judge, score, wait-gating, loop), staying in sync with the pop-out's bar;
//   without one the bar only colours the strip.
// Does not own: the bar or the strip (KeyboardModeBar, KeyboardStripView), the pop-out window, the dock, the engine.
// Tests: TabForge.Avalonia.Tests.KeyboardLearnEngineTests.
public sealed class KeyboardModePane : DockPanel
{
    public const string Heading = "Keyboard view", PopOutText = "Pop out", CloseText = "✕";
    private KeyboardSessionBinding? _binding;
    private KeyboardLearnEngine? _engine;

    /// <param name="popOut">Opens the Keyboard mode window.</param>
    /// <param name="close">Closes the pane.</param>
    public KeyboardModePane(Action popOut, Action close)
    {
        _watch.Marked = _ => ShowDocumentTracks();
        _watch.ProjectChanged = (_, _) => ShowDocumentTracks();
        var heading = new TextBlock { Text = Heading, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(11, 0, 0, 0) };
        heading[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextMuted");
        var pop = new Button { Content = PopOutText, Margin = new Thickness(0, 0, 4, 0) };
        pop.Click += (_, _) => popOut();
        var shut = new Button { Content = CloseText, Margin = new Thickness(0, 0, 4, 0) };
        shut.Click += (_, _) => close();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Children = { pop, shut } };
        var top = new DockPanel { Margin = new Thickness(0, 4, 0, 0), Children = { buttons, heading } };
        DockPanel.SetDock(buttons, Dock.Right);
        var bar = new WrapPanel { Margin = new Thickness(6, 3) };
        Bar = new KeyboardModeBar(bar);
        Bar.Drive(Strip);
        SetDock(top, Dock.Top);
        SetDock(bar, Dock.Top);
        Children.Add(top);
        Children.Add(bar);
        Children.Add(Strip);
        this[!BackgroundProperty] = new DynamicResourceExtension("PanelBackground");
    }

    public KeyboardModeBar Bar { get; }

    public KeyboardStripView Strip { get; } = new();

    private AvaloniaPlaybackSession? _session;
    private SongDocument? _document;
    // Listens on the DOCUMENT while attached, so the bar's track list follows the song an undo or redo restores.
    private readonly DocumentWatch _watch = new();
    private bool _attached;

    /// <summary>Follows <paramref name="session"/> of <paramref name="document"/> through <paramref name="engine"/> (null unbinds): the bar drives the shared
    /// Learn engine and the track follows the document's selection, in sync with the pop-out's bar.</summary>
    public void BindPlayback(AvaloniaPlaybackSession? session, SongDocument document, KeyboardLearnEngine engine)
    {
        _session = session;
        _document = document;
        _engine = engine;
        Bar.SetTracks(document.Project.Tracks.Select(t => t.Name), Math.Max(0, document.TrackIndex));
        FollowSession();
    }

    // The session binding exists only while the pane is attached, so a docked pane out of view does no per-frame work.
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        _engine?.SetPaneAttached(true);
        ShowDocumentTracks();
        FollowSession();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _attached = false;
        _engine?.SetPaneAttached(false);
        FollowSession();
    }

    private void ShowDocumentTracks()
    {
        if (_document is { } document) Bar.SetTracks(document.Project.Tracks.Select(t => t.Name), Math.Max(0, document.TrackIndex));
    }

    private void FollowSession()
    {
        _binding?.Dispose();
        _binding = null;
        _watch.Bind(_attached ? _document : null);
        if (_attached && _session is { } session)
        {
            if (_engine is not null && _document is { } document)
                _binding = new KeyboardSessionBinding(Strip, Bar, session, () => document, _engine);
        }
    }
}
