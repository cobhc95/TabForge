using System.Windows;
using System.Windows.Media.Imaging;
using TabForge.Views;

namespace TabForge;

/// <summary>Drawing errors in the self-drawn controls are contained in the app, logged once per cause, and rethrown for command-line runs.</summary>
public static partial class SelfTest
{
    private static void TestRenderGuardContainment()
    {
        var rethrow = TabEditorControl.RethrowRenderFailures;
        var contained0 = RenderGuard.Contained;
        var logged0 = RenderGuard.LoggedCauses;
        var icon = new SettingsNavigationIcon { Kind = "Home", Width = 25, Height = 25 };
        icon.Measure(new Size(25, 25));
        icon.Arrange(new Rect(0, 0, 25, 25));

        void Draw()
        {
            icon.InvalidateVisual();
            icon.UpdateLayout();
            icon.Measure(new Size(25, 25));
            icon.Arrange(new Rect(0, 0, 25, 25));
            var bitmap = new RenderTargetBitmap(25, 25, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            bitmap.Render(icon);
        }

        try
        {
            RenderGuard.FaultInjection = control => { if (control == "SettingsNavigationIcon") throw new InvalidOperationException("injected render fault"); };
            TabEditorControl.RethrowRenderFailures = false;
            var escaped = "";
            try { for (var i = 0; i < 4; i++) Draw(); }
            catch (Exception ex) { escaped = ex.GetType().Name + ": " + ex.Message; }
            Check("a drawing error in a self-drawn control is contained in the app", escaped.Length == 0 && RenderGuard.Contained > contained0,
                escaped.Length > 0 ? escaped : $"contained {RenderGuard.Contained - contained0}");
            Check("the same drawing error is logged once, however often the control repaints",
                RenderGuard.LoggedCauses == logged0 + 1, $"{RenderGuard.LoggedCauses - logged0} causes logged");

            TabEditorControl.RethrowRenderFailures = true;
            var thrown = false;
            try { Draw(); }
            catch (Exception) { thrown = true; }
            Check("a drawing error in a self-drawn control still fails a command-line run", thrown);
        }
        finally
        {
            RenderGuard.FaultInjection = null;
            TabEditorControl.RethrowRenderFailures = rethrow;
        }
        Draw();
        Check("a control draws normally again once the fault is gone", true);
    }
}
