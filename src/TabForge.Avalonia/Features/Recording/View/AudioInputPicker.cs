using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.MarkupExtensions;
using TabForge.Models;
using TabForge.Platform;

namespace TabForge.Avalonia.Features.Recording.View;

/// <summary>
/// Owns: the preview window for a track's input picker (<see cref="TrackInputPicker"/>), which has no track of its own.
/// Does not own: the picker, the input lists (AudioInputs), writing the choice to a track.
/// Tests: TabForge.Avalonia.Tests.ArmedRowTests.
/// </summary>
public static class AudioInputPicker
{
    /// <summary>A small window with one picker on the current inputs; its choices are kept in the window only.</summary>
    /// <param name="ui">The UI thread (the picker's device poll).</param>
    /// <param name="midiDevices">Lists the MIDI input devices present now.</param>
    public static Window SampleWindow(IUiThread ui, Func<IReadOnlyList<string>> midiDevices)
    {
        var window = new Window { Title = "Track input", Width = 320, SizeToContent = SizeToContent.Height, CanResize = false, ShowInTaskbar = false };
        window[!Window.BackgroundProperty] = new DynamicResourceExtension("PanelBackground");
        window[!Window.ForegroundProperty] = new DynamicResourceExtension("TextPrimary");
        var audio = AudioInputs.Input1;
        IReadOnlyList<string> midi = new List<string>();
        window.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Children = { new TextBlock { Text = "Input", Margin = new Thickness(0, 0, 0, 4) }, new TrackInputPicker(() => audio, () => midi, id => audio = id, list => midi = list, ui, midiDevices) },
        };
        return window;
    }
}
