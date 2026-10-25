using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using TabForge.Avalonia.Platform;
using TabForge.Documents;

namespace TabForge.Avalonia.Views;

/// <summary>
/// The inline track name editor: a text box laid over a track's name cell in the header. Enter, or leaving the box, keeps the typed name;
/// Escape keeps the old one. A kept name that differs is one undo step through <see cref="DocumentEdits"/>.
/// Owns: the text box, its placement over the row, and the commit or cancel of the one edit open at a time.
/// Does not own: drawing the name (<see cref="TrackHeaderColumn"/>), the double-click and the Rename row that start an edit (<see cref="ArrangementView"/>,
/// MainWindowTrackRowEdits).
/// Tests: TabForge.Avalonia.Tests.ArrangementViewTests.
/// </summary>
public sealed class TrackNameEditor
{
    // Matches the size the header draws track names at.
    private const double NameFontSize = 13;

    private readonly TimelineView _timeline;
    private readonly TrackHeaderColumn _header;
    private IEditableDocument? _document;
    private int _row = -1;

    public TrackNameEditor(TimelineView timeline, TrackHeaderColumn header)
    {
        _timeline = timeline;
        _header = header;
        Box = new TextBox
        {
            IsVisible = false, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            FontSize = NameFontSize, Padding = new Thickness(4, 0), MinHeight = 0, VerticalContentAlignment = VerticalAlignment.Center,
        };
        Box.KeyDown += OnKeyDown;
        Box.LostFocus += (_, _) => Finish(commit: true);
    }

    /// <summary>The text box; the host places it in the arrangement over the header, where it is hidden until an edit begins.</summary>
    public TextBox Box { get; }

    /// <summary>Opens the editor on the name of the track at <paramref name="row"/> of <paramref name="document"/>; an edit still open is committed first.</summary>
    public void Begin(IEditableDocument document, int row)
    {
        if (row < 0 || row >= document.Project.Tracks.Count) return;
        Finish(commit: true);
        _document = document;
        _row = row;
        var (left, width) = TrackRowCells.Column("name", _header.NameColumnWidth);
        Box.Margin = new Thickness(left, _timeline.RowTop(row), 0, 0);
        Box.Width = width;
        Box.Height = _timeline.RowHeight;
        Box.Text = document.Project.Tracks[row].Name;
        Box.IsVisible = true;
        Box.Focus();
        Box.SelectAll();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Finish(commit: true);
        else if (e.Key == Key.Escape) Finish(commit: false);
        else return;
        e.Handled = true;
    }

    /// <summary>Hides the box; with <paramref name="commit"/> the typed name is kept, else the track keeps its old name.</summary>
    private void Finish(bool commit)
    {
        if (_document is null) return;
        var document = _document;
        var row = _row;
        var text = Box.Text ?? "";
        _document = null;
        _row = -1;
        Box.IsVisible = false;
        if (commit) Commit(document, row, text);
        _header.InvalidateVisual();
    }

    /// <summary>Sets the track's name to <paramref name="text"/> as one undo step; a name that is unchanged is no edit.</summary>
    private static void Commit(IEditableDocument document, int row, string text) =>
        DocumentEdits.Run(document, project =>
        {
            if (row >= project.Tracks.Count || string.Equals(project.Tracks[row].Name, text, StringComparison.Ordinal)) return false;
            project.Tracks[row].Name = text;
            return true;
        }, label: "Rename track");
}
