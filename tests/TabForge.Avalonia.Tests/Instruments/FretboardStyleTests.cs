using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using TabForge.Avalonia.Platform;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Platform.Theme;
using TabForge.Presenters;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the fretboard look checks: the five presets and the names they are made of, that every texture file the look names ships, the settings'
/// defaults and validation, the real-instrument proportions (margin beyond the outer strings, gauges, inlay positions and sizes), the string vibration
/// levels, that the board draws every inlay shape and respects Off, and that the Customize panel writes each pick to the settings.
/// Tests: FretboardStyles, FretboardRealRules, FretboardArtist, FretboardInlayPlan, SettingsValidator.
/// </summary>
public class FretboardStyleTests
{
    [Fact]
    public void EveryPresetIsMadeOfKnownNamesAndReadsBackAsItself()
    {
        Assert.Equal(5, FretboardStyles.Presets.Count);
        foreach (var (name, look) in FretboardStyles.Presets)
        {
            Assert.Equal(look, FretboardStyles.Normalize(look));
            Assert.Equal(name, FretboardStyles.PresetOf(look));
            Assert.Equal(look, FretboardStyles.OfPreset(name.ToUpperInvariant()));
        }
        Assert.Equal(FretboardStyles.Presets.Select(p => p.Name).Distinct().Count(), FretboardStyles.Presets.Count);
        Assert.Equal(FretboardStyles.Custom, FretboardStyles.PresetOf(FretboardStyles.DefaultLook with { Wood = "Walnut" }));
        Assert.Null(FretboardStyles.OfPreset(FretboardStyles.Custom));
    }

    [Fact]
    public void ANameFromNoListFallsBackToTheDefaultLook()
    {
        var bad = new FretboardStyles.Look("Mahogany", "Neon", "Stars", "Plastic", "Gold", "Rope");
        Assert.Equal(FretboardStyles.DefaultLook, FretboardStyles.Normalize(bad));
        Assert.Equal("Ebony", FretboardStyles.Pick(FretboardStyles.Woods, "ebony", "x"));
    }

    [Fact]
    public void EveryTextureTheCatalogueNamesShips()
    {
        var folder = Path.Combine(TestRepo.CheckoutRoot(), "Source", "src", "TabForge.Avalonia", "Assets", "Fretboards");
        var files = FretboardStyles.Woods.Select(FretboardStyles.WoodFile)
            .Concat(FretboardStyles.Bindings.Select(FretboardStyles.BindingFile).OfType<string>())
            .Concat(FretboardStyles.InlayMaterials.Select(FretboardStyles.InlayFile))
            .Concat(FretboardStyles.CapoLooks.SelectMany(look => new[] { false, true }.Select(shadow => FretboardStyles.CapoFile(look, partial: false, shadow))));
        foreach (var file in files) Assert.True(File.Exists(Path.Combine(folder, file)), $"the look names {file}, which is not in Assets/Fretboards");
    }

    [Fact]
    public void SettingsStartOnTheDefaultLookAndTheValidatorMendsBadNames()
    {
        var editing = new EditingSettings();
        Assert.Equal(FretboardStyles.DefaultLook, FretboardStyles.LookOf(editing));
        Assert.Equal("Mild", editing.FretboardVibration);
        Assert.False(editing.FretboardStringNumbers);
        editing.FretboardWood = "nonsense";
        editing.FretboardVibration = "Wild";
        editing.FretboardInlayShape = "Blocks";
        var settings = new AppSettings { Editing = editing };
        SettingsValidator.Normalize(settings);
        Assert.Equal(FretboardStyles.DefaultLook.Wood, settings.Editing.FretboardWood);
        Assert.Equal("Mild", settings.Editing.FretboardVibration);
        Assert.Equal("Blocks", settings.Editing.FretboardInlayShape);
    }

    [Fact]
    public void ThePreferencesLookRowAppliesAPresetAndCustomIsAState()
    {
        var editing = new EditingSettings();
        FretboardStyles.Apply(editing, FretboardStyles.OfPreset("Ebony blocks"));
        Assert.Equal("Ebony blocks", FretboardStyles.PresetOf(FretboardStyles.LookOf(editing)));
        editing.FretboardCapoLook = "Bronze";
        Assert.Equal(FretboardStyles.Custom, FretboardStyles.PresetOf(FretboardStyles.LookOf(editing)));
        FretboardStyles.Apply(editing, null);
        Assert.Equal("Bronze", editing.FretboardCapoLook);
    }

    [Fact]
    public void VibrationLevelsGrowAndOffIsStill()
    {
        Assert.Equal(0, FretboardStyles.VibrationPeak("Off"));
        Assert.Equal(1.0, FretboardStyles.VibrationPeak("Mild"));
        Assert.Equal(1.0, FretboardStyles.VibrationPeak("garbage"));
        Assert.True(FretboardStyles.VibrationPeak("Medium") > FretboardStyles.VibrationPeak("Mild"));
        Assert.True(FretboardStyles.VibrationPeak("Strong") > FretboardStyles.VibrationPeak("Medium"));
        Assert.True(FretboardStyles.VibrationRate("Slow") < FretboardStyles.VibrationRate("Normal"));
        Assert.True(FretboardStyles.VibrationRate("Fast") > FretboardStyles.VibrationRate("Normal"));
    }

    [Fact]
    public void RealProportionsFollowTheResearchedInstrument()
    {
        // Wood beyond the outer strings: 0.44 of the spacing on a guitar (3.2 of 7.3 mm), 0.34 on a bass (3.5 of 10.4 mm).
        Assert.Equal(3.2 / 7.3, FretboardRealRules.EdgeMarginRatio(bass: false), 1);
        Assert.Equal(3.5 / 10.4, FretboardRealRules.EdgeMarginRatio(bass: true), 1);
        // Strings thicken toward the bass and a bass is heavier than a guitar throughout.
        for (var i = 1; i < 6; i++) Assert.True(FretboardRealRules.GaugeMm(i, 6, false) > FretboardRealRules.GaugeMm(i - 1, 6, false));
        Assert.True(FretboardRealRules.GaugeMm(0, 4, true) > FretboardRealRules.GaugeMm(5, 6, false) * 0.9);
        Assert.False(FretboardRealRules.IsWound(2, 6, false));
        Assert.True(FretboardRealRules.IsWound(3, 6, false));
        Assert.False(FretboardRealRules.IsWound(0, 4, true));
        Assert.True(FretboardRealRules.IsWound(1, 4, true));
        // Position inlays: 3 5 7 9 12 15 17 19 21 24, double at 12 and 24.
        var inlays = Enumerable.Range(1, 24).Where(FretboardRealRules.HasInlay).ToArray();
        Assert.Equal(new[] { 3, 5, 7, 9, 12, 15, 17, 19, 21, 24 }, inlays);
        Assert.Equal(new[] { 12, 24 }, inlays.Where(FretboardRealRules.IsDouble).ToArray());
        Assert.Equal(26, FretboardRealRules.DoubleOffset(26, 6));
        Assert.True(FretboardRealRules.DotDiameter(26, false, false) > FretboardRealRules.DotDiameter(26, false, true));
        Assert.True(FretboardRealRules.FinHeightShare(24) > FretboardRealRules.FinHeightShare(3));
        Assert.InRange(FretboardRealRules.StringWidthPx(5, 6, 26, false), 0.9, 4.4);
    }

    private static InstrumentFretNumberTests.RecordingDrawContext DrawWith(EditingSettings editing, double phaseMs = -1, bool playing = false)
    {
        var track = DemoSongFactory.Create().Tracks[0];
        var view = new InstrumentPanelView();
        view.Look = FretboardSettingsBinding.Look.From(new AppSettings { Editing = editing });
        view.Show(track, System.Array.Empty<InstrumentLitNotes.Lit>());
        view.Measure(new Size(1100, 170));
        view.Arrange(new Rect(0, 0, 1100, 170));
        if (playing)
        {
            view.ShowPlayback(new[] { new TabForge.Playback.NoteEvent { OnsetMs = 1000, DurationMs = 800, StringIndex = 1, Fret = 3, Midi = 67 } }, 1200);
            view.TestSetVibrationPhase(phaseMs);
        }
        var draw = new InstrumentFretNumberTests.RecordingDrawContext();
        view.Draw(draw, light: false);
        return draw;
    }

    [AvaloniaTheory]
    [InlineData("Dots", 12)]
    [InlineData("Small dots", 12)]
    [InlineData("Blocks", 10)]
    [InlineData("Trapezoids", 10)]
    [InlineData("Shark fins", 10)]
    [InlineData("None", 0)]
    public void EveryInlayShapeIsPlannedAtEachMarkedFretAndRebuiltOnlyWhenTheShapeChanges(string shape, int expected)
    {
        var view = new InstrumentPanelView();
        view.Show(DemoSongFactory.Create().Tracks[0], System.Array.Empty<InstrumentLitNotes.Lit>());
        view.Measure(new Size(1100, 170));
        view.Arrange(new Rect(0, 0, 1100, 170));
        var draw = new InstrumentFretNumberTests.RecordingDrawContext();
        var plan = new FretboardInlayPlan();
        var look = FretboardStyles.DefaultLook with { InlayShape = shape };
        var scene = new FretboardScene(draw, _ => default, view.FretboardLayout(), look, false, new FretboardAssets(), plan);
        var placed = plan.For(scene);
        // Ten marked frets: the dot shapes double two of them (twelve inlays); every other shape is one continuous inlay per fret (ten), None plans nothing.
        Assert.Equal(expected, placed.Length);
        Assert.All(placed, p => Assert.True(p.Side > 0));
        Assert.Same(placed, plan.For(scene));
        var other = scene with { Look = look with { InlayShape = shape == "Blocks" ? "Dots" : "Blocks" } };
        Assert.NotSame(placed, plan.For(other));
    }

    [AvaloniaTheory]
    [InlineData(false, 10)]
    [InlineData(true, 15)]
    public void GentleSheenIsConfinedToTheWoodForEveryTexture(bool light, int alpha)
    {
        var layout = FretboardGeometryRules.Compute(new FretboardGeometryRules.Input(6, 24, 24, 1, false, 0), new UiRect(0, 0, 1100, 200));
        var sheen = Palette.Get("FretboardWoodSheen", light);
        Assert.Equal(new UiColor((byte)alpha, 255, 255, 255), sheen);
        foreach (var woodName in FretboardStyles.Woods)
        {
            var draw = new InstrumentFretNumberTests.RecordingDrawContext();
            var scene = new FretboardScene(draw, token => Palette.Get(token, light), layout,
                FretboardStyles.DefaultLook with { Wood = woodName }, false, new FretboardAssets(), new FretboardInlayPlan());
            FretboardArtist.DrawBackdrop(scene);
            var wood = FretboardArtist.Wood(layout, false);
            var bands = draw.Rects.Where(r => r.Fill == sheen).ToArray();
            Assert.Equal(5, bands.Length);
            Assert.All(bands, band =>
            {
                Assert.Equal(wood.X, band.X);
                Assert.Equal(wood.Width, band.Width);
                Assert.True(band.Y >= wood.Y && band.Y + band.Height <= wood.Bottom);
            });
            draw.Rects.Clear();
            FretboardArtist.DrawInlays(scene);
            FretboardArtist.DrawWires(scene);
            FretboardArtist.DrawStrings(scene, _ => default);
            Assert.DoesNotContain(draw.Rects, r => r.Fill == sheen);
            Assert.DoesNotContain(draw.LineStrokes, stroke => stroke.Colour == sheen);
            Assert.DoesNotContain(draw.Ellipses, e => e.Fill == sheen);
        }
    }

    [AvaloniaFact]
    public void AStillStringIsDrawnStillAndOffKeepsASoundingStringStill()
    {
        static double Offset(string level)
        {
            var view = new InstrumentPanelView();
            view.Look = FretboardSettingsBinding.Look.From(new AppSettings { Editing = new EditingSettings { FretboardVibration = level } });
            view.Show(DemoSongFactory.Create().Tracks[0], System.Array.Empty<InstrumentLitNotes.Lit>());
            view.ShowPlayback(new[] { new TabForge.Playback.NoteEvent { OnsetMs = 1000, DurationMs = 800, StringIndex = 1, Fret = 3 } }, 1200);
            view.TestSetVibrationPhase(37);
            return System.Math.Abs(view.StringWobble(1).Offset);
        }
        Assert.Equal(0, Offset("Off"));
        Assert.True(Offset("Mild") > 0);
        Assert.True(Offset("Strong") > Offset("Mild"));
    }

    [AvaloniaFact]
    public void ClickPreviewRingsOnlyThePickedStringAtItsFret()
    {
        var view = new InstrumentPanelView();
        view.Show(DemoSongFactory.Create().Tracks[0], System.Array.Empty<InstrumentLitNotes.Lit>());
        view.StartPreviewVibration(new InstrumentNotePick(64, 2, 5));
        Assert.Equal(5, view.StringWobble(2).Fret);
        Assert.Equal(default, view.StringWobble(1));
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void VibratingStringStaysStillOnNutSide(bool leftHanded)
    {
        var layout = FretboardGeometryRules.Compute(new FretboardGeometryRules.Input(6, 24, 24, 1, leftHanded, 0), new UiRect(0, 0, 1100, 200));
        var draw = new InstrumentFretNumberTests.RecordingDrawContext();
        var scene = new FretboardScene(draw, _ => default, layout, FretboardStyles.DefaultLook, false, new FretboardAssets(), new FretboardInlayPlan());
        FretboardArtist.DrawStrings(scene, i => i == 1 ? (2, 5) : default);
        var anchor = FretboardGeometryRules.FretX(layout, 5);
        var y = FretboardGeometryRules.StringY(layout, 1);
        var bent = draw.LineOps.Where(l => l.Y1 != l.Y2 && System.Math.Abs(l.Y1 - y) < 3 && System.Math.Abs(l.Y2 - y) < 3).ToArray();
        Assert.NotEmpty(bent);
        Assert.All(bent, line => Assert.True(leftHanded ? line.X1 <= anchor && line.X2 <= anchor : line.X1 >= anchor && line.X2 >= anchor));
    }

    [AvaloniaFact]
    public void StringNumbersAreOffByDefaultAndShowWhenAsked()
    {
        Assert.DoesNotContain(DrawWith(new EditingSettings()).Texts, t => t.Text == "E");
        var numbers = DrawWith(new EditingSettings { FretboardStringNumbers = true });
        Assert.True(numbers.Texts.Count > DrawWith(new EditingSettings()).Texts.Count);
    }
}
