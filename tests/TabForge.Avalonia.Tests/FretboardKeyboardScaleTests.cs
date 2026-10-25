using System.Linq;
using Avalonia;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Platform;
using TabForge.Presenters;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the keyboard view shows the scale highlight (the shaded style tints the scale's keys, the root more strongly) and, with the note names setting,
/// names every key it is wide enough for.
/// Does not own: the scale rule (ScaleHighlightStyles), the look's settings (FretboardSettingsBinding) or the fretboard's marks (FretboardShadedScaleTests).
/// Tests: TabForge.Avalonia.Tests.FretboardKeyboardScaleTests.
/// </summary>
public sealed class FretboardKeyboardScaleTests
{
    private static readonly Size PaneSize = new(1000, 150);

    [AvaloniaFact]
    public void KeyboardTintsTheScaleKeysAndNamesEveryKeyWithTheNoteNamesSetting()
    {
        var track = DemoSongFactory.Create().Tracks[0];
        var view = new InstrumentPanelView { Kind = InstrumentPanelKind.Keyboard };
        view.Show(track, System.Array.Empty<InstrumentLitNotes.Lit>());
        view.Measure(PaneSize);
        view.Arrange(new Rect(0, 0, PaneSize.Width, PaneSize.Height));
        var settings = new AppSettings { Editing = { ScaleHighlight = "C Major", ScaleHighlightStyle = ScaleHighlightStyles.Shaded } };

        view.Look = FretboardSettingsBinding.Look.From(settings);
        var scaled = new InstrumentFretNumberTests.RecordingDrawContext();
        view.Draw(scaled, light: false);

        settings.Editing.ScaleHighlight = null;
        view.Look = FretboardSettingsBinding.Look.From(settings);
        var plain = new InstrumentFretNumberTests.RecordingDrawContext();
        view.Draw(plain, light: false);
        Assert.True(scaled.Rects.Select(r => r.Fill).Distinct().Count() > plain.Rects.Select(r => r.Fill).Distinct().Count(), "the scale keys are not tinted");

        Assert.DoesNotContain(plain.Texts, t => t.Text == "D");
        settings.Editing.ShowNoteNames = true;
        settings.Editing.ScaleHighlight = "C Major";
        view.Look = FretboardSettingsBinding.Look.From(settings);
        var named = new InstrumentFretNumberTests.RecordingDrawContext();
        view.Draw(named, light: false);
        Assert.Contains(named.Texts, t => t.Text == "D");
    }
}
