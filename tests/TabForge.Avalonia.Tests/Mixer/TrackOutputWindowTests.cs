using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Views;
using TabForge.Playback;
using TabForge.Presenters;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the MIDI output window checks (structure, presenter texts, device choice, the test and close buttons, dark and light PNGs, contrast,
/// the preview's menu item).
/// Does not own: the window, the presenter or the palette. The sample devices come from TrackOutputWindow.SampleDevices.
/// Tests: this class.
/// </summary>
public class TrackOutputWindowTests
{
    private const string Track = "Guitar";

    private static TrackOutputWindow Make(List<int> chosen, int current = 0) =>
        new(Track, TrackOutputWindow.SampleDevices, current, id => chosen.Add(id), () => { });

    [AvaloniaFact]
    public void ControlsMatchWpfWindowRolesAndTextsInOrder()
    {
        var window = Make(new List<int>());
        window.Show();

        // The window's controls in tree order, written from the app's Views/Timeline/TrackOutputWindow.cs: the buttons first
        // (Test sound, Close), then the heading, the device list and the hint.
        var expected = new[]
        {
            ("Button", TrackOutputPresenter.TestButton),
            ("Button", TrackOutputPresenter.CloseButton),
            ("TextBlock", TrackOutputPresenter.Heading(Track)),
            ("ComboBox", ""),
            ("TextBlock", TrackOutputPresenter.Hint),
        };
        var found = DialogTestKit.LayoutControls(window)
            .Where(c => c is Button or TextBlock or ComboBox)
            .Select(c => (Role: c.GetType().Name, Text: c switch
            {
                Button b => b.Content as string ?? "",
                TextBlock t => t.Text ?? "",
                _ => "",
            }))
            .ToList();
        Assert.Equal(expected, found);
        Assert.Equal(TrackOutputPresenter.Title, window.Title);
        Assert.Equal((420d, 210d, 340d, 190d), (window.Width, window.Height, window.MinWidth, window.MinHeight));
        Assert.True(window.FindControl<Button>("CloseButton")!.IsCancel);
        window.Close();
    }

    [AvaloniaFact]
    public void DeviceListShowsTheCurrentOutputAndReportsAChoice()
    {
        var chosen = new List<int>();
        var window = Make(chosen, current: 0);
        window.Show();
        var box = window.FindControl<ComboBox>("DeviceBox")!;
        Assert.Equal(TrackOutputWindow.SampleDevices.Count, box.Items.Count);
        Assert.Equal(0, box.SelectedIndex);
        Assert.Empty(chosen);

        box.SelectedIndex = 1;
        Assert.Equal(new[] { 1 }, chosen);
        window.Close();
    }

    [AvaloniaFact]
    public void MissingDeviceShowsAMarkAndReportsTheSavedName()
    {
        var devices = new List<MidiOutputDeviceInfo>
        {
            new() { DeviceId = 0, Name = "Speakers" },
        };
        var chosen = new List<(int Id, string? Name)>();
        var window = new TrackOutputWindow(Track, devices, 7, "Unplugged", (id, name) => chosen.Add((id, name)), () => { });
        window.Show();
        var box = window.FindControl<ComboBox>("DeviceBox")!;
        Assert.Equal(2, box.Items.Count);
        Assert.Equal(1, box.SelectedIndex);
        Assert.Equal(TrackOutputPresenter.MissingDeviceText("Unplugged"), (box.SelectedItem as MidiOutputDeviceInfo)?.Name);
        Assert.Empty(chosen);

        box.SelectedIndex = 0;
        Assert.Equal(new (int, string?)[] { (0, "Speakers") }, chosen);
        window.Close();
    }

    [AvaloniaFact]
    public void KnownDeviceByNameSelectsItAfterAReorder()
    {
        var devices = new List<MidiOutputDeviceInfo>
        {
            new() { DeviceId = 0, Name = "US-2x2" },
            new() { DeviceId = 1, Name = "Speakers" },
        };
        var chosen = new List<(int Id, string? Name)>();
        var window = new TrackOutputWindow(Track, devices, 1, "US-2x2", (id, name) => chosen.Add((id, name)), () => { });
        window.Show();
        var box = window.FindControl<ComboBox>("DeviceBox")!;
        Assert.Equal(2, box.Items.Count);   // no missing mark: the saved name is listed
        Assert.Equal(0, box.SelectedIndex);
        window.Close();
    }

    [AvaloniaFact]
    public void TestAndCloseButtonsRunTheirActions()
    {
        var testCount = 0;
        var window = new TrackOutputWindow(Track, TrackOutputWindow.SampleDevices, 0, _ => { }, () => testCount++);
        window.Show();
        DialogTestKit.Click(window.FindControl<Button>("TestButton")!);
        Assert.Equal(1, testCount);

        DialogTestKit.Click(window.FindControl<Button>("CloseButton")!);
        Assert.False(window.IsVisible);
    }

    [AvaloniaTheory]
    [InlineData(false, "midi-output-dark.png")]
    [InlineData(true, "midi-output-light.png")]
    public void RendersPngInEachTheme(bool light, string fileName)
    {
        using var theme = DialogTestKit.UseTheme(light);
        var window = Make(new List<int>());
        var path = DialogTestKit.SavePng(window, nameof(TrackOutputWindowTests), fileName);
        Assert.True(new System.IO.FileInfo(path).Length > 0);
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void TextReadsAtLeastFourAndAHalfToOneOnTheWindow(bool light)
    {
        using var theme = DialogTestKit.UseTheme(light);
        var window = Make(new List<int>());
        window.Show();
        foreach (var text in new IBrush?[] { window.FindControl<TextBlock>("HeadingText")!.Foreground, window.FindControl<TextBlock>("HintText")!.Foreground, window.FindControl<Button>("CloseButton")!.Foreground })
            Assert.True(DialogTestKit.Contrast(text, window.Background, "MIDI output") >= 4.5);
        window.Close();
    }

    [AvaloniaFact]
    public void SoundAudioSetupRowOpensMidiOutput()
    {
        var main = new MainWindow();
        main.Show();
        var item = main.GetLogicalDescendants().OfType<MenuItem>().Single(m => m.Header as string == "MIDI / Audio setup");
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        var opened = main.OwnedWindows.OfType<TrackOutputWindow>().ToList();
        Assert.Single(opened);
        foreach (var window in opened) window.Close();
        main.Close();
    }
}
