using System.Windows;
using System.Windows.Controls;
using TabForge.Views;

namespace TabForge;

/// <summary>Tools > Chord finder… and Tools > Song stats… open a small themed window each and close from their Close button (Esc).</summary>
public static partial class SelfTest
{
    private static void TestToolsChordFinderAndSongStats() => RunInWindowFixture((window, context) =>
    {
        var previous = DialogHost.Capture;
        var captured = new List<Window>();
        try
        {
            var tools = LtField<Menu>(window, "MainMenu")!.Items.OfType<MenuItem>().Single(m => m.Header as string == "_Tools");
            foreach (var header in new[] { "Chord finder…", "Song stats…" })
            {
                DialogHost.Capture = w => { captured.Add(w); return false; };   // the window is built and returned, not shown
                tools.Items.OfType<MenuItem>().Single(m => m.Header as string == header).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            }
            var chord = captured.ElementAtOrDefault(0);
            var stats = captured.ElementAtOrDefault(1);
            Check("Tools > Chord finder… opens the Chord finder window",
                captured.Count == 2 && chord?.Title == "Chord finder", string.Join(" | ", captured.Select(w => w.Title)));
            Check("Tools > Song stats… opens the Song stats window", stats?.Title == "Song stats");
            Check("Chord finder and Song stats close from their Close button (Esc)",
                chord is not null && stats is not null &&
                Logical<Button>(chord).Any(b => b.IsCancel && b.Content as string == "Close") &&
                Logical<Button>(stats).Any(b => b.IsCancel && b.Content as string == "Close"));
            Check("the practice panel is gone from the side (no Practice / Mixer dock panel)",
                window.GetType().GetField("LowerPanelScroll", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic) is null);
        }
        finally { DialogHost.Capture = previous; }
    });
}
