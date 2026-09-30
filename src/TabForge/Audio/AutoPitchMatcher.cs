using TabForge.Audio.Contracts;
using TabForge.Models;
using TabForge.Plugins;
using TabForge.Services;

namespace TabForge.Audio;

/// <summary>One <see cref="EngineCommand.MeasurePitch"/> answer.</summary>
public sealed record PitchResult(int Slot, int Index, int RequestId, PitchMatch.Status Status, int Transpose, double Confidence, int[] Notes, double[] Offsets);

/// <summary>
/// Automatic pitch matching: when a chain's instrument loads or its preset changes, the engine measures the octave it really sounds
/// at (silently, into a scratch buffer) and the offset is stored on the <see cref="PluginSlot"/> and applied as an implicit transpose
/// right before that plug-in. Drum tracks (channel 10 / drum kits) are never measured nor transposed. UI thread only.
/// </summary>
public sealed class AutoPitchMatcher
{
    private readonly AudioEngineClient _engine;
    private readonly Func<PluginSettings> _settings;
    private readonly Dictionary<int, (TrackModel Track, PluginSlot Slot, int Tries)> _pending = new();
    /// <summary>A measurement finished or an offset changed (FX window refreshes its label).</summary>
    public event Action<TrackModel>? Changed;

    public AutoPitchMatcher(AudioEngineClient engine, Func<PluginSettings> settings)
    {
        _engine = engine; _settings = settings;
        engine.ChainAcknowledged += OnAck;
        engine.PresetChanged += (track, slot) => { if (slot.Type == PluginSlotType.Instrument) { slot.AutoPitchOffset = null; Later(() => Measure(track, slot), 400); } };
        engine.PitchMeasured += OnMeasured;
    }

    public static bool IsDrums(TrackModel track) => track.Kind == TrackKind.Drums || track.MidiChannel == 9;

    /// <summary>The chain's switch: its own choice, else the global default. Drums: always off.</summary>
    public bool Enabled(TrackModel track) => !track.IsBus && !IsDrums(track) && (track.Rig.AutoPitchMatch ?? _settings().AutoPitchMatch);

    /// <summary>Busy measuring this plug-in.</summary>
    public bool IsMeasuring(PluginSlot slot) => _pending.Values.Any(p => ReferenceEquals(p.Slot, slot));

    /// <summary>Test notes in the track's range: the lowest open string, a fifth and an octave above.</summary>
    public static int[] NotesFor(TrackModel track)
        => PitchMatch.TestNotes(track.StringTunings.Count > 0 ? track.StringTunings.Min() + track.Capo : null, track.Kind == TrackKind.Bass ? 28 : 40);

    private void OnAck(ChainAck ack)
    {
        if (_engine.TrackAt(ack.Slot) is not { } track) return;
        Apply(track);
        if (!Enabled(track)) return;
        foreach (var slot in track.Rig.Plugins)
            if (slot.Type == PluginSlotType.Instrument && slot.Enabled && slot.AutoPitchOffset is null && !IsMeasuring(slot)) Measure(track, slot);
    }

    /// <summary>Measures (again) one instrument now.</summary>
    public void Measure(TrackModel track, PluginSlot slot, int tries = 0)
    {
        if (!Enabled(track) || slot.Type != PluginSlotType.Instrument) return;
        var id = _engine.MeasurePitch(track, slot, NotesFor(track));
        if (id != 0) _pending[id] = (track, slot, tries);
        Changed?.Invoke(track);
    }

    /// <summary>Sends the track's effective transposes to the engine (0 everywhere when switched off).</summary>
    public void Apply(TrackModel track)
    {
        var on = Enabled(track);
        var list = new List<(int, int)>();
        for (var i = 0; i < track.Rig.Plugins.Count; i++)
            if (on && track.Rig.Plugins[i] is { Type: PluginSlotType.Instrument, AutoPitchOffset: { } st and not 0 }) list.Add((i, st));
        _engine.SetAutoPitch(track, list);
        Changed?.Invoke(track);
    }

    private void OnMeasured(PitchResult r)
    {
        if (!_pending.Remove(r.RequestId, out var p)) return;
        switch (r.Status)
        {
            case PitchMatch.Status.Deferred:
                if (p.Tries < 30) Later(() => { if (p.Slot.AutoPitchOffset is null) Measure(p.Track, p.Slot, p.Tries + 1); }, 2000);
                break;
            case PitchMatch.Status.Failed:
                break;
            default:
                p.Slot.AutoPitchOffset = r.Status == PitchMatch.Status.Ok ? r.Transpose : 0;
                break;
        }
        Apply(p.Track);
    }

    /// <summary>Label next to the checkbox: "+24 st", "matched", "measuring…", "".</summary>
    public string Describe(TrackModel track)
    {
        if (!Enabled(track)) return IsDrums(track) ? "drums: not transposed" : "";
        var inst = track.Rig.Plugins.Where(s => s.Type == PluginSlotType.Instrument && s.Enabled).ToList();
        if (inst.Count == 0) return "";
        if (inst.Any(IsMeasuring)) return "measuring…";
        var parts = inst.Select(s => s.AutoPitchOffset is { } o ? (o == 0 ? "matched" : $"{o:+0;-0} st") : "not measured").Distinct();
        return string.Join(", ", parts);
    }

    private static void Later(Action action, int ms)
    {
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
        timer.Tick += (_, _) => { timer.Stop(); action(); };
        timer.Start();
    }
}
