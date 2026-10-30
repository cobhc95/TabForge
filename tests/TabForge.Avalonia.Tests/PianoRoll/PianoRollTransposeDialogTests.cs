using System.Collections.Generic;
using System.Linq;
using TabForge.Presenters;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the Move notes popup's model: the title and count lines, the words ('up 1', 'down 2', 'no change') and unit caption in both modes, the Stay in the key caption and
//   its fallback with no scale, the octave amount (12 / 7), the Before / After chips, the snap tick (visibility, default from Lock, effect, only half steps), the range warning
//   and the answer. The one-undo-step commit is covered by PianoRollKeyboardTests.
// Does not own: the popup (PianoRollTransposePopup) or the edit (PianoRollPresenter.TransposeAsync).
// Tests: PianoRollTransposeDialog.
public sealed class PianoRollTransposeDialogTests
{
    private static readonly IReadOnlySet<int> CMajor = new HashSet<int> { 0, 2, 4, 5, 7, 9, 11 };

    private static PianoRollTransposeDialog Make(int[] pitches, int initial = 1, bool steps = false, IReadOnlySet<int>? scale = null, bool lockScale = false, string? name = "C major") =>
        new(pitches.Length, pitches, initial, steps, scale, lockScale, scale is null ? null : name);

    [Fact]
    public void TitleAndCountAreSingularOrPlural()
    {
        Assert.Equal("Move notes up or down", PianoRollTransposeDialog.TitleText);
        Assert.Equal("26 notes selected", new PianoRollTransposeDialog(26, new[] { 60 }, 1, false, null, false).CountText);
        Assert.Equal("1 note selected", Make(new[] { 60 }).CountText);
    }

    [Fact]
    public void ValueReadsInWordsWithAUnitCaptionPerMode()
    {
        var half = Make(new[] { 60 }, 1);
        Assert.Equal("up 1", half.DeltaWords);
        Assert.Equal("half step", half.UnitCaption);
        half.SetDelta(-2);
        Assert.Equal("down 2", half.DeltaWords);
        Assert.Equal("half steps", half.UnitCaption);
        half.SetDelta(0);
        Assert.Equal("no change", half.DeltaWords);
        var stay = Make(new[] { 60 }, 1, steps: true, scale: CMajor);
        Assert.Equal("step", stay.UnitCaption);
        stay.SetDelta(3);
        Assert.Equal("steps", stay.UnitCaption);
    }

    [Fact]
    public void StayInTheKeyNeedsAScaleAndCaptionsSayWhy()
    {
        var none = Make(new[] { 60 }, steps: true);
        Assert.False(none.ScaleSteps);   // no scale: it stays on half steps
        Assert.Equal("pick a key first", none.StayCaption);
        var some = Make(new[] { 60 }, scale: CMajor, name: "D minor");
        Assert.Equal("only notes of D minor", some.StayCaption);
        some.ScaleSteps = true;
        Assert.True(some.ScaleSteps);
        none.ScaleSteps = true;
        Assert.False(none.ScaleSteps);
    }

    [Fact]
    public void OctavePillsMoveTwelveHalfStepsOrSevenSteps()
    {
        var model = Make(new[] { 60 }, 0, scale: CMajor);
        Assert.Equal(12, model.OctaveAmount);
        model.ScaleSteps = true;
        Assert.Equal(7, model.OctaveAmount);
        model.Add(model.OctaveAmount);
        Assert.Equal(7, model.Delta);
        Assert.Equal("up 7", model.DeltaWords);
    }

    [Fact]
    public void ChipsShowTheLowestThreeDistinctPitchesAndWhereTheyLand()
    {
        var half = Make(new[] { 67, 60, 64, 60, 72 }, 2);
        Assert.Equal(new[] { 60, 64, 67 }.Select(PianoRollPositionText.PitchName), half.BeforeChips);
        Assert.Equal(new[] { 62, 66, 69 }.Select(PianoRollPositionText.PitchName), half.AfterChips);
        var stay = Make(new[] { 60, 64, 67 }, 1, steps: true, scale: CMajor);
        Assert.Equal(new[] { 62, 65, 69 }.Select(PianoRollPositionText.PitchName), stay.AfterChips);   // each note moves one step of C major
        Assert.Single(Make(new[] { 55, 55 }).BeforeChips);
    }

    [Fact]
    public void SnapTickShowsOnlyForAScaleInHalfStepsDefaultsToLockAndPullsNotesIntoKey()
    {
        Assert.False(Make(new[] { 60 }).SnapVisible);   // no scale
        var off = Make(new[] { 60 }, 1, scale: CMajor, lockScale: false);
        Assert.True(off.SnapVisible);
        Assert.False(off.SnapToKey);
        Assert.Equal(PianoRollPositionText.PitchName(61), off.AfterChips[0]);   // C#4, out of key
        off.SnapToKey = true;
        Assert.Equal(PianoRollPositionText.PitchName(62), off.AfterChips[0]);   // pulled up to D4
        Assert.True(off.Answer.Snap);
        Assert.False(off.ScaleLock);   // this move only: the Lock setting is untouched
        Assert.True(Make(new[] { 60 }, scale: CMajor, lockScale: true).SnapToKey);   // default = Lock
        off.ScaleSteps = true;
        Assert.False(off.SnapVisible);
        Assert.Equal("Snap the result to the key (C major)", off.SnapText);
    }

    [Fact]
    public void AmountPastTheRangeWarnsAndTheWholeSelectionStopsTogether()
    {
        var model = Make(new[] { 0, 60 }, 0);
        model.Add(-1);
        Assert.Equal("Some notes would go out of range", model.WarningText);
        Assert.Equal(new[] { 0, 60 }.Select(PianoRollPositionText.PitchName), model.AfterChips);
        model.SetDelta(200);
        Assert.Equal(127, model.Delta);
        Assert.NotEqual("", model.WarningText);
        model.SetDelta(67);
        Assert.Equal("", model.WarningText);   // the highest note lands exactly on 127
        var steps = Make(new[] { 60 }, 0, steps: true, scale: CMajor);
        steps.SetDelta(999);
        Assert.Equal(48, steps.Delta);
    }

    [Fact]
    public void SignedFormatsPlusStyle()
    {
        Assert.Equal("+3", PianoRollTransposeDialog.Signed(3));
        Assert.Equal("-2", PianoRollTransposeDialog.Signed(-2));
        Assert.Equal("0", PianoRollTransposeDialog.Signed(0));
    }

    [Fact]
    public void AnswerCarriesTheNumberModeAndSnap()
    {
        var model = Make(new[] { 60 }, 3, steps: true, scale: CMajor, lockScale: true);
        Assert.Equal(new MoveNotesAnswer(3, true, true), model.Answer);   // Stay in the key follows the Lock setting as before
    }
}
