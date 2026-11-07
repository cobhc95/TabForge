using System.Text;

namespace TabForge.Sf2Synth.Sf2;

// Owns: parsing the pdta "hydra" (phdr pbag pmod pgen inst ibag imod igen shdr) into presets, instruments, zones and sample headers,
//   including global zones (first zone without the link generator, §7.3/7.7).
// Does not own: the RIFF framing and sample data (SoundFont).
// Tests: Sf2SynthTests.
internal static class Sf2Reader
{
    private readonly record struct Bag(int Gen, int Mod);
    private readonly record struct GenRec(int Op, short Amount);

    public static void Read(BinaryReader r, long end, SoundFont font)
    {
        var chunks = new Dictionary<string, byte[]>();
        while (r.BaseStream.Position + 8 <= end)
        {
            var id = SoundFont.Id(r);
            var size = r.ReadInt32();
            chunks[id] = r.ReadBytes(size);
            if ((size & 1) != 0) r.ReadByte();
        }
        byte[] C(string id) => chunks.TryGetValue(id, out var b) ? b : throw new InvalidDataException("missing " + id);

        font.Samples = Records(C("shdr"), 46, b => new SampleHeader(Name(b), b.ReadInt32(), b.ReadInt32(), b.ReadInt32(), b.ReadInt32(),
            b.ReadInt32(), b.ReadByte(), b.ReadSByte()));
        var ibag = Records(C("ibag"), 4, b => new Bag(b.ReadUInt16(), b.ReadUInt16()));
        var igen = Records(C("igen"), 4, b => new GenRec(b.ReadUInt16(), b.ReadInt16()));
        var imod = Records(C("imod"), 10, ReadMod);
        var inst = Records(C("inst"), 22, b => (Name: Name(b), Bag: (int)b.ReadUInt16()));
        font.Instruments = new Instrument[Math.Max(0, inst.Length - 1)];
        for (var i = 0; i < font.Instruments.Length; i++)
        {
            var zones = Zones(inst[i].Bag, inst[i + 1].Bag, ibag, igen, imod, (int)G.SampleId, out var global);
            font.Instruments[i] = new Instrument(inst[i].Name, global, zones);
        }
        var pbag = Records(C("pbag"), 4, b => new Bag(b.ReadUInt16(), b.ReadUInt16()));
        var pgen = Records(C("pgen"), 4, b => new GenRec(b.ReadUInt16(), b.ReadInt16()));
        var pmod = Records(C("pmod"), 10, ReadMod);
        var phdr = Records(C("phdr"), 38, b => (Name: Name(b), Program: (int)b.ReadUInt16(), Bank: (int)b.ReadUInt16(), Bag: (int)b.ReadUInt16()));
        var presets = new List<Preset>();
        for (var i = 0; i + 1 < phdr.Length; i++)
        {
            var zones = Zones(phdr[i].Bag, phdr[i + 1].Bag, pbag, pgen, pmod, (int)G.Instrument, out var global);
            presets.Add(new Preset(phdr[i].Name, phdr[i].Bank, phdr[i].Program) { Global = global, Zones = zones });
        }
        font.Presets = presets;
    }

    private static Mod ReadMod(BinaryReader b) => new(b.ReadUInt16(), b.ReadUInt16(), b.ReadInt16(), b.ReadUInt16(), b.ReadUInt16());

    private static string Name(BinaryReader b) => Encoding.ASCII.GetString(b.ReadBytes(20)).Split('\0')[0];

    private static T[] Records<T>(byte[] chunk, int size, Func<BinaryReader, T> read)
    {
        using var b = new BinaryReader(new MemoryStream(chunk));
        var list = new T[chunk.Length / size];
        for (var i = 0; i < list.Length; i++) { b.BaseStream.Position = (long)i * size; list[i] = read(b); }
        return list;
    }

    private static Zone[] Zones(int from, int to, Bag[] bags, GenRec[] gens, Mod[] mods, int link, out Zone? global)
    {
        global = null;
        var zones = new List<Zone>();
        for (var z = from; z < to && z + 1 < bags.Length; z++)
        {
            var zone = new Zone();
            Array.Copy(Gens.Defaults, zone.Gen, Gens.Count);
            for (var g = bags[z].Gen; g < bags[z + 1].Gen && g < gens.Length; g++)
            {
                var (op, amount) = (gens[g].Op, gens[g].Amount);
                if (op == (int)G.KeyRange) (zone.KeyLo, zone.KeyHi) = (amount & 0xFF, (amount >> 8) & 0xFF);
                else if (op == (int)G.VelRange) (zone.VelLo, zone.VelHi) = (amount & 0xFF, (amount >> 8) & 0xFF);
                else if (op == link) zone.Link = (ushort)amount;
                else if (op < Gens.Count) { zone.Gen[op] = amount; zone.Set |= 1UL << op; }
            }
            zone.Mods = mods.AsSpan(Math.Min(bags[z].Mod, mods.Length), Math.Max(0, Math.Min(bags[z + 1].Mod, mods.Length) - Math.Min(bags[z].Mod, mods.Length))).ToArray();
            if (zone.Link >= 0) zones.Add(zone);
            else if (z == from) global = zone;
        }
        return zones.ToArray();
    }
}
