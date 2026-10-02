namespace TabForge;

/// <summary>The toolbar tempo box: Enter applies what was typed (clamped), anything else keeps the current tempo.</summary>
public static partial class SelfTest
{
    private static void TestTempoBoxText()
    {
        Check("tempo box: 200 is applied as typed", MainWindow.ResolveTempoText("200", 150) == 200);
        Check("tempo box: spaces around the number are ignored", MainWindow.ResolveTempoText(" 90 ", 150) == 90);
        Check("tempo box: out-of-range values are clamped to 20-400", MainWindow.ResolveTempoText("5", 150) == 20 && MainWindow.ResolveTempoText("999", 150) == 400);
        Check("tempo box: text that is not a number keeps the current tempo", MainWindow.ResolveTempoText("abc", 150) == 150 && MainWindow.ResolveTempoText("", 150) == 150 && MainWindow.ResolveTempoText(null, 150) == 150);
    }
}
