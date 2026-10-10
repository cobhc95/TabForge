using System.Windows.Threading;
using TabForge.Controllers;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Playback;

namespace TabForge.KeyboardMode;

// Owns: the practice track while Keyboard mode listens to a MIDI keyboard: the selected track is skipped by the playback scheduler (PlaybackEngine.SetPracticeSilence: never its Mute, so nothing is
//   saved or marked changed and a plug-in instrument stays audible) and the player's keys play through that track's sound by the live path a monitored MIDI track uses (LiveMidiThru), its program set
//   once. Cleared exactly (silence off, a held note let go) when listening stops, the track or song changes, the pane hides or the window closes. A track that is armed and monitored already plays the
//   keys through the recording path, so nothing is sent twice.
// Does not own: the MIDI input (KeyboardModeMidiListener raises Thru on the driver thread), the scheduler (PlaybackEngine), the live send (LiveMidiThru) or when it is on (KeyboardModeFrameController).
// Tests: TestKeyboardModePracticeTrack.
internal sealed class KeyboardModePracticeTrack : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private PlaybackEngine? _engine;
    private DocumentSession? _document;
    private TrackModel? _track;
    private int _index = -1;
    private int _channel = -1;   // the channel and device the keys were played on, kept so Clear never recomputes them from a project that changed since
    private int _device;
    private volatile bool _on;

    public KeyboardModePracticeTrack(Dispatcher dispatcher) { _dispatcher = dispatcher; }

    /// <summary>The track that is silent and sounds the player's keys now; null when none.</summary>
    public TrackModel? Track => _on ? _track : null;
    /// <summary>How many messages were played through the track (a self-test counter).</summary>
    public int Sent { get; private set; }

    /// <summary>UI thread, each frame: <paramref name="on"/> makes <paramref name="track"/> of <paramref name="document"/> the practice track (cheap when nothing changed).</summary>
    public void Apply(DocumentSession? document, TrackModel? track, bool on)
    {
        var index = on && document is not null && track is not null ? document.Project.Tracks.IndexOf(track) : -1;
        var engine = index >= 0 ? document!.Playback.Engine : null;
        if (index == _index && ReferenceEquals(engine, _engine) && ReferenceEquals(track, _track) && ReferenceEquals(document, _document)) return;
        Clear();
        if (index < 0) return;
        _document = document; _track = track; _engine = engine; _index = index;
        _channel = ChannelOf(document!, index); _device = track!.MidiOutputDeviceId;
        engine!.SetPracticeSilence(index);
        if (_channel >= 0) LiveMidiThru.SetProgram(document!, track, _channel);
        _on = true;
    }

    /// <summary>MIDI driver thread: a channel message of the player, played on the UI thread through the practice track.</summary>
    public void OnThru(int status, int data1, int data2)
    {
        if (!_on) return;
        _dispatcher.BeginInvoke(DispatcherPriority.Normal, () => Play(status, data1, data2));
    }

    /// <summary>UI thread: plays one message through the practice track (nothing when there is none, or the recording path monitors it already).</summary>
    internal void Play(int status, int data1, int data2)
    {
        if (!_on || _document is not { } document || _track is not { } track) return;
        var channel = ChannelOf(document, _index);
        if (channel < 0 || RecordingController.MonitorsLive(track, channel)) return;
        LiveMidiThru.Send(document, track, channel, status, data1, data2);
        Sent++;
    }

    private static int ChannelOf(DocumentSession document, int index)
    {
        var channels = ChannelAllocator.Assign(document.Project);
        return index < channels.Length ? channels[index] : -1;
    }

    private static readonly (int Status, int Data1, int Data2)[] Reset =
    {
        (0xB0, 64, 0), (0xB0, 123, 0), (0xB0, 121, 0), (0xE0, 0x00, 0x40), (0xD0, 0, 0)
    };

    /// <summary>Gives the track back: the scheduler plays it again and a key still held is let go.</summary>
    public void Clear()
    {
        if (_on && _document is { } document && _track is { } track && _channel >= 0 && !RecordingController.MonitorsLive(track, _channel))
        {
            // Sustain off first (under it All Notes Off keeps notes sounding), then All Notes Off, Reset All Controllers, bend centre and pressure 0.
            var channel = _channel;
            if (track.IsAudio)
                foreach (var (s, d1, d2) in Reset) LiveMidiThru.Send(document, track, channel, s, d1, d2);
            else
                foreach (var (s, d1, d2) in Reset) document.Playback.Engine.SendLive(_device, s | channel, d1, d2);
        }
        _on = false;
        if (_engine is { } engine && engine.PracticeSilence == _index) engine.SetPracticeSilence(-1);
        _engine = null; _document = null; _track = null; _index = -1;
    }

    public void Dispose() => Clear();
}
