using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TabForge.Diagnostics;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Services;
using RenderDraw = TabForge.Visualization.Draw;

namespace TabForge.Views;

public sealed class NotePreviewEventArgs : EventArgs
{
    public NotePreviewEventArgs(int midi, int deviceId, int channel, int program)
    {
        Midi = midi; DeviceId = deviceId; Channel = channel; Program = program;
    }
    public int Midi { get; }
    public int DeviceId { get; }
    public int Channel { get; }
    public int Program { get; }
}

public sealed class ContextMenuEventArgs : EventArgs
{
    public ContextMenuEventArgs(Point position) => Position = position;
    public Point Position { get; }
    /// <summary>The spot that was right-clicked (not selected: right-click never moves the cursor).</summary>
    public int Measure { get; init; } = -1;
    public int Cell { get; init; } = -1;
    public int StringIndex { get; init; } = -1;
    /// <summary>A note sits on the clicked string at the clicked beat.</summary>
    public bool OnNote { get; init; }
    /// <summary>The click landed inside the current score selection: the menu acts on it and the selection is kept.</summary>
    public bool InsideSelection { get; init; }
    /// <summary>The click landed on the page below the header (a beat position), not on the header strip.</summary>
    public bool OverBeat { get; init; }
}
