namespace TabForge.Rendering;

// Owns: the last-used File > Render options (stored in the app settings) and the bounds choices they name.
// Does not own: the render plan (RenderSpecBuilder) or the render dialog (RenderWindow, in the app).
// Tests: no dedicated test class.
/// <summary>Last-used File > Render options (stored in the app settings).</summary>
public sealed class RenderSettings
{
    public int Source { get; set; }          // 0 master, 1 stems of the selected tracks, 2 all stems, 3 master and stems
    public List<string>? StemTrackNames { get; set; }   // checked tracks for "Stems: selected tracks" (null = not chosen yet)
    public int Bounds { get; set; }          // RenderBounds
    public double CustomStartSec { get; set; }
    public double CustomEndSec { get; set; }
    /// <summary>"Custom bars": 1-based first / last bar (0 = not chosen yet: bar 1 / the last bar).</summary>
    public int CustomFromBar { get; set; }
    public int CustomToBar { get; set; }
    /// <summary>"Custom sections": the first bar (0-based) of the chosen first / last section (-1 = not chosen yet: first / last section).</summary>
    public int CustomFromSectionBar { get; set; } = -1;
    public int CustomToSectionBar { get; set; } = -1;
    public int TailMode { get; set; } = 1;   // RenderTailMode
    public int TailMs { get; set; } = 3000;
    public string Directory { get; set; } = "";
    public string Pattern { get; set; } = "$project";
    public int SampleRate { get; set; }      // 0 = engine
    public bool Mono { get; set; }
    public bool SingleThread { get; set; }
    public bool RealtimePace { get; set; }
    public int Format { get; set; } = 1;     // 0 WAV16, 1 WAV24, 2 WAV32f, 3 MP3
    public int Mp3Kbps { get; set; } = 192;
    public bool OpenFolder { get; set; } = true;
    /// <summary>Transparent master safety limiter (-0.3 dBFS) on the rendered master mix (A7-A01). On by default; stems are not limited.</summary>
    public bool SafetyLimiter { get; set; } = true;
}

public enum RenderBounds { Song, TimeSelection, Bars, Custom, CustomBars, CustomSections }
