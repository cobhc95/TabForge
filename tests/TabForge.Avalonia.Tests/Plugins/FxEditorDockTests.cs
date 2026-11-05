using System;
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
//   the window once it is shown, a refusal by Windows to create the native editor window is reported on the status line instead of thrown, and the frame
//   is the editor's size on screen at any UI scale.
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

    [AvaloniaTheory]
    [InlineData(1.0)]
    [InlineData(0.95)]
    [InlineData(1.25)]
    public void FrameShowsTheWholeEditorAtAnyUiScale(double uiScale)
    {
        const int EditorWidth = 1575, EditorHeight = 1022, Pixel = 1;
        var area = new FxEditorArea(() => IntPtr.Zero, (_, _) => { });
        var window = new Window { Width = 2400, Height = 1600 };
        window.Content = WindowUiScale.Attach(window, area, new TabForge.Services.AppearanceSettings { UiScale = uiScale });
        window.Show();
        area.ShowEditor();
        area.UpdateLayout();

        area.SetEditorSize(EditorWidth, EditorHeight);

        Assert.InRange(area.FrameSize.Width * uiScale * window.RenderScaling, EditorWidth - Pixel, EditorWidth + Pixel);
        Assert.InRange(area.FrameSize.Height * uiScale * window.RenderScaling, EditorHeight - Pixel, EditorHeight + Pixel);
        Assert.False(area.HorizontalBarVisible);
        Assert.False(area.VerticalBarVisible);
        window.Close();
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
