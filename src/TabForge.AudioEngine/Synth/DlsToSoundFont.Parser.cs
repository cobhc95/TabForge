using System.Text;
using System.Buffers.Binary;

namespace TabForge.AudioEngine.Synth;

// Owns: the RIFF / DLS parser: bounds-checked chunk walking, instrument, region, wave and articulation reading.
public static partial class DlsToSoundFont
{
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
}
