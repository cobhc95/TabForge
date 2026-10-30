using System;
using System.Collections.Generic;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Views.Fx;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: when the docked plug-in editor shows: the pure rule (not under the BYPASSED stamp, not while another window covers the FX window),
/// and the area applying it by hiding the native surface and telling the engine popup (a fake call records the popup and its visibility).
/// Does not own: the native ShowWindow/RedrawWindow calls or the windows.
/// Tests: this class.
/// </summary>
public class PluginEditorVisibilityTests
{
    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    public void EditorShowsOnlyWithoutTheStampAndWithoutACover(bool stampShown, bool covered, bool expected) =>
        Assert.Equal(expected, PluginEditorVisibility.EditorShows(stampShown, covered));

    [AvaloniaFact]
    public void ACoverHidesAndRestoresTheSurfaceAndPopup()
    {
        var calls = new List<(IntPtr Popup, bool Visible)>();
        var area = new FxEditorArea(() => (IntPtr)9, (popup, visible) => calls.Add((popup, visible)));
        area.ShowEditor();
        Assert.True(area.Surface.IsVisible);

        Assert.True(area.SetCovered(true));
        Assert.False(area.Surface.IsVisible);
        Assert.Equal(((IntPtr)9, false), calls[^1]);
        Assert.False(area.SetCovered(true));   // a repeated cover is already applied

        Assert.True(area.SetCovered(false));
        Assert.True(area.Surface.IsVisible);
        Assert.Equal(((IntPtr)9, true), calls[^1]);
    }

    [AvaloniaFact]
    public void TheStampKeepsTheEditorHiddenWhenTheCoverGoes()
    {
        var calls = new List<(IntPtr Popup, bool Visible)>();
        var area = new FxEditorArea(() => (IntPtr)9, (popup, visible) => calls.Add((popup, visible)));
        area.ShowEditor();

        area.ShowOverlay(true, light: false);
        area.SetCovered(true);
        area.SetCovered(false);
        Assert.False(area.Surface.IsVisible);
        Assert.Equal(((IntPtr)9, false), calls[^1]);

        area.ShowOverlay(false, light: false);
        Assert.True(area.Surface.IsVisible);
        Assert.Equal(((IntPtr)9, true), calls[^1]);
    }
}
