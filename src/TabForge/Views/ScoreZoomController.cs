using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using TabForge.Services;
using TabForge.Views;

namespace TabForge.Views;

/// <summary>What the score zoom controller needs from its window.</summary>
internal interface IScoreZoomHost : IPaneHost
{
    ComboBox ZoomCombo { get; }
    ScrollViewer ScoreScroll { get; }
    Border ScorePage { get; }
    ScoreFollowCoordinator Follow { get; }
    /// <summary>True while a document's view state is being restored (zoom-box events are ignored).</summary>
    bool Restoring { get; }
}

// Owns: the score zoom factor, the zoom box, and the page width / one-line layout of the score page in its scroll viewer.
// Does not own: the score drawing (TabEditorControl), follow scrolling (ScoreFollowCoordinator), per-document view state.
// Tests: TestZoomComboShowsValue, TestFollowSurvivesZoom, TestResizeDuringPlayback.
internal sealed class ScoreZoomController
{
    private readonly IScoreZoomHost _host;
    private bool _zoomSync;
    private InputGate<(double Width, double Zoom, bool Horizontal, bool Centre)> _pageInputs;   // the score layout reacts to these, not to height
    private SettleAction? _centreSettle;

    public ScoreZoomController(IScoreZoomHost host) => _host = host;

    /// <summary>The zoom (1 = 100%); 0 means fit width.</summary>
    public double Factor { get; set; }

    private bool Interactive => _host.Window.IsLoaded && !_host.Restoring;

    public void OnComboChanged()
    {
        if (_zoomSync || !Interactive || _host.ZoomCombo.SelectedItem is not ComboBoxItem item) return;
        ApplyZoomText(item.Content?.ToString() ?? "Fit width");
    }

    public void CommitCustomZoom()
    {
        if (!Interactive || _host.ZoomCombo.SelectedItem is ComboBoxItem) return;
        ApplyZoomText(_host.ZoomCombo.Text);
    }

    public void ApplyZoomText(string text, Point? zoomAnchor = null)
    {
        var trimmed = text.Trim();
        if (trimmed.StartsWith("Fit", StringComparison.OrdinalIgnoreCase))
        {
            Factor = 0;
        }
        else if (double.TryParse(trimmed.TrimEnd('%').Trim(), out var percent) && double.IsFinite(percent))
        {
            percent = Math.Clamp(percent, 50, 200);
            Factor = percent / 100.0;
        }
        else
        {
            UpdateZoomControl();
            return;
        }

        UpdateZoomControl();
        ApplyPageWidth(zoomAnchor);
        _host.SetStatus(Factor <= 0 ? "Zoom: fit width" : $"Zoom: {Factor * 100:0}%");
        _host.SaveSettings();
    }

    public void UpdateZoomControl()
    {
        if (_host.ZoomCombo is not { } combo) return;
        // The editable text box only exists once the template is applied (the box lives in a dock pane that
        // starts collapsed), so ShowZoomOn applies the template first; re-entrancy is guarded, and the box
        // re-syncs when it loads or becomes visible.
        var was = _zoomSync;
        _zoomSync = true;
        try { ShowZoomOn(combo, Factor); }
        finally { _zoomSync = was; }
    }

    /// <summary>Makes an editable zoom combo display the given zoom (0 = fit width); returns the text shown.</summary>
    internal static string ShowZoomOn(ComboBox combo, double zoomFactor)
    {
        combo.ApplyTemplate();
        var label = zoomFactor <= 0 ? "Fit width" : $"{Math.Clamp(zoomFactor * 100, 50, 200):0}%";
        var preset = combo.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Content?.ToString(), label, StringComparison.Ordinal));
        combo.SelectedItem = preset;
        if (preset is null) combo.SelectedIndex = -1;
        combo.Text = label;
        return label;
    }

    /// <summary>Steps the zoom (used by the configurable Zoom in/out commands).</summary>
    public void ZoomBy(int direction, Point? zoomAnchor = null)
    {
        var currentPercent = _host.Editor.Zoom * 100;
        var nextPercent = Math.Clamp(Math.Round(currentPercent / 10, MidpointRounding.AwayFromZero) * 10 + direction * 10, 50, 200);
        ApplyZoomText($"{nextPercent:0}%", zoomAnchor);
    }

    public void OnScrollSizeChanged()
    {
        var editor = _host.Editor;
        // A height-only change (a splitter drag, a window drag) leaves the page layout alone; one-line mode only re-centres once the drag settles.
        if (_pageInputs.Changed((Math.Round(_host.ScoreScroll.ActualWidth, 1), Factor, editor.HorizontalScroll, editor.CenterSystems))) { ApplyPageWidth(); return; }
        if (editor.HorizontalScroll) (_centreSettle ??= new SettleAction(CentreHorizontalPage)).Request();
    }

    /// <summary>Applies either fixed-paper zoom or continuous viewport reflow using one shared layout path.</summary>
    public void ApplyPageWidth(Point? zoomAnchor = null)
    {
        var editor = _host.Editor; var scroll = _host.ScoreScroll; var page = _host.ScorePage;
        var follow = _host.Follow; var dispatcher = _host.Window.Dispatcher;
        follow.OnScoreLayoutChanging();
        page.HorizontalAlignment = HorizontalAlignment.Center;
        scroll.HorizontalContentAlignment = HorizontalAlignment.Center;
        // ViewportWidth still describes the previous layout during a resize. ActualWidth is the
        // new constraint here; using the old viewport leaves continuous systems at the narrow width.
        var viewport = scroll.ActualWidth > 1 ? scroll.ActualWidth : scroll.ViewportWidth;
        if (viewport <= 1) { dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(follow.ReanchorAfterZoom)); return; }
        if (editor.HorizontalScroll)
        {
            // One continuous line: the score sizes itself to the line (so edits that lengthen it just
            // grow the scroll range); it starts at the left and scrolls/follows horizontally.
            editor.Zoom = Math.Clamp(Factor <= 0 ? 1.0 : Factor, 0.5, 2.0);
            editor.Width = double.NaN;
            page.Width = double.NaN;
            page.HorizontalAlignment = HorizontalAlignment.Left;
            scroll.HorizontalContentAlignment = HorizontalAlignment.Left;
            scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            editor.InvalidateScoreLayout();
            editor.InvalidateMeasure();
            // Both page and seamless one-line layouts use the available vertical space.
            dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(CentreHorizontalPage));
            dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(follow.ReanchorAfterZoom));
            return;
        }
        page.Margin = new Thickness(0);
        var oldFactor = editor.Zoom;
        var factor = editor.CenterSystems
            ? Math.Clamp(Factor <= 0 ? 1.0 : Factor, 0.5, 2.0)
            : Math.Clamp(Factor <= 0
                ? Math.Max(1, viewport - 2) / TabEditorControl.BasePageWidth
                : Factor, 0.5, 2.0);
        // Page mode keeps a stable logical sheet and scales that sheet as one object. Continuous
        // mode instead changes composition width with zoom, then centres every resulting system.
        var pageWidth = editor.CenterSystems
            ? Math.Max(380, Math.Max(1, viewport - 2) / factor)
            : TabEditorControl.BasePageWidth;
        var width = pageWidth * factor;
        var oldRenderedWidth = editor.ActualWidth > 1 ? editor.ActualWidth : TabEditorControl.BasePageWidth * oldFactor;
        var oldPageLeft = oldRenderedWidth + 2 < viewport
            ? (viewport - oldRenderedWidth - 2) / 2
            : 0;
        var newPageLeft = width + 2 < viewport ? (viewport - width - 2) / 2 : 0;
        var oldHorizontalOffset = scroll.HorizontalOffset;
        var oldVerticalOffset = scroll.VerticalOffset;
        var anchor = zoomAnchor ?? new Point(scroll.ViewportWidth / 2, 0);

        editor.PageWidthOverride = pageWidth;
        editor.Zoom = factor;
        editor.Width = width;
        page.Width = width + 2;
        scroll.HorizontalScrollBarVisibility = width + 2 > viewport + 1 ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
        editor.InvalidateMeasure();
        editor.InvalidateScoreLayout();
        if (Math.Abs(factor - oldFactor) > 0.001)
        {
            dispatcher.BeginInvoke(new Action(() =>
            {
                scroll.ScrollToHorizontalOffset((oldHorizontalOffset + anchor.X - oldPageLeft) * factor / oldFactor
                    - (anchor.X - newPageLeft));
                scroll.ScrollToVerticalOffset((oldVerticalOffset + anchor.Y) * factor / oldFactor - anchor.Y);
                // The zoom-induced scroll is not the user's: keep following and re-anchor on the playhead.
                dispatcher.BeginInvoke(new Action(follow.ReanchorAfterZoom), DispatcherPriority.Background);
            }), DispatcherPriority.Loaded);
        }
        else dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(follow.ReanchorAfterZoom));

        WriteLayoutLog(viewport, width);
    }

    private void WriteLayoutLog(double viewport, double width)
    {
        var layoutLog = Environment.GetEnvironmentVariable("TABFORGE_LAYOUT_LOG") == "1" ? Path.Combine(Path.GetTempPath(), "tabforge-layout.log")
            : Services.Trace.IsOn(Services.Trace.Layout) ? Services.Trace.PathFor(Services.Trace.Layout) : null;
        if (layoutLog is null) return;
        var w = _host.Window; var editor = _host.Editor; var scroll = _host.ScoreScroll;
        try
        {
            DiagnosticFileService.AppendCappedLine(layoutLog,
                $"dpi={VisualTreeHelper.GetDpi(w).DpiScaleX:0.##} window={w.ActualWidth:0}x{w.ActualHeight:0} " +
                $"viewport={viewport:0} page={width:0} editorActual={editor.ActualWidth:0} " +
                $"hOffset={scroll.HorizontalOffset:0} extent={scroll.ExtentWidth:0}",
                InputLimits.MaxLayoutLogBytes);
        }
        catch (Exception ex) { Debug.WriteLine($"Opt-in layout log write failed: {ex}"); }
    }

    private void CentreHorizontalPage()
    {
        if (!_host.Editor.HorizontalScroll) return;
        var page = _host.ScorePage;
        var viewport = _host.ScoreScroll.ViewportHeight;
        var height = page.ActualHeight;
        var top = viewport > 1 && height > 1 ? Math.Max(0, Math.Floor((viewport - height) / 2)) : 0;
        if (Math.Abs(page.Margin.Top - top) > 0.5) page.Margin = new Thickness(0, top, 0, 0);
    }
}
