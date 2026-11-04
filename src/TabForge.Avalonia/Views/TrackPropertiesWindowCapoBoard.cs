using TabForge.Avalonia.Platform.Controls;

namespace TabForge.Avalonia.Views;

/// <summary>
/// The mini fretboard of the track properties Capo card: the shared <see cref="FretboardView"/> showing the
/// full bar and all partial bars, clipped to their covered strings. Dragging any bar previews
/// its fret on the board only; the release raises <see cref="FretboardView.CommitRequested"/> once
/// (a FretboardCapoDrag: one commit per drag, none when unchanged); empty frets request placement, bars select.
/// Owns: nothing beyond the shared board (the name the window looks up). Does not own: the capo values (the
/// window's working copies), the song or the undo step. Placement and selection live in FretboardView.
/// Tests: TabForge.Avalonia.Tests.TrackPropertiesRedesignTests.
/// </summary>
internal sealed class TrackPropertiesWindowCapoBoard : FretboardView
{
    internal TrackPropertiesWindowCapoBoard() => Height = Dialogs.DialogMetrics.TrackCapoCardBoardHeight;
}
