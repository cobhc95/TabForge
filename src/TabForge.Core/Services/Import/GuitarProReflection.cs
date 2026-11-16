using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using AlphaTab;
using AlphaTab.Importer;
using TabForge.Models;
using TabForge.Plugins;

namespace TabForge.Services;

// Owns: reading properties of the score library's objects by name, and the record of names that were missing.
// Does not own: the conversion rules built on those reads.
// Tests: no dedicated test class.
/// <summary>Reads alphaTab's model by property name through reflection, so a missing or throwing property reads as absent and an alphaTab upgrade degrades gracefully.</summary>
internal static class GuitarProReflection
{
    internal static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    /// <summary>Diagnostics: "Type.Name" of every property the importer asked for that alphaTab does not have.</summary>
    internal static IReadOnlyCollection<string> MissingPropertyNames => Properties.Where(p => p.Value.Missing).Select(p => p.Key.Type.Name + "." + p.Key.Name).ToList();

    internal static object? Get(object? target, string property)
    {
        if (target is null) return null;
        // Reflection over alphaTab's model tolerates version differences: a missing property, or a
        // getter that throws on this particular object, reads as "absent".
        try { return PropertyOf(target.GetType(), property)?.GetValue(target); }
        catch (TargetInvocationException) { return null; } // Not logged: optional alphaTab member probe: the fallback value is used
    }

    // The case-insensitive lookup per (type, name), found once: an import asks for the same few dozen names millions of times
    // (performance audit 2026-11: the uncached lookup was over half of the conversion time of a large song). Info null: absent
    // (Missing: recorded so a self-test can fail on a silently lost feature) or ambiguous.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(Type Type, string Name), (PropertyInfo? Info, bool Missing)> Properties = new();

    private static PropertyInfo? PropertyOf(Type type, string property)
    {
        if (Properties.TryGetValue((type, property), out var known)) return known.Info;
        PropertyInfo? info;
        var missing = false;
        try
        {
            info = type.GetProperty(property, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            missing = info is null;
        }
        catch (AmbiguousMatchException) { info = null; } // Not logged: optional alphaTab member probe: the fallback value is used
        return Properties.GetOrAdd((type, property), (info, missing)).Info;
    }

    internal static IEnumerable<object> AsObjects(object? value)
    {
        if (value is null || value is string) yield break;
        if (value is IEnumerable enumerable)
            foreach (var item in enumerable)
                if (item is not null) yield return item;
    }

    internal static string? GetString(object target, string property) => Get(target, property)?.ToString();

    internal static int GetInt(object? target, string property, int fallback)
    {
        if (target is null) return fallback;
        return ConvertToInt(Get(target, property), fallback);
    }

    internal static double GetDouble(object? target, string property, double fallback)
    {
        var value = Get(target, property);
        if (value is null) return fallback;
        try { return Convert.ToDouble(value, CultureInfo.InvariantCulture); }
        catch (Exception ex) when (IsConversionFailure(ex)) { return fallback; } // Not logged: optional alphaTab member probe: the fallback value is used
    }

    internal static int ConvertToInt(object? value, int fallback)
    {
        if (value is null) return fallback;
        try { return Convert.ToInt32(value, CultureInfo.InvariantCulture); }
        catch (Exception ex) when (IsConversionFailure(ex)) { return fallback; } // Not logged: optional alphaTab member probe: the fallback value is used
    }

    internal static bool GetBool(object? target, string property, bool fallback)
    {
        var value = Get(target, property);
        if (value is null) return fallback;
        try { return Convert.ToBoolean(value, CultureInfo.InvariantCulture); }
        catch (Exception ex) when (IsConversionFailure(ex)) { return fallback; } // Not logged: optional alphaTab member probe: the fallback value is used
    }

    internal static bool IsConversionFailure(Exception ex) =>
        ex is FormatException or InvalidCastException or OverflowException;
}
