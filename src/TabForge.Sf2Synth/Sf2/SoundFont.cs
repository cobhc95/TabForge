using System.Runtime.InteropServices;
using System.Text;

namespace TabForge.Sf2Synth.Sf2;

// Owns: loading a .sf2 (RIFF sfbk: 16-bit smpl, pdta via Sf2Reader) and finding a preset by bank and program.
// Does not own: the hydra parsing (Sf2Reader), playback (Synthesizer).
// Tests: Sf2SynthTests.
public sealed class SoundFont
{
    internal short[] Data { get; private set; } = [];
    internal SampleHeader[] Samples { get; set; } = [];
    internal Instrument[] Instruments { get; set; } = [];
    public IReadOnlyList<Preset> Presets { get; internal set; } = [];
    private Dictionary<int, Preset> _byNumber = [];

    public SoundFont(string path) : this(File.ReadAllBytes(path)) { }

    public SoundFont(byte[] file)
    {
        using var r = new BinaryReader(new MemoryStream(file), Encoding.ASCII);
        if (Id(r) != "RIFF") throw new InvalidDataException("not a RIFF file");
        var end = r.ReadInt32() + 8;
        if (Id(r) != "sfbk") throw new InvalidDataException("not a SoundFont 2 file");
        while (r.BaseStream.Position + 8 <= end)
        {
            var id = Id(r);
            var size = r.ReadInt32();
            var next = r.BaseStream.Position + size + (size & 1);
            if (id == "LIST")
            {
                var type = Id(r);
                if (type == "sdta") ReadSdta(r, next);
                else if (type == "pdta") Sf2Reader.Read(r, next, this);
            }
            r.BaseStream.Position = next;
        }
        if (Data.Length == 0 || Presets.Count == 0) throw new InvalidDataException("the SoundFont has no samples or presets");
        _byNumber = Presets.GroupBy(p => Key(p.Bank, p.Program)).ToDictionary(g => g.Key, g => g.First());
    }

    internal static string Id(BinaryReader r) => Encoding.ASCII.GetString(r.ReadBytes(4));

    private void ReadSdta(BinaryReader r, long end)
    {
        while (r.BaseStream.Position + 8 <= end)
        {
            var id = Id(r);
            var size = r.ReadInt32();
            var next = r.BaseStream.Position + size + (size & 1);
            if (id == "smpl") Data = MemoryMarshal.Cast<byte, short>(r.ReadBytes(size & ~1)).ToArray();
            r.BaseStream.Position = next;
        }
    }

    private static int Key(int bank, int program) => (bank << 7) | (program & 0x7F);

    /// <summary>The preset, else program 0 of the bank... else the first preset (GM fallback rules: drums bank 128, melodic bank 0).</summary>
    public Preset Find(int bank, int program, bool drum)
    {
        if (drum) return _byNumber.GetValueOrDefault(Key(128, program)) ?? _byNumber.GetValueOrDefault(Key(128, 0)) ?? Presets[0];
        return _byNumber.GetValueOrDefault(Key(bank, program)) ?? _byNumber.GetValueOrDefault(Key(0, program)) ?? Presets[0];
    }
}
