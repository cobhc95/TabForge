using System.Linq;
using TabForge.Models;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the pure Copy/Cut/Paste/Delete/Duplicate routing table (EditRouting): every edit id is classified, focus decides the command, track removal asks, None never falls back.
// Does not own: the key dispatch or the dialogs that use the table.
// Tests: EditRouting.
public sealed class EditRoutingTests
{
    private readonly EditRouting _routing = new();
    private static readonly EditSelectionState Everything = new(true, true, true, true, true, true, true);

    private static readonly string[] Verbs = { "Copy", "Cut", "Paste", "PasteSpecial", "Delete", "Duplicate" };

    [Fact]
    public void EveryEditCommandIsClassified()
    {
        // Future ids: any catalogue action in the edit families whose name ends in a routed verb must be in the table.
        var families = new[] { "Edit.", "Clip.", "Range.", "TrackRow." };
        var verbIds = HotkeyCatalog.All.Select(a => a.Id)
            .Where(id => families.Any(id.StartsWith) && Verbs.Contains(id[(id.IndexOf('.') + 1)..])).ToList();
        Assert.NotEmpty(verbIds);
        foreach (var id in verbIds)
            Assert.True(_routing.Classify(id).HasValue, $"{id} is an edit verb but EditRouting does not classify it");
        Assert.Null(_routing.Classify("Edit.Undo"));
    }

    [Theory]
    [InlineData(FocusTarget.ScoreSelection, "Edit.Cut")]
    [InlineData(FocusTarget.Loop, "Range.Cut")]
    [InlineData(FocusTarget.Clips, "Clip.Cut")]
    [InlineData(FocusTarget.TrackRow, "TrackRow.Cut")]
    [InlineData(FocusTarget.None, null)]
    public void CutRoutesByFocus(FocusTarget focus, string? expected)
    {
        var routed = _routing.Resolve(focus, Everything, EditVerb.Cut);
        Assert.Equal(expected, routed.CommandId);
        Assert.Equal(expected is not null, routed.IsAvailable);
    }

    [Fact]
    public void DeleteOnTrackRowNeedsConfirm()
    {
        var delete = _routing.Resolve(FocusTarget.TrackRow, Everything, EditVerb.Delete);
        var cut = _routing.Resolve(FocusTarget.TrackRow, Everything, EditVerb.Cut);
        Assert.Equal("TrackRow.Delete", delete.CommandId);
        Assert.True(delete.NeedsConfirm);
        Assert.True(cut.NeedsConfirm);
        Assert.False(_routing.Resolve(FocusTarget.Clips, Everything, EditVerb.Delete).NeedsConfirm);
        Assert.False(_routing.Resolve(FocusTarget.TrackRow, Everything, EditVerb.Copy).NeedsConfirm);
    }

    [Fact]
    public void NoneFocusIsNotAvailableNotScoreFallback()
    {
        foreach (var verb in System.Enum.GetValues<EditVerb>())
        {
            var routed = _routing.Resolve(FocusTarget.None, Everything, verb);
            Assert.False(routed.IsAvailable);
            Assert.False(string.IsNullOrEmpty(routed.NotAvailableReason));
        }
    }

    [Fact]
    public void PasteOnTrackRowNeedsTrackOnClipboard()
    {
        var barsOnly = Everything with { ClipboardHasTrack = false };
        Assert.False(_routing.Resolve(FocusTarget.TrackRow, barsOnly, EditVerb.Paste).IsAvailable);
        Assert.Equal("TrackRow.Paste", _routing.Resolve(FocusTarget.TrackRow, Everything, EditVerb.Paste).CommandId);
    }

    [Fact]
    public void DuplicateRoutesTheRangeToTheBarDuplicateAndNotTheScore()
    {
        Assert.Equal(EditRouting.RangeDuplicateId, _routing.Resolve(FocusTarget.Loop, Everything, EditVerb.Duplicate).CommandId);
        Assert.False(_routing.Resolve(FocusTarget.ScoreSelection, Everything, EditVerb.Duplicate).IsAvailable);
    }

    [Fact]
    public void RemappedGestureStillRoutes()
    {
        // One rebound key (e.g. Ctrl+K) now holds every Delete command: focus picks the one that applies.
        var candidates = new[] { "Clip.Delete", "Range.Delete", "TrackRow.Delete" };
        Assert.Equal("Clip.Delete", _routing.Pick(FocusTarget.Clips, candidates));
        Assert.Equal("Range.Delete", _routing.Pick(FocusTarget.Loop, candidates));
        Assert.Equal("TrackRow.Delete", _routing.Pick(FocusTarget.TrackRow, candidates));
        Assert.Null(_routing.Pick(FocusTarget.None, candidates));
        // The shared Edit.* command serves both the score and a timeline range.
        Assert.Equal("Range.Copy", _routing.Pick(FocusTarget.Loop, new[] { "Edit.Copy", "Range.Copy", "Clip.Copy" }));
        Assert.Equal("Edit.Copy", _routing.Pick(FocusTarget.ScoreSelection, new[] { "Edit.Copy", "Range.Copy", "Clip.Copy" }));
        Assert.Equal("Clip.Copy", _routing.Pick(FocusTarget.Clips, new[] { "Edit.Copy", "Clip.Copy" }));
    }
}
