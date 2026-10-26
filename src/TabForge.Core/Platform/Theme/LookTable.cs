using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TabForge.Platform.Theme;

/// <summary>
/// One condition of a state rule. A property condition (<see cref="Property"/>) names a property of the control (or of the template part
/// <see cref="Source"/>); an attached property is written "Owner.Name". A data condition (<see cref="Binding"/>) names a bound path with an
/// optional source after a bar: "IsActive", "Tag|Self", "IsSelected|Ancestor:ListBoxItem", "Text|Element:SearchBox".
/// </summary>
public sealed record LookCondition(
    [property: JsonPropertyName("property")] string? Property,
    [property: JsonPropertyName("value")] string Value,
    [property: JsonPropertyName("source")] string? Source = null,
    [property: JsonPropertyName("binding")] string? Binding = null);

/// <summary>
/// One change a rule makes: on the template part <see cref="Part"/> (null: the control), set <see cref="Property"/> ("Name" on the control,
/// "Owner.Name" on a part, e.g. "Border.Background") to the theme colour <see cref="Brush"/> (a theme brush name, painted per theme) or to
/// <see cref="Literal"/> (a plain value such as "0.45", "Visible", "2").
/// </summary>
public sealed record LookSetter(
    [property: JsonPropertyName("part")] string? Part,
    [property: JsonPropertyName("property")] string Property,
    [property: JsonPropertyName("brush")] string? Brush = null,
    [property: JsonPropertyName("literal")] string? Literal = null);

/// <summary>A rule: when every condition holds, the setters apply. Rules apply in list order, a later one winning.</summary>
public sealed record LookRule(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("when")] IReadOnlyList<LookCondition> When,
    [property: JsonPropertyName("set")] IReadOnlyList<LookSetter> Set);

/// <summary>A control look: the control kind it styles ("Button", "MenuItem", "ScrollBar") and its state rules.</summary>
public sealed record Look(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("rules")] IReadOnlyList<LookRule> Rules);

/// <summary>
/// The theme table's state rules for every control look, read from the JSON files in Platform/Theme/Looks (embedded in the app). The table is
/// toolkit-neutral: the adapter builds template triggers from it (<c>LookRule</c> markup), another toolkit builds its own state styles.
/// Owns: reading and finding looks. Does not own: colour values (AvaloniaLooks paints the named brushes per theme) or template shapes (XAML).
/// Tests: no dedicated test class.
/// </summary>
public static class LookTable
{
    private static readonly Lazy<IReadOnlyDictionary<string, Look>> All = new(Load);

    /// <summary>The look named <paramref name="name"/>; throws for an unknown look so a typo fails at load.</summary>
    public static Look Get(string name) => All.Value.TryGetValue(name, out var look) ? look : throw new ArgumentException($"No look named '{name}' in the theme table.");

    /// <summary>The rule <paramref name="rule"/> of the look <paramref name="look"/>.</summary>
    public static LookRule Rule(string look, string rule) =>
        Get(look).Rules.FirstOrDefault(r => r.Name == rule) ?? throw new ArgumentException($"The look '{look}' has no rule '{rule}'.");

    /// <summary>Every look, by name.</summary>
    public static IReadOnlyDictionary<string, Look> Looks => All.Value;

    private static IReadOnlyDictionary<string, Look> Load()
    {
        var asm = typeof(LookTable).Assembly;
        var looks = new Dictionary<string, Look>(StringComparer.Ordinal);
        foreach (var name in asm.GetManifestResourceNames().Where(n => n.Contains(".Theme.Looks.", StringComparison.Ordinal) && n.EndsWith(".json", StringComparison.Ordinal)).OrderBy(n => n, StringComparer.Ordinal))
        {
            using var stream = asm.GetManifestResourceStream(name)!;
            foreach (var look in JsonSerializer.Deserialize<List<Look>>(stream) ?? new List<Look>())
                looks.Add(look.Name, look);
        }
        return looks;
    }
}
