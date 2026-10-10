using TabForge.Views;
using TabForge.Views.Score;

namespace TabForge;

public static partial class SelfTest
{
    private static void TestTempoMarkSize()
    {
        // Both sizes go through ScoreText.MakeTextIn with the same zoom and area scale, so the constants give the drawn ratio.
        var black = ScoreText.Brush(System.Windows.Media.Colors.Black);
        var tempo = ScoreText.MakeTextIn(ScoreTextArea.BarInfo, "♩ = 112", ScoreMarkText.TempoSize, black, System.Windows.FontWeights.Bold);
        var ratio = ScoreMarkText.TempoSize / 9.0;
        Check("tempo mark is 30-40% larger than the 9 pt bar labels", ratio >= 1.3 && ratio <= 1.4);
        Check("tempo mark is larger than the beat text", ScoreMarkText.TempoSize > ScoreMarkText.BeatTextSize);
        Check("tempo mark text is measured", tempo.Width > 0);

        // A dense tempo-change bar: bar number, bar-start tempo, a mid-bar tempo that overlaps it and the section title stack without overlap.
        var sky = new MarkSkyline();
        const double staffTop = 200;
        var placed = new List<System.Windows.Rect>();
        void Stack(string text, double size, double x, double distance)
        {
            var ft = ScoreText.MakeTextIn(ScoreTextArea.BarInfo, text, size, black, System.Windows.FontWeights.Bold);
            var h = size * 1.2;
            var top = sky.PlaceAbove(x, x + ft.Width, h, staffTop - distance);
            placed.Add(new System.Windows.Rect(x, top, ft.Width, h));
        }
        Stack("87", 9, 24, 14);
        Stack("♩ = 112", ScoreMarkText.TempoSize, 24, 14);
        Stack("♩ = 96", ScoreMarkText.TempoSize, 60, 14);
        Stack("Interlude: Glass", 10, 24, 30);
        var overlapping = 0;
        for (var i = 0; i < placed.Count; i++)
            for (var j = i + 1; j < placed.Count; j++)
                if (placed[i].IntersectsWith(placed[j])) overlapping++;
        Check("tempo marks and neighbouring labels do not overlap", overlapping == 0);
        Check("bar-start tempo mark sits above the staff row", placed[1].Bottom <= staffTop - 14 + 0.01);
    }
}
