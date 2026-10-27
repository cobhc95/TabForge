using System.Linq;
using TabForge.Audio;
using TabForge.Features.Recording.Rules;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the check of the Core MIDI take: notes kept during a recording become one MIDI clip per pass, held notes end at the stop,
/// and a loop lap starts a new pass at the loop start.
/// Does not own: where MIDI comes from (the recording controllers), placing clips on lanes (RecordedTakes).
/// Tests: this class.
/// </summary>
public class MidiTakeTests
{
    [Fact]
    public void NotesBecomeOneClipAndAHeldNoteEndsAtTheStop()
    {
        var take = new MidiTake();
        take.Keep(0x90, 60, 100, 1.0, 0, null, sec => sec);
        take.Keep(0x80, 60, 0, 1.5, 0, null, sec => sec);
        take.Keep(0x90, 64, 90, 2.0, 0, null, sec => sec);
        var clips = take.Finish("Guitar", 3.0);
        var clip = Assert.Single(clips);
        Assert.Equal("Guitar MIDI", clip.Name);
        Assert.Equal(1.0, clip.StartSec, 6);
        Assert.Equal(2.0, clip.SourceLengthSec, 6);
        Assert.Equal(new[] { 60, 64 }, clip.Notes!.Select(n => n.Pitch));
        Assert.Equal(1.0, clip.Notes![1].LengthSec, 6);
    }

    [Fact]
    public void ALoopLapStartsANewPassAtTheLoopStart()
    {
        var take = new MidiTake();
        var loop = (Start: 0.0, End: 4.0);
        take.Keep(0x90, 60, 100, 1.0, 0, loop, sec => sec);
        take.Keep(0x90, 62, 100, 0.5, 1, loop, sec => sec);
        var clips = take.Finish("Keys", 2.0);
        Assert.Equal(new[] { "Keys MIDI take 1", "Keys MIDI take 2" }, clips.Select(c => c.Name));
        Assert.Equal(0.0, clips[1].StartSec, 6);
        Assert.Equal(3.0, clips[0].Notes!.Single().LengthSec, 6);
    }
}
