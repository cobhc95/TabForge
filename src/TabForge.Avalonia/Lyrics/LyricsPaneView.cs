using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Markup.Xaml.MarkupExtensions;
using TabForge.Avalonia.Transport;
using TabForge.Documents;
using TabForge.Lyrics;

namespace TabForge.Avalonia.Lyrics;

// Owns: the Lyrics dock pane: one row per bar of the shown song (bar number, section name, a TextBox of the bar's syllables). Enter commits
//       the bar and moves to the next one, leaving the box commits it, and the row of the bar under the playhead takes the AccentSoft fill.
// Does not own: the lyric rules and edits (LyricsController in TabForge.Core), the document tabs (LyricsPaneFlow wires them), the playhead.
// Tests: LyricsPaneTests.
public sealed class LyricsPaneView : Border
{
    private const string PlainRowBrush = "PanelBrush";
    private const string PlayheadRowBrush = "AccentSoft";
    private const string LabelBrush = "TextMuted";
    private const string LabelSeparator = " | ";

    private readonly StackPanel _list = new();
    private readonly List<LyricRow> _rows = new();
    private SongDocument? _document;
    private AvaloniaPlaybackSession? _session;
    private int _playheadBar = -1;

    public LyricsPaneView()
    {
        Padding = new Thickness(8);
        this[!BackgroundProperty] = new DynamicResourceExtension("PanelBrush");
        Child = new ScrollViewer { Content = _list, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }

    /// <summary>Shows the lyrics of <paramref name="document"/>, following <paramref name="session"/>'s playhead; null shows an empty pane.</summary>
    public void Show(SongDocument? document, AvaloniaPlaybackSession? session)
    {
        if (_document is not null) _document.DisplayStateChanged -= OnDocumentChanged;
        if (_session is not null) _session.PlayheadBarChanged -= OnPlayheadBarChanged;
        _document = document;
        _session = session;
        if (_document is not null) _document.DisplayStateChanged += OnDocumentChanged;
        if (_session is not null) _session.PlayheadBarChanged += OnPlayheadBarChanged;
        _playheadBar = _session?.Transport.PlayheadBar ?? -1;
        Rebuild();
    }

    /// <summary>Re-reads the shown document: texts of unfocused boxes and labels in place, the rows again only when the bar count changed.</summary>
    public void Refresh()
    {
        if (_document is null) return;
        var lines = LyricsController.RowsFor(_document);
        if (lines.Count != _rows.Count)
        {
            Rebuild();
            return;
        }
        for (var i = 0; i < lines.Count; i++)
        {
            var row = _rows[i];
            row.Label.Text = LabelOf(lines[i]);
            if (!row.Box.IsFocused) row.Box.Text = lines[i].Text;
        }
    }

    /// <summary>The text in each bar box, in bar order (what the pane shows now).</summary>
    public IReadOnlyList<string> ShownLyricTexts => _rows.ConvertAll(row => row.Box.Text ?? "");

    private void OnDocumentChanged(object? sender, EventArgs e) => Refresh();

    private void OnPlayheadBarChanged()
    {
        _playheadBar = _session?.Transport.PlayheadBar ?? -1;
        ApplyPlayheadHighlight();
    }

    private void Rebuild()
    {
        _rows.Clear();
        _list.Children.Clear();
        if (_document is not null)
        {
            foreach (var line in LyricsController.RowsFor(_document))
            {
                var row = BuildRow(line);
                _rows.Add(row);
                _list.Children.Add(row.Frame);
            }
        }
        ApplyPlayheadHighlight();
    }

    private LyricRow BuildRow(LyricBarRow line)
    {
        var label = new TextBlock { Text = LabelOf(line), TextWrapping = TextWrapping.Wrap };
        label[!TextBlock.ForegroundProperty] = new DynamicResourceExtension(LabelBrush);
        var box = new TextBox { Text = line.Text };
        var frame = new Border
        {
            Padding = new Thickness(4),
            Margin = new Thickness(0, 0, 0, 4),
            Child = new StackPanel { Spacing = 2, Children = { label, box } },
        };
        var row = new LyricRow(line.BarIndex, frame, label, box);
        box.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            CommitAndRefresh(row);
            var next = _rows.IndexOf(row) + 1;
            if (next < _rows.Count) _ = _rows[next].Box.Focus();
        };
        box.LostFocus += (_, _) => CommitAndRefresh(row);
        return row;
    }

    private void CommitAndRefresh(LyricRow row)
    {
        if (_document is null) return;
        LyricsController.SetBarText(_document, row.Bar, row.Box.Text ?? "");
        Refresh();
    }

    private void ApplyPlayheadHighlight()
    {
        foreach (var row in _rows)
            row.Frame[!Border.BackgroundProperty] = new DynamicResourceExtension(row.Bar == _playheadBar ? PlayheadRowBrush : PlainRowBrush);
    }

    private static string LabelOf(LyricBarRow line)
    {
        var bar = "Bar " + (line.BarIndex + 1);
        return string.IsNullOrWhiteSpace(line.SectionName) ? bar : bar + LabelSeparator + line.SectionName;
    }

    private sealed record LyricRow(int Bar, Border Frame, TextBlock Label, TextBox Box);
}
