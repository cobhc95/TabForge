using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using TabForge.Documents;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia.Views.TransportPopups;

// Owns: the piano roll's view of the master scale panel: the ONE shared scale pick (the same readout and pick as the status bar and the fretboard; a pick sticks to the
//   section and track), plus its own extras (highlight the scale on the grid, SHOW ROWS: all pitches / scale pitches only / pitches used by the notes, lock to scale) and the
//   muted fretboard section with its link. SHOW ROWS is saved in the shown song (one undo step); the highlight and the lock are settings. The toolbar and the open panel follow each other.
// Does not own: the shared sections (ScalePanel), the readout and picks (StatusScaleReadout), the rows and the lock themselves (PianoRollView), the settings file.
// Tests: TabForge.Avalonia.Tests.ScalePanelHostTests, PianoRollRowsPerSongTests.
// Threads: UI thread only.
internal sealed class PianoRollScaleHost : ReadoutScaleHost
{
    private const string GridTitle = "PIANO ROLL", HighlightText = "Highlight the scale on the grid", LockText = "Lock to scale", RowsLabel = "SHOW ROWS";

    private static readonly IReadOnlyList<(string Value, string Text)> RowChoices = new (string Value, string Text)[]
    {
        (PianoRollScale.FoldNone, "All pitches"), (PianoRollScale.FoldScale, "Scale pitches only"), (PianoRollScale.FoldUsed, "Pitches used by the notes"),
    };

    private readonly Func<EditingSettings> _editing;
    private readonly Func<SongDocument?> _song;
    private readonly Action _sync;
    private bool _applying;

    /// <param name="song">The song on screen: SHOW ROWS is read from and written to it.</param>
    /// <param name="sync">Redraws the roll and relights the toolbar controls after a grid option changed.</param>
    public PianoRollScaleHost(StatusScaleReadout readout, ScaleHostLinks links, Func<EditingSettings> editing, Func<SongDocument?> song, Action sync)
        : base(readout, links) => (_editing, _song, _sync) = (editing, song, sync);

    public override void Pick(ScaleMatch scale) => Readout.PickFor(scale, keep: true, fromFretboard: false);

    /// <summary>The toolbar changed a scale control: an open panel shows it.</summary>
    public void Notify()
    {
        if (!_applying) Readout.NotifyRefreshed();
    }

    public override IReadOnlyList<ScaleExtraSection> Extras => new[] { new ScaleExtraSection(GridTitle, _ => Options()) };

    public override IReadOnlyList<ScaleGreyedSection> Greyed => new[] { FretboardLink() };

    private void Apply(Action change)
    {
        _applying = true;
        try { change(); _sync(); }
        finally { _applying = false; }
        Readout.NotifyRefreshed();
    }

    /// <summary>SHOW ROWS of the song on screen: the choice is the song's, so another song's pick never shows here.</summary>
    internal void ChooseRows(string fold) => Apply(() => StoreRows(fold));

    private void StoreRows(string fold)
    {
        if (_song() is not { } document) return;
        DocumentEdits.Run(document, project => PianoRollScale.StoreRows(project, fold), invalidatesTimeline: false, label: "Show rows");
    }

    private Control Options()
    {
        var editing = _editing();
        var panel = new StackPanel();
        panel.Children.Add(ScalePanelParts.Check("PianoRollScaleShown", HighlightText, editing.PianoRollScaleShown, v => Apply(() => _editing().PianoRollScaleShown = v)));
        var fold = PianoRollScale.RowsOf(_song()?.Project);
        foreach (var control in ScalePanelParts.Segmented(RowsLabel, RowChoices, fold, "PianoRollFold", ChooseRows, () => false)) panel.Children.Add(control);
        panel.Children.Add(ScalePanelParts.Check("PianoRollScaleLock", LockText, editing.PianoRollScaleLock, v => Apply(() => _editing().PianoRollScaleLock = v)));
        return panel;
    }
}
