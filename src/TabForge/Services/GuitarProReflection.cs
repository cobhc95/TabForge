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
// Tests: TestGuitarProFiles.
/// <summary>Reads alphaTab's model by property name through reflection, so a missing or throwing property reads as absent and an alphaTab upgrade degrades gracefully.</summary>
internal static class GuitarProReflection
{
    internal static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    internal static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> MissingNames = new();

    /// <summary>Diagnostics: "Type.Name" of every property the importer asked for that alphaTab does not have.</summary>
    internal static IReadOnlyCollection<string> MissingPropertyNames => MissingNames.Keys.ToList();

    internal static object? Get(object? target, string property)
    {
        if (target is null) return null;
        // Reflection over alphaTab's model tolerates version differences: a missing property, or a
        // getter that throws on this particular object, reads as "absent".
        try
        {
            var info = target.GetType().GetProperty(property, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            // Record names that do not exist so a self-test can fail on a silently lost feature.
            if (info is null) MissingNames.TryAdd(target.GetType().Name + "." + property, 0);
            return info?.GetValue(target);
        }
        catch (AmbiguousMatchException) { return null; }
        catch (TargetInvocationException) { return null; }
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
        catch (Exception ex) when (IsConversionFailure(ex)) { return fallback; }
    }

    internal static int ConvertToInt(object? value, int fallback)
    {
        if (value is null) return fallback;
        try { return Convert.ToInt32(value, CultureInfo.InvariantCulture); }
        catch (Exception ex) when (IsConversionFailure(ex)) { return fallback; }
    }

    internal static bool GetBool(object? target, string property, bool fallback)
    {
        var value = Get(target, property);
        if (value is null) return fallback;
        try { return Convert.ToBoolean(value, CultureInfo.InvariantCulture); }
        catch (Exception ex) when (IsConversionFailure(ex)) { return fallback; }
    }

    internal static bool IsConversionFailure(Exception ex) =>
        ex is FormatException or InvalidCastException or OverflowException;
}
