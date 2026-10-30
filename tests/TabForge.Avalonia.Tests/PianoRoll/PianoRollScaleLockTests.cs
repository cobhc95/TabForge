using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TabForge.Presenters;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the piano roll's scale lock: the pure snap and diatonic step, and the presenter's locked paths (moves, scale-step transposes and commits snap into key).
// Does not own: the toolbar toggle (PianoRollToolbarLayoutTests) or the grid dimming (PianoRollViewTests).
// Tests: PianoRollScaleLock, PianoRollPresenter.
public sealed class PianoRollScaleLockTests
{
    private static readonly IReadOnlySet<int> CMajor = new HashSet<int> { 0, 2, 4, 5, 7, 9, 11 };

    [Fact]
    public void SnapKeepsPitchesAlreadyInTheScale()
    {
        Assert.Equal(60, PianoRollScaleLock.Snap(60, CMajor));
        Assert.Equal(64, PianoRollScaleLock.Snap(64, CMajor));
        Assert.Equal(127, PianoRollScaleLock.Snap(127, CMajor));   // G9
    }

    [Fact]
    public void SnapMovesToTheNearestScalePitch()
    {
        Assert.Equal(62, PianoRollScaleLock.Snap(61, CMajor));   // C#: ties go up by default
        Assert.Equal(60, PianoRollScaleLock.Snap(61, CMajor, direction: -1));
        Assert.Equal(64, PianoRollScaleLock.Snap(63, CMajor));
        Assert.Equal(62, PianoRollScaleLock.Snap(63, CMajor, direction: -1));
    }

    [Fact]
    public void SnapWithoutAScaleKeepsThePitch()
    {
        Assert.Equal(61, PianoRollScaleLock.Snap(61, null));
        Assert.Equal(61, PianoRollScaleLock.Snap(61, new HashSet<int>()));
        Assert.Equal(127, PianoRollScaleLock.Snap(200, CMajor));
        Assert.Equal(0, PianoRollScaleLock.Snap(-5, CMajor));
    }

    [Fact]
    public void StepMovesByScaleDegrees()
    {
        Assert.Equal(62, PianoRollScaleLock.Step(60, CMajor, 1));
        Assert.Equal(62, PianoRollScaleLock.Step(64, CMajor, -1));
        Assert.Equal(72, PianoRollScaleLock.Step(71, CMajor, 1));   // B4 up is C5
        Assert.Equal(71, PianoRollScaleLock.Step(72, CMajor, -1));
        Assert.Equal(72, PianoRollScaleLock.Step(60, CMajor, 7));   // seven steps up is the octave
    }

    [Fact]
    public void StepWithoutAScaleMovesBySemitones()
    {
        Assert.Equal(62, PianoRollScaleLock.Step(60, null, 2));
        Assert.Equal(0, PianoRollScaleLock.Step(0, new HashSet<int>(), -1));   // clamps at the range end
        Assert.Equal(127, PianoRollScaleLock.Step(127, null, 1));
    }

    [Fact]
    public async Task LockedMovesSnapIntoKey()
    {
        var presenter = PianoRollSongs.Presenter(PianoRollSongs.Midi());
        presenter.Scale = CMajor;
        presenter.ScaleLock = true;
        presenter.Select(new[] { 0 });

        Assert.True(await presenter.MoveAsync(0, 1));

        Assert.Equal(new[] { 62, 64 }, presenter.Notes.Select(n => n.Pitch).ToArray());   // C# snaps up to D, E stays
    }

    [Fact]
    public async Task ScaleStepsTransposeStaysInKey()
    {
        var presenter = PianoRollSongs.Presenter(PianoRollSongs.Midi());
        presenter.Scale = CMajor;
        presenter.ScaleSteps = true;
        presenter.Select(new[] { 0, 1 });

        Assert.True(await presenter.TransposeAsync(1));
        Assert.Equal(new[] { 62, 65 }, presenter.Notes.Select(n => n.Pitch).ToArray());   // C to D, E to F

        Assert.True(await presenter.TransposeAsync(-1));
        Assert.Equal(new[] { 60, 64 }, presenter.Notes.Select(n => n.Pitch).ToArray());

        Assert.True(await presenter.TransposeAsync(12));
        Assert.Equal(new[] { 72, 76 }, presenter.Notes.Select(n => n.Pitch).ToArray());   // an octave follows as seven steps
    }

    [Fact]
    public async Task TransposeFallsBackToSemitonesWithoutAScale()
    {
        var presenter = PianoRollSongs.Presenter(PianoRollSongs.Midi());
        presenter.Scale = null;
        presenter.ScaleSteps = true;
        presenter.Select(new[] { 0, 1 });

        Assert.True(await presenter.TransposeAsync(1));
        Assert.Equal(new[] { 61, 65 }, presenter.Notes.Select(n => n.Pitch).ToArray());
    }

    [Fact]
    public async Task CommittedDrawsSnapIntoKey()
    {
        var presenter = PianoRollSongs.Presenter(PianoRollSongs.Midi());
        presenter.Scale = CMajor;
        presenter.ScaleLock = true;

        Assert.True(await presenter.CommitAsync(PianoRollEdits.Add(presenter.Notes, new PianoRollNote(0, 1, 61, 90)), PianoRollEditKind.Notes, "Add note"));

        Assert.Contains(62, presenter.Notes.Select(n => n.Pitch));
    }
}
