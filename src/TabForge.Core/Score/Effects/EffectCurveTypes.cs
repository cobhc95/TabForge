using System.Globalization;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Views.EffectEditors;

// Owns: the named curve types of the bend and tremolo-bar editors (Bend, Release, Dive, Dip ...), the curve each one sets and when each one is available.
// Does not own: the type list control (CurveSidePanel), the user's presets (EffectPresetStore) or the point rules (EffectCurve).
// Tests: TabForge.Avalonia.Tests.EffectCurveEditorTests.
/// <summary>One curve type: its name and its curve as "offset:value" pairs (offsets 0..60, values in quarter tones); "P" is the level the previous note ended on.</summary>
public sealed record CurveType(string Name, string Curve, bool NeedsPrevious = false);

/// <summary>The curve types of an editor and what choosing one sets.</summary>
public static class EffectCurveTypes
{
    private const string Previous = "P";
    /// <summary>The level a type that starts from the previous note shows when it is listed without one (a full bend, a two tone dive).</summary>
    public const double DefaultBendPrevious = 4, DefaultTremoloPrevious = -8;

    /// <summary>The bend types, in list order.</summary>
    public static IReadOnlyList<CurveType> Bend => new CurveType[]
    {
        new("Bend", "0:0 30:4 60:4"),
        new("Release", "0:P 30:0 60:0", NeedsPrevious: true),
        new("Bend/Release", "0:0 20:4 40:4 60:0"),
        new("Hold", "0:P 60:P", NeedsPrevious: true),
        new("Prebend", "0:4 60:4"),
        new("Prebend/Bend", "0:2 20:2 40:4 60:4"),
        new("Prebend/Release", "0:4 30:4 60:0"),
    };

    /// <summary>The tremolo-bar types, in list order.</summary>
    public static IReadOnlyList<CurveType> Tremolo => new CurveType[]
    {
        new("Dive", "0:0 60:-8"),
        new("Dip", "0:0 30:-4 60:0"),
        new("Hold", "0:P 60:P", NeedsPrevious: true),
        new("Predive", "0:-8 60:-8"),
        new("Predive/Dive", "0:-4 20:-4 60:-8"),
        new("Release up", "0:-8 60:0"),
        new("Inverted dip", "0:0 30:4 60:0"),
        new("Return", "0:0 15:-4 45:-4 60:0"),
        new("Release down", "0:4 60:0"),
    };

    public static IReadOnlyList<CurveType> Of(EffectEditorKind kind) => kind switch
    {
        EffectEditorKind.Bend => Bend,
        EffectEditorKind.TremoloBar => Tremolo,
        _ => Array.Empty<CurveType>(),
    };

    /// <summary>True when the type can be chosen: a type that starts from the previous note needs that note to end on a bend.</summary>
    public static bool Available(CurveType type, double previous) => !type.NeedsPrevious || previous != 0;

    /// <summary>The curve a type sets, with <paramref name="previous"/> as the level of the note before.</summary>
    public static List<BendPointModel> Points(CurveType type, double previous) =>
        type.Curve.Split(' ').Select(pair => pair.Split(':')).Select(a => new BendPointModel
        {
            Offset = double.Parse(a[0], CultureInfo.InvariantCulture),
            Value = a[1] == Previous ? previous : double.Parse(a[1], CultureInfo.InvariantCulture),
        }).ToList();

    /// <summary>The types of <paramref name="kind"/> as listed presets, the previous-note ones at their default level.</summary>
    public static EffectPresetEntry[] Presets(EffectEditorKind kind) => Of(kind).Select(type => new EffectPresetEntry
    {
        Kind = kind.ToString(), Name = type.Name, Points = Points(type, DefaultPrevious(kind)),
    }).ToArray();

    /// <summary>The level a previous-note type shows when it is drawn without a real note before it (the type list icons).</summary>
    public static double DefaultPrevious(EffectEditorKind kind) => kind == EffectEditorKind.Bend ? DefaultBendPrevious : DefaultTremoloPrevious;
}
