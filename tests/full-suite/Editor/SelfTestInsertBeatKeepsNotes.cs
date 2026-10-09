using System.Collections.Generic;
using TabForge.Models;
using TabForge.Services;

namespace TabForge;

public static partial class SelfTest
{
    /// <summary>Insert beat never drops a note from the bar's last slot; only empty trailing cells fall off.</summary>
    private static void TestInsertBeatKeepsNotes()
    {
        static TabCell Note() { var c = new TabCell(); c.Notes.Add(new TabNote { StringIndex = 0, Fret = 3, MidiValue = 67 }); return c; }
        var full = new List<TabCell> { Note(), Note(), Note(), Note() };
        EditCommands.InsertBeatAt(full, 0, 4, new TabCell());
        Check("a full bar keeps every note on insert", full.Count == 5 && full[4].Notes.Count == 1, full.Count.ToString());
        var open = new List<TabCell> { Note(), new TabCell(), new TabCell(), new TabCell() };
        EditCommands.InsertBeatAt(open, 0, 4, new TabCell());
        Check("empty trailing cells still fall off", open.Count == 4 && open[1].Notes.Count == 1);
    }
}
