using System;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Interactivity;
using TabForge.Avalonia.Transport;
using TabForge.Documents;

namespace TabForge.Avalonia.Lyrics;

// Owns: the Lyrics dock pane: one text box of the shown song's lyrics (Project.Lyrics). Leaving the box commits the text through
//     DocumentEdits (one undo step, one dirty change). A button opens the per-bar syllable editor (LyricsByBarWindow) for the same song.
// Does not own: the syllables of a bar (LyricsController, LyricsPaneView), the document tabs (LyricsPaneFlow wires them), the playhead.
// Tests: TabForge.Avalonia.Tests.LyricsSongPaneTests.
/// <summary>The song-level Lyrics pane. UI thread only.</summary>
public sealed class LyricsSongPane : Border
{
    /// <summary>The button text that opens the per-bar syllable editor.</summary>
    public const string ByBarButtonText = "Edit by bar…";

    private readonly Func<AvaloniaPlaybackSession?> _session;
    private readonly TextBox _box = new()
    {
        Name = "LyricsBox", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 120,
        VerticalAlignment = VerticalAlignment.Stretch,
    };
    private SongDocument? _document;
    private bool _showing;

    /// <param name="session">The playback session of the shown song (read when the per-bar editor opens); null when the window has no playback.</param>
    public LyricsSongPane(Func<AvaloniaPlaybackSession?> session)
    {
        _session = session;
        Padding = new Thickness(8);
        this[!BackgroundProperty] = new DynamicResourceExtension("PanelBrush");
        AutomationProperties.SetName(_box, "Lyrics");
        _box.LostFocus += (_, _) => CommitText();
        var byBar = new Button { Name = "LyricsByBar", Content = ByBarButtonText, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0) };
        AutomationProperties.SetName(byBar, ByBarButtonText);
        byBar.Click += (_, _) => OpenByBar();
        var stack = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(byBar, Dock.Bottom);
        stack.Children.Add(byBar);
        stack.Children.Add(_box);
        Child = stack;
    }

    /// <summary>Shows the lyrics of <paramref name="document"/>; null shows an empty box.</summary>
    public void Show(SongDocument? document)
    {
        if (_document is not null) _document.DisplayStateChanged -= OnDocumentChanged;
        _document = document;
        if (_document is not null) _document.DisplayStateChanged += OnDocumentChanged;
        ShowText();
    }

    /// <summary>Puts the keyboard focus in the lyrics box, the caret at the end of the text (Insert > Lyrics).</summary>
    public void FocusEditor()
    {
        _box.Focus();
        _box.CaretIndex = _box.Text?.Length ?? 0;
    }

    /// <summary>Commits the box's text to the shown song, as one edit; nothing happens when the text is unchanged.</summary>
    public void CommitText()
    {
        if (_showing || _document is null) return;
        var text = _box.Text ?? "";
        DocumentEdits.Run(_document, project => { if (project.Lyrics == text) return false; project.Lyrics = text; return true; }, invalidatesTimeline: false, label: "Edit lyrics");
    }

    private void ShowText()
    {
        _showing = true;
        try { _box.Text = _document?.Project.Lyrics ?? ""; }
        finally { _showing = false; }
    }

    /// <summary>The song's lyrics changed elsewhere (Project settings): the box follows, unless the person is typing in it.</summary>
    private void OnDocumentChanged(object? sender, EventArgs e)
    {
        if (!_box.IsFocused) ShowText();
    }

    private void OpenByBar()
    {
        if (_document is null) return;
        CommitText();
        if (TopLevel.GetTopLevel(this) is not Window owner) return;   // not on screen: there is nothing to open the editor over
        _ = new LyricsByBarWindow(_document, _session()).ShowDialog(owner);
    }
}
