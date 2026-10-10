using System.Windows.Controls;
using TabForge.Views;

namespace TabForge;

/// <summary>The status bar's tempo: the tempo in force at the shown bar, through a real main window (part of <see cref="SelfTest"/>).</summary>
public static partial class SelfTest
{
    /// <summary>The base tempo before the first change, then each change as the position moves; a seek updates it.</summary>
    private static void TestLiveTempoStatus() => RunInWindowFixture((a, context) =>
    {
        var song = DoSong(1, 8);
        song.Tempo = 150;
        song.Tracks[0].Measures[2].TempoChange = 112;   // bar 3
        song.Tracks[0].Measures[4].TempoChange = 90;    // bar 5: a second change
        DoOpen(a, song);
        var editor = LtField<TabEditorControl>(a, "Editor")!;
        var master = LtField<TextBlock>(a, "MasterInfoText")!;
        string At(int bar)
        {
            editor.SetPosition(bar, 0, 1, seekPlayback: false);
            return master.Text;
        }
        Check("live tempo: bar 1 shows the starting tempo", At(0).StartsWith("♩=150"), At(0));
        Check("live tempo: bar 3 shows the new tempo", At(2).StartsWith("♩=112"), At(2));
        Check("live tempo: the change carries to the bars after it", At(3).StartsWith("♩=112"), At(3));
        Check("live tempo: bar 5 shows the second change", At(4).StartsWith("♩=90"), At(4));
        Check("live tempo: back at bar 1 shows the starting tempo again", At(0).StartsWith("♩=150"), At(0));
        Check("live tempo: the toolbar BPM stays the base tempo", song.Tempo == 150);
    });
}
