using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using TabForge.Playback;
using TabForge.Presenters;

namespace TabForge.Avalonia.Views;

/// <summary>
/// MIDI output of the selected track (Sound > MIDI / Audio setup): the device its notes are sent to, applied as soon as it changes,
/// and a test note.
/// Owns: the window's wiring and its two buttons. Does not own: the texts and the device choice rule (TrackOutputPresenter), the track edit
/// (the <paramref name="chose"/> callback), the test note (the <paramref name="test"/> callback), the device list (the host's MIDI outputs).
/// The preview has no MIDI outputs: it gets a sample device list (TrackOutputWindow.SampleDevices) and no-op callbacks.
/// Tests: TabForge.Avalonia.Tests.TrackOutputWindowTests.
/// </summary>
public partial class TrackOutputWindow : Window
{
    /// <summary>For the XAML loader and designer only: the app passes the real track, devices and callbacks.</summary>
    public TrackOutputWindow() : this("Demo track", SampleDevices, 0, _ => { }, () => { }) { }

    public TrackOutputWindow(string trackName, IReadOnlyList<MidiOutputDeviceInfo> devices, int current, Action<int> chose, Action test)
    {
        InitializeComponent();
        Title = TrackOutputPresenter.Title;
        HeadingText.Text = TrackOutputPresenter.Heading(trackName);
        HintText.Text = TrackOutputPresenter.Hint;
        TestButton.Content = TrackOutputPresenter.TestButton;
        CloseButton.Content = TrackOutputPresenter.CloseButton;
        DeviceBox.ItemsSource = devices;
        DeviceBox.SelectedIndex = devices.ToList().FindIndex(d => d.DeviceId == current);
        DeviceBox.SelectionChanged += (_, _) =>
        {
            object? selected = DeviceBox.SelectedItem is MidiOutputDeviceInfo device ? device.DeviceId : null;
            if (TrackOutputPresenter.ChosenDeviceId(selected) is { } id) chose(id);
        };
        TestButton.Click += (_, _) => test();
        CloseButton.Click += (_, _) => Close();
    }

    /// <summary>Sample outputs for the preview, which has no MIDI devices.</summary>
    public static IReadOnlyList<MidiOutputDeviceInfo> SampleDevices { get; } =
    [
        new MidiOutputDeviceInfo { DeviceId = 0, Name = "Sample MIDI output 1" },
        new MidiOutputDeviceInfo { DeviceId = 1, Name = "Sample MIDI output 2" },
    ];
}
