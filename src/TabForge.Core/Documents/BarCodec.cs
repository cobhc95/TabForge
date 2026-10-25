using TabForge.Models;

namespace TabForge.Documents;

// Owns: the exact binary form of one bar: every JSON property of a bar except its number, written and read in one fixed field order.
// Does not own: the bar number (TrackState keeps it) and the byte primitives (BarWriter, BarReader).
// Tests: TestUndoDeltaStates.
// Long methods: WriteCells (66 lines) and ReadCells (74 lines) follow the fixed field order of each cell and note, one flag word and
//   one field at a time; that order is the format, so each stays one method rather than helpers that would scatter it.
/// <summary>
/// Exact binary form of one bar (everything the JSON form holds except <see cref="MeasureModel.Number"/>, which the track state
/// keeps separately). <see cref="Handled"/> lists the covered JSON properties; a self-test fails when the model gains one it misses.
/// </summary>
internal static class BarCodec
{
    public static readonly IReadOnlyDictionary<Type, string[]> Handled = new Dictionary<Type, string[]>
    {
        [typeof(MeasureModel)] = new[]
        {
            nameof(MeasureModel.Number), nameof(MeasureModel.Cells), nameof(MeasureModel.TimeSigNum), nameof(MeasureModel.TimeSigDenom),
            nameof(MeasureModel.KeySignature), nameof(MeasureModel.KeySignatureMinor), nameof(MeasureModel.Clef), nameof(MeasureModel.RepeatStart),
            nameof(MeasureModel.RepeatEnd), nameof(MeasureModel.RepeatCount), nameof(MeasureModel.AlternateEnding), nameof(MeasureModel.AlternateEndingMask),
            nameof(MeasureModel.IsDoubleBar), nameof(MeasureModel.SimileOneBar), nameof(MeasureModel.SimileTwoBar), nameof(MeasureModel.SectionName),
            nameof(MeasureModel.TempoChange), nameof(MeasureModel.MidBarTempos), nameof(MeasureModel.TripletFeel), nameof(MeasureModel.TripletFeelKind),
            nameof(MeasureModel.FreeTime), nameof(MeasureModel.ForceLineBreak), nameof(MeasureModel.PreventLineBreak), nameof(MeasureModel.Anacrusis),
            nameof(MeasureModel.Directions), nameof(MeasureModel.Voice2Cells)
        },
        [typeof(TabCell)] = new[]
        {
            nameof(TabCell.Notes), nameof(TabCell.RhythmicPosition), nameof(TabCell.DurationDenominator), nameof(TabCell.Dots), nameof(TabCell.IsTriplet),
            nameof(TabCell.TupletNumerator), nameof(TabCell.TupletDenominator), nameof(TabCell.IsRest), nameof(TabCell.WrittenRest), nameof(TabCell.IsTied),
            nameof(TabCell.SoundDurationPercent), nameof(TabCell.OctaveShiftSemitones), nameof(TabCell.BeamMode), nameof(TabCell.BreakSecondaryBeamBefore),
            nameof(TabCell.StemDirection), nameof(TabCell.IsGrace), nameof(TabCell.GraceBeforeBeat), nameof(TabCell.Fermata), nameof(TabCell.Accent),
            nameof(TabCell.Staccato), nameof(TabCell.Tenuto), nameof(TabCell.WhammyPoints), nameof(TabCell.TremoloPickDenominator), nameof(TabCell.BrushStepSlots), nameof(TabCell.ChordName),
            nameof(TabCell.Text), nameof(TabCell.Lyrics), nameof(TabCell.Mix)
        },
        [typeof(TabNote)] = new[]
        {
            nameof(TabNote.StringIndex), nameof(TabNote.Fret), nameof(TabNote.MidiValue), nameof(TabNote.Velocity), nameof(TabNote.Ghost), nameof(TabNote.Dead),
            nameof(TabNote.IsGraceNote), nameof(TabNote.GraceBeforeBeat), nameof(TabNote.GraceOnsetOffsetSlots), nameof(TabNote.GraceDurationSlots),
            nameof(TabNote.Tied), nameof(TabNote.BendPoints), nameof(TabNote.BendTypeName), nameof(TabNote.BendStyleName), nameof(TabNote.SlideTargetMidi),
            nameof(TabNote.TrillTargetMidi), nameof(TabNote.TrillDurationDenominator), nameof(TabNote.HarmonicFret), nameof(TabNote.LeftHandFinger),
            nameof(TabNote.RightHandFinger), nameof(TabNote.Techniques)
        },
        [typeof(BendPointModel)] = new[] { nameof(BendPointModel.Offset), nameof(BendPointModel.Value) },
        [typeof(MixChange)] = new[]
        {
            nameof(MixChange.Program), nameof(MixChange.Volume), nameof(MixChange.Pan), nameof(MixChange.Chorus), nameof(MixChange.Reverb),
            nameof(MixChange.Phaser), nameof(MixChange.Tremolo), nameof(MixChange.Tempo), nameof(MixChange.TransitionBeats), nameof(MixChange.AllTracks)
        },
        [typeof(TempoPoint)] = new[] { nameof(TempoPoint.Slot), nameof(TempoPoint.Tempo), nameof(TempoPoint.RampSlots) },
    };

    private static int Bit(bool value, int shift) => value ? 1 << shift : 0;

    public static void Write(BarWriter w, MeasureModel? m)
    {
        if (m is null) { w.VarU(0); return; }
        w.VarU((ulong)(1 | Bit(m.RepeatStart, 1) | Bit(m.RepeatEnd, 2) | Bit(m.IsDoubleBar, 3) | Bit(m.SimileOneBar, 4) | Bit(m.SimileTwoBar, 5) |
                       Bit(m.TripletFeel, 6) | Bit(m.FreeTime, 7) | Bit(m.ForceLineBreak, 8) | Bit(m.PreventLineBreak, 9) | Bit(m.Anacrusis, 10)));
        w.NInt(m.TimeSigNum);
        w.NInt(m.TimeSigDenom);
        w.NInt(m.KeySignature);
        w.NBool(m.KeySignatureMinor);
        w.NInt(m.TempoChange);
        w.Int(m.RepeatCount);
        w.Int(m.AlternateEnding);
        w.Int(m.AlternateEndingMask);
        w.Str(m.Clef);
        w.Str(m.SectionName);
        w.Str(m.TripletFeelKind);
        w.Str(m.Directions);
        WriteCells(w, m.Cells);
        WriteCells(w, m.Voice2Cells);
        w.Count(m.MidBarTempos?.Count);
        if (m.MidBarTempos is { } tempos)
            foreach (var point in tempos)
            {
                if (point is null) { w.VarU(0); continue; }
                w.VarU(1);
                w.Dbl(point.Slot);
                w.Int(point.Tempo);
                w.Dbl(point.RampSlots);
            }
    }

    private static void WriteCells(BarWriter w, List<TabCell>? cells)
    {
        w.Count(cells?.Count);
        if (cells is null) return;
        foreach (var c in cells)
        {
            if (c is null) { w.VarU(0); continue; }
            w.VarU((ulong)(1 | Bit(c.IsTriplet, 1) | Bit(c.IsRest, 2) | Bit(c.IsTied, 3) | Bit(c.BreakSecondaryBeamBefore, 4) | Bit(c.IsGrace, 5) |
                           Bit(c.GraceBeforeBeat, 6) | Bit(c.Fermata, 7) | Bit(c.Staccato, 8) | Bit(c.Tenuto, 9) | Bit(c.WrittenRest, 10)));
            w.Int(c.DurationDenominator);
            w.Int(c.Dots);
            w.Int(c.TupletNumerator);
            w.Int(c.TupletDenominator);
            w.Int(c.SoundDurationPercent);
            w.Int(c.OctaveShiftSemitones);
            w.Int(c.Accent);
            w.Int(c.TremoloPickDenominator);
            w.Dbl(c.BrushStepSlots);
            w.Int((int)c.BeamMode);
            w.Int((int)c.StemDirection);
            w.NDbl(c.RhythmicPosition);
            w.Str(c.ChordName);
            w.Str(c.Text);
            w.Str(c.Lyrics);
            if (c.Mix is not { } mix) w.VarU(0);
            else
            {
                w.VarU((ulong)(1 | Bit(mix.AllTracks, 1)));
                w.NInt(mix.Program);
                w.NInt(mix.Volume);
                w.NInt(mix.Pan);
                w.NInt(mix.Chorus);
                w.NInt(mix.Reverb);
                w.NInt(mix.Phaser);
                w.NInt(mix.Tremolo);
                w.NInt(mix.Tempo);
                w.Int(mix.TransitionBeats);
            }
            WritePoints(w, c.WhammyPoints);
            w.Count(c.Notes?.Count);
            if (c.Notes is null) continue;
            foreach (var n in c.Notes)
            {
                if (n is null) { w.VarU(0); continue; }
                w.VarU((ulong)(1 | Bit(n.Ghost, 1) | Bit(n.Dead, 2) | Bit(n.IsGraceNote, 3) | Bit(n.GraceBeforeBeat, 4) | Bit(n.Tied, 5) | Bit(n.HarmonicFret.HasValue, 6)));
                w.Int(n.StringIndex);
                w.Int(n.Fret);
                w.Int(n.MidiValue);
                w.Int(n.Velocity);
                w.Int(n.SlideTargetMidi);
                w.Int(n.TrillTargetMidi);
                w.Int(n.TrillDurationDenominator);
                w.NInt(n.LeftHandFinger);
                w.NInt(n.RightHandFinger);
                w.Dbl(n.GraceOnsetOffsetSlots);
                w.Dbl(n.GraceDurationSlots);
                if (n.HarmonicFret is { } harmonicFret) w.Dbl(harmonicFret);
                w.Str(n.BendTypeName);
                w.Str(n.BendStyleName);
                WritePoints(w, n.BendPoints);
                w.Count(n.Techniques?.Count);
                if (n.Techniques is { } techniques)
                    foreach (var technique in techniques) w.Str(technique);
            }
        }
    }

    private static void WritePoints(BarWriter w, List<BendPointModel>? points)
    {
        w.Count(points?.Count);
        if (points is null) return;
        foreach (var point in points)
        {
            if (point is null) { w.VarU(0); continue; }
            w.VarU(1);
            w.Dbl(point.Offset);
            w.Dbl(point.Value);
        }
    }

    public static MeasureModel? Read(ReadOnlySpan<byte> bytes)
    {
        var r = new BarReader(bytes);
        var flags = r.VarU();
        if (flags == 0) return null;
        bool F(int shift) => (flags & (1UL << shift)) != 0;
        var m = new MeasureModel
        {
            RepeatStart = F(1), RepeatEnd = F(2), IsDoubleBar = F(3), SimileOneBar = F(4), SimileTwoBar = F(5),
            TripletFeel = F(6), FreeTime = F(7), ForceLineBreak = F(8), PreventLineBreak = F(9), Anacrusis = F(10),
            TimeSigNum = r.NInt(), TimeSigDenom = r.NInt(), KeySignature = r.NInt(), KeySignatureMinor = r.NBool(), TempoChange = r.NInt(),
            RepeatCount = r.Int(), AlternateEnding = r.Int(), AlternateEndingMask = r.Int(),
            Clef = r.Str()!, SectionName = r.Str()!, TripletFeelKind = r.Str()!, Directions = r.Str()!,
        };
        m.Cells = ReadCells(ref r)!;
        m.Voice2Cells = ReadCells(ref r)!;
        var tempoCount = r.Count();
        if (tempoCount < 0) m.MidBarTempos = null;
        else
        {
            var tempos = new List<TempoPoint>(tempoCount);
            for (var index = 0; index < tempoCount; index++)
            {
                if (r.VarU() == 0) { tempos.Add(null!); continue; }
                var slot = r.Dbl();
                var tempo = r.Int();
                tempos.Add(new TempoPoint(slot, tempo, r.Dbl()));
            }
            m.MidBarTempos = tempos;
        }
        if (!r.AtEnd) throw new InvalidDataException("The undo state is damaged.");
        return m;
    }

    private static List<TabCell>? ReadCells(ref BarReader r)
    {
        var count = r.Count();
        if (count < 0) return null;
        var cells = new List<TabCell>(count);
        for (var index = 0; index < count; index++)
        {
            var flags = r.VarU();
            if (flags == 0) { cells.Add(null!); continue; }
            bool F(int shift) => (flags & (1UL << shift)) != 0;
            var c = new TabCell
            {
                IsTriplet = F(1), IsRest = F(2), IsTied = F(3), BreakSecondaryBeamBefore = F(4), IsGrace = F(5),
                GraceBeforeBeat = F(6), Fermata = F(7), Staccato = F(8), Tenuto = F(9), WrittenRest = F(10),
                DurationDenominator = r.Int(), Dots = r.Int(), TupletNumerator = r.Int(), TupletDenominator = r.Int(),
                SoundDurationPercent = r.Int(), OctaveShiftSemitones = r.Int(), Accent = r.Int(), TremoloPickDenominator = r.Int(), BrushStepSlots = r.Dbl(),
                BeamMode = (BeamMode)r.Int(), StemDirection = (StemDirection)r.Int(),
                RhythmicPosition = r.NDbl(), ChordName = r.Str(), Text = r.Str(), Lyrics = r.Str()!,
            };
            var mixFlags = r.VarU();
            if (mixFlags != 0)
                c.Mix = new MixChange
                {
                    AllTracks = (mixFlags & 2) != 0,
                    Program = r.NInt(), Volume = r.NInt(), Pan = r.NInt(), Chorus = r.NInt(), Reverb = r.NInt(),
                    Phaser = r.NInt(), Tremolo = r.NInt(), Tempo = r.NInt(), TransitionBeats = r.Int(),
                };
            c.WhammyPoints = ReadPoints(ref r)!;
            var noteCount = r.Count();
            if (noteCount < 0) c.Notes = null!;
            else
            {
                var notes = new List<TabNote>(noteCount);
                for (var noteIndex = 0; noteIndex < noteCount; noteIndex++)
                {
                    var noteFlags = r.VarU();
                    if (noteFlags == 0) { notes.Add(null!); continue; }
                    bool N(int shift) => (noteFlags & (1UL << shift)) != 0;
                    var n = new TabNote
                    {
                        Ghost = N(1), Dead = N(2), IsGraceNote = N(3), GraceBeforeBeat = N(4), Tied = N(5),
                        StringIndex = r.Int(), Fret = r.Int(), MidiValue = r.Int(), Velocity = r.Int(),
                        SlideTargetMidi = r.Int(), TrillTargetMidi = r.Int(), TrillDurationDenominator = r.Int(),
                        LeftHandFinger = r.NInt(), RightHandFinger = r.NInt(),
                        GraceOnsetOffsetSlots = r.Dbl(), GraceDurationSlots = r.Dbl(),
                    };
                    if (N(6)) n.HarmonicFret = r.Dbl();
                    n.BendTypeName = r.Str()!; n.BendStyleName = r.Str()!;
                    n.BendPoints = ReadPoints(ref r)!;
                    var techniqueCount = r.Count();
                    if (techniqueCount < 0) n.Techniques = null!;
                    else
                    {
                        // The model's case-insensitive set; a set that held two spellings of one name keeps both (ordinal).
                        var techniques = n.Techniques;
                        for (var t = 0; t < techniqueCount; t++)
                        {
                            var name = r.Str()!;
                            if (!techniques.Add(name))
                            {
                                techniques = new HashSet<string>(techniques, StringComparer.Ordinal);
                                techniques.Add(name);
                            }
                        }
                        n.Techniques = techniques;
                    }
                    notes.Add(n);
                }
                c.Notes = notes;
            }
            cells.Add(c);
        }
        return cells;
    }

    private static List<BendPointModel>? ReadPoints(ref BarReader r)
    {
        var count = r.Count();
        if (count < 0) return null;
        var points = new List<BendPointModel>(count);
        for (var index = 0; index < count; index++)
        {
            if (r.VarU() == 0) { points.Add(null!); continue; }
            var offset = r.Dbl();
            points.Add(new BendPointModel { Offset = offset, Value = r.Dbl() });
        }
        return points;
    }
}
