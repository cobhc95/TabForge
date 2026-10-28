using System;
using TabForge.AudioEngine.Plugins;
using TabForge.Avalonia.Views.Fx;
using TabForge.Models;
using TabForge.Plugins;
using TabForge.Presenters;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the generic plug-in controls decisions (which formats have a panel, forced by the menu item, fallback when the native editor cannot open or this system has no editor host)
/// and the VST3 plain/normalized value mapping of the panel.
/// Does not own: the panel controls and the engine calls (FxParameterTests).
/// Tests: this class.
/// </summary>
public class FxGenericControlsTests
{
    [Theory]
    [InlineData(PluginFormats.Vst3, true)]
    [InlineData(PluginFormats.Clap, true)]
    [InlineData(PluginFormats.Vst2, true)]
    [InlineData("", true)]
    [InlineData("AU", false)]
    public void Formats_with_parameters(string format, bool expected) => Assert.Equal(expected, FxGenericControls.Supports(format));

    [Fact]
    public void Native_editor_that_opens_does_not_open_the_panel() =>
        Assert.False(FxGenericControls.ShouldShow(true, FxEditorMode.Docked, forced: false, editorFailed: false));

    [Fact]
    public void Failed_docked_editor_opens_the_panel() =>
        Assert.True(FxGenericControls.ShouldShow(true, FxEditorMode.Docked, forced: false, editorFailed: true));

    [Theory]
    [InlineData(FxEditorMode.Docked, true)]
    [InlineData(FxEditorMode.Floating, true)]
    [InlineData(FxEditorMode.Blocked, false)]
    [InlineData(FxEditorMode.None, false)]
    public void Forcing_the_panel_needs_a_loaded_plugin(FxEditorMode mode, bool expected) =>
        Assert.Equal(expected, FxGenericControls.ShouldShow(true, mode, forced: true, editorFailed: false));

    [Fact]
    public void Unsupported_plugin_never_shows_the_panel() =>
        Assert.False(FxGenericControls.ShouldShow(false, FxEditorMode.Docked, forced: true, editorFailed: true));

    [Fact]
    public void System_without_editor_host_falls_back_only_without_a_native_area()
    {
        Assert.True(FxGenericControls.NativeEditorMissing(IntPtr.Zero, supported: false));
        Assert.False(FxGenericControls.NativeEditorMissing(IntPtr.Zero, supported: true));   // Windows: the area is still being created
        Assert.False(FxGenericControls.NativeEditorMissing((IntPtr)5, supported: false));    // an editor host gave the area a handle
    }

    [Fact]
    public void Menu_command_is_in_the_catalogue_and_needs_list_focus()
    {
        Assert.Equal("Show generic controls", FxGenericControls.MenuText);
        Assert.True(TabForge.Services.Features.FxChainIds.NeedsListFocus(TabForge.Services.Features.FxChainIds.ShowGenericControls));
    }

    [Theory]
    [InlineData(0, 0.0, 0.0)]
    [InlineData(0, 0.37, 0.37)]
    [InlineData(4, 0.5, 2.0)]
    [InlineData(4, 0.62, 2.0)]
    [InlineData(1, 1.0, 1.0)]
    public void Plain_value_of_a_normalized_one(int steps, double normalized, double plain) => Assert.Equal(plain, Vst3ParameterMap.ToPlain(normalized, steps), 9);

    [Theory]
    [InlineData(0, 0.37, 0.37)]
    [InlineData(0, 7.0, 1.0)]
    [InlineData(0, -2.0, 0.0)]
    [InlineData(4, 2.0, 0.5)]
    [InlineData(4, 2.4, 0.5)]
    [InlineData(4, 99, 1.0)]
    public void Normalized_value_of_a_plain_one(int steps, double plain, double normalized) => Assert.Equal(normalized, Vst3ParameterMap.ToNormalized(plain, steps), 9);

    [Fact]
    public void Stepped_range_is_whole_steps_and_continuous_is_unit()
    {
        Assert.Equal((0.0, 4.0), Vst3ParameterMap.Range(4));
        Assert.Equal((0.0, 1.0), Vst3ParameterMap.Range(0));
    }
}
