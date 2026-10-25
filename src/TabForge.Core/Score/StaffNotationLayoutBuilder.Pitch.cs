using System.Globalization;
using TabForge.Models;
using TabForge.Services;
using TabForge.Views.Score;
using static TabForge.Views.StaffNotationGeometry;
using static TabForge.Views.StaffNotationArcs;

namespace TabForge.Views;

// Owns: the accidental resolution, key alterations and pitch spelling steps of the staff layout (partial of StaffNotationLayoutBuilder).
// Tests: no dedicated test class.

internal static partial class StaffNotationLayoutBuilder
{
    internal static void ResolveAccidentals(IReadOnlyList<StaffNotationBeat> beats, IReadOnlyList<int> keyAlterations)
    {
        var state = new Dictionary<(int Letter, int Octave), int>();
        foreach (var beat in beats)
        foreach (var note in beat.Notes.OrderBy(n => n.StaffStep))
        {
            if (note.Source.Dead) continue;   // an x head has no pitch: no accidental, and it leaves the bar's accidental state alone
            var signatureAlteration = keyAlterations[note.Letter];
            var key = (note.Letter, note.Octave);
            var current = state.TryGetValue(key, out var prior) ? prior : signatureAlteration;
            if (!note.Source.Tied && !beat.Cell.IsTied && note.Alteration != current)
                note.Accidental = note.Alteration switch { > 0 => "♯", < 0 => "♭", _ => "♮" };
            state[key] = note.Alteration;
        }
    }

    internal static void AssignAccidentalColumns(IReadOnlyList<StaffNotationBeat> beats)
    {
        foreach (var beat in beats)
        {
            var placed = new List<(int Step, int Column)>();
            foreach (var note in beat.Notes.Where(n => n.Accidental is not null).OrderBy(n => n.StaffStep))
            {
                var column = 0;
                while (placed.Any(p => p.Column == column && Math.Abs(p.Step - note.StaffStep) <= 2)) column++;
                note.AccidentalColumn = column;
                placed.Add((note.StaffStep, column));
            }
        }
    }

    internal static int[] KeyAlterations(int keySignature)
    {
        var alterations = new int[7];
        var count = Math.Clamp(Math.Abs(keySignature), 0, 7);
        var order = keySignature >= 0 ? SharpOrder : FlatOrder;
        var value = keySignature >= 0 ? 1 : -1;
        for (var i = 0; i < count; i++) alterations[order[i]] = value;
        return alterations;
    }

    internal static PitchSpelling SpellPitch(int midi, IReadOnlyList<int> keyAlterations, int keySignature)
    {
        var midiOctave = midi / 12 - 1;
        PitchSpelling? best = null;
        var bestScore = double.MaxValue;
        for (var octave = midiOctave - 1; octave <= midiOctave + 1; octave++)
        for (var letter = 0; letter < 7; letter++)
        for (var alteration = -1; alteration <= 1; alteration++)
        {
            var naturalMidi = 12 * (octave + 1) + NaturalPitchClasses[letter];
            if (naturalMidi + alteration != midi) continue;
            var expected = keyAlterations[letter];
            var score = alteration == expected ? 0.0 : 10 + Math.Abs(alteration - expected);
            score += Math.Abs(alteration) * 0.1;
            if (keySignature > 0 && alteration < 0 || keySignature < 0 && alteration > 0) score += 0.25;
            if (keySignature == 0 && alteration < 0) score += 0.2; // neutral keys default to sharp spelling
            if (score < bestScore)
            {
                bestScore = score;
                best = new PitchSpelling(letter, octave, alteration, octave * 7 + letter);
            }
        }
        return best ?? new PitchSpelling(0, midiOctave, 0, midiOctave * 7);
    }
}
