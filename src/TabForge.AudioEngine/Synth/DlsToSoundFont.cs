using System.Text;
using System.Buffers.Binary;

namespace TabForge.AudioEngine.Synth;

// Owns: converting the Windows General MIDI bank (gm.dls) into an in-memory SoundFont 2 image for MeltySynth: parsing the untrusted
//   file, mapping articulation, the gain calibration, and the cached load.
// Does not own: the synthesiser (MeltySynth) and the General MIDI synth that plays the bank (GmSynth).
// Tests: TestGmPolyphony.
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
public static class DlsToSoundFont
{
    private static readonly Lazy<MeltySynth.SoundFont> WindowsBank =
        new(() => LoadWindowsBankCore(true), LazyThreadSafetyMode.PublicationOnly);
    private static readonly Lazy<MeltySynth.SoundFont> WindowsBankRaw =
        new(() => LoadWindowsBankCore(false), LazyThreadSafetyMode.PublicationOnly);

    /// <summary>Path of the Windows GM/GS DLS bank.</summary>
    public static string WindowsBankPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "drivers", "gm.dls");

    /// <summary>Loads, converts and caches the Windows GM bank (gm.dls) as a MeltySynth SoundFont.</summary>
    /// <exception cref="FileNotFoundException">gm.dls is not present.</exception>
    /// <exception cref="InvalidDataException">gm.dls could not be parsed or converted.</exception>
    public static MeltySynth.SoundFont LoadWindowsBank() => GmSynthTuning.Calibrate ? WindowsBank.Value : WindowsBankRaw.Value;

    private static MeltySynth.SoundFont LoadWindowsBankCore(bool calibrated)
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
        var sf2 = Convert(dls, calibrated ? GmSynthTuning.CalibrationAttenuationDb : null);
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

    // ------------------------------------------------------------------------------------------------
    // RIFF / DLS parser
    // ------------------------------------------------------------------------------------------------

    private static class DlsParser
    {
        private const int MaxInstruments = 65536, MaxRegions = 4096, MaxConnections = 4096, MaxLoops = 64;

        private readonly record struct Chunk(uint Id, int Offset, int Size, uint ListType);

        private static uint FourCC(string s) =>
            (uint)(s[0] | (s[1] << 8) | (s[2] << 16) | (s[3] << 24));

        private static readonly uint IdRiff = FourCC("RIFF"), IdList = FourCC("LIST"), IdDls = FourCC("DLS "),
            IdColh = FourCC("colh"), IdLins = FourCC("lins"), IdIns = FourCC("ins "), IdInsh = FourCC("insh"),
            IdLrgn = FourCC("lrgn"), IdRgn = FourCC("rgn "), IdRgn2 = FourCC("rgn2"), IdRgnh = FourCC("rgnh"),
            IdWsmp = FourCC("wsmp"), IdWlnk = FourCC("wlnk"), IdLart = FourCC("lart"), IdLar2 = FourCC("lar2"),
            IdArt1 = FourCC("art1"), IdArt2 = FourCC("art2"), IdPtbl = FourCC("ptbl"), IdWvpl = FourCC("wvpl"),
            IdWave = FourCC("wave"), IdFmt = FourCC("fmt "), IdData = FourCC("data"), IdInfo = FourCC("INFO"),
            IdInam = FourCC("INAM");

        /// <summary>Enumerates the sub-chunks in [start, end). Truncated trailing chunks are ignored.</summary>
        private static List<Chunk> Children(byte[] d, int start, int end)
        {
            var list = new List<Chunk>();
            long pos = start;
            while (pos + 8 <= end)
            {
                uint id = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan((int)pos));
                uint size = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan((int)pos + 4));
                long dataStart = pos + 8;
                if (size > end - dataStart) break; // truncated / corrupt: stop at the parent boundary
                uint listType = 0;
                int off = (int)dataStart, sz = (int)size;
                if ((id == IdList || id == IdRiff) && size >= 4)
                {
                    listType = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(off));
                    off += 4; sz -= 4;
                }
                list.Add(new Chunk(id, off, sz, listType));
                long next = dataStart + size + (size & 1);
                if (next <= pos) break; // cannot happen, but guarantees progress
                pos = next;
            }
            return list;
        }

        private static ushort U16(byte[] d, int o) => BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(o, 2));
        private static short S16(byte[] d, int o) => BinaryPrimitives.ReadInt16LittleEndian(d.AsSpan(o, 2));
        private static uint U32(byte[] d, int o) => BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(o, 4));
        private static int S32(byte[] d, int o) => BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(o, 4));

        public static DlsBank Parse(byte[] d)
        {
            if (d.Length < 12 || U32(d, 0) != IdRiff || U32(d, 8) != IdDls)
                throw new InvalidDataException("The data is not a DLS sound bank (missing RIFF 'DLS ' header).");
            long riffSize = U32(d, 4);
            int end = (int)Math.Min(d.Length, 8 + riffSize);

            var bank = new DlsBank();
            uint[]? cues = null;
            Chunk? wvpl = null;
            foreach (var c in Children(d, 12, end))
            {
                if (c.Id == IdList && c.ListType == IdLins) ParseInstruments(d, c, bank);
                else if (c.Id == IdPtbl) cues = ParsePoolTable(d, c);
                else if (c.Id == IdList && c.ListType == IdWvpl) wvpl = c;
            }
            if (wvpl is null) throw new InvalidDataException("The DLS bank has no wave pool ('wvpl').");
            if (bank.Instruments.Count == 0) throw new InvalidDataException("The DLS bank contains no instruments.");

            // Waves, remembering each LIST 'wave' offset relative to the wave pool's data.
            var offsetToWave = new Dictionary<long, int>();
            foreach (var c in Children(d, wvpl.Value.Offset, wvpl.Value.Offset + wvpl.Value.Size))
            {
                if (c.Id != IdList || c.ListType != IdWave) continue;
                var wave = ParseWave(d, c);
                if (wave is null) continue;
                long chunkStart = c.Offset - 12; // start of the 'LIST' header
                offsetToWave[chunkStart - wvpl.Value.Offset] = bank.Waves.Count;
                bank.Waves.Add(wave);
            }
            if (bank.Waves.Count == 0) throw new InvalidDataException("The DLS wave pool contains no usable 16-bit/8-bit PCM waves.");

            if (cues is not null)
            {
                bank.PoolTable = new int[cues.Length];
                for (int i = 0; i < cues.Length; i++)
                {
                    // ulOffset is relative to the first byte after the 'wvpl' list type; tolerate the
                    // (rare) writers that measure from the LIST header instead.
                    if (offsetToWave.TryGetValue(cues[i], out int w)) bank.PoolTable[i] = w;
                    else if (offsetToWave.TryGetValue((long)cues[i] - 12, out w)) bank.PoolTable[i] = w;
                    else bank.PoolTable[i] = -1;
                }
            }
            else
            {
                bank.PoolTable = Enumerable.Range(0, bank.Waves.Count).ToArray();
            }
            return bank;
        }

        private static uint[]? ParsePoolTable(byte[] d, Chunk c)
        {
            if (c.Size < 8) return null;
            uint cbSize = U32(d, c.Offset);
            uint count = U32(d, c.Offset + 4);
            if (cbSize < 8 || cbSize > c.Size) return null;
            long available = (c.Size - cbSize) / 4;
            if (count > available) count = (uint)available;
            var cues = new uint[count];
            for (int i = 0; i < count; i++) cues[i] = U32(d, c.Offset + (int)cbSize + 4 * i);
            return cues;
        }

        private static void ParseInstruments(byte[] d, Chunk lins, DlsBank bank)
        {
            foreach (var c in Children(d, lins.Offset, lins.Offset + lins.Size))
            {
                if (c.Id != IdList || c.ListType != IdIns) continue;
                if (bank.Instruments.Count >= MaxInstruments) break;
                var ins = new DlsInstrument();
                bool haveHeader = false;
                foreach (var s in Children(d, c.Offset, c.Offset + c.Size))
                {
                    if (s.Id == IdInsh && s.Size >= 12)
                    {
                        uint bankField = U32(d, s.Offset + 4);
                        ins.IsDrum = (bankField & 0x80000000u) != 0;
                        ins.Bank = bankField & 0x7FFFFFFFu;
                        ins.Program = U32(d, s.Offset + 8) & 0x7F;
                        haveHeader = true;
                    }
                    else if (s.Id == IdList && s.ListType == IdLrgn)
                    {
                        foreach (var r in Children(d, s.Offset, s.Offset + s.Size))
                        {
                            if (r.Id != IdList || (r.ListType != IdRgn && r.ListType != IdRgn2)) continue;
                            if (ins.Regions.Count >= MaxRegions) break;
                            var region = ParseRegion(d, r);
                            if (region is not null) ins.Regions.Add(region);
                        }
                    }
                    else if (s.Id == IdList && (s.ListType == IdLart || s.ListType == IdLar2))
                    {
                        ins.Articulation = ParseArticulation(d, s, ins.Articulation);
                    }
                    else if (s.Id == IdList && s.ListType == IdInfo)
                    {
                        ins.Name = ReadName(d, s) ?? ins.Name;
                    }
                }
                if (haveHeader && ins.Regions.Count > 0) bank.Instruments.Add(ins);
            }
        }

        private static string? ReadName(byte[] d, Chunk info)
        {
            foreach (var c in Children(d, info.Offset, info.Offset + info.Size))
            {
                if (c.Id != IdInam || c.Size <= 0) continue;
                int len = 0;
                while (len < c.Size && d[c.Offset + len] != 0) len++;
                return Encoding.Latin1.GetString(d, c.Offset, len).Trim();
            }
            return null;
        }

        private static DlsRegion? ParseRegion(byte[] d, Chunk rgn)
        {
            var region = new DlsRegion();
            bool haveHeader = false, haveLink = false;
            foreach (var c in Children(d, rgn.Offset, rgn.Offset + rgn.Size))
            {
                if (c.Id == IdRgnh && c.Size >= 12)
                {
                    region.KeyLow = U16(d, c.Offset);
                    region.KeyHigh = U16(d, c.Offset + 2);
                    region.VelLow = U16(d, c.Offset + 4);
                    region.VelHigh = U16(d, c.Offset + 6);
                    region.Options = U16(d, c.Offset + 8);
                    region.KeyGroup = U16(d, c.Offset + 10);
                    if (region.VelLow == 0 && region.VelHigh == 0) region.VelHigh = 127; // DLS1 writers leave it zero
                    region.KeyLow = Math.Min(region.KeyLow, (ushort)127);
                    region.KeyHigh = Math.Min(region.KeyHigh, (ushort)127);
                    region.VelLow = Math.Min(region.VelLow, (ushort)127);
                    region.VelHigh = Math.Min(region.VelHigh, (ushort)127);
                    haveHeader = region.KeyLow <= region.KeyHigh && region.VelLow <= region.VelHigh;
                }
                else if (c.Id == IdWsmp) region.Sample = ParseWsmp(d, c) ?? region.Sample;
                else if (c.Id == IdWlnk && c.Size >= 12)
                {
                    region.TableIndex = U32(d, c.Offset + 8);
                    haveLink = true;
                }
                else if (c.Id == IdList && (c.ListType == IdLart || c.ListType == IdLar2))
                {
                    region.Articulation = ParseArticulation(d, c, region.Articulation);
                }
            }
            return haveHeader && haveLink ? region : null;
        }

        private static WaveSample? ParseWsmp(byte[] d, Chunk c)
        {
            if (c.Size < 20) return null;
            uint cbSize = U32(d, c.Offset);
            if (cbSize < 20 || cbSize > c.Size) return null;
            var w = new WaveSample
            {
                UnityNote = Math.Min(U16(d, c.Offset + 4), (ushort)127),
                FineTune = S16(d, c.Offset + 6),
                Attenuation = S32(d, c.Offset + 8),
                Options = U32(d, c.Offset + 12),
            };
            uint loops = U32(d, c.Offset + 16);
            int pos = c.Offset + (int)cbSize;
            int end = c.Offset + c.Size;
            for (uint i = 0; i < loops && i < MaxLoops; i++)
            {
                if (pos + 16 > end) break;
                uint loopSize = U32(d, pos);
                if (loopSize < 16 || loopSize > end - pos) break;
                w.Loops.Add(new WaveLoop(U32(d, pos + 4), U32(d, pos + 8), U32(d, pos + 12)));
                pos += (int)loopSize;
            }
            return w;
        }

        private static List<Connection> ParseArticulation(byte[] d, Chunk lart, List<Connection>? existing)
        {
            var list = existing ?? new List<Connection>();
            foreach (var c in Children(d, lart.Offset, lart.Offset + lart.Size))
            {
                if ((c.Id != IdArt1 && c.Id != IdArt2) || c.Size < 8) continue;
                uint cbSize = U32(d, c.Offset);
                uint count = U32(d, c.Offset + 4);
                if (cbSize < 8 || cbSize > c.Size) continue;
                long available = (c.Size - cbSize) / 12;
                if (count > available) count = (uint)available;
                int pos = c.Offset + (int)cbSize;
                for (int i = 0; i < count && list.Count < MaxConnections; i++, pos += 12)
                    list.Add(new Connection(U16(d, pos), U16(d, pos + 2), U16(d, pos + 4), U16(d, pos + 6), S32(d, pos + 8)));
            }
            return list;
        }

        private static DlsWave? ParseWave(byte[] d, Chunk waveList)
        {
            var wave = new DlsWave();
            int channels = 0, bits = 0, format = 0;
            Chunk? data = null;
            foreach (var c in Children(d, waveList.Offset, waveList.Offset + waveList.Size))
            {
                if (c.Id == IdFmt && c.Size >= 16)
                {
                    format = U16(d, c.Offset);
                    channels = U16(d, c.Offset + 2);
                    wave.SampleRate = (int)Math.Min(U32(d, c.Offset + 4), 192000u);
                    bits = U16(d, c.Offset + 14);
                }
                else if (c.Id == IdData) data = c;
                else if (c.Id == IdWsmp) wave.Sample = ParseWsmp(d, c) ?? wave.Sample;
                else if (c.Id == IdList && c.ListType == IdInfo) wave.Name = ReadName(d, c) ?? wave.Name;
            }
            if (data is null || format != 1 || channels < 1 || wave.SampleRate < 400) return null;
            int frameBytes = channels * (bits / 8);
            if ((bits != 16 && bits != 8) || frameBytes <= 0) return null;
            int frames = data.Value.Size / frameBytes;
            var pcm = new short[frames];
            int o = data.Value.Offset;
            for (int i = 0; i < frames; i++, o += frameBytes) // first channel only
                pcm[i] = bits == 16 ? S16(d, o) : (short)((d[o] - 128) << 8);
            wave.Data = pcm;
            return wave;
        }
    }

    // ------------------------------------------------------------------------------------------------
    // Articulation -> SF2 generators
    // ------------------------------------------------------------------------------------------------

    /// <summary>Resolved per-region synthesis parameters, in SF2 units.</summary>
    private sealed class Articulation
    {
        // Volume envelope (timecents; sustain in cB of attenuation).
        public double Eg1Delay = -12000, Eg1Attack = -12000, Eg1Hold = -12000, Eg1Decay = -12000, Eg1Release = -12000;
        public double Eg1SustainCb;
        public double KeyToEg1Decay, KeyToEg1Hold; // SF2 tc/key
        // Modulation envelope.
        public double Eg2Delay = -12000, Eg2Attack = -12000, Eg2Hold = -12000, Eg2Decay = -12000, Eg2Release = -12000;
        public double Eg2SustainPercentDrop; // SF2 sustainModEnv in 0.1% below peak
        public double KeyToEg2Decay, KeyToEg2Hold;
        public double Eg2ToPitch, Eg2ToFilter;
        // LFOs (absolute cents / timecents).
        public double LfoFreq = -851.3179, LfoDelay = -7972.6274; // DLS1 defaults: 5 Hz, 10 ms
        public double VibFreq = -851.3179, VibDelay = -7972.6274;
        public double LfoToPitch, LfoToVolumeCb, LfoToFilter, VibToPitch;
        // Misc.
        public double GainDb;      // constant gain from articulation (dB, negative = quieter)
        public double Pan;         // SF2 0.1% units
        public double FilterFc = 13500, FilterQCb;
        public double ReverbSend, ChorusSend; // 0.1%
        public double ScaleTuning = 100;
        public double PitchCents;
    }

    private static double Tc(int dlsScale) => dlsScale == TimeCentsZero ? -12000 : dlsScale / 65536.0;

    private static Articulation Resolve(List<Connection>? instrumentArt, List<Connection>? regionArt)
    {
        // Later entries override earlier ones for the same (source, control, destination).
        var map = new Dictionary<(ushort, ushort, ushort), int>();
        if (instrumentArt is not null) foreach (var c in instrumentArt) map[(c.Source, c.Control, c.Destination)] = c.Scale;
        if (regionArt is not null) foreach (var c in regionArt) map[(c.Source, c.Control, c.Destination)] = c.Scale;

        bool Has(ushort src, ushort ctl, ushort dst, out int v) => map.TryGetValue((src, ctl, dst), out v);

        var a = new Articulation();
        if (Has(SrcNone, SrcNone, DstEg1Delay, out int v)) a.Eg1Delay = Tc(v);
        if (Has(SrcNone, SrcNone, DstEg1Attack, out v)) a.Eg1Attack = Tc(v);
        if (Has(SrcNone, SrcNone, DstEg1Hold, out v)) a.Eg1Hold = Tc(v);
        if (Has(SrcNone, SrcNone, DstEg1Decay, out v)) a.Eg1Decay = Tc(v);
        if (Has(SrcNone, SrcNone, DstEg1Release, out v)) a.Eg1Release = Tc(v);
        double sustainPct = 100; // DLS default: 100 %
        if (Has(SrcNone, SrcNone, DstEg1Sustain, out v)) sustainPct = Math.Clamp(v / 65536.0 / 10.0, 0, 100);
        a.Eg1SustainCb = SustainPercentToCb(sustainPct);

        // Key number scaling: DLS scale is tc over the full 0..127 key range (source normalised to key/128);
        // SF2 is tc per key relative to key 60 with the opposite sign convention.
        if (Has(SrcKeyNumber, SrcNone, DstEg1Decay, out v)) { double s = v / 65536.0; a.KeyToEg1Decay = -s / 128.0; a.Eg1Decay += s * 60 / 128.0; }
        if (Has(SrcKeyNumber, SrcNone, DstEg1Hold, out v)) { double s = v / 65536.0; a.KeyToEg1Hold = -s / 128.0; a.Eg1Hold += s * 60 / 128.0; }

        if (Has(SrcNone, SrcNone, DstEg2Delay, out v)) a.Eg2Delay = Tc(v);
        if (Has(SrcNone, SrcNone, DstEg2Attack, out v)) a.Eg2Attack = Tc(v);
        if (Has(SrcNone, SrcNone, DstEg2Hold, out v)) a.Eg2Hold = Tc(v);
        if (Has(SrcNone, SrcNone, DstEg2Decay, out v)) a.Eg2Decay = Tc(v);
        if (Has(SrcNone, SrcNone, DstEg2Release, out v)) a.Eg2Release = Tc(v);
        double eg2Sustain = 100;
        if (Has(SrcNone, SrcNone, DstEg2Sustain, out v)) eg2Sustain = Math.Clamp(v / 65536.0 / 10.0, 0, 100);
        a.Eg2SustainPercentDrop = (100 - eg2Sustain) * 10;
        if (Has(SrcKeyNumber, SrcNone, DstEg2Decay, out v)) { double s = v / 65536.0; a.KeyToEg2Decay = -s / 128.0; a.Eg2Decay += s * 60 / 128.0; }
        if (Has(SrcKeyNumber, SrcNone, DstEg2Hold, out v)) { double s = v / 65536.0; a.KeyToEg2Hold = -s / 128.0; a.Eg2Hold += s * 60 / 128.0; }
        if (Has(SrcEg2, SrcNone, DstPitch, out v)) a.Eg2ToPitch = v / 65536.0;
        if (Has(SrcEg2, SrcNone, DstFilterCutoff, out v)) a.Eg2ToFilter = v / 65536.0;

        if (Has(SrcNone, SrcNone, DstLfoFrequency, out v)) a.LfoFreq = v / 65536.0;
        if (Has(SrcNone, SrcNone, DstLfoStartDelay, out v)) a.LfoDelay = Tc(v);
        if (Has(SrcNone, SrcNone, DstVibFrequency, out v)) a.VibFreq = v / 65536.0; else a.VibFreq = a.LfoFreq;
        if (Has(SrcNone, SrcNone, DstVibStartDelay, out v)) a.VibDelay = Tc(v); else a.VibDelay = a.LfoDelay;
        if (Has(SrcLfo, SrcNone, DstPitch, out v)) a.LfoToPitch = v / 65536.0;
        if (Has(SrcLfo, SrcNone, DstAttenuation, out v)) a.LfoToVolumeCb = -v / 65536.0; // gain -> attenuation
        if (Has(SrcLfo, SrcNone, DstFilterCutoff, out v)) a.LfoToFilter = v / 65536.0;
        if (Has(SrcVibrato, SrcNone, DstPitch, out v)) a.VibToPitch = v / 65536.0;

        if (Has(SrcNone, SrcNone, DstAttenuation, out v)) a.GainDb = v / 655360.0;
        if (Has(SrcNone, SrcNone, DstPan, out v)) a.Pan = v / 65536.0;
        if (Has(SrcNone, SrcNone, DstPitch, out v)) a.PitchCents = v / 65536.0;
        if (Has(SrcNone, SrcNone, DstFilterCutoff, out v) && v != int.MaxValue) a.FilterFc = v / 65536.0;
        if (Has(SrcNone, SrcNone, DstFilterQ, out v)) a.FilterQCb = v / 65536.0;
        if (Has(SrcNone, SrcNone, DstReverb, out v)) a.ReverbSend = v / 65536.0;
        if (Has(SrcNone, SrcNone, DstChorus, out v)) a.ChorusSend = v / 65536.0;
        if (Has(SrcKeyNumber, SrcNone, DstPitch, out v)) a.ScaleTuning = v / 65536.0 / 128.0;
        return a;
    }

    /// <summary>
    /// DLS EG1 sustain is a fraction of peak on the envelope's 96 dB attenuation scale:
    /// attenuation = 96 dB * (1 - sustain%).
    /// </summary>
    private static double SustainPercentToCb(double pct) => 960.0 * (1.0 - pct / 100.0);

    // ------------------------------------------------------------------------------------------------
    // SF2 writer
    // ------------------------------------------------------------------------------------------------

    private static class Sf2Writer
    {
        private const int SamplePadding = 46;

        /// <summary>
        /// MeltySynth multiplies the SF2 initialAttenuation generator by 0.4 (Polyphone convention), so a DLS
        /// attenuation of X dB is written as X / 0.4 dB to make MeltySynth apply exactly X dB.
        /// </summary>
        private const double MeltySynthAttenuationFactor = 0.4;

        // SF2 generator operators.
        private const ushort GStartLoopOfs = 2, GEndLoopOfs = 3, GModLfoToPitch = 5, GVibLfoToPitch = 6,
            GModEnvToPitch = 7, GFilterFc = 8, GFilterQ = 9, GModLfoToFilter = 10, GModEnvToFilter = 11,
            GModLfoToVolume = 13, GChorus = 15, GReverb = 16, GPan = 17, GDelayModLfo = 21, GFreqModLfo = 22,
            GDelayVibLfo = 23, GFreqVibLfo = 24, GDelayModEnv = 25, GAttackModEnv = 26, GHoldModEnv = 27,
            GDecayModEnv = 28, GSustainModEnv = 29, GReleaseModEnv = 30, GKeyToModEnvHold = 31, GKeyToModEnvDecay = 32,
            GDelayVolEnv = 33, GAttackVolEnv = 34, GHoldVolEnv = 35, GDecayVolEnv = 36, GSustainVolEnv = 37,
            GReleaseVolEnv = 38, GKeyToVolEnvHold = 39, GKeyToVolEnvDecay = 40, GInstrument = 41, GKeyRange = 43,
            GVelRange = 44, GStartLoopCoarse = 45, GAttenuation = 48, GEndLoopCoarse = 50, GCoarseTune = 51,
            GFineTune = 52, GSampleId = 53, GSampleModes = 54, GScaleTuning = 56, GExclusiveClass = 57,
            GRootKey = 58;

        private readonly struct SampleHeader
        {
            public readonly string Name;
            public readonly uint Start, End, LoopStart, LoopEnd, Rate;
            public readonly byte Pitch;
            public readonly sbyte Correction;
            public SampleHeader(string name, uint start, uint end, uint loopStart, uint loopEnd, uint rate, byte pitch, sbyte corr)
            { Name = name; Start = start; End = end; LoopStart = loopStart; LoopEnd = loopEnd; Rate = rate; Pitch = pitch; Correction = corr; }
        }

        public static byte[] Write(DlsBank bank, Func<bool, int, int, double>? extraAttenuationDb = null)
        {
            // ---- samples ----
            long totalSamples = 0;
            foreach (var w in bank.Waves) totalSamples += w.Data.Length + SamplePadding;
            if (totalSamples * 2 > int.MaxValue - 1_000_000) throw new InvalidDataException("The DLS wave pool is too large.");
            var smpl = new byte[totalSamples * 2];
            var headers = new List<SampleHeader>(bank.Waves.Count);
            uint cursor = 0;
            for (int i = 0; i < bank.Waves.Count; i++)
            {
                var w = bank.Waves[i];
                for (int s = 0; s < w.Data.Length; s++)
                    BinaryPrimitives.WriteInt16LittleEndian(smpl.AsSpan((int)(cursor + s) * 2), w.Data[s]);
                uint start = cursor, end = cursor + (uint)w.Data.Length;
                uint ls = start, le = end;
                if (w.Sample is { Loops.Count: > 0 } ws && TryLoop(ws.Loops[0], w.Data.Length, out uint a, out uint b))
                { ls = start + a; le = start + b; }
                byte pitch = (byte)(w.Sample?.UnityNote ?? 60);
                sbyte corr = 0; // tuning is carried per zone (overridingRootKey + fineTune generators)
                string name = string.IsNullOrWhiteSpace(w.Name) ? $"wave{i}" : w.Name;
                headers.Add(new SampleHeader(name, start, end, ls, le, (uint)w.SampleRate, pitch, corr));
                cursor = end + SamplePadding;
            }

            // ---- instruments & presets ----
            var inst = new MemoryStream(); var ibag = new MemoryStream(); var igen = new MemoryStream();
            var phdr = new MemoryStream(); var pbag = new MemoryStream(); var pgen = new MemoryStream();
            int ibagCount = 0, igenCount = 0, pbagCount = 0, pgenCount = 0, instCount = 0;

            // Sort presets by bank/program (SF2 readers do not require it, but it keeps the file tidy); drop
            // duplicate bank/program pairs (first wins).
            var seen = new HashSet<(int, int)>();
            var ordered = bank.Instruments
                .Select(i => (Ins: i, Bank: i.IsDrum ? 128 : (int)Math.Min((i.Bank >> 8) & 0x7F, 127u), Prog: (int)i.Program))
                .OrderBy(t => t.Bank).ThenBy(t => t.Prog).ToList();

            foreach (var (ins, presetBank, program) in ordered)
            {
                if (!seen.Add((presetBank, program))) continue;
                int instBagStart = ibagCount;
                foreach (var r in ins.Regions)
                {
                    if (r.TableIndex >= bank.PoolTable.Length) continue;
                    int waveIndex = bank.PoolTable[r.TableIndex];
                    if (waveIndex < 0) continue;
                    var wave = bank.Waves[waveIndex];
                    var hdr = headers[waveIndex];
                    var ws = r.Sample ?? wave.Sample ?? new WaveSample();
                    var art = Resolve(ins.Articulation, r.Articulation);

                    WriteBag(ibag, igenCount, 0); ibagCount++;
                    void G(ushort op, int amount) { WriteGen(igen, op, (short)Math.Clamp(amount, short.MinValue, short.MaxValue)); igenCount++; }
                    void GR(ushort op, byte lo, byte hi) { WriteGenRange(igen, op, lo, hi); igenCount++; }
                    int R(double x) => (int)Math.Round(x);

                    GR(GKeyRange, (byte)r.KeyLow, (byte)r.KeyHigh);
                    if (r.VelLow != 0 || r.VelHigh != 127) GR(GVelRange, (byte)r.VelLow, (byte)r.VelHigh);

                    // Loops (region wsmp overrides the wave's).
                    bool looped = ws.Loops.Count > 0 && TryLoop(ws.Loops[0], wave.Data.Length, out _, out _);
                    if (looped)
                    {
                        TryLoop(ws.Loops[0], wave.Data.Length, out uint la, out uint lb);
                        long dStart = (long)(hdr.Start + la) - hdr.LoopStart;
                        long dEnd = (long)(hdr.Start + lb) - hdr.LoopEnd;
                        if (dStart != 0) { G(GStartLoopOfs, (int)(dStart % 32768)); if (dStart / 32768 != 0) G(GStartLoopCoarse, (int)(dStart / 32768)); }
                        if (dEnd != 0) { G(GEndLoopOfs, (int)(dEnd % 32768)); if (dEnd / 32768 != 0) G(GEndLoopCoarse, (int)(dEnd / 32768)); }
                        G(GSampleModes, ws.Loops[0].Type == 1 ? 3 : 1);
                    }

                    // Level: wsmp gain + constant articulation gain (DLS gain units: 1/655360 dB).
                    double gainDb = ws.Attenuation / 655360.0 + art.GainDb;
                    // Measured level calibration against the GS synth (isDrum, program, key) -> extra attenuation dB, >= 0.
                    if (extraAttenuationDb is not null) gainDb -= extraAttenuationDb(ins.IsDrum, program, (r.KeyLow + r.KeyHigh) / 2);
                    // Positive region gains would be lost to the clamp; measured: none of the measured regions has one (kick 35 = 0 dB),
                    // the remaining differences are corrected by the calibration table in GmSynthTuning.
                    double attenDb = Math.Max(0, -gainDb);
                    if (attenDb > 0) G(GAttenuation, R(attenDb * 10 / MeltySynthAttenuationFactor));

                    // Tuning.
                    G(GRootKey, ws.UnityNote);
                    // The wsmp fine tune is the sample's own offset from its unity note; measured over every program, adding it (not
                    // subtracting it) puts the notes in tune (mean error 0.26 -> 0.07 semitone, electric guitars were up to 0.8 sharp).
                    double fine = ws.FineTune + art.PitchCents;
                    int coarse = (int)Math.Truncate(fine / 100);
                    int fineRest = R(fine - coarse * 100);
                    if (coarse != 0) G(GCoarseTune, coarse);
                    if (fineRest != 0) G(GFineTune, fineRest);
                    if (Math.Abs(art.ScaleTuning - 100) > 0.5) G(GScaleTuning, R(art.ScaleTuning));

                    // Volume envelope.
                    if (art.Eg1Delay > -12000) G(GDelayVolEnv, R(art.Eg1Delay));
                    G(GAttackVolEnv, R(Math.Max(art.Eg1Attack, -12000)));
                    if (art.Eg1Hold > -12000) G(GHoldVolEnv, R(art.Eg1Hold));
                    G(GDecayVolEnv, R(Math.Max(art.Eg1Decay, -12000)));
                    G(GSustainVolEnv, R(Math.Clamp(art.Eg1SustainCb, 0, 1440)));
                    G(GReleaseVolEnv, R(Math.Max(art.Eg1Release, -12000)));
                    if (art.KeyToEg1Decay != 0) G(GKeyToVolEnvDecay, R(art.KeyToEg1Decay));
                    if (art.KeyToEg1Hold != 0) G(GKeyToVolEnvHold, R(art.KeyToEg1Hold));

                    // Modulation envelope (only when it drives something).
                    if (art.Eg2ToPitch != 0 || art.Eg2ToFilter != 0)
                    {
                        if (art.Eg2Delay > -12000) G(GDelayModEnv, R(art.Eg2Delay));
                        G(GAttackModEnv, R(Math.Max(art.Eg2Attack, -12000)));
                        if (art.Eg2Hold > -12000) G(GHoldModEnv, R(art.Eg2Hold));
                        G(GDecayModEnv, R(Math.Max(art.Eg2Decay, -12000)));
                        G(GSustainModEnv, R(Math.Clamp(art.Eg2SustainPercentDrop, 0, 1000)));
                        G(GReleaseModEnv, R(Math.Max(art.Eg2Release, -12000)));
                        if (art.KeyToEg2Decay != 0) G(GKeyToModEnvDecay, R(art.KeyToEg2Decay));
                        if (art.KeyToEg2Hold != 0) G(GKeyToModEnvHold, R(art.KeyToEg2Hold));
                        if (art.Eg2ToPitch != 0) G(GModEnvToPitch, R(art.Eg2ToPitch));
                        if (art.Eg2ToFilter != 0) G(GModEnvToFilter, R(art.Eg2ToFilter));
                    }

                    // LFOs. The DLS1 LFO drives SF2's modulation LFO (volume/filter/pitch); the vibrato LFO
                    // carries DLS2 vibrato and the mod-wheel vibrato that MeltySynth applies to it.
                    if (art.LfoToPitch != 0 || art.LfoToVolumeCb != 0 || art.LfoToFilter != 0)
                    {
                        G(GFreqModLfo, R(art.LfoFreq));
                        G(GDelayModLfo, R(Math.Max(art.LfoDelay, -12000)));
                        if (art.LfoToPitch != 0) G(GModLfoToPitch, R(art.LfoToPitch));
                        if (art.LfoToVolumeCb != 0) G(GModLfoToVolume, R(art.LfoToVolumeCb));
                        if (art.LfoToFilter != 0) G(GModLfoToFilter, R(art.LfoToFilter));
                    }
                    G(GFreqVibLfo, R(art.VibFreq));
                    G(GDelayVibLfo, R(Math.Max(art.VibDelay, -12000)));
                    if (art.VibToPitch != 0) G(GVibLfoToPitch, R(art.VibToPitch));

                    if (art.Pan != 0) G(GPan, R(Math.Clamp(extraAttenuationDb is null ? art.Pan : GmSynthTuning.MapPan(art.Pan), -500, 500)));   // calibrated: GS kit width
                    if (art.FilterFc < 13500) G(GFilterFc, R(Math.Clamp(art.FilterFc, 1500, 13500)));
                    if (art.FilterQCb > 0) G(GFilterQ, R(Math.Clamp(art.FilterQCb, 0, 960)));
                    if (art.ReverbSend > 0) G(GReverb, R(Math.Clamp(art.ReverbSend, 0, 1000)));
                    if (art.ChorusSend > 0) G(GChorus, R(Math.Clamp(art.ChorusSend, 0, 1000)));
                    if (r.KeyGroup != 0) G(GExclusiveClass, Math.Min(r.KeyGroup, (ushort)127));

                    G(GSampleId, waveIndex);
                }
                if (ibagCount == instBagStart) continue; // no playable regions

                WriteName(inst, string.IsNullOrWhiteSpace(ins.Name) ? $"Program {program}" : ins.Name);
                WriteU16(inst, (ushort)instBagStart);

                WriteName(phdr, string.IsNullOrWhiteSpace(ins.Name) ? $"Program {program}" : ins.Name);
                WriteU16(phdr, (ushort)program);
                WriteU16(phdr, (ushort)presetBank);
                WriteU16(phdr, (ushort)pbagCount);
                WriteU32(phdr, 0); WriteU32(phdr, 0); WriteU32(phdr, 0);
                WriteBag(pbag, pgenCount, 0); pbagCount++;
                WriteGen(pgen, GInstrument, (short)instCount); pgenCount++;
                instCount++;
                if (ibagCount > ushort.MaxValue - 2 || igenCount > ushort.MaxValue - 2)
                    throw new InvalidDataException("The DLS bank has too many regions for a SoundFont 2 file.");
            }
            if (instCount == 0) throw new InvalidDataException("The DLS bank has no instrument with a playable region.");

            // Terminal records.
            WriteName(inst, "EOI"); WriteU16(inst, (ushort)ibagCount);
            WriteBag(ibag, igenCount, 0);
            WriteGen(igen, 0, 0);
            WriteName(phdr, "EOP"); WriteU16(phdr, 0); WriteU16(phdr, 0); WriteU16(phdr, (ushort)pbagCount);
            WriteU32(phdr, 0); WriteU32(phdr, 0); WriteU32(phdr, 0);
            WriteBag(pbag, pgenCount, 0);
            WriteGen(pgen, 0, 0);
            var pmod = new byte[10]; var imod = new byte[10];

            var shdr = new MemoryStream();
            foreach (var h in headers)
            {
                WriteName(shdr, h.Name);
                WriteU32(shdr, h.Start); WriteU32(shdr, h.End); WriteU32(shdr, h.LoopStart); WriteU32(shdr, h.LoopEnd);
                WriteU32(shdr, h.Rate);
                shdr.WriteByte(h.Pitch); shdr.WriteByte((byte)h.Correction);
                WriteU16(shdr, 0); WriteU16(shdr, 1); // link, mono
            }
            WriteName(shdr, "EOS");
            for (int i = 0; i < 26; i++) shdr.WriteByte(0);

            // ---- assemble RIFF ----
            var info = new MemoryStream();
            WriteChunk(info, "ifil", new byte[] { 2, 0, 1, 0 });
            WriteChunk(info, "isng", ZString("EMU8000"));
            WriteChunk(info, "INAM", ZString("Microsoft GS (gm.dls)"));
            var sdta = new MemoryStream();
            WriteChunk(sdta, "smpl", smpl);
            var pdta = new MemoryStream();
            WriteChunk(pdta, "phdr", phdr.ToArray());
            WriteChunk(pdta, "pbag", pbag.ToArray());
            WriteChunk(pdta, "pmod", pmod);
            WriteChunk(pdta, "pgen", pgen.ToArray());
            WriteChunk(pdta, "inst", inst.ToArray());
            WriteChunk(pdta, "ibag", ibag.ToArray());
            WriteChunk(pdta, "imod", imod);
            WriteChunk(pdta, "igen", igen.ToArray());
            WriteChunk(pdta, "shdr", shdr.ToArray());

            var body = new MemoryStream(capacity: smpl.Length + (int)pdta.Length + 1024);
            body.Write(Encoding.ASCII.GetBytes("sfbk"));
            WriteList(body, "INFO", info.ToArray());
            WriteList(body, "sdta", sdta.ToArray());
            WriteList(body, "pdta", pdta.ToArray());

            var file = new MemoryStream(capacity: (int)body.Length + 8);
            file.Write(Encoding.ASCII.GetBytes("RIFF"));
            WriteU32(file, (uint)body.Length);
            body.Position = 0;
            body.CopyTo(file);
            return file.ToArray();
        }

        private static bool TryLoop(WaveLoop loop, int frames, out uint start, out uint end)
        {
            start = loop.Start;
            end = (uint)Math.Min((ulong)loop.Start + loop.Length, (ulong)frames);
            return loop.Length >= 2 && start < end && end - start >= 2;
        }

        private static byte[] ZString(string s)
        {
            var b = Encoding.ASCII.GetBytes(s);
            var r = new byte[(b.Length + 2) & ~1]; // null terminated, even length
            b.CopyTo(r, 0);
            return r;
        }

        private static void WriteName(Stream s, string name)
        {
            var buf = new byte[20];
            var b = Encoding.ASCII.GetBytes(name.Length > 19 ? name[..19] : name);
            b.CopyTo(buf, 0);
            s.Write(buf);
        }

        private static void WriteU16(Stream s, ushort v) { Span<byte> b = stackalloc byte[2]; BinaryPrimitives.WriteUInt16LittleEndian(b, v); s.Write(b); }
        private static void WriteU32(Stream s, uint v) { Span<byte> b = stackalloc byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(b, v); s.Write(b); }
        private static void WriteBag(Stream s, int gen, int mod) { WriteU16(s, (ushort)gen); WriteU16(s, (ushort)mod); }
        private static void WriteGen(Stream s, ushort op, short amount) { WriteU16(s, op); WriteU16(s, unchecked((ushort)amount)); }
        private static void WriteGenRange(Stream s, ushort op, byte lo, byte hi) { WriteU16(s, op); s.WriteByte(lo); s.WriteByte(hi); }

        private static void WriteChunk(Stream s, string id, byte[] data)
        {
            s.Write(Encoding.ASCII.GetBytes(id));
            WriteU32(s, (uint)data.Length);
            s.Write(data);
            if ((data.Length & 1) != 0) s.WriteByte(0);
        }

        private static void WriteList(Stream s, string type, byte[] data)
        {
            s.Write(Encoding.ASCII.GetBytes("LIST"));
            WriteU32(s, (uint)(data.Length + 4));
            s.Write(Encoding.ASCII.GetBytes(type));
            s.Write(data);
        }
    }

}
