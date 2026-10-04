using System.Windows;
using System.Windows.Media;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Services;
using TabForge.Views;

namespace TabForge.Controllers;

// Owns: Convert to instrument track on an audio track: the instrument picker (search focused), the clip prompt (or the remembered answer) and the one-step conversion.
// Does not own: the model change (TrackController.ConvertAudioToInstrument) or the window refresh afterwards.
// Tests: TestConvertToInstrumentFlow.
internal sealed class ConvertToInstrumentFlow
{
    internal const string AskAnswer = "Ask", WriteAnswer = "Write as notation", KeepAnswer = "Keep as MIDI";
    private readonly TrackController _tracks;
    private readonly EditingSettings _editing;

    public ConvertToInstrumentFlow(TrackController tracks, EditingSettings editing) { _tracks = tracks; _editing = editing; }

    /// <summary>Picks the instrument and, for a track with clips, what happens to them; null when cancelled at either step (nothing changes).</summary>
    public EditResult? Run(Window? owner, DocumentSession doc, TrackModel audio, Func<double, (int Bar, double Fraction)> barAt, Action saveSettings)
    {
        if (!audio.IsAudio) return null;
        var picked = InstrumentPickerWindow.Show(owner, null, ColourOf(audio));
        if (picked is null) return null;
        var hasMidi = audio.AudioClips.Any(c => c.IsMidi);
        var write = false;
        if (audio.AudioClips.Count > 0)
        {
            if (hasMidi && _editing.ConvertMidiClips is WriteAnswer or KeepAnswer) write = _editing.ConvertMidiClips == WriteAnswer;
            else
            {
                var answer = ConvertTrackPrompts.AskClips(owner, audio.Name, hasMidi, out var remember);
                if (answer == ConvertTrackPrompts.ClipAnswer.Cancel) return null;
                write = hasMidi && answer == ConvertTrackPrompts.ClipAnswer.WriteNotation;
                if (hasMidi && remember) { _editing.ConvertMidiClips = write ? WriteAnswer : KeepAnswer; saveSettings(); }
            }
        }
        return _tracks.ConvertAudioToInstrument(doc, audio, InstrumentNaming.WithoutStringCount(picked), write ? barAt : null);
    }

    private static Color ColourOf(TrackModel track)
    {
        try { return (Color)ColorConverter.ConvertFromString(track.ColorHex); } catch { return Colors.SteelBlue; }
    }
}
