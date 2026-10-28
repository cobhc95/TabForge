using System.Globalization;

namespace TabForge.Audio.Metering;

// Owns: what the master meter shows: the dB of each bar (with a fall rate), the peak-hold lines, the latched clip light, the number text and the tooltip text.
// Does not own: where readings come from (the engine, through AudioEngineClient.ReadMasterMeter), the drawing (Views/Meters/MasterMeterView), the choices (MasterMeterSettings).
// Tests: TabForge.Avalonia.Tests.MasterMeterTests.
// Thread: UI thread. A feed allocates nothing unless the number text changes (a few times a second).
/// <summary>The meter's state, fed a reading about 30 times a second; says whether anything on screen changed.</summary>
public sealed class MasterMeterModel
{
    /// <summary>Bar scale: the bottom is silence, the top is full scale.</summary>
    public const double FloorDb = -60, CeilingDb = 0;

    /// <summary>The bars turn from green to yellow from here up.</summary>
    public const double WarmDb = -12;

    /// <summary>How fast a peak bar falls back, in dB per second (the engine already smooths RMS and loudness).</summary>
    public const double FallDbPerSecond = 40;

    /// <summary>The number changes at most this often, so it can be read.</summary>
    public const double NumberIntervalMs = 150;

    private const string SilentText = "-inf";

    private readonly Func<MasterMeterSettings> _settings;
    private double _lastMs = double.NaN, _numberAtMs = double.NegativeInfinity, _holdLeftMs, _holdRightMs;
    private int _lastClips;
    private bool _haveClipBaseline;
    private string _mode = MasterMeterSettings.Peak;

    public MasterMeterModel(Func<MasterMeterSettings> settings) => _settings = settings;

    public double LeftDb { get; private set; } = FloorDb;
    public double RightDb { get; private set; } = FloorDb;

    /// <summary>The held peak of each bar in dB; the floor when there is none.</summary>
    public double HoldLeftDb { get; private set; } = FloorDb;
    public double HoldRightDb { get; private set; } = FloorDb;

    /// <summary>True while the hold line shows (the hold is not Off).</summary>
    public bool HoldShown { get; private set; }

    /// <summary>The clip light: on once the master reached full scale, until it is reset.</summary>
    public bool ClipLit { get; private set; }

    /// <summary>The small number: the louder bar's level ("-18", or "-23.0" for loudness; "-inf" for silence).</summary>
    public string NumberText { get; private set; } = SilentText;

    /// <summary>Where a level sits on the bar, 0 (floor) to 1 (ceiling).</summary>
    public static double Fraction(double db) => Math.Clamp((db - FloorDb) / (CeilingDb - FloorDb), 0, 1);

    /// <summary>A linear level in dB; the floor for silence and NaN.</summary>
    public static double ToDb(double linear) => linear > 0 ? Math.Max(FloorDb, 20 * Math.Log10(linear)) : FloorDb;

    /// <summary>Switches the clip light off; true when it was on.</summary>
    public bool ResetClip()
    {
        var was = ClipLit;
        ClipLit = false;
        return was;
    }

    /// <summary>The tooltip: what the bars show in a mode.</summary>
    public static string Tip(string mode, bool showNumber) => (mode switch
    {
        MasterMeterSettings.Rms => "Master meter: RMS level in dB (about 300 ms average), left and right.",
        MasterMeterSettings.Loudness => "Master meter: short-term loudness in LUFS (EBU R128, K-weighted, 3 second window).",
        _ => "Master meter: peak level in dBFS, left and right.",
    }) + (showNumber ? " The number is the louder bar." : "") + " Click to reset the clip light, double-click to open the mixer, right-click for options.";

    /// <summary>Takes one reading (null: no engine) at <paramref name="nowMs"/>; true when the bars, hold, clip light or number changed.</summary>
    public bool Feed(MasterMeterReading? reading, double nowMs)
    {
        var settings = _settings();
        var mode = MasterMeterSettings.ParseMode(settings.Mode);
        var r = reading ?? MasterMeterReading.Silent(_lastClips);
        var dt = double.IsNaN(_lastMs) ? 0 : Math.Max(0, (nowMs - _lastMs) / 1000);
        _lastMs = nowMs;
        double left, right;
        switch (mode)
        {
            case MasterMeterSettings.Rms: left = ToDb(r.RmsLeft); right = ToDb(r.RmsRight); break;
            case MasterMeterSettings.Loudness: left = right = Math.Clamp(r.ShortTermLufs, FloorDb, CeilingDb + 6); break;
            default: left = ToDb(r.PeakLeft); right = ToDb(r.PeakRight); break;
        }
        var fall = mode == MasterMeterSettings.Peak ? FallDbPerSecond * dt : double.PositiveInfinity;
        var changed = mode != _mode;
        _mode = mode;
        var newLeft = Math.Max(left, LeftDb - fall);
        var newRight = Math.Max(right, RightDb - fall);
        changed |= Fraction(newLeft) != Fraction(LeftDb) || Fraction(newRight) != Fraction(RightDb);
        LeftDb = newLeft; RightDb = newRight;
        changed |= UpdateHold(settings.HoldSeconds, newLeft, newRight, nowMs);
        changed |= UpdateClip(r.Clips);
        changed |= UpdateNumber(mode, settings.ShowNumber, Math.Max(newLeft, newRight), nowMs);
        return changed;
    }

    private bool UpdateHold(int seconds, double left, double right, double nowMs)
    {
        if (seconds <= 0)
        {
            var was = HoldShown;
            HoldShown = false; HoldLeftDb = HoldRightDb = FloorDb;
            return was;
        }
        var heldMs = seconds * 1000.0;
        var beforeLeft = Fraction(HoldLeftDb); var beforeRight = Fraction(HoldRightDb); var shown = HoldShown;
        if (left >= HoldLeftDb || nowMs - _holdLeftMs > heldMs) { HoldLeftDb = left; _holdLeftMs = nowMs; }
        if (right >= HoldRightDb || nowMs - _holdRightMs > heldMs) { HoldRightDb = right; _holdRightMs = nowMs; }
        HoldShown = true;
        return !shown || beforeLeft != Fraction(HoldLeftDb) || beforeRight != Fraction(HoldRightDb);
    }

    // The clip count only goes up while one engine runs; a lower count is a new engine (a new baseline, no light).
    private bool UpdateClip(int clips)
    {
        var lit = false;
        if (_haveClipBaseline && clips > _lastClips && !ClipLit) lit = ClipLit = true;
        _lastClips = clips; _haveClipBaseline = true;
        return lit;
    }

    private bool UpdateNumber(string mode, bool show, double db, double nowMs)
    {
        if (!show || nowMs - _numberAtMs < NumberIntervalMs) return false;
        _numberAtMs = nowMs;
        var text = db <= FloorDb ? SilentText
            : mode == MasterMeterSettings.Loudness ? db.ToString("0.0", CultureInfo.InvariantCulture)
            : Math.Round(db).ToString("0", CultureInfo.InvariantCulture);
        if (text == NumberText) return false;
        NumberText = text;
        return true;
    }
}
