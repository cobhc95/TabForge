using System.Globalization;

namespace TabForge.LiveInput;

// Owns: the live input strip's fixed numbers and pure maths: knob ranges, the tuner's reference and window choices, and the meters' dB scale and texts.
// Does not own: the stored values (LiveInputSettings), the peak hold (PeakHold), the tuner's note rule (TunerPresenter), the drawing (Avalonia LiveInputStrip).
// Tests: no dedicated test class.
/// <summary>The colour class of a meter level.</summary>
public enum MeterTone { Normal, Warm, Clip }

/// <summary>Rules of the "FX: Live input" window's strip: meters, tuner and knobs.</summary>
public static class LiveStripRules
{
    public const int MinGainDb = -24, MaxGainDb = 24, DefaultGainDb = 0;
    public const int MinReferenceHz = 415, MaxReferenceHz = 466, DefaultReferenceHz = 440;
    public const int DefaultWindowMs = 100;
    /// <summary>The strip tuner's needle and cents text are green within this many cents of the note, else warning.</summary>
    public const double InTuneCents = 3;
    /// <summary>Meter scale: the bottom is silence ("-inf"), the top is full scale (0 dB). The level text reads up to <see cref="TextCeilingDb"/>, so a clipping level shows.</summary>
    public const double MeterFloorDb = -60, MeterCeilingDb = 0, TextCeilingDb = 6;

    /// <summary>A level from <see cref="WarmDb"/> up draws warm, from <see cref="ClipDb"/> up it draws as clipping.</summary>
    public const double WarmDb = -12, ClipDb = -1;

    private static readonly IReadOnlyList<int> WindowChoices = Array.AsReadOnly(new[] { 5, 10, 20, 25, 30, 40, 50, 100, 200 });
    private static readonly IReadOnlyList<string> DbTexts = BuildDbTexts();
    private static readonly IReadOnlyList<string> CentsTexts = BuildCentsTexts();
    private static readonly IReadOnlyList<string> WindowItemTexts = Array.AsReadOnly(WindowChoices.Select(ms => string.Create(CultureInfo.InvariantCulture, $"{ms} ms")).ToArray());

    /// <summary>The tuner's analysis windows in ms (as Reaper offers): longer windows detect lower notes.</summary>
    public static IReadOnlyList<int> WindowChoicesMs => WindowChoices;

    /// <summary>A window choice as the dropdown and Preferences show it ("100 ms").</summary>
    public static string WindowText(int ms) => string.Create(CultureInfo.InvariantCulture, $"{ms} ms");

    /// <summary>A window choice as the strip's dropdown shows it ("100 ms"); the strings are made once.</summary>
    public static string WindowItemText(int ms)
    {
        for (var i = 0; i < WindowChoices.Count; i++) if (WindowChoices[i] == ms) return WindowItemTexts[i];
        return WindowItemText(DefaultWindowMs);
    }

    /// <summary>The window a text names ("100 ms"); the default for text that is not one of the choices.</summary>
    public static int ParseWindow(string? text) =>
        int.TryParse(text?.Replace(" ms", ""), NumberStyles.None, CultureInfo.InvariantCulture, out var ms) && WindowChoices.Contains(ms) ? ms : DefaultWindowMs;

    public static int ClampGainDb(int db) => Math.Clamp(db, MinGainDb, MaxGainDb);

    /// <summary>A linear peak in dB; <see cref="MeterFloorDb"/> for silence (and for NaN).</summary>
    public static double PeakToDb(double peak) => peak > 0 ? Math.Max(MeterFloorDb, 20 * Math.Log10(peak)) : MeterFloorDb;

    /// <summary>Where a dB value sits on the meter, 0 (floor) to 1 (ceiling).</summary>
    public static double MeterFraction(double db) => Math.Clamp((db - MeterFloorDb) / (MeterCeilingDb - MeterFloorDb), 0, 1);

    /// <summary>How a meter fills at a level: normal, warm near the top, clipping at full scale.</summary>
    public static MeterTone ToneForDb(double db) => db >= ClipDb ? MeterTone.Clip : db >= WarmDb ? MeterTone.Warm : MeterTone.Normal;

    /// <summary>The meter's level text for a dB value rounded to a whole dB ("-12 dB", "-inf" at the floor); the strings are made once, so a frame allocates nothing.</summary>
    public static string DbText(double db)
    {
        var whole = (int)Math.Round(db);
        return whole <= MeterFloorDb ? DbTexts[0] : DbTexts[Math.Min(whole, (int)TextCeilingDb) - (int)MeterFloorDb];
    }

    /// <summary>True when the needle and cents text read green: within <see cref="InTuneCents"/> of the note.</summary>
    public static bool IsInTune(double cents) => Math.Abs(cents) <= InTuneCents;

    /// <summary>The cents text for a whole number of cents, clamped to -50..+50 ("-12 ¢"); the strings are made once.</summary>
    public static string CentsText(int cents) => CentsTexts[Math.Clamp(cents, -50, 50) + 50];

    /// <summary>The gain knob's text ("+3 dB").</summary>
    public static string GainText(int db) => string.Create(CultureInfo.InvariantCulture, $"{db:+0;-0;0} dB");

    private static IReadOnlyList<string> BuildCentsTexts()
    {
        var texts = new string[101];
        for (var i = 0; i < texts.Length; i++) texts[i] = string.Create(CultureInfo.InvariantCulture, $"{i - 50:+0;-0;0} ¢");
        return Array.AsReadOnly(texts);
    }

    private static IReadOnlyList<string> BuildDbTexts()
    {
        var texts = new string[(int)(TextCeilingDb - MeterFloorDb) + 1];
        texts[0] = "-inf";
        for (var i = 1; i < texts.Length; i++) texts[i] = string.Create(CultureInfo.InvariantCulture, $"{(int)MeterFloorDb + i} dB");
        return Array.AsReadOnly(texts);
    }
}
