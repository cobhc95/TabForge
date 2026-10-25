using System.Collections.Generic;
using System.Runtime.CompilerServices;
using TabForge.Features.Recording.Rules;
using TabForge.Models;
using TabForge.Platform;

namespace TabForge.Avalonia.Platform;

// Owns: the per-timeline cache of the repeat view's derived data: the repeat map of the performed order and each audio clip's fold, recomputed only when the
//   performed order (a new array whenever the song's timing changes) or the clip span changes, plus the skipped-pass hatch stroke, so a frame draws without refolding.
// Does not own: the fold rules (RepeatFold, RepeatStructure) or the drawing (TimelineAudioClips).
// Tests: TabForge.Avalonia.Tests.RepeatViewTests, SkippedPassHatchTests.
internal sealed class TimelineFoldCache
{
    private sealed class Fold
    {
        public PerformedBar[]? Performed;
        public double StartSec, EndSec;
        public IReadOnlyList<RepeatSlice>? Slices;
    }

    private readonly ConditionalWeakTable<AudioClip, Fold> _folds = new();
    private PerformedBar[]? _mapOf;
    private RepeatStructure? _map;
    private UiStroke _hatch;

    /// <summary>The repeat map of <paramref name="performed"/>, built once per performed order.</summary>
    public RepeatStructure MapOf(PerformedBar[] performed)
    {
        if (_map is null || !ReferenceEquals(_mapOf, performed))
        {
            _map = RepeatStructure.Of(performed);
            _mapOf = performed;
        }
        return _map;
    }

    /// <summary>The folded slices of <paramref name="clip"/> over <paramref name="performed"/>, or null when it crosses no repeat (see <see cref="RepeatFold.FoldedSlices"/>).</summary>
    public IReadOnlyList<RepeatSlice>? FoldOf(PerformedBar[] performed, AudioClip clip)
    {
        var fold = _folds.GetOrCreateValue(clip);
        if (ReferenceEquals(fold.Performed, performed) && fold.StartSec == clip.StartSec && fold.EndSec == clip.EndSec) return fold.Slices;
        var map = MapOf(performed);
        fold.Slices = RepeatPasses.HasRepeatSpan(performed, clip.StartSec, clip.EndSec - clip.StartSec, map)
            ? RepeatFold.FoldedSlices(clip.StartSec, clip.EndSec, performed, map)
            : null;
        (fold.Performed, fold.StartSec, fold.EndSec) = (performed, clip.StartSec, clip.EndSec);
        return fold.Slices;
    }

    /// <summary>The hatch stroke for <paramref name="ink"/> (rebuilt only when the colour changes).</summary>
    public UiStroke HatchStroke(UiColor ink, double width)
    {
        if (_hatch.Colour != ink || _hatch.Width != width) _hatch = new UiStroke(ink, width);
        return _hatch;
    }
}
