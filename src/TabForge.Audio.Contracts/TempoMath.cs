using System.Runtime.CompilerServices;

namespace TabForge.Audio.Contracts;

/// <summary>
/// The one place where a tempo (quarter notes per minute) is turned into time and back. The engine's MIDI processors and the
/// application's playback and timeline code both call it, so a beat is the same length everywhere.
/// Every member is allocation-free and safe on the audio thread. The operand order of each expression is part of the contract:
/// floating-point products and quotients are not associative, so callers that need the exact same value keep using the same member.
/// </summary>
public static class TempoMath
{
    /// <summary>Tempo used when a transport reports none (BPM at or below 1).</summary>
    public const double FallbackBpm = 120;

    /// <summary>Milliseconds in one minute: a quarter note lasts <c>MsPerMinute / bpm</c> ms.</summary>
    public const double MsPerMinute = 60000.0;

    /// <summary>The tempo to use for a transport tempo: the value itself when it is above 1, otherwise <see cref="FallbackBpm"/> (also for NaN).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Effective(double bpm) => bpm > 1 ? bpm : FallbackBpm;

    /// <summary>Seconds in one beat (quarter note) at <paramref name="bpm"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double SecondsPerBeat(double bpm) => 60.0 / bpm;

    /// <summary>Milliseconds in one beat (quarter note) at <paramref name="bpm"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double MsPerBeat(double bpm) => MsPerMinute / bpm;

    /// <summary>Seconds that <paramref name="beats"/> quarter notes last at <paramref name="bpm"/> (multiplies before it divides).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double BeatsToSeconds(double beats, double bpm) => beats * 60.0 / bpm;

    /// <summary>Quarter notes that pass in <paramref name="seconds"/> at <paramref name="bpm"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double SecondsToBeats(double seconds, double bpm) => seconds * bpm / 60.0;

    /// <summary>Samples in one beat at <paramref name="bpm"/>, by way of <see cref="SecondsPerBeat"/> (divides before it multiplies).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double SamplesPerBeat(double bpm, int sampleRate) => SecondsPerBeat(bpm) * sampleRate;

    /// <summary>Samples that <paramref name="beats"/> quarter notes last at <paramref name="bpm"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double BeatsToSamples(double beats, double bpm, int sampleRate) => BeatsToSeconds(beats, bpm) * sampleRate;

    /// <summary>
    /// Samples in one beat as <c>sampleRate * 60 / bpm</c> (multiplies before it divides). Differs from <see cref="SamplesPerBeat"/>
    /// in the last bit for some tempos; each caller keeps the form it always had.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double SampleRateBeat(int sampleRate, double bpm) => BeatsToSeconds(sampleRate, bpm);
}
