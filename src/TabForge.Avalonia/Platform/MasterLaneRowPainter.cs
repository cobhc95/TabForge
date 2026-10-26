using System;
using Avalonia;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Platform.Theme;

namespace TabForge.Avalonia.Platform;

/// <summary>
/// The "Master" row's cells in the track list, in the track rows' columns: the label in the name column, the master
/// FX split button (FX and power), the master volume slider (0-100 %, the transport's master knob) and the master pan slider. The master row has
/// these and no M or S. Drawn with the <see cref="TrackRowCells"/> builders.
/// Owns: the master row's label and the drawing of its three cells, from the song's master bus and pan and the state the caller passes (<see cref="Shown"/>),
/// the master cells' tooltips and the master volume range.
/// Does not own: the row's background and rule (<see cref="TrackHeaderColumn"/>), the lane above track 1 (<see cref="TimelineView"/>), the master volume
/// itself (the transport's knob), the master edits and the pointer handling of these cells (<see cref="MainWindowMasterLane"/>, TimelineInputController).
/// Tests: TabForge.Avalonia.Tests.MasterLaneRowTests, TabForge.Avalonia.Tests.MainWindowMasterLaneTests.
/// </summary>
public static class MasterLaneRowPainter
{
    /// <summary>The label the row shows in the name column.</summary>
    public const string Label = "Master";

    /// <summary>The master volume range: the transport knob's 0-100 %.</summary>
    public const int VolumeMinimum = 0, VolumeMaximum = 100;

    /// <summary>The master pan range: the track pan slider's -64..63.</summary>
    public static readonly (int Minimum, int Maximum) PanRange = TrackRowCells.SliderRange(TrackRowCells.Cell.Pan);

    private const string Font = "Segoe UI";
    private const double NameTextInset = 4;
    private const string FxTooltip = "Master effects: FX opens the chain applied to the whole mix";
    private const string FxPowerTooltip = "Master effects: the power switch bypasses the chain";
    private const string VolumeTooltip = "Master volume for all tracks (0–100 %, the same as the master knob)";
    private const string PanTooltip = "Master pan: moves the whole mix left or right";

    /// <summary>The tooltip text of a master cell.</summary>
    public static string TooltipOf(TrackRowCells.Cell cell) => cell switch
    {
        TrackRowCells.Cell.Fx => FxTooltip,
        TrackRowCells.Cell.FxPower => FxPowerTooltip,
        TrackRowCells.Cell.Volume => VolumeTooltip,
        _ => PanTooltip,
    };

    /// <summary>
    /// What the row shows in one paint: the volume percent and the pan value (a drag's preview, else the master's own value), the FX part under the
    /// pointer (0 the FX label, 1 the power symbol, -1 neither) and the drawn state of each cell.
    /// </summary>
    public readonly record struct Shown(int VolumePercent, int Pan, int FxPart, Func<TrackRowCells.Cell, TrackRowCells.CellPaint> PaintOf);

    /// <summary>Draws the label and the three cells in the row whose top is <paramref name="top"/> and height <paramref name="height"/>.</summary>
    public static void Draw(IDrawContext d, Func<string, UiColor> ink, SongProject project, double top, double height, double nameWidth, Shown shown)
    {
        Rect Box(TrackRowCells.Cell cell) => TrackRowCells.Box(cell, top, height, nameWidth);
        var (nameLeft, _) = TrackRowCells.Column("name", nameWidth);
        var label = d.Text(Label, Font, 13, ink("TextPrimary"), d.PixelsPerDip);
        d.DrawText(label, nameLeft + NameTextInset, top + (height - label.Height) / 2);
        var master = project.Mixer.Master;
        TrackRowCells.DrawFx(d, ink, Box(TrackRowCells.Cell.Fx), Box(TrackRowCells.Cell.FxPower), master.On, master.Rig.Plugins.Count, shown.FxPart);
        TrackRowCells.DrawSlider(d, ink, Box(TrackRowCells.Cell.Volume), VolumeMinimum, VolumeMaximum, shown.VolumePercent, shown.PaintOf(TrackRowCells.Cell.Volume));
        TrackRowCells.DrawSlider(d, ink, Box(TrackRowCells.Cell.Pan), PanRange.Minimum, PanRange.Maximum, shown.Pan, shown.PaintOf(TrackRowCells.Cell.Pan));
    }
}
