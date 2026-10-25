using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace TabForge.Platform;

// Owns: typed knob value parsing (plain numbers, units, ratios, comma/dot decimals).
// Does not own: the knob control or the settings binding.
// Tests: knob control tests.

/// <summary>Reads typed knob values: plain numbers and the usual units ("2.1", "-6 dB", "75%", "2:1", "2.0:1", "100 ms"), comma or dot decimals.</summary>
public static class KnobValueParser
{
    private static readonly Regex Ratio = new(@"^([+-]?\d+(?:[.,]\d+)?)\s*:\s*(\d+(?:[.,]\d+)?)?$", RegexOptions.Compiled);
    private static readonly Regex Number = new(@"^([+-]?(?:\d+(?:[.,]\d*)?|[.,]\d+))\s*([a-z%]*)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex Infinity = new(@"^([+-]?)inf(?:inity)?(?:\s*db)?$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly HashSet<string> Units = new(StringComparer.OrdinalIgnoreCase) { "", "db", "%", "ms", "s", "x", "hz", "khz", "st", "bpm", "ct", "cents" };

    /// <summary>The displayed number (units dropped; a ratio a:b is a/b). "inf" and "-inf" give the infinities.</summary>
    public static bool TryParse(string? text, out double value)
    {
        value = 0;
        if (text is null) return false;
        var t = text.Trim().Replace('−', '-');
        if (t.Length == 0) return false;
        var inf = Infinity.Match(t);
        if (inf.Success) { value = inf.Groups[1].Value == "-" ? double.NegativeInfinity : double.PositiveInfinity; return true; }
        var r = Ratio.Match(t);
        if (r.Success)
        {
            if (!D(r.Groups[1].Value, out var a)) return false;
            var b = 1.0;
            if (r.Groups[2].Success && (!D(r.Groups[2].Value, out b) || b == 0)) return false;
            value = a / b;
            return true;
        }
        var m = Number.Match(t);
        return m.Success && Units.Contains(m.Groups[2].Value) && D(m.Groups[1].Value, out value);
    }

    private static bool D(string s, out double v) =>
        double.TryParse(s.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v) && double.IsFinite(v);

    /// <summary>Pan text to a 0-127 knob value (64 = centre): "Centre", "L 20", "R 5", or a signed offset such as -20 / +5.</summary>
    public static double? ParsePan(string? text)
    {
        if (text is null) return null;
        var t = text.Trim();
        if (t.Equals("centre", StringComparison.OrdinalIgnoreCase) || t.Equals("center", StringComparison.OrdinalIgnoreCase) || t.Equals("c", StringComparison.OrdinalIgnoreCase)) return 64;
        if (t.Length > 1 && t[0] is 'L' or 'l' or 'R' or 'r')
            return TryParse(t[1..], out var n) && n >= 0 && double.IsFinite(n) ? 64 + (t[0] is 'L' or 'l' ? -n : n) : null;
        return TryParse(t, out var o) && double.IsFinite(o) ? 64 + o : null;
    }
}
