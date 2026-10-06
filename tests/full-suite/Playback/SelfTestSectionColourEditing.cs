using TabForge.Controllers;
using TabForge.Models;
using TabForge.Services;
using TabForge.Views;

namespace TabForge;

public static partial class SelfTest
{
    private static void TestSectionColourEditing()
    {
        var markers = new List<MarkerModel>
        {
            new() { Title = "Intro", ColorHex = "#2E74B5" },
            new() { Title = "Verse", ColorHex = "#3F9B4F" },
            new() { Title = "Chorus", ColorHex = "#2E74B5" },
            new() { Title = "Chorus 2", ColorHex = "#C24B5A" },
        };
        var resolved = SectionColours.Resolve(markers, matchSimilar: true, System.Windows.Media.Colors.Gray);
        var chorus = markers[2];
        var chorusTwo = markers[3];
        markers[0].MeasureIndex = 0;
        markers[1].MeasureIndex = 1;
        markers[2].MeasureIndex = 2;
        markers[3].MeasureIndex = 3;
        var project = new SongProject
        {
            Tracks = { new TrackModel { Measures = { new MeasureModel(), new MeasureModel(), new MeasureModel(), new MeasureModel() } } },
            Markers = markers
        };
        var shown = resolved[chorus];
        Check("section editor proposes the resolved display colour instead of a colliding stored colour",
            shown != System.Windows.Media.Color.FromRgb(0x2E, 0x74, 0xB5) &&
            SectionColours.EditorColourHex(chorus) == Visualization.ColourText.Hex(shown));

        var beforeCancel = ProjectService.Snapshot(project);
        Check("an unchanged editor result is a no-op and leaves automatic colour data untouched",
            !SectionColours.ApplyEdit(markers, chorus, chorus.Title, SectionColours.EditorColourHex(chorus), matchSimilar: true) &&
            !chorus.ColorIsExplicit && !chorusTwo.ColorIsExplicit &&
            ProjectService.Snapshot(project) == beforeCancel);

        Check("a rename alone joins the resulting family without changing stored family colours",
            SectionColours.ApplyEdit(markers, chorus, "Verse 2", SectionColours.EditorColourHex(chorus), matchSimilar: true) &&
            chorus.Title == "Verse 2" && chorus.ColorHex == "#2E74B5" && !chorus.ColorIsExplicit &&
            chorusTwo.ColorHex == "#C24B5A" && !chorusTwo.ColorIsExplicit &&
            SectionColours.Resolve(markers, matchSimilar: true, System.Windows.Media.Colors.Gray)[chorus] ==
            SectionColours.Resolve(markers, matchSimilar: true, System.Windows.Media.Colors.Gray)[markers[1]]);
        SectionColours.ApplyEdit(markers, chorus, "Chorus", SectionColours.EditorColourHex(chorus), matchSimilar: true);

        SectionColours.ApplyEdit(markers, chorus, chorus.Title, "#2E74B5", matchSimilar: true);
        var pinned = SectionColours.Resolve(markers, matchSimilar: true, System.Windows.Media.Colors.Gray);
        Check("a chosen colliding hue stays exact across the whole similar-name family",
            chorus.ColorHex == "#2E74B5" && chorusTwo.ColorHex == "#2E74B5" &&
            chorus.ColorIsExplicit && chorusTwo.ColorIsExplicit && pinned[chorus] == pinned[chorusTwo] &&
            pinned[chorus] == System.Windows.Media.Color.FromRgb(0x2E, 0x74, 0xB5) &&
            SectionColorValueConverter.SectionColour(chorusTwo) == pinned[chorusTwo]);

        var clipboard = new ArrangementController().CaptureSectionSnapshot(project, chorus);
        var roundTrip = ProjectService.RestoreBytes(ProjectService.SnapshotBytes(project));
        var duplicate = new SongProject
        {
            Tracks = { new TrackModel { Measures = { new MeasureModel(), new MeasureModel(), new MeasureModel(), new MeasureModel() } } },
            Markers = { new MarkerModel { MeasureIndex = 0, Title = chorus.Title, ColorHex = chorus.ColorHex, ColorIsExplicit = chorus.ColorIsExplicit } }
        };
        SectionReorderService.Insert(duplicate, 1, new List<List<MeasureModel>> { new() { new MeasureModel(), new MeasureModel() } }, chorus);
        Check("an explicit family colour survives section cloning and project roundtrip",
            clipboard?.Marker?.ColorIsExplicit == true && duplicate.Markers[1].ColorIsExplicit &&
            roundTrip.Markers[2].ColorIsExplicit && roundTrip.Markers[3].ColorIsExplicit);

        SectionColours.ApplyEdit(markers, chorus, chorus.Title, "#AABBCC", matchSimilar: false);
        Check("with similar-colour matching off, an explicit choice changes only the selected marker",
            chorus.ColorHex == "#AABBCC" && chorusTwo.ColorHex == "#2E74B5" && chorus.ColorIsExplicit && chorusTwo.ColorIsExplicit);

        var reorderedFamily = new List<MarkerModel>
        {
            new() { Title = "Verse", ColorHex = "#C24B5A" },
            new() { Title = "Verse 2", ColorHex = "#AABBCC", ColorIsExplicit = true },
        };
        var matchingOff = SectionColours.Resolve(reorderedFamily, matchSimilar: false, System.Windows.Media.Colors.Gray);
        var matchingOn = SectionColours.Resolve(reorderedFamily, matchSimilar: true, System.Windows.Media.Colors.Gray);
        Check("an explicit later family choice leads when earlier automatic members resolve, while matching off keeps each colour",
            matchingOff[reorderedFamily[0]] == Visualization.Draw.Tame(System.Windows.Media.Color.FromRgb(0xC2, 0x4B, 0x5A)) &&
            matchingOff[reorderedFamily[1]] == System.Windows.Media.Color.FromRgb(0xAA, 0xBB, 0xCC) &&
            matchingOn[reorderedFamily[0]] == matchingOn[reorderedFamily[1]] &&
            matchingOn[reorderedFamily[0]] == System.Windows.Media.Color.FromRgb(0xAA, 0xBB, 0xCC));
    }
}
