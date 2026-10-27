using System.Collections.Generic;
using System.Linq;
using TabForge.Platform.Theme;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>Pins the shared track, section and preset colour lists, so a later edit cannot drift the colours a song or picker gets.</summary>
public sealed class NamedColourSwatchesTests
{
    [Fact]
    public void SharedColourListsKeepTheirValuesAndOrder()
    {
        Assert.Equal(
            new[]
            {
                ("Red", "#B8403A"), ("Orange", "#C46A2B"), ("Amber", "#B8902A"), ("Yellow", "#A89A2E"), ("Olive", "#7A8A34"),
                ("Green", "#3F8F4E"), ("Teal", "#2E8A7E"), ("Turquoise", "#2A96A6"), ("Blue", "#3A6FB8"), ("Indigo", "#5357B0"),
                ("Purple", "#7A4FB0"), ("Magenta", "#A4468E"), ("Pink", "#B8577A"), ("Brown", "#8A5E3C"), ("Grey", "#6E7580"), ("Slate", "#4A5563"),
            },
            NamedColourSwatches.All.ToArray());
        Assert.Equal(
            new[]
            {
                ("Blue", "#2E74B5"), ("Green", "#3FB950"), ("Gold", "#D8A032"), ("Violet", "#8B5CF6"),
                ("Teal", "#00A6A6"), ("Rose", "#E06C75"), ("Slate", "#64748B"), ("Pink", "#C45A9A"),
            },
            NamedColourSwatches.Sections.ToArray());
        Assert.Equal("#F61A16", NamedColourSwatches.TrackDefaultColour);
        Assert.Equal("#2E74B5", NamedColourSwatches.SectionDefaultColour);
    }

    [Fact]
    public void PresetListKeepsItsValuesAndOrder()
    {
        Assert.Equal(
            new[]
            {
                ("Glide blue", "#4C9AFF"), ("Cyan", "#22D3EE"), ("Teal", "#14B8A6"), ("Emerald", "#10B981"),
                ("Green", "#3FB950"), ("Lime", "#84CC16"), ("Violet", "#8B5CF6"), ("Indigo", "#6366F1"),
                ("Magenta", "#D946EF"), ("Rose", "#F43F5E"), ("Crimson", "#DC2626"), ("Orange", "#F97316"),
                ("Amber", "#F59E0B"), ("White", "#F2F4F6"), ("Silver", "#98A1AE"), ("Slate", "#4B5563"),
                ("Charcoal", "#23272D"), ("Black", "#111111"),
            },
            NamedColourSwatches.Presets.ToArray());
    }

    [Fact]
    public void TrackRotationStartsWithTheDefaultTrackColour()
    {
        Assert.Equal(
            new[] { "#F61A16", "#ED2224", "#F4E014", "#2248E8", "#35B954", "#D850C6" },
            NamedColourSwatches.TrackRotationColours.ToArray());
    }

    [Fact]
    public void LabelOfGivesPaletteNamesAndCustomHex()
    {
        Assert.Equal("Red", NamedColourSwatches.LabelOf("#b8403a"));
        Assert.Equal("Glide blue", NamedColourSwatches.LabelOf("#4C9AFF"));
        Assert.Equal("Custom (#A12424)", NamedColourSwatches.LabelOf("#a12424"));
    }

    [Fact]
    public void ListsAreTypedReadOnly()
    {
        // Compile-time check: the lists are exposed as read-only, so a caller cannot write into them.
        IReadOnlyList<(string Name, string Hex)> all = NamedColourSwatches.All;
        IReadOnlyList<string> rotation = NamedColourSwatches.TrackRotationColours;
        Assert.Equal(16, all.Count);
        Assert.Equal(6, rotation.Count);
    }
}
