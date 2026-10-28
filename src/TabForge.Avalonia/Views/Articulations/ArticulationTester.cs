using System;
using System.Threading.Tasks;
using TabForge.Avalonia.Views.Fx;
using TabForge.Models;
using TabForge.Playback;

namespace TabForge.Avalonia.Views.Articulations;

// Owns: the Test button of an Articulations row: the rule sent to the track instrument, then a short test note (never blocks: the waits are async).
// Does not own: the row (ArticulationRow) or the engine routing (IFxChainHost.SendInstrumentMidi).
// Tests: no dedicated test class.
internal static class ArticulationTester
{
    private const int TestNote = 52, TestVelocity = 100, NoteMs = 500, KeyswitchVelocity = 100;

    /// <summary>Sends the rule, waits the lead, plays one note and releases. Returns a status line for the window.</summary>
    public static async Task<string> RunAsync(IFxChainHost host, TrackModel track, ArticulationRule rule, int leadMs)
    {
        var pitch = TestNote;
        var velocity = TestVelocity;
        var keyswitch = -1;
        switch (rule.Action)
        {
            case ArticulationAction.Keyswitch:
                keyswitch = rule.A;
                if (!host.SendInstrumentMidi(track, 0x90, keyswitch, KeyswitchVelocity)) return NotRouted;
                break;
            case ArticulationAction.Cc:
                if (!host.SendInstrumentMidi(track, 0xB0, rule.A, rule.B)) return NotRouted;
                break;
            case ArticulationAction.Velocity: velocity = Math.Clamp(rule.A, 1, MidiLimits.Max); break;
            case ArticulationAction.Replace: pitch = rule.A; break;
        }
        try
        {
            if (keyswitch >= 0) { await Task.Delay(Math.Max(1, leadMs)); host.SendInstrumentMidi(track, 0x80, keyswitch, 0); }
            if (!host.SendInstrumentMidi(track, 0x90, pitch, velocity)) return NotRouted;
            await Task.Delay(NoteMs);
        }
        finally
        {
            host.SendInstrumentMidi(track, 0x80, pitch, 0);
            if (keyswitch >= 0) host.SendInstrumentMidi(track, 0x80, keyswitch, 0);
        }
        return $"Test sent to the instrument (note {ArticulationTechniques.NoteName(pitch)}).";
    }

    private const string NotRouted = "The instrument is not running in the audio engine yet (is it loaded and switched on?), so there is nothing to test.";
}
