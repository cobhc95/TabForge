using TabForge.Models;
using TabForge.Services;

namespace TabForge.LiveInput;

// Owns: the one live input of the program: its stand-in track (the session's live FX chain), whether it is on, and what the
//   engine sync needs to know about that track (wanted, its level, that it is no song's track).
// Does not own: turning it on and off (LiveInputController), the saved chain (LiveInputSettings), the engine sync itself
//   (AudioEngineClient asks this class; see README.md for the hooks).
// Tests: LiveInputStripTests.
/// <summary>
/// One per audio engine client, so every window and tab shares it: live input belongs to the program, not to a song. The stand-in is
/// an armed, monitored audio track without clips that is in no song: the engine monitors its input through its chain like any
/// record-armed track. It is wanted at every sync, whichever song syncs, so a tab switch keeps the same engine slot and chain.
/// </summary>
public sealed class LiveInputArm
{
    private bool _wanted;

    /// <summary>The stand-in track.</summary>
    public TrackModel Track { get; } = new()
    {
        Name = "Live input", InstrumentName = "Live input", Kind = TrackKind.Audio, MidiSound = false,
        SoundSource = SoundSources.Plugins, MonitorInput = true,
    };

    /// <summary>Raised when live input turns on or off.</summary>
    public event Action? Changed;

    /// <summary>Raised when the stand-in's own volume, pan or mute changes (every mixer shows the same values).</summary>
    public event Action? LevelChanged;

    public bool IsOn => Track.RecordArm;

    /// <summary>The MIDI keyboard of live input (the first window's controller makes it; every window shares it).</summary>
    internal LiveInputMidi? Midi { get; set; }

    /// <summary>The restore choice under which this run decided whether to restore the saved chain; null: not yet. Another choice decides again.</summary>
    public string? RestoreDecided { get; set; }

    /// <summary>The master volume in percent and the master pan offset as the latest sync was given them (the engine applies the master in every track's level and pan).</summary>
    public int MasterPercent { get; private set; } = 100;
    public int MasterPan { get; private set; }

    public void SetMaster(int percent, int pan) { MasterPercent = percent; MasterPan = pan; }

    /// <summary>Capture only: shows live input as on or off in every window without putting anything in the engine.</summary>
    internal void ShowLit(bool on) { Track.RecordArm = on; Changed?.Invoke(); }

    public void Arm()
    {
        if (IsOn) return;
        Track.RecordArm = _wanted = true;
        Changed?.Invoke();
    }

    /// <summary>Stops monitoring. The stand-in stays in the engine (silent) until <see cref="Release"/>, so its plug-in states can still be read.</summary>
    public void Disarm()
    {
        if (!IsOn) return;
        Track.RecordArm = false;
        Changed?.Invoke();
    }

    /// <summary>Lets the stand-in leave the engine at the next sync (nothing when it was turned on again meanwhile).</summary>
    public void Release() { if (!IsOn) _wanted = false; }

    /// <summary>Engine sync: adds the stand-in to the wanted tracks while live input is on, whichever song syncs.</summary>
    public void AddWanted(List<TrackModel> wanted, PluginSettings settings)
    {
        if (!_wanted) return;
        Track.AudioInput = settings.LiveInput.Input;
        wanted.Add(Track);
    }

    /// <summary>Engine sync: true for the stand-in, which is no song's track (no group mute, group level or group bus applies to it).</summary>
    public bool Owns(TrackModel track) => ReferenceEquals(track, Track);

    /// <summary>True while the stand-in is in the engine with the plug-in at <paramref name="path"/> in its chain (a crash of it needs a sync).</summary>
    public bool Uses(string path) => _wanted && Track.Rig.Plugins.Any(p => string.Equals(p.Path, path, StringComparison.OrdinalIgnoreCase));

    /// <summary>Sets the stand-in's own volume and pan (0-127 each, pan 64 centre); the engine gets them at the next sync.</summary>
    public void SetLevel(int volume, int pan)
    {
        Track.Volume = Math.Clamp(volume, 0, MidiLimits.Max);
        Track.Pan = Math.Clamp(pan, 0, MidiLimits.Max);
        LevelChanged?.Invoke();
    }

    /// <summary>The gain in dB the monitored input has going into the chain (see <see cref="LiveStripRules"/>); the engine gets it at the next sync.</summary>
    public int GainDb { get; private set; }

    public void SetGain(int db)
    {
        GainDb = LiveStripRules.ClampGainDb(db);
        LevelChanged?.Invoke();
    }

    /// <summary>Silences the stand-in without disarming it: its volume is kept and comes back on unmute.</summary>
    public void SetMute(bool mute)
    {
        Track.Mute = mute;
        LevelChanged?.Invoke();
    }

    /// <summary>Solo, as a track's: the live input plays whatever its mute says, and the song's tracks that are not soloed go silent. Live only; never saved.</summary>
    public bool Solo => Track.Solo;

    public void SetSolo(bool solo)
    {
        Track.Solo = solo;
        LevelChanged?.Invoke();
    }

    /// <summary>Engine sync: the stand-in's level and pan: its own volume under the master volume (0 silences it, as it does a song's tracks, and when muted unless soloed) and its pan under the master pan.</summary>
    public (int Volume, int Pan) Mix => (Track.Mute && !Track.Solo ? 0 : Math.Clamp(Track.Volume * Math.Clamp(MasterPercent, 0, 100) / 100, 0, MidiLimits.Max), Math.Clamp(Track.Pan + MasterPan, 0, MidiLimits.Max));
}
