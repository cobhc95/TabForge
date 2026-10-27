using System.Text;
using System.Buffers.Binary;

namespace TabForge.AudioEngine.Synth;

// Owns: converting the Windows General MIDI bank (gm.dls) into an in-memory SoundFont 2 image for MeltySynth: parsing the untrusted
//   file, mapping articulation, the gain calibration, and the cached load.
// Does not own: the synthesiser (MeltySynth) and the General MIDI synth that plays the bank (GmSynth).
// Tests: AudioSmokeTests.
// Split: the RIFF / DLS parser, articulation resolution and SF2 writer live in DlsToSoundFont.Parser/Articulation/Sf2Writer.cs.
/// <summary>
/// Converts a Downloadable Sounds (DLS Level 1/2) bank - in practice the Windows General MIDI bank
/// <c>%WINDIR%\System32\drivers\gm.dls</c> used by the Microsoft GS Wavetable Synth - into an in-memory
/// SoundFont 2 image that MeltySynth can load.
/// </summary>
/// <remarks>
/// The parser treats the input as untrusted: every chunk length is bounds-checked against its parent,
/// every loop advances, and malformed pieces are skipped rather than trusted. Articulation is mapped to
/// SF2 generators with MeltySynth's gain model in mind (MeltySynth applies only 40% of the SF2
/// initialAttenuation generator, so attenuation values are pre-scaled to land on the DLS gain in dB).
/// Mappings were tuned by measuring MeltySynth renders against the real Microsoft GS Wavetable Synth.
/// </remarks>
public static partial class DlsToSoundFont
{
    // Loaded once; the MeltySynth SoundFont is shared read-only across Synthesizer instances by design.
    private static readonly Lazy<MeltySynth.SoundFont> WindowsBank =
        new(() => LoadWindowsBankCore(), LazyThreadSafetyMode.ExecutionAndPublication);


    /// <summary>Loads, converts and caches the Windows GM bank (gm.dls) as a MeltySynth SoundFont.</summary>
    /// <exception cref="FileNotFoundException">gm.dls is not present.</exception>
    /// <exception cref="InvalidDataException">gm.dls could not be parsed or converted.</exception>
    public static MeltySynth.SoundFont LoadWindowsBank() => WindowsBank.Value;

    private static MeltySynth.SoundFont LoadWindowsBankCore()
    {
        var path = WindowsBankPath;
        if (!File.Exists(path))
            throw new FileNotFoundException($"The Windows General MIDI sound bank was not found at '{path}'.", path);
        byte[] dls;
        try { dls = File.ReadAllBytes(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new FileNotFoundException($"The Windows General MIDI sound bank at '{path}' could not be read: {ex.Message}", path, ex);
        }
        var sf2 = Convert(dls, GmSynthTuning.CalibrationAttenuationDb);
        try
        {
            using var ms = new MemoryStream(sf2, writable: false);
            return new MeltySynth.SoundFont(ms);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            throw new InvalidDataException($"The SoundFont converted from '{path}' was rejected by the synthesiser: {ex.Message}", ex);
        }
    }

    /// <summary>Converts a DLS file image to a SoundFont 2 (.sf2) file image.</summary>
    /// <exception cref="InvalidDataException">The data is not a usable DLS bank.</exception>
    public static byte[] Convert(byte[] dls, Func<bool, int, int, double>? extraAttenuationDb = null)
    {
        ArgumentNullException.ThrowIfNull(dls);
        var bank = DlsParser.Parse(dls);
        return Sf2Writer.Write(bank, extraAttenuationDb);
    }

    // ------------------------------------------------------------------------------------------------
    // DLS model
    // ------------------------------------------------------------------------------------------------

    internal readonly record struct Connection(ushort Source, ushort Control, ushort Destination, ushort Transform, int Scale);

    internal readonly record struct WaveLoop(uint Type, uint Start, uint Length);

    internal sealed class WaveSample
    {
        public ushort UnityNote = 60;
        public short FineTune;
        public int Attenuation;
        public uint Options;
        public List<WaveLoop> Loops = new();
    }

    internal sealed class DlsRegion
    {
        public ushort KeyLow, KeyHigh = 127, VelLow, VelHigh = 127;
        public ushort Options, KeyGroup;
        public WaveSample? Sample;
        public uint TableIndex;
        public List<Connection>? Articulation;
    }

    internal sealed class DlsInstrument
    {
        public uint Bank, Program;
        public bool IsDrum;
        public string Name = "";
        public List<DlsRegion> Regions = new();
        public List<Connection>? Articulation;
    }

    internal sealed class DlsWave
    {
        public short[] Data = Array.Empty<short>();
        public int SampleRate = 22050;
        public WaveSample? Sample;
        public string Name = "";
    }

    internal sealed class DlsBank
    {
        public List<DlsInstrument> Instruments = new();
        public List<DlsWave> Waves = new();
        /// <summary>Maps a pool-table index (wlnk.ulTableIndex) to an index into <see cref="Waves"/>, or -1.</summary>
        public int[] PoolTable = Array.Empty<int>();
    }

    // DLS connection sources.
    internal const ushort SrcNone = 0x0000, SrcLfo = 0x0001, SrcVelocity = 0x0002, SrcKeyNumber = 0x0003,
        SrcEg1 = 0x0004, SrcEg2 = 0x0005, SrcPitchWheel = 0x0006, SrcPolyPressure = 0x0007,
        SrcChannelPressure = 0x0008, SrcVibrato = 0x0009, SrcCc1 = 0x0081, SrcCc7 = 0x0087,
        SrcCc10 = 0x008A, SrcCc11 = 0x008B, SrcCc91 = 0x00DB, SrcCc93 = 0x00DD;

    // DLS connection destinations.
    internal const ushort DstNone = 0x0000, DstAttenuation = 0x0001, DstPitch = 0x0003, DstPan = 0x0004,
        DstKeyNumber = 0x0005, DstChorus = 0x0080, DstReverb = 0x0081,
        DstLfoFrequency = 0x0104, DstLfoStartDelay = 0x0105, DstVibFrequency = 0x0114, DstVibStartDelay = 0x0115,
        DstEg1Attack = 0x0206, DstEg1Decay = 0x0207, DstEg1Release = 0x0209, DstEg1Sustain = 0x020A,
        DstEg1Delay = 0x020B, DstEg1Hold = 0x020C, DstEg1Shutdown = 0x020D,
        DstEg2Attack = 0x030A, DstEg2Decay = 0x030B, DstEg2Release = 0x030D, DstEg2Sustain = 0x030E,
        DstEg2Delay = 0x030F, DstEg2Hold = 0x0310,
        DstFilterCutoff = 0x0500, DstFilterQ = 0x0501;

    /// <summary>DLS "zero time" / "unset" sentinel for 32-bit time cents.</summary>
    private const int TimeCentsZero = unchecked((int)0x80000000);
}
