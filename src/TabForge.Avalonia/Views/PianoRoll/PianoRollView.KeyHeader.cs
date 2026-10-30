using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;

namespace TabForge.Avalonia.Views;

// Owns: the Piano / Names radio pair above the key strip (the ruler's top-left corner): exactly one on, the accent
//   look while on (PianoRollToolLook, as the toolbar toggles), keyboard-focusable with tooltips; Piano shows piano
//   keys, Names writes the note name on every key. Both flip the same per-track setting the old key-strip menu used,
//   so saved songs keep their choice.
// Does not own: the setting itself (TrackModel.PianoRollNoteNames through SetNoteNames) or the key strip's paint.
// Tests: TabForge.Avalonia.Tests.PianoRollKeyboardTests, PianoRollToolbarLayoutTests.
public sealed partial class PianoRollView
{
    private readonly ToggleButton _piano = new() { Name = "PianoRollKeyPiano", Content = "Piano" };

    /// <summary>The Piano / Names pair in the ruler's top-left corner (the empty area left of the ruler).</summary>
    internal Control BuildKeyHeader()
    {
        PianoRollToolbarLayout.StyleButton(_piano);
        PianoRollToolLook.Apply(_piano);
        ToolTip.SetTip(_piano, "Key strip: piano keys instead of note names; saved with this track");
        _piano.Click += (_, _) => { SetNoteNames(false); Surface.Focus(); };
        _names.Focusable = true;   // the corner pair stays keyboard-reachable (the toolbar buttons are not)
        var frame = PianoRollToolbarLayout.Frame(_piano, _names);
        frame.Name = "PianoRollKeyHeader";
        frame.Width = PianoRollSurface.KeyStripSize;
        frame.Height = PianoRollSurface.RulerSize;
        frame.HorizontalAlignment = HorizontalAlignment.Left;
        frame.VerticalAlignment = VerticalAlignment.Top;
        return frame;
    }

    /// <summary>Lights the Piano / Names pair from the track's saved choice (exactly one on).</summary>
    internal void RefreshKeyHeader()
    {
        _piano.IsChecked = !Surface.NoteNames;
        _names.IsChecked = Surface.NoteNames;
    }
}
