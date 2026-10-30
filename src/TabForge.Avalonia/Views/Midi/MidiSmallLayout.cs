using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using TabForge.Avalonia.Views.Dialogs;

namespace TabForge.Avalonia.Views.Midi;

// Owns: the small-window rules of the MIDI processing window: the three-column grid with minimums and proportional stars
//   (both lists shrink first, the editor last), the clipped panes and the ellipsized catalog text with its full-name tooltip.
// Does not own: the window, the metrics, or the parameter editors.
// Tests: MidiSmallLayoutTests (every processor at MinWidth, MinWidth+50 and 1000 DIPs).
internal static class MidiSmallLayout
{
    /// <summary>The three-column body: both lists shrink first to their minimums while the editor keeps twice the star share,
    /// clipped to its own bounds so no column paints into the next.</summary>
    public static Grid BodyGrid()
    {
        var grid = new Grid { Name = "BodyGrid", ClipToBounds = true, ColumnSpacing = DialogMetrics.MidiColumnGap };
        grid.ColumnDefinitions = new ColumnDefinitions
        {
            new ColumnDefinition(new GridLength(1, GridUnitType.Star)) { MinWidth = DialogMetrics.MidiCatalogMinWidth, MaxWidth = DialogMetrics.MidiCatalogWidth },
            new ColumnDefinition(new GridLength(1, GridUnitType.Star)) { MinWidth = DialogMetrics.MidiListMinWidth, MaxWidth = DialogMetrics.MidiListWidth },
            new ColumnDefinition(new GridLength(2, GridUnitType.Star)) { MinWidth = DialogMetrics.MidiEditorMinWidth },
        };
        return grid;
    }

    /// <summary>One catalog row: ellipsized with the full name in a wrapped tooltip, never painting past its column.</summary>
    public static TextBlock CatalogText(string display)
    {
        var text = new TextBlock { Text = display, TextTrimming = TextTrimming.CharacterEllipsis, ClipToBounds = true };
        MidiSharedRows.Tip(text, display);
        return text;
    }

    /// <summary>Clips the pane to its own column bounds (the grid already limits its width).</summary>
    public static T Clip<T>(T control) where T : Control
    {
        control.ClipToBounds = true;
        return control;
    }
}
