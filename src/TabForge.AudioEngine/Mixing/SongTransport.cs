using System.Diagnostics;
using TabForge.Audio.Contracts;

namespace TabForge.AudioEngine.Mixing;

// Owns: the song transport as TabForge last described it (immutable parts, reader-thread write, audio-thread read).

/// <summary>
/// The song transport as TabForge last described it: position (playing, song seconds at a Stopwatch stamp), tempo and the
/// bar map. Written by the engine's command reader thread, read by the audio thread. Each part is one immutable object published with
/// <see cref="Volatile.Write{T}(ref T, T)"/>, so a reader never sees a torn pair; it lives outside <see cref="MixEngine"/>, so replacing the
/// mixer (device change) never loses an update that arrived meanwhile.
/// </summary>
public sealed class SongTransport
{
    /// <summary>Song position: <see cref="SongSec"/> at Stopwatch time <see cref="Stamp"/> (the MIDI time-stamp clock).</summary>
    public sealed record Position(bool Playing, double SongSec, long Stamp, double Speed = 1.0)
    {
        /// <summary>Song seconds at <paramref name="ticks"/> (Stopwatch): advances at <see cref="Speed"/> song seconds per real second while playing, holds while stopped.</summary>
        public double SecAt(double ticks) => Playing ? SongSec + (ticks - Stamp) / Stopwatch.Frequency * Speed : SongSec;
    }

    private Position _position = new(false, 0, 0);
    private TransportBar[] _bars = Array.Empty<TransportBar>();
    private double _tempo = 120;

    public Position Current => Volatile.Read(ref _position);
    public TransportBar[] Bars => Volatile.Read(ref _bars);
    public double Tempo => Volatile.Read(ref _tempo);

    public void SetPosition(bool playing, double songSec, long stamp, double speed = 1.0) => Volatile.Write(ref _position, new Position(playing, songSec, stamp, speed));
    public void SetTempo(double tempo) => Volatile.Write(ref _tempo, tempo);
    public void SetMap(TransportBar[] bars) => Volatile.Write(ref _bars, bars);
}
