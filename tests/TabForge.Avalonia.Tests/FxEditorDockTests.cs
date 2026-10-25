using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using TabForge.Avalonia.Views.Fx;
using TabForge.Presenters;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the docking of the plug-in editor area after the FX chain window is shown (FxChainWindow.ShowOver, FxEditorArea.DockSurface): the surface joins
//   the window once it is shown, and a refusal by Windows to create the native editor window is reported on the status line instead of thrown.
// Tests: this class (the real Windows refusal is not reproducible headless; the refusal is injected through PluginEditorSurface.RefuseNativeWindowForTest).
public class FxEditorDockTests
{
    [AvaloniaFact]
    public void EditorSurfaceJoinsTheWindowOnceItIsShown()
    {
        var owner = new Window();
        owner.Show();
        var window = FxTestKit.Window();
        var area = window.GetLogicalDescendants().OfType<FxEditorArea>().Single();

        window.ShowOver(owner);

        Assert.True(area.Surface.IsAttachedToVisualTree());
        window.Close();
        owner.Close();
    }

    [AvaloniaFact]
    public void RefusedNativeEditorWindowIsReportedAndTheWindowStaysOpen()
    {
        var owner = new Window();
        owner.Show();
        var window = FxTestKit.Window();
        var area = window.GetLogicalDescendants().OfType<FxEditorArea>().Single();
        area.Surface.RefuseNativeWindowForTest = true;

        window.ShowOver(owner);

        Assert.True(window.IsVisible);
        Assert.False(area.Surface.IsAttachedToVisualTree());
        Assert.Equal(FxChainPresenter.EditorUnavailableNotice, DialogTestKit.Named<TextBlock>(window, "StatusLine").Text);
        window.Close();
        owner.Close();
    }
}
