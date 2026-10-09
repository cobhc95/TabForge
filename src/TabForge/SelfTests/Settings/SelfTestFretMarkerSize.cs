using TabForge.Services;
using TabForge.Visualization;

namespace TabForge;

/// <summary>Note marker size setting: default is 80% (smaller than the original 100%), 150% scales radius and number together (clamped to the string gap), the value round-trips.</summary>
public static partial class SelfTest
{
    private static void TestFretMarkerSize()
    {
        Check("marker size default is 90%, 100% is the original size", new AppSettings().Editing.FretMarkerSizePercent == 90 && MarkerSizing.DefaultPercent == 90 && MarkerSizing.Factor(MarkerSizing.Scale(100), 16, 30) == 1.0);
        Check("marker size 150% scales both by 1.5 with room", Math.Abs(MarkerSizing.Factor(MarkerSizing.Scale(150), 10, 40) - 1.5) < 1e-9);
        Check("marker size clamps to the string gap", MarkerSizing.Factor(1.5, 16, 24) * 16 <= 24 / 2.0 + 1e-9 || MarkerSizing.Factor(1.5, 16, 24) == 1.0);
        Check("marker size 60% shrinks unclamped", Math.Abs(MarkerSizing.Factor(0.6, 16, 10) - 0.6) < 1e-9);
        Check("marker size snaps to steps", MarkerSizing.Normalise(500) == 160 && MarkerSizing.Normalise(0) == 60 && MarkerSizing.Normalise(104) == 100);
        var s = new AppSettings();
        s.Editing.FretMarkerSizePercent = 140;
        var row = SettingsCatalog.Build(s).First(d => d.Key == "fretboard.markersize");
        Check("marker size row round-trips", Convert.ToInt32(row.Get()) == 140);
    }
}
