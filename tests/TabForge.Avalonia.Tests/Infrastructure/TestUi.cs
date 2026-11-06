// Owns: the small UI-thread and byte helpers many test classes repeated word for word: settling a window (queued jobs, then
//   layout, three times) and the MIDI variable-length quantity read and write.
// Does not own: product code, window fixtures (each class builds its own) or the dialog checks (DialogTestKit).
// Tests: used by the classes that import it with `using static TabForge.Avalonia.Tests.TestUi;`.
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Threading;

namespace TabForge.Avalonia.Tests;

internal static class TestUi
{
    /// <summary>Runs the UI thread's queued jobs and lays out <paramref name="window"/>, three passes, so posted work and the layout it causes settle.</summary>
    public static void Settle(Window window)
    {
        for (var pass = 0; pass < 3; pass++)
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
        }
    }

    /// <summary>Reads a MIDI variable-length quantity at <paramref name="p"/> (at most four bytes) and moves past it.</summary>
    public static int ReadVarLen(byte[] b, ref int p)
    {
        var value = 0;
        for (var i = 0; i < 4; i++)
        {
            var x = b[p++];
            value = (value << 7) | (x & 0x7F);
            if ((x & 0x80) == 0) return value;
        }
        return value;
    }

    /// <summary>Appends <paramref name="v"/> to <paramref name="o"/> as a MIDI variable-length quantity.</summary>
    public static void Vlq(List<byte> o, int v)
    {
        var parts = new Stack<byte>(); parts.Push((byte)(v & 0x7F));
        for (v >>= 7; v > 0; v >>= 7) parts.Push((byte)(v & 0x7F | 0x80));
        o.AddRange(parts);
    }
}
