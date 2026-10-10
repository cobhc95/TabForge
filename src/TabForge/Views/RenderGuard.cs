using System.Windows;
using System.Windows.Media;
using TabForge.Diagnostics;
using TabForge.Services;

namespace TabForge.Views;

/// <summary>
/// Containment for the self-drawn controls' OnRender. WPF re-runs a failed render on every layout pass, so an exception
/// escaping OnRender becomes an endless series of error dialogs. A control wraps its drawing like this (no allocation unless it fails):
/// <code>try { RenderGuard.Inject("Name"); RenderCore(dc); }
/// catch (Exception ex) when (RenderGuard.Contain(ex, "Name", dc, ActualWidth, ActualHeight)) { }</code>
/// The error is logged once per distinct cause and a small outlined placeholder is drawn. Command-line runs
/// (<see cref="TabEditorControl.RethrowRenderFailures"/>) let the exception through so audits still fail loudly.
/// </summary>
internal static class RenderGuard
{
    /// <summary>Drawing errors contained since start (self-test and diagnostics read it).</summary>
    internal static int Contained;
    /// <summary>Distinct causes written to the diagnostics log (self-test reads it).</summary>
    internal static int LoggedCauses;
    /// <summary>Self-test hook: throws for the named control to prove the containment.</summary>
    internal static Action<string>? FaultInjection;

    private static readonly HashSet<string> Logged = new(StringComparer.Ordinal);
    private static readonly Pen PlaceholderPen = MakePen();

    private static Pen MakePen()
    {
        var brush = new SolidColorBrush(Color.FromRgb(0xC0, 0x4D, 0x5B));
        brush.Freeze();
        var pen = new Pen(brush, 1) { DashStyle = DashStyles.Dash };
        pen.Freeze();
        return pen;
    }

    internal static void Inject(string control) => FaultInjection?.Invoke(control);

    /// <summary>Exception filter: true when the failure was contained (the caller's catch block is then empty).</summary>
    internal static bool Contain(Exception ex, string control, DrawingContext dc, double width, double height)
    {
        if (TabEditorControl.RethrowRenderFailures || ex is OutOfMemoryException) return false;
        Contained++;
        var key = control + "|" + ex.GetType().FullName + "|" + ex.TargetSite + "|" + ex.Message;
        if (Logged.Count < 32 && Logged.Add(key))
        {
            LoggedCauses++;
            System.Diagnostics.Debug.WriteLine($"{control} drawing failed: {ex}");
            if (FaultInjection is null)
            {
                try { DiagnosticFileService.WriteText(FilePathPolicy.DefaultDiagnosticsPath($"render-error-{DateTime.Now:yyyyMMdd-HHmmss}.log"), $"{DateTime.Now:O}{Environment.NewLine}{control}{Environment.NewLine}{ex}"); }
                catch (Exception logError) { System.Diagnostics.Debug.WriteLine($"Render error log could not be written: {logError}"); } // Not logged: the render error log itself failed
            }
        }
        try
        {
            if (width > 3 && height > 3 && !double.IsInfinity(width) && !double.IsInfinity(height))
                dc.DrawRectangle(null, PlaceholderPen, new Rect(1, 1, width - 2, height - 2));
        }
        catch (Exception noteError) { System.Diagnostics.Debug.WriteLine($"Render placeholder failed: {noteError}"); } // Not logged: placeholder note failed: the render is already marked
        return true;
    }
}
