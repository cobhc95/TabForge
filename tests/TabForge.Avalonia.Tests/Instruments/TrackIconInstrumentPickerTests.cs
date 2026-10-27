using System.Linq;
using TabForge.Avalonia.Platform;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the headless checks of the track icon's instrument picker run (presenter and apply level, no windows): the focus-family
/// open state, the hover/press cell geometry and tooltip, the live preview without an undo step, the bit-identical Cancel restore,
/// the one-step OK commit with undo back, and the header repaint per preview.
/// Does not own: the windows, the header drawing or the engine (wiring: TrackRowCells, TrackHeaderColumn, InstrumentPickerWindow,
/// MainWindowTrackRowOptions); expected open states come from InstrumentPickerPresenter.
/// Tests: this class.
/// </summary>
public class TrackIconInstrumentPickerTests
{
    private static System.Collections.Generic.IReadOnlyList<(string Name, string Family, int Program, bool IsDrumKit)> Catalogue() =>
        InstrumentCatalog.All.Select(e => (Name: e.Name, Family: e.Category, Program: e.Program, IsDrumKit: e.IsDrumKit)).ToList();

    private static SongDocument OpenDoc(TrackKind kind, string instrument)
    {
        var project = SongDocument.BlankProject();
        project.Tracks.Clear();
        project.Tracks.Add(NewTrackFactory.CreateTrack(project, kind));
        var doc = new SongDocument(() => null) { Project = project };
        var track = project.Tracks[0];
        Assert.True(AudioToInstrumentConversion.ApplyInstrument(project, track, instrument));
        project.IsDirty = false;
        return doc;
    }

    [Fact]
    public void IconOpenStateExpandsOnlyTheTrackFamilyWithCurrentSelected()
    {
        var entries = Catalogue();
        var presenter = new InstrumentPickerPresenter(entries, "Distortion Guitar", focusFamily: true);
        Assert.Equal("Distortion Guitar", presenter.Chosen);
        var distortion = entries.ToList().FindIndex(e => e.Name == "Distortion Guitar");
        Assert.True(distortion >= 0 && presenter.IsChosen(distortion));
        foreach (var family in presenter.Families)
        {
            var state = presenter.StateOf(family);
            Assert.True(state.HeaderVisible);
            Assert.Equal(family == "Guitar", state.Open);
            Assert.Equal(family == "Guitar", state.TilesVisible);
        }
    }

    [Fact]
    public void OtherPickerEntryPointsKeepEveryFamilyOpen()
    {
        var presenter = new InstrumentPickerPresenter(Catalogue(), "Distortion Guitar", focusFamily: false);
        foreach (var family in presenter.Families)
            Assert.True(presenter.StateOf(family).TilesVisible);
    }

    [Fact]
    public void KindCellSitsInTheKindColumnWithTheChangeInstrumentTooltip()
    {
        var (left, width) = TrackRowCells.Column("kind");
        var box = TrackRowCells.Box(TrackRowCells.Cell.Kind, 0, 40);
        Assert.True(box.X >= left && box.Right <= left + width);
        Assert.Equal("kind", TrackHeaderColumn.CellId(TrackRowCells.Cell.Kind));
        Assert.Equal("Change instrument", TrackHeaderColumn.TooltipOf(0, TrackRowCells.Cell.Kind));
    }

    [Fact]
    public void PreviewAppliesLiveWithoutUndo_OkCommitsOneStep()
    {
        var doc = OpenDoc(TrackKind.Guitar, "Distortion Guitar");
        var track = doc.Project.Tracks[0];
        var undoCount = doc.Undo.UndoCount;
        var session = new InstrumentPreviewSession(doc.Project, track);
        Assert.Equal("Distortion Guitar", session.OriginalName);

        session.Preview("Clean Electric Guitar");
        Assert.Equal("Clean Electric Guitar", track.InstrumentName);
        Assert.Equal(27, track.MidiProgram);
        Assert.Equal(undoCount, doc.Undo.UndoCount);
        Assert.False(doc.Project.IsDirty);

        var commit = session.Commit(doc, "Clean Electric Guitar");
        Assert.True(commit.Changed);
        Assert.Equal(undoCount + 1, doc.Undo.UndoCount);
        Assert.Equal(InstrumentPreviewSession.CommitLabel, doc.Undo.Latest?.Label);
        Assert.True(doc.Project.IsDirty);

        Assert.NotNull(DocumentEdits.Undo(doc));
        Assert.Equal("Distortion Guitar", doc.Project.Tracks[0].InstrumentName);
        Assert.Equal(30, doc.Project.Tracks[0].MidiProgram);
    }

    [Fact]
    public void PreviewThenCancelRestoresTheTrackBitIdentical()
    {
        var doc = OpenDoc(TrackKind.Guitar, "Distortion Guitar");
        var track = doc.Project.Tracks[0];
        var before = DocumentEdits.Fingerprint(doc);
        var undoCount = doc.Undo.UndoCount;
        var session = new InstrumentPreviewSession(doc.Project, track);

        session.Preview("Acoustic Grand Piano");
        Assert.NotEqual("Distortion Guitar", track.InstrumentName);

        session.Cancel();
        Assert.Equal(before, DocumentEdits.Fingerprint(doc));
        Assert.Equal(undoCount, doc.Undo.UndoCount);
        Assert.Equal(0, doc.Undo.RedoCount);
        Assert.False(doc.Project.IsDirty);
        Assert.Equal("Distortion Guitar", track.InstrumentName);
        Assert.Equal(30, track.MidiProgram);
        Assert.Equal("Modern Rhythm", track.Rig.Name);
        Assert.Equal("Generic Guitar", track.Rig.ArticulationMap);
        Assert.Equal(TrackKind.Guitar, track.Kind);
    }

    [Fact]
    public void DrumTrackPreviewConvertsKindLiveAndCancelRestoresDrumsExactly()
    {
        var doc = OpenDoc(TrackKind.Drums, "Drum Kit (Standard)");
        var track = doc.Project.Tracks[0];
        track.Measures[0].Cells[0].Notes.Add(new TabNote { StringIndex = 2, Fret = 38, MidiValue = 38 });
        doc.Project.IsDirty = false;
        var before = DocumentEdits.Fingerprint(doc);
        var undoCount = doc.Undo.UndoCount;

        var presenter = new InstrumentPickerPresenter(Catalogue(), "Drum Kit (Standard)", focusFamily: true);
        foreach (var family in presenter.Families)
            Assert.Equal(family == "Drum Kits", presenter.StateOf(family).TilesVisible);

        var session = new InstrumentPreviewSession(doc.Project, track);
        session.Preview("Acoustic Grand Piano");
        Assert.NotEqual(TrackKind.Drums, track.Kind);
        Assert.NotEqual(9, track.MidiChannel);

        session.Cancel();
        Assert.Equal(before, DocumentEdits.Fingerprint(doc));
        Assert.Equal(undoCount, doc.Undo.UndoCount);
        Assert.Equal(0, doc.Undo.RedoCount);
        Assert.False(doc.Project.IsDirty);
        Assert.Equal(TrackKind.Drums, track.Kind);
        Assert.Equal(9, track.MidiChannel);
        var note = Assert.Single(track.Measures[0].Cells[0].Notes);
        Assert.Equal((2, 38, 38), (note.StringIndex, note.Fret, note.MidiValue));
    }

    [Fact]
    public void EveryPreviewRepaintsTheHeaderIcon()
    {
        var doc = OpenDoc(TrackKind.Guitar, "Distortion Guitar");
        var session = new InstrumentPreviewSession(doc.Project, doc.Project.Tracks[0]);
        var repaints = 0;
        session.Changed += () => repaints++;
        foreach (var name in new[] { "Clean Electric Guitar", "Acoustic Grand Piano", "Grand Piano", "Electric Bass (Pick)", "Distortion Guitar" })
            session.Preview(name);
        Assert.Equal(5, repaints);
    }

    [Fact]
    public void CommittingTheOriginalStoresNoStep()
    {
        var doc = OpenDoc(TrackKind.Guitar, "Distortion Guitar");
        var undoCount = doc.Undo.UndoCount;
        var session = new InstrumentPreviewSession(doc.Project, doc.Project.Tracks[0]);
        session.Preview("Clean Electric Guitar");
        var commit = session.Commit(doc, "Distortion Guitar");
        Assert.False(commit.Changed);
        Assert.Equal(undoCount, doc.Undo.UndoCount);
        Assert.False(doc.Project.IsDirty);
    }
}
