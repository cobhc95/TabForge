namespace TabForge.Playback;

/// <summary>Transport state of the playback engine.</summary>
public enum PlaybackState { Stopped, Playing, Paused }

/// <summary>
/// Everything the playback compiler needs to know about *how* to play.
/// Positions are bar/cell (editor grid), never milliseconds, so a seek is deterministic.
/// </summary>
public sealed class PlaybackOptions
{
    public int StartBar { get; set; }
    public int StartCell { get; set; }
    public double Speed { get; set; } = 1.0;
    public bool Loop { get; set; }
    public int LoopStartBar { get; set; }
    public int LoopEndBar { get; set; }
    /// <summary>Grid cell inside the first loop bar; -1 is treated as the bar start.</summary>
    public int LoopStartCell { get; set; }
    /// <summary>Inclusive grid cell inside the last loop bar; -1 means the bar end.</summary>
    public int LoopEndCell { get; set; } = -1;
    public bool Metronome { get; set; }
    public bool CountIn { get; set; }
    /// <summary>
    /// Honour the mixer's mute/solo state. Playback does; a file export must not, or exporting a song
    /// while one track happens to be muted would silently write a file with that track missing.
    /// </summary>
    public bool RespectMuteSolo { get; set; } = true;
    /// <summary>How many bars of count-in to play before the score starts.</summary>
    public int CountInBars { get; set; } = 1;
    public bool RepeatExpansion { get; set; } = true;
    /// <summary>Leave out MIDI clips (used for the song-time map the clips are placed with).</summary>
    public bool SkipClips { get; set; }
    /// <summary>General MIDI note used for the metronome's accented first beat.</summary>
    public int MetronomeAccentNote { get; set; } = 34;
    /// <summary>General MIDI note used for the other metronome beats.</summary>
    public int MetronomeClickNote { get; set; } = 33;
    /// <summary>Master metronome level in percent.</summary>
    public int MetronomeVolume { get; set; } = 70;
    /// <summary>First-beat level in percent before master metronome volume.</summary>
    public int MetronomeAccentVolume { get; set; } = 100;
    /// <summary>Regular-beat level in percent before master metronome volume.</summary>
    public int MetronomeClickVolume { get; set; } = 76;
    /// <summary>Metronome events per beat: 1 quarter, 2 eighths, 3 triplets, 4 sixteenths.</summary>
    public int MetronomeSubdivision { get; set; } = 1;
    /// <summary>Playback-only timelines can precompile all subdivision choices for seamless live changes.</summary>
    public bool LiveMetronomeEvents { get; set; }
    /// <summary>Longest a let-ring note may ring on, in milliseconds.</summary>
    public double LetRingCapMs { get; set; } = 2000;

    public PlaybackOptions Clone() => (PlaybackOptions)MemberwiseClone();

    /// <summary>
    /// True when both would compile the same timeline from the same start. Used to skip restarts for
    /// "changes" that change nothing (e.g. re-applying settings while playing), which would otherwise
    /// cause an audible hiccup.
    /// </summary>
    public bool CompilesSameAs(PlaybackOptions other) =>
        Speed.Equals(other.Speed) && Loop == other.Loop && LoopStartBar == other.LoopStartBar &&
        LoopEndBar == other.LoopEndBar && LoopStartCell == other.LoopStartCell && LoopEndCell == other.LoopEndCell &&
        Metronome == other.Metronome && CountIn == other.CountIn &&
        RespectMuteSolo == other.RespectMuteSolo && CountInBars == other.CountInBars &&
        RepeatExpansion == other.RepeatExpansion && MetronomeAccentNote == other.MetronomeAccentNote &&
        MetronomeClickNote == other.MetronomeClickNote && MetronomeVolume == other.MetronomeVolume &&
        MetronomeAccentVolume == other.MetronomeAccentVolume && MetronomeClickVolume == other.MetronomeClickVolume &&
        MetronomeSubdivision == other.MetronomeSubdivision && LiveMetronomeEvents == other.LiveMetronomeEvents &&
        LetRingCapMs.Equals(other.LetRingCapMs);
}

/// <summary>A musical playback position reported to the UI (bar/cell come from the timeline bar map).</summary>
public sealed class PlaybackPosition
{
    public int Bar { get; set; }
    public int Cell { get; set; }
    public double BarFraction { get; set; }
    public double ElapsedMs { get; set; }
}
