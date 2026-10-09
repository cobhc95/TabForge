using System.Text.Json.Serialization;
using TabForge.Docking;
using TabForge.Documents;

namespace TabForge.Services;

// Owns: the playback preferences data: metronome, count-in, preview and linked-audio folders.
// Does not own: loading, saving and validating (AppSettingsStore, SettingsValidator) or the Preferences rows (SettingsCatalog).
// Tests: TestPreferencesCatalog, TestSettingsFileSplitSnapshots.
public sealed class AudioSettings
{
    /// <summary>Folders (network or removable) the user allowed a song to read linked audio from; revoked in the "Linked audio" review window.</summary>
    public List<MediaApproval> ApprovedMedia { get; set; } = new();
    public bool Metronome { get; set; }
    public bool CountIn { get; set; }
    public int CountInBars { get; set; } = 1;
    public double Speed { get; set; } = 1.0;
    public bool PreviewNotes { get; set; } = true;
    /// <summary>How long a previewed note sounds, in milliseconds.</summary>
    public int PreviewLengthMs { get; set; } = 260;
    /// <summary>Let-ring tail ceiling in milliseconds.</summary>
    public int LetRingCapMs { get; set; } = 2000;
    /// <summary>GM note used for the metronome click.</summary>
    public int MetronomeClick { get; set; } = 34;
    /// <summary>GM note used for the accented (first beat) click.</summary>
    public int MetronomeAccent { get; set; } = 33;
    /// <summary>Master metronome output level, 0–100 percent.</summary>
    public int MetronomeVolume { get; set; } = 70;
    /// <summary>Layer extra percussion hits on every click so it is much louder than the song.</summary>
    public bool MetronomeBoost { get; set; } = true;
    public int CountInVolume { get; set; } = 70;
    /// <summary>"same" or "accent,click" MIDI notes for the count-in.</summary>
    public string CountInSound { get; set; } = "same";
    public bool CountInOnlyAtSongStart { get; set; }
    public bool CountInEachSection { get; set; }
    /// <summary>Clicking Loop with no selected area loops the section being played (off: loops the whole song).</summary>
    public bool LoopButtonLoopsSection { get; set; }
    /// <summary>Loop repeats; 0 = infinite.</summary>
    public int LoopCount { get; set; }
    public bool LoopCountInEachLoop { get; set; }
    public bool LoopSpeedTrainer { get; set; }
    public int LoopTrainerFrom { get; set; } = 50;
    public int LoopTrainerTo { get; set; } = 100;
    public int LoopTrainerStep { get; set; } = 10;
    /// <summary>First-beat volume, 0–100 percent.</summary>
    public int MetronomeAccentVolume { get; set; } = 100;
    /// <summary>Regular click volume, 0–100 percent.</summary>
    public int MetronomeClickVolume { get; set; } = 76;
    /// <summary>Clicks per beat: 1, 2, 3 (triplets), or 4.</summary>
    public int MetronomeSubdivision { get; set; } = 1;
    /// <summary>What the loop button loops when no area is selected: "Section", "Bar" or "Song".</summary>
    public string LoopDefaultScope { get; set; } = "Section";
    /// <summary>Forget the selected loop area when looping is switched off.</summary>
    public bool LoopClearAreaOnDisable { get; set; } = true;
    /// <summary>Fretboard preview layout: "TabForge" (fixed look-ahead) or one of the "the reference: …" beat/bar layouts.</summary>
    public string FretboardStyle { get; set; } = "TabForge";
    /// <summary>Master volume applied on top of every track's volume, 0–100 percent.</summary>
    public int MasterVolume { get; set; } = 100;
    /// <summary>What the master volume knob scales.</summary>
    public bool MasterAffectsTracks { get; set; } = true;
    public bool MasterAffectsMetronome { get; set; }
    public bool MasterAffectsPreview { get; set; }
    /// <summary>Arrangement pan controls: rotary knobs (true) or horizontal sliders.</summary>
    public bool PanKnobs { get; set; }
    /// <summary>One-time switch of existing settings to the standard pan slider.</summary>
    public bool PanSliderAdopted { get; set; }
    public bool VolumeKnobs { get; set; }
}
