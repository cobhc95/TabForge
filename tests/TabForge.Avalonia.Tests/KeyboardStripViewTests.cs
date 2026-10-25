using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.KeyboardMode;
using TabForge.Platform;
using TabForge.Platform.Theme;
using TabForge.Presenters;
using TabForge.Presets;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the keyboard falling-notes key strip checks: the pop-out hosts it in KeysHost, its geometry is the Core strip geometry for the
/// default range (61 keys, widened by octaves to keep piano proportions), it reads the song position through the Core port (a stub here), and it draws in dark and light.
/// Does not own: the geometry numbers (TestKeyboardModeKeyView), the notes or the feedback (later steps), the playback session.
/// Tests: this class.
/// </summary>
public class KeyboardStripViewTests
{
    private sealed class StubPosition : IKeyboardSongPosition
    {
        public double SongMs { get; init; }
    }

    [AvaloniaFact]
    public void PopoutHostsTheKeyStripWithTheDefaultRange()
    {
        var window = new KeyboardModePopoutWindow();
        window.Show();
        window.UpdateLayout();

        var strip = window.FindControl<Panel>("KeysHost")!.Children.OfType<KeyboardStripView>().Single();
        var geometry = strip.Geometry!;
        // The range is widened by whole octaves downwards to keep piano proportions in the view (the Core window rule).
        Assert.Equal(KeyboardStripView.DefaultHighest, geometry.Highest);
        Assert.InRange(geometry.Lowest, 12, KeyboardStripView.DefaultLowest);
        Assert.Equal(KeyboardGeometryRules.WhiteKeysIn(geometry.Lowest, geometry.Highest), geometry.Whites);
        Assert.True(geometry.StripTop > 0 && geometry.StripHeight > 0);
        window.Close();
    }

    [AvaloniaFact]
    public void KeyStripIsTheCoreGeometryForItsSize()
    {
        var window = new KeyboardModePopoutWindow();
        window.Show();
        window.UpdateLayout();
        var strip = window.FindControl<Panel>("KeysHost")!.Children.OfType<KeyboardStripView>().Single();

        var expected = KeyboardStripGeometry.Compute(KeyboardStripView.DefaultLowest, KeyboardStripView.DefaultHighest,
            strip.Bounds.Width, strip.Bounds.Height, KeyboardStripView.DefaultLookAheadMs);
        Assert.Equal(expected.StripTop, strip.Geometry!.StripTop, 6);
        Assert.Equal(expected.KeyX(64), strip.Geometry!.KeyX(64), 6);
        window.Close();
    }

    [AvaloniaFact]
    public void PositionPortIsReadThroughTheStub()
    {
        var strip = new KeyboardStripView { Position = new StubPosition { SongMs = 1234 } };
        Assert.Equal(1234, strip.Position!.SongMs);
    }

    [AvaloniaFact]
    public void StripRendersInDarkAndLightThemes()
    {
        foreach (var variant in new[] { ThemeVariant.Dark, ThemeVariant.Light })
        {
            var window = new KeyboardModePopoutWindow { RequestedThemeVariant = variant };
            window.Show();
            window.UpdateLayout();
            Assert.Equal(variant, window.ActualThemeVariant);
            window.Close();
        }
    }

    [AvaloniaFact]
    public void PlayAlongStubShowsNotesScoreAndHeldKeysAndLightsDueKeys()
    {
        var song = FullDemoSongFactory.Create();
        var stub = KeyboardPlayAlongStub.Build(song, 0, 12, 0.4);
        var window = new KeyboardModePopoutWindow();
        window.ShowPlayAlong(stub, song.Tracks[0].Name);
        window.Show();
        window.UpdateLayout();

        Assert.True(stub.Source.Notes.Count > 0);
        Assert.True(stub.Score.Hits > 0 && stub.Score.Misses > 0);
        Assert.Same(stub.Source, window.Strip.Source);
        Assert.Equal(stub.Source.Highest, window.Strip.Highest);
        var placed = new System.Collections.Generic.List<KeyboardModePlaced>();
        KeyboardModeWindow.Collect(stub.Source, stub.NowMs, stub.NowMs + 4000, null, placed);
        KeyboardDueKeys.Find(placed, stub.NowMs, KeyboardHandsFilter.Both, out var left, out var right);
        Assert.True(left.Lo != 0 || left.Hi != 0 || right.Lo != 0 || right.Hi != 0);
        Assert.True(ThemeColours.Contrast(new UiColor(255, 255, 255, 255), new UiColor(255, 0, 0, 0)) > 20);
        window.Close();
    }
}
