using System.Collections.Generic;
using TabForge.Presenters;

namespace TabForge.Avalonia.Platform.Controls;

/// <summary>The two modes that share the header strip: compact mode and the Band view.</summary>
public enum HeaderMode { Compact, Band }

/// <summary>The command ids of the two header modes, whose toolbar toggles the band menu and the compact menu show.</summary>
public static class ModeCommandIds
{
    public const string BandView = "View.BandView", CompactMode = "View.CompactMode";
}

// Owns: which header modes are on, in the order they were entered, and so the caption the shared header strip shows: the mode entered last, and when
//   that one is left, the mode entered before it. Both modes can be on at once; each one's own leave keeps the other's state.
// Does not own: the modes themselves (MainWindowCompactMode, the Band view's controller), the strip that shows the caption (CompactTrackStrip).
// Tests: TabForge.Avalonia.Tests.CompactBandHeaderTests.
// Threads: UI thread only.
public sealed class HeaderModeCaption
{
    /// <summary>The Band view's caption text in the header strip (the compact mode's text is CompactModeStripPresenter.Caption).</summary>
    public const string BandCaption = "BAND VIEW";

    private readonly List<HeaderMode> _onInOrder = new();

    /// <summary>The mode was entered: it becomes the one the caption names.</summary>
    public void Enter(HeaderMode mode)
    {
        _onInOrder.Remove(mode);
        _onInOrder.Add(mode);
    }

    /// <summary>The mode was left: the caption falls back to the mode entered before it.</summary>
    public void Leave(HeaderMode mode) => _onInOrder.Remove(mode);

    /// <summary>Whether the mode is on.</summary>
    public bool IsOn(HeaderMode mode) => _onInOrder.Contains(mode);

    /// <summary>Whether any mode is on: the shared hiding (menu row, compact timeline) holds while this is true.</summary>
    public bool AnyOn => _onInOrder.Count > 0;

    /// <summary>The mode entered last, which the strip's caption, capsules and close button belong to; null when neither mode is on.</summary>
    public HeaderMode? Top => _onInOrder.Count == 0 ? null : _onInOrder[^1];

    /// <summary>The caption of the mode entered last; empty when neither mode is on.</summary>
    public string Caption => Top is { } top ? CaptionOf(top) : "";

    /// <summary>The caption text of a mode.</summary>
    public static string CaptionOf(HeaderMode mode) => mode == HeaderMode.Compact ? CompactModeStripPresenter.Caption : BandCaption;
}
