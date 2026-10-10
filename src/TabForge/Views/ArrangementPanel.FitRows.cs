using TabForge.Controllers;

namespace TabForge.Views;

// Owns: the ArrangementPanel side of ITrackListRows (the constants the fit controller reads).
public sealed partial class ArrangementPanel : ITrackListRows
{
    double ITrackListRows.DefaultRowHeight => DefaultTrackRowHeight;
    double ITrackListRows.MaxRowHeight => MaxTrackRowHeight;
    double ITrackListRows.CollapsedHeight => CollapsedPaneHeight;
    void ITrackListRows.BeginResizePreview() => BeginResizePreview();
    void ITrackListRows.EndResizePreview() => EndResizePreview();
}
