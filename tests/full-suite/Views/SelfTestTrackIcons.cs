using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using TabForge.Controllers;
using TabForge.Models;
using TabForge.Services;
using TabForge.Views;

namespace TabForge;

/// <summary>The track-row instrument icons: the mapping, the icon button, the catalogue it opens, and the removed dropdown column (part of <see cref="SelfTest"/>).</summary>
public static partial class SelfTest
{
    /// <summary>Every catalogue sound, drum kit and the audio track has an icon with artwork; one icon per instrument.</summary>
    private static void TestTrackSilhouetteMap()
    {
        var missing = InstrumentCatalog.All.Select(TrackSilhouette.KeyFor).Where(k => !TrackSilhouette.Has(k)).Distinct().ToList();
        Check("track icons: every catalogue instrument maps to an icon with artwork", missing.Count == 0, string.Join(", ", missing));
        Check("track icons: all 128 programs, the drum kits and the audio track have artwork",
            Enumerable.Range(0, 128).All(p => TrackSilhouette.Has(TrackSilhouette.KeyFor(p, false))) && TrackSilhouette.Has(TrackSilhouette.KeyFor(0, true)) && TrackSilhouette.Has(TrackSilhouette.AudioKey));
        Check("track icons: the families get their own icons (guitar, bass, drums, piano, voice differ)",
            new[] { TrackSilhouette.KeyFor(29, false), TrackSilhouette.KeyFor(33, false), TrackSilhouette.KeyFor(0, true), TrackSilhouette.KeyFor(0, false), TrackSilhouette.KeyFor(53, false) }.Distinct().Count() == 5);

        // One icon per instrument: two Distortion Guitar tracks with different string counts show the same icon.
        var p = new SongProject();
        var tracks = new TrackController();
        var six = tracks.CreateTrack(p, TrackKind.Guitar);
        var seven = tracks.CreateTrack(p, TrackKind.Guitar);
        p.Tracks.Add(six); p.Tracks.Add(seven);
        tracks.ApplyEdit(p, new TrackEditRequest(0, TrackEditKind.SelectInstrument, "Distortion Guitar"));
        tracks.ApplyEdit(p, new TrackEditRequest(1, TrackEditKind.SelectInstrument, "Distortion Guitar"));
        seven.StringTunings.Add(35);
        Check("track icons: the same instrument always shows the same icon (6- and 7-string Distortion Guitar)",
            six.StringTunings.Count != seven.StringTunings.Count && TrackRowWidgets.IconKeyOf(six) == TrackRowWidgets.IconKeyOf(seven) && TrackRowWidgets.IconKeyOf(six) == TrackSilhouette.KeyFor(30, false));
        var element = TrackSilhouette.Element(TrackRowWidgets.IconKeyOf(six), 24);
        Check("track icons: the icon is vector art (a Viewbox of paths, no bitmap)",
            element is Viewbox { Child: Canvas canvas } && canvas.Children.OfType<System.Windows.Shapes.Path>().Any() && !VisualDescendants<Image>(element).Any());
    }

    private static IEnumerable<T> TiLogical<T>(DependencyObject root) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is T match) yield return match;
            foreach (var deeper in TiLogical<T>(child)) yield return deeper;
        }
    }

    /// <summary>The icon button between the cogwheel and the record button, the catalogue it opens, one undo step, the audio icon, the dropdown gone.</summary>
    private static void TestTrackIconButton() => RunInWindowFixture((a, context) =>
    {
        var arrangement = LtField<ArrangementPanel>(a, "Arrangement")!;
        var previous = DialogHost.Capture;
        try
        {
            Check("track icons: the default row is 34 px (new settings too) and the icon is 24 px",
                ArrangementPanel.DefaultTrackRowHeight == 34 && new TimelineSettings().TrackRowHeight == 34 && TrackRowWidgets.IconSize == 24);
            Check("track icons: the INSTRUMENT dropdown column is gone; the icon column sits right after the cogwheel",
                !TrackColumnLayout.DefaultOrder.Contains("instrument") && TrackColumnLayout.DefaultOrder[0] == "settings" && TrackColumnLayout.DefaultOrder[1] == "kind" && TrackColumnLayout.DefaultOrder[2] == "colour");

            var song = DoSong(1);
            song.Tracks.Add(new TrackController().CreateTrack(song, TrackKind.Audio));
            var doc = DoOpen(a, song);
            arrangement.UpdateLayout();
            var rows = VisualDescendants<Border>(arrangement).Where(b => (AutomationProperties.GetName(b) ?? "").StartsWith("Track ")).ToList();
            var guitarRow = rows.First(b => AutomationProperties.GetName(b)!.StartsWith("Track 1:"));
            var audioRow = rows.First(b => AutomationProperties.GetName(b)!.StartsWith("Track 2:"));
            var button = VisualDescendants<Button>(guitarRow).FirstOrDefault(b => (AutomationProperties.GetName(b) ?? "").StartsWith("Instrument:"));
            var guitar = doc.Project.Tracks[0];
            Check("track icons: an instrument row has the icon button with the instrument's name in its tooltip",
                button is not null && (button.ToolTip as string) == $"{guitar.InstrumentName} — click to change");
            Check("track icons: no row has a dropdown or the old picker button",
                !VisualDescendants<ComboBox>(guitarRow).Any() && !VisualDescendants<Button>(arrangement).Any(b => (b.ToolTip as string ?? "") == "Instrument / VST for this track"));
            if (button is not null)
            {
                var gridOf = VisualDescendants<Grid>(guitarRow).First(g => g.ColumnDefinitions.Count == TrackColumnLayout.DefaultOrder.Length);
                int Column(UIElement e) { DependencyObject? d = e; while (d is not null && System.Windows.Media.VisualTreeHelper.GetParent(d) != gridOf) d = System.Windows.Media.VisualTreeHelper.GetParent(d); return d is UIElement u ? Grid.GetColumn(u) : -1; }
                var cog = VisualDescendants<Button>(guitarRow).First(b => (AutomationProperties.GetName(b) ?? "").EndsWith("properties"));
                var arm = VisualDescendants<RecordArmButton>(guitarRow).First();
                Check("track icons: the icon sits between the cogwheel and the record button", Column(cog) == 0 && Column(button) == 1 && Column(arm) == 2, $"{Column(cog)} {Column(button)} {Column(arm)}");
            }

            // The click opens the catalogue on the current family, the others collapsed, the search box ready; choosing applies in one undo step.
            var family = InstrumentCatalog.ForTrack(guitar.InstrumentName, guitar.MidiProgram, guitar.MidiChannel == 9)?.Category;
            List<string> openFamilies = new();
            var searchReady = false;
            DialogHost.Capture = dialog =>
            {
                var panels = TiLogical<WrapPanel>(dialog).ToList();
                var headers = TiLogical<TextBlock>(dialog).Where(t => t.FontSize == 13).ToList();
                openFamilies = headers.Where((h, i) => i < panels.Count && panels[i].Visibility == Visibility.Visible).Select(h => h.Text.TrimStart('▾', '▸', ' ')).ToList();
                searchReady = FocusManager.GetFocusedElement(dialog) is TextBox;
                var search = TiLogical<TextBox>(dialog).First();
                search.Text = "Slap Bass";
                var tile = TiLogical<Border>(dialog).First(b => b.Visibility == Visibility.Visible && (b.ToolTip as string ?? "").StartsWith("Slap Bass"));
                tile.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonDownEvent });
                return true;
            };
            var undoBefore = doc.Undo.UndoCount;
            button?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check("track icons: the catalogue opens with only the current instrument's family open", openFamilies.Count == 1 && openFamilies[0] == family, $"{guitar.InstrumentName} ({family}): " + string.Join(",", openFamilies));
            Check("track icons: the catalogue's search box has the focus", searchReady);
            Check("track icons: choosing changes the instrument in one undo step", guitar.InstrumentName == "Slap Bass" && doc.Undo.UndoCount == undoBefore + 1, $"{guitar.InstrumentName} {doc.Undo.UndoCount - undoBefore}");
            CseUndo(a);
            Check("track icons: one undo restores the instrument", doc.Project.Tracks[0].InstrumentName != "Slap Bass");

            // The audio row: the waveform icon, not clickable.
            arrangement.UpdateLayout();
            audioRow = VisualDescendants<Border>(arrangement).First(b => (AutomationProperties.GetName(b) ?? "").StartsWith("Track 2:"));
            var audioIcon = VisualDescendants<Border>(audioRow).FirstOrDefault(b => AutomationProperties.GetName(b) == "Audio track");
            Check("track icons: an audio row shows the waveform icon with the tooltip Audio track", audioIcon is not null && (audioIcon.ToolTip as string) == "Audio track");
            Check("track icons: the audio icon is not clickable (no button, not focusable)",
                audioIcon is not null && !VisualDescendants<Button>(audioIcon).Any() && !audioIcon.Focusable && !VisualDescendants<Button>(audioRow).Any(b => (AutomationProperties.GetName(b) ?? "").StartsWith("Instrument:")));
        }
        finally { DialogHost.Capture = previous; }
    });
}
