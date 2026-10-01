using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using TabForge.Controllers;
using TabForge.Diagnostics;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Plugins;
using TabForge.Services;
using TabForge.Visualization;

namespace TabForge.Views;

/// <summary>Separate lightweight visual for section drags; pointer movement never redraws the timeline grid.</summary>
internal sealed class SectionDragOverlay : FrameworkElement
{
    private TrackTimeline.SectionDragSnapshot? _snapshot;
    private DrawingGroup? _blockDrawing;
    private readonly TranslateTransform _translation = new();

    public SectionDragOverlay()
    {
        IsHitTestVisible = false;
        SnapsToDevicePixels = true;
    }

    public void SetPreview(TrackTimeline.SectionDragPreview? preview)
    {
        if (preview is null)
        {
            _snapshot = null;
            _blockDrawing = null;
            _translation.X = 0;
            Visibility = Visibility.Collapsed;
            InvalidateVisual();
            return;
        }

        Visibility = Visibility.Visible;
        _translation.X = preview.X;
        RenderTransform = _translation;
        if (ReferenceEquals(_snapshot, preview.Snapshot)) return;

        _snapshot = preview.Snapshot;
        using (Draw.UseDpi(this)) _blockDrawing = BuildBlockDrawing(_snapshot, ActualHeight);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        try { RenderGuard.Inject("SectionDragOverlay"); RenderCore(dc); }
        catch (Exception ex) when (RenderGuard.Contain(ex, "SectionDragOverlay", dc, ActualWidth, ActualHeight)) { }
    }

    private void RenderCore(DrawingContext dc)
    {
        base.OnRender(dc);
        if (_blockDrawing is not null) dc.DrawDrawing(_blockDrawing);
    }

    private static DrawingGroup BuildBlockDrawing(TrackTimeline.SectionDragSnapshot snapshot, double actualHeight)
    {
        var project = snapshot.Project;
        var geometry = snapshot.Geometry;
        var start = Math.Clamp(snapshot.StartBar, 0, geometry.BarCount);
        var end = Math.Clamp(snapshot.EndBar, start, geometry.BarCount);
        var group = new DrawingGroup();
        if (end <= start)
        {
            group.Freeze();
            return group;
        }

        var originalX = geometry.XOfBar(start);
        var blockWidth = geometry.XOfBar(end) - originalX;
        using (var dc = group.Open())
        {

        var headerY = ArrangementPanel.RulerHeight + 1;
        var headerHeight = ArrangementPanel.SectionHeight - 4;
        var rowTop = ArrangementPanel.RulerHeight + ArrangementPanel.SectionHeight;
        var blockRect = new Rect(0, headerY, blockWidth, Math.Max(headerHeight,
            actualHeight - headerY - 2));
        var tint = Color.FromArgb(48, snapshot.Color.R, snapshot.Color.G, snapshot.Color.B);
        dc.DrawRoundedRectangle(Draw.Solid(snapshot.Color, 0.12), Draw.Pen(snapshot.Color, 8, 0.10),
            blockRect, 4, 4);
        dc.DrawRectangle(Draw.Solid(tint), null,
            new Rect(0, rowTop, blockWidth, Math.Max(0, actualHeight - rowTop)));
        for (var bar = start; bar < end; bar++)
        {
            var sourceX = geometry.XOfBar(bar);
            var nextX = geometry.XOfBar(bar + 1);
            var x = sourceX - originalX;
            if (nextX - sourceX >= 15)
                Draw.At(dc, (bar + 1).ToString(), x + 3, 4, 9, Draw.Solid(Colors.White, 0.9), true);
        }

        // Repaint a compact ghost of each source bar in every track row, preserving the visual
        // relationship between the section header and its complete musical span.
        for (var trackIndex = 0; trackIndex < project.Tracks.Count; trackIndex++)
        {
            var track = project.Tracks[trackIndex];
            var y = rowTop + ArrangementPanel.RowTopOf(project, trackIndex) - snapshot.VerticalScrollOffset;
            if (y + ArrangementPanel.TrackRowHeight < rowTop || y > actualHeight) continue;
            for (var bar = start; bar < end && bar < track.Measures.Count; bar++)
            {
                var sourceX = geometry.XOfBar(bar);
                var nextX = geometry.XOfBar(bar + 1);
                var x = sourceX - originalX;
                var width = Math.Max(1, nextX - sourceX - 1);
                var measure = track.Measures[bar];
                var occupied = measure.SimileOneBar || measure.SimileTwoBar;
                if (!occupied)
                    for (var cellIndex = 0; cellIndex < measure.Cells.Count; cellIndex++)
                        if (measure.Cells[cellIndex].Notes.Count > 0) { occupied = true; break; }
                var cell = new Rect(x + 1, y + 2, Math.Max(1, width - 2), ArrangementPanel.TrackRowHeight - 4);
                dc.DrawRoundedRectangle(Draw.Solid(snapshot.Color, occupied ? 0.46 : 0.13),
                    Draw.Pen(snapshot.Color, 0.8, 0.62), cell, 2, 2);
                if (occupied)
                {
                    var slotWidth = cell.Width / Math.Max(1, measure.Cells.Count);
                    for (var slot = 0; slot < measure.Cells.Count; slot++)
                    {
                        var eventCell = measure.Cells[slot];
                        if (eventCell.Notes.Count == 0 || eventCell.IsRest) continue;
                        var eventX = cell.X + slot * slotWidth;
                        var eventWidth = Math.Max(2, Math.Min(cell.Right - eventX - 1,
                            Math.Max(3, MusicTime.ConsumeSlots(eventCell) * slotWidth - 1)));
                        dc.DrawRoundedRectangle(Draw.Solid(snapshot.Color, 0.82), null,
                            new Rect(eventX, cell.Y + cell.Height / 2 - 2.5, eventWidth, 5), 2, 2);
                    }
                }
            }
        }

        var header = new Rect(0, headerY, Math.Max(2, blockWidth - 2), headerHeight);
        dc.DrawRoundedRectangle(Draw.Solid(Color.FromArgb(230, snapshot.Color.R, snapshot.Color.G, snapshot.Color.B)),
            Draw.Pen(Colors.White, 1.2, 0.85), header, 3, 3);
        if (!string.IsNullOrWhiteSpace(snapshot.Title) && header.Width > 28)
            Draw.At(dc, snapshot.Title, header.X + 6, header.Y + 2, 11, Draw.Solid(Colors.White), true);

        dc.DrawRoundedRectangle(null, Draw.Pen(snapshot.Color, 2.5, 0.98), blockRect, 4, 4);
        }
        group.Freeze();
        return group;
    }
}
