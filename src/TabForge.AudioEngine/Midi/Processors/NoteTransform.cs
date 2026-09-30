namespace TabForge.AudioEngine.Midi.Processors;

/// <summary>
/// Base of the stateless-looking note processors (channel map / route, note range, transpose, note map, velocity).
/// It owns the held-note table, so a note-off always leaves with the pitch and channel its note-on actually left with,
/// whatever the parameters did in between, and a dropped note-on drops its note-off too.
/// </summary>
public abstract class NoteTransform : IMidiProcessor
{
    protected readonly HeldNotes Held = new();
    /// <summary>Input channel filter: 0 = any, 1..16 = only that channel (other channels pass untouched).</summary>
    protected int InCh;

    /// <summary>Maps a note-on; return false to drop it. <paramref name="ch"/> is 0-based.</summary>
    protected abstract bool MapNote(ref int ch, ref int note, ref int vel);
    /// <summary>Maps any other channel message (CC, program, pressure, bend); return false to drop it.</summary>
    protected virtual bool MapOther(int kind, ref int ch, ref int d1, ref int d2) => true;

    public void Process(MidiBuffer input, MidiBuffer output, in MidiContext ctx)
    {
        for (var i = 0; i < input.Count; i++)
        {
            var e = input.Items[i];
            var st = e.Status;
            if (st >= 0xF0) { output.Add(e); continue; }
            var kind = st & 0xF0; var ch = st & 0x0F;
            if (InCh != 0 && ch != InCh - 1) { output.Add(e); continue; }
            if (kind == 0x90 && e.Data2 > 0)
            {
                int och = ch, note = e.Data1, vel = e.Data2;
                if (!MapNote(ref och, ref note, ref vel)) continue;
                note = Math.Clamp(note, 0, 127); och = Math.Clamp(och, 0, 15); vel = Math.Clamp(vel, 1, 127);
                // The same key pressed again without a release: end the earlier output note first if it differs.
                if (Held.TryGet(ch, e.Data1, out var pch, out var pnote) && (pch != och || pnote != note))
                    output.Add(e.Frame, (byte)(0x80 | pch), (byte)pnote, 0);
                Held.Set(ch, e.Data1, och, note);
                output.Add(e.Frame, (byte)(0x90 | och), (byte)note, (byte)vel);
            }
            else if (kind is 0x80 or 0x90)
            {
                if (Held.TryGet(ch, e.Data1, out var pch, out var pnote))
                {
                    Held.Clear(ch, e.Data1);
                    output.Add(e.Frame, (byte)(kind | pch), (byte)pnote, e.Data2);
                }
                else
                {
                    int och = ch, note = e.Data1, vel = 64;
                    if (MapNote(ref och, ref note, ref vel)) output.Add(e.Frame, (byte)(kind | Math.Clamp(och, 0, 15)), (byte)Math.Clamp(note, 0, 127), e.Data2);
                }
            }
            else if (kind == 0xA0)
            {
                if (Held.TryGet(ch, e.Data1, out var pch, out var pnote)) output.Add(e.Frame, (byte)(0xA0 | pch), (byte)pnote, e.Data2);
                else output.Add(e);
            }
            else
            {
                int och = ch, d1 = e.Data1, d2 = e.Data2;
                if (MapOther(kind, ref och, ref d1, ref d2))
                    output.Add(e.Frame, (byte)(kind | Math.Clamp(och, 0, 15)), (byte)Math.Clamp(d1, 0, 127), (byte)Math.Clamp(d2, 0, 127));
            }
        }
    }

    public virtual void Adopt(IMidiProcessor previous, bool sameParameters)
    {
        if (previous is NoteTransform p) Held.CopyFrom(p.Held);
    }

    public virtual void Reset() => Held.Reset();
}
