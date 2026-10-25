using System.Text;
using System.Buffers.Binary;

namespace TabForge.AudioEngine.Synth;

// Owns: the SF2 writer: serialising the DLS bank into the SoundFont 2 RIFF image (sample, preset, instrument chunks).
public static partial class DlsToSoundFont
{
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

        /// <summary>The instrument and preset chunks being written, with the number of records in each.</summary>
        private sealed class Pdta
        {
            public readonly MemoryStream Inst = new(), IBag = new(), IGen = new(), PHdr = new(), PBag = new(), PGen = new();
            public int IBagCount, IGenCount, PBagCount, PGenCount, InstCount;

            public void G(ushort op, int amount) { WriteGen(IGen, op, (short)Math.Clamp(amount, short.MinValue, short.MaxValue)); IGenCount++; }
            public void GR(ushort op, byte lo, byte hi) { WriteGenRange(IGen, op, lo, hi); IGenCount++; }
        }

        private static int R(double x) => (int)Math.Round(x);

        public static byte[] Write(DlsBank bank, Func<bool, int, int, double>? extraAttenuationDb = null)
        {
            var smpl = BuildSamples(bank, out var headers);
            var p = new Pdta();
            WritePresets(bank, headers, p, extraAttenuationDb);
            WriteTerminalRecords(p);
            return AssembleRiff(smpl, p, BuildSampleHeaders(headers));
        }

        /// <summary>The sample chunk (every wave padded) and one header per wave.</summary>
        private static byte[] BuildSamples(DlsBank bank, out List<SampleHeader> headers)
        {
            long totalSamples = 0;
            foreach (var w in bank.Waves) totalSamples += w.Data.Length + SamplePadding;
            if (totalSamples * 2 > int.MaxValue - 1_000_000) throw new InvalidDataException("The DLS wave pool is too large.");
            var smpl = new byte[totalSamples * 2];
            headers = new List<SampleHeader>(bank.Waves.Count);
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
            return smpl;
        }

        /// <summary>One instrument and one preset for every DLS instrument with a playable region (sorted by bank and program, first of a duplicate pair wins).</summary>
        private static void WritePresets(DlsBank bank, List<SampleHeader> headers, Pdta p, Func<bool, int, int, double>? extraAttenuationDb)
        {
            // Sort presets by bank/program (SF2 readers do not require it, but it keeps the file tidy); drop
            // duplicate bank/program pairs (first wins).
            var seen = new HashSet<(int, int)>();
            var ordered = bank.Instruments
                .Select(i => (Ins: i, Bank: i.IsDrum ? 128 : (int)Math.Min((i.Bank >> 8) & 0x7F, 127u), Prog: (int)i.Program))
                .OrderBy(t => t.Bank).ThenBy(t => t.Prog).ToList();

            foreach (var (ins, presetBank, program) in ordered)
            {
                if (!seen.Add((presetBank, program))) continue;
                int instBagStart = p.IBagCount;
                foreach (var r in ins.Regions)
                    WriteRegion(bank, headers, p, ins, r, program, extraAttenuationDb);
                if (p.IBagCount == instBagStart) continue; // no playable regions

                WriteName(p.Inst, string.IsNullOrWhiteSpace(ins.Name) ? $"Program {program}" : ins.Name);
                WriteU16(p.Inst, (ushort)instBagStart);

                WriteName(p.PHdr, string.IsNullOrWhiteSpace(ins.Name) ? $"Program {program}" : ins.Name);
                WriteU16(p.PHdr, (ushort)program);
                WriteU16(p.PHdr, (ushort)presetBank);
                WriteU16(p.PHdr, (ushort)p.PBagCount);
                WriteU32(p.PHdr, 0); WriteU32(p.PHdr, 0); WriteU32(p.PHdr, 0);
                WriteBag(p.PBag, p.PGenCount, 0); p.PBagCount++;
                WriteGen(p.PGen, GInstrument, (short)p.InstCount); p.PGenCount++;
                p.InstCount++;
                if (p.IBagCount > ushort.MaxValue - 2 || p.IGenCount > ushort.MaxValue - 2)
                    throw new InvalidDataException("The DLS bank has too many regions for a SoundFont 2 file.");
            }
            if (p.InstCount == 0) throw new InvalidDataException("The DLS bank has no instrument with a playable region.");
        }

        /// <summary>One instrument zone: the key and velocity range, loop, level, tuning, envelopes, LFOs, filter, sends and the sample to play.</summary>
        private static void WriteRegion(DlsBank bank, List<SampleHeader> headers, Pdta p, DlsInstrument ins, DlsRegion r, int program, Func<bool, int, int, double>? extraAttenuationDb)
        {
            if (r.TableIndex >= bank.PoolTable.Length) return;
            int waveIndex = bank.PoolTable[r.TableIndex];
            if (waveIndex < 0) return;
            var wave = bank.Waves[waveIndex];
            var hdr = headers[waveIndex];
            var ws = r.Sample ?? wave.Sample ?? new WaveSample();
            var art = Resolve(ins.Articulation, r.Articulation);

            WriteBag(p.IBag, p.IGenCount, 0); p.IBagCount++;
            p.GR(GKeyRange, (byte)r.KeyLow, (byte)r.KeyHigh);
            if (r.VelLow != 0 || r.VelHigh != 127) p.GR(GVelRange, (byte)r.VelLow, (byte)r.VelHigh);

            WriteLoopGens(p, ws, wave, hdr);
            WriteLevelAndTuning(p, ws, art, ins.IsDrum, program, r, extraAttenuationDb);
            WriteEnvelopes(p, art);
            WriteLfoFilterAndSends(p, art, r, extraAttenuationDb);
            p.G(GSampleId, waveIndex);
        }

        /// <summary>Loops (the region wsmp overrides the one of the wave).</summary>
        private static void WriteLoopGens(Pdta p, WaveSample ws, DlsWave wave, SampleHeader hdr)
        {
            bool looped = ws.Loops.Count > 0 && TryLoop(ws.Loops[0], wave.Data.Length, out _, out _);
            if (looped)
            {
                TryLoop(ws.Loops[0], wave.Data.Length, out uint la, out uint lb);
                long dStart = (long)(hdr.Start + la) - hdr.LoopStart;
                long dEnd = (long)(hdr.Start + lb) - hdr.LoopEnd;
                if (dStart != 0) { p.G(GStartLoopOfs, (int)(dStart % 32768)); if (dStart / 32768 != 0) p.G(GStartLoopCoarse, (int)(dStart / 32768)); }
                if (dEnd != 0) { p.G(GEndLoopOfs, (int)(dEnd % 32768)); if (dEnd / 32768 != 0) p.G(GEndLoopCoarse, (int)(dEnd / 32768)); }
                p.G(GSampleModes, ws.Loops[0].Type == 1 ? 3 : 1);
            }
        }

        /// <summary>Level (wsmp gain plus the constant articulation gain) and tuning of a zone.</summary>
        private static void WriteLevelAndTuning(Pdta p, WaveSample ws, Articulation art, bool isDrum, int program, DlsRegion r, Func<bool, int, int, double>? extraAttenuationDb)
        {
            // Level: wsmp gain + constant articulation gain (DLS gain units: 1/655360 dB).
            double gainDb = ws.Attenuation / 655360.0 + art.GainDb;
            // Measured level calibration against the GS synth (isDrum, program, key) -> extra attenuation dB, >= 0.
            if (extraAttenuationDb is not null) gainDb -= extraAttenuationDb(isDrum, program, (r.KeyLow + r.KeyHigh) / 2);
            // Positive region gains would be lost to the clamp; measured: none of the measured regions has one (kick 35 = 0 dB),
            // the remaining differences are corrected by the calibration table in GmSynthTuning.
            double attenDb = Math.Max(0, -gainDb);
            if (attenDb > 0) p.G(GAttenuation, R(attenDb * 10 / MeltySynthAttenuationFactor));

            // Tuning.
            p.G(GRootKey, ws.UnityNote);
            // The wsmp fine tune is the sample's own offset from its unity note; measured over every program, adding it (not
            // subtracting it) puts the notes in tune (mean error 0.26 -> 0.07 semitone, electric guitars were up to 0.8 sharp).
            double fine = ws.FineTune + art.PitchCents;
            int coarse = (int)Math.Truncate(fine / 100);
            int fineRest = R(fine - coarse * 100);
            if (coarse != 0) p.G(GCoarseTune, coarse);
            if (fineRest != 0) p.G(GFineTune, fineRest);
            if (Math.Abs(art.ScaleTuning - 100) > 0.5) p.G(GScaleTuning, R(art.ScaleTuning));
        }

        /// <summary>The volume envelope, and the modulation envelope when it drives something.</summary>
        private static void WriteEnvelopes(Pdta p, Articulation art)
        {
            // Volume envelope.
            if (art.Eg1Delay > -12000) p.G(GDelayVolEnv, R(art.Eg1Delay));
            p.G(GAttackVolEnv, R(Math.Max(art.Eg1Attack, -12000)));
            if (art.Eg1Hold > -12000) p.G(GHoldVolEnv, R(art.Eg1Hold));
            p.G(GDecayVolEnv, R(Math.Max(art.Eg1Decay, -12000)));
            p.G(GSustainVolEnv, R(Math.Clamp(art.Eg1SustainCb, 0, 1440)));
            p.G(GReleaseVolEnv, R(Math.Max(art.Eg1Release, -12000)));
            if (art.KeyToEg1Decay != 0) p.G(GKeyToVolEnvDecay, R(art.KeyToEg1Decay));
            if (art.KeyToEg1Hold != 0) p.G(GKeyToVolEnvHold, R(art.KeyToEg1Hold));

            // Modulation envelope (only when it drives something).
            if (art.Eg2ToPitch != 0 || art.Eg2ToFilter != 0)
            {
                if (art.Eg2Delay > -12000) p.G(GDelayModEnv, R(art.Eg2Delay));
                p.G(GAttackModEnv, R(Math.Max(art.Eg2Attack, -12000)));
                if (art.Eg2Hold > -12000) p.G(GHoldModEnv, R(art.Eg2Hold));
                p.G(GDecayModEnv, R(Math.Max(art.Eg2Decay, -12000)));
                p.G(GSustainModEnv, R(Math.Clamp(art.Eg2SustainPercentDrop, 0, 1000)));
                p.G(GReleaseModEnv, R(Math.Max(art.Eg2Release, -12000)));
                if (art.KeyToEg2Decay != 0) p.G(GKeyToModEnvDecay, R(art.KeyToEg2Decay));
                if (art.KeyToEg2Hold != 0) p.G(GKeyToModEnvHold, R(art.KeyToEg2Hold));
                if (art.Eg2ToPitch != 0) p.G(GModEnvToPitch, R(art.Eg2ToPitch));
                if (art.Eg2ToFilter != 0) p.G(GModEnvToFilter, R(art.Eg2ToFilter));
            }
        }

        /// <summary>LFOs, pan, filter, effect sends and the exclusive class.</summary>
        private static void WriteLfoFilterAndSends(Pdta p, Articulation art, DlsRegion r, Func<bool, int, int, double>? extraAttenuationDb)
        {
            // LFOs. The DLS1 LFO drives SF2's modulation LFO (volume/filter/pitch); the vibrato LFO
            // carries DLS2 vibrato and the mod-wheel vibrato that MeltySynth applies to it.
            if (art.LfoToPitch != 0 || art.LfoToVolumeCb != 0 || art.LfoToFilter != 0)
            {
                p.G(GFreqModLfo, R(art.LfoFreq));
                p.G(GDelayModLfo, R(Math.Max(art.LfoDelay, -12000)));
                if (art.LfoToPitch != 0) p.G(GModLfoToPitch, R(art.LfoToPitch));
                if (art.LfoToVolumeCb != 0) p.G(GModLfoToVolume, R(art.LfoToVolumeCb));
                if (art.LfoToFilter != 0) p.G(GModLfoToFilter, R(art.LfoToFilter));
            }
            p.G(GFreqVibLfo, R(art.VibFreq));
            p.G(GDelayVibLfo, R(Math.Max(art.VibDelay, -12000)));
            if (art.VibToPitch != 0) p.G(GVibLfoToPitch, R(art.VibToPitch));

            if (art.Pan != 0) p.G(GPan, R(Math.Clamp(extraAttenuationDb is null ? art.Pan : GmSynthTuning.MapPan(art.Pan), -500, 500)));   // calibrated: GS kit width
            if (art.FilterFc < 13500) p.G(GFilterFc, R(Math.Clamp(art.FilterFc, 1500, 13500)));
            if (art.FilterQCb > 0) p.G(GFilterQ, R(Math.Clamp(art.FilterQCb, 0, 960)));
            if (art.ReverbSend > 0) p.G(GReverb, R(Math.Clamp(art.ReverbSend, 0, 1000)));
            if (art.ChorusSend > 0) p.G(GChorus, R(Math.Clamp(art.ChorusSend, 0, 1000)));
            if (r.KeyGroup != 0) p.G(GExclusiveClass, Math.Min(r.KeyGroup, (ushort)127));
        }

        private static void WriteTerminalRecords(Pdta p)
        {
            // Terminal records.
            WriteName(p.Inst, "EOI"); WriteU16(p.Inst, (ushort)p.IBagCount);
            WriteBag(p.IBag, p.IGenCount, 0);
            WriteGen(p.IGen, 0, 0);
            WriteName(p.PHdr, "EOP"); WriteU16(p.PHdr, 0); WriteU16(p.PHdr, 0); WriteU16(p.PHdr, (ushort)p.PBagCount);
            WriteU32(p.PHdr, 0); WriteU32(p.PHdr, 0); WriteU32(p.PHdr, 0);
            WriteBag(p.PBag, p.PGenCount, 0);
            WriteGen(p.PGen, 0, 0);
        }

        private static byte[] BuildSampleHeaders(List<SampleHeader> headers)
        {
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
            return shdr.ToArray();
        }

        private static byte[] AssembleRiff(byte[] smpl, Pdta p, byte[] shdr)
        {
            var pmod = new byte[10]; var imod = new byte[10];

            // ---- assemble RIFF ----
            var info = new MemoryStream();
            WriteChunk(info, "ifil", new byte[] { 2, 0, 1, 0 });
            WriteChunk(info, "isng", ZString("EMU8000"));
            WriteChunk(info, "INAM", ZString("Microsoft GS (gm.dls)"));
            var sdta = new MemoryStream();
            WriteChunk(sdta, "smpl", smpl);
            var pdta = new MemoryStream();
            WriteChunk(pdta, "phdr", p.PHdr.ToArray());
            WriteChunk(pdta, "pbag", p.PBag.ToArray());
            WriteChunk(pdta, "pmod", pmod);
            WriteChunk(pdta, "pgen", p.PGen.ToArray());
            WriteChunk(pdta, "inst", p.Inst.ToArray());
            WriteChunk(pdta, "ibag", p.IBag.ToArray());
            WriteChunk(pdta, "imod", imod);
            WriteChunk(pdta, "igen", p.IGen.ToArray());
            WriteChunk(pdta, "shdr", shdr);

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
