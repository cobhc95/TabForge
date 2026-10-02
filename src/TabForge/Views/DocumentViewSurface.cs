using System.Windows.Controls;
using TabForge.Controllers;
using TabForge.Documents;

namespace TabForge.Views;

/// <summary>The score editor, the track list and the tempo and lyrics boxes of one window, as the surface a document's view state is read from and shown on.</summary>
internal sealed class DocumentViewSurface : IDocumentViewHost
{
    private readonly TabEditorControl _editor;
    private readonly DataGrid _trackGrid;
    private readonly TextBox _tempoBox, _lyricsBox;

    public DocumentViewSurface(TabEditorControl editor, DataGrid trackGrid, TextBox tempoBox, TextBox lyricsBox)
        => (_editor, _trackGrid, _tempoBox, _lyricsBox) = (editor, trackGrid, tempoBox, lyricsBox);

    public string TempoText => _tempoBox.Text;
    public string LyricsText => _lyricsBox.Text;

    public void Read(DocumentSession doc)
    {
        doc.CursorBar = _editor.SelectedMeasure;
        doc.CursorCell = _editor.SelectedCell;
        doc.CursorString = _editor.SelectedString;
        doc.ActiveVoiceIndex = _editor.ActiveVoiceIndex;
        doc.TrackIndex = Math.Max(0, _trackGrid.SelectedIndex);
        doc.Notation = _editor.Notation;
        doc.DarkPaper = _editor.DarkPaper;
        doc.ContinuousScoreView = _editor.CenterSystems;
        doc.HorizontalScoreView = _editor.HorizontalScroll;
        doc.DurationDenominator = _editor.CurrentDurationDenominator;
        doc.DurationDots = _editor.CurrentDots;
        doc.DurationTriplet = _editor.CurrentTriplet;
        doc.TupletNumerator = _editor.CurrentTupletNumerator;
        doc.TupletDenominator = _editor.CurrentTupletDenominator;
    }

    public void Show(DocumentSession doc, bool darkPaper)
    {
        _tempoBox.Text = doc.Project.Tempo.ToString();
        _lyricsBox.Text = doc.Project.Lyrics ?? "";
        _editor.Project = doc.Project;
        _editor.Notation = doc.Notation;
        _editor.CenterSystems = doc.ContinuousScoreView;
        _editor.HorizontalScroll = doc.HorizontalScoreView;
        doc.DarkPaper = darkPaper;   // score paper is an app-wide appearance choice, not per tab
        _editor.DarkPaper = darkPaper;
        _editor.CurrentDurationDenominator = doc.DurationDenominator;
        _editor.CurrentDots = doc.DurationDots;
        _editor.CurrentTriplet = doc.DurationTriplet;
        _editor.SetTupletEntryState(doc.TupletNumerator, doc.TupletDenominator);
        _editor.SelectedTrackIndex = Math.Max(0, doc.TrackIndex);
        _editor.SetPosition(doc.CursorBar, doc.CursorCell, doc.CursorString);
        _editor.SetActiveVoice(doc.ActiveVoiceIndex);
    }
}
