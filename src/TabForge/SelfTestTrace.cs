using TabForge.Diagnostics;

namespace TabForge;

// TABFORGE_TRACE parsing: the single opt-in debug switch (Audit 4, step 5).
public static partial class SelfTest
{
    private static void TestTraceSwitchAreas()
    {
        Check("trace: unset enables nothing", Trace.ParseAreas(null).Count == 0 && Trace.ParseAreas("  ").Count == 0);
        var some = Trace.ParseAreas("Playback, layout;ui bogus");
        Check("trace: named areas parse case-insensitively with , ; and space",
            some.SetEquals(new[] { Trace.Playback, Trace.Layout, Trace.Ui }), string.Join(",", some));
        var all = Trace.ParseAreas("all");
        Check("trace: all enables every area",
            all.SetEquals(new[] { Trace.Playback, Trace.Engine, Trace.Layout, Trace.Import, Trace.Ui }), string.Join(",", all));
        Check("trace: files go to the diagnostics folder",
            string.Equals(Trace.PathFor(Trace.Layout), System.IO.Path.Combine(TabForge.Services.UserPaths.Diagnostics, "trace-layout.log"), StringComparison.OrdinalIgnoreCase));   // also right under --profile
    }
}
