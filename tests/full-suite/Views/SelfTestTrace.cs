using TabForge.Services;

namespace TabForge;

// TABFORGE_TRACE parsing: the single opt-in debug switch.
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
            string.Equals(Trace.PathFor(Trace.Layout), System.IO.Path.Combine(UserPaths.Diagnostics, "trace-layout.log"), StringComparison.OrdinalIgnoreCase));   // also right under --profile
    }

    private static void TestErrorLog()
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tf-errlog-" + Guid.NewGuid().ToString("N"));
        var file = System.IO.Path.Combine(dir, "errors.log");
        try
        {
            var clock = new DateTime(2026, 1, 1, 12, 0, 0);
            var log = new ErrorLog(() => file, 2000, () => clock);
            log.Write("ui", "first");
            log.Write("ui", "first"); log.Write("ui", "first");
            clock = clock.AddMilliseconds(500); log.Write("ui", "first");
            clock = clock.AddSeconds(2); log.Write("ui", "other\r\nline");
            var text = System.IO.File.ReadAllText(file);
            Check("error log: writes area-tagged lines", text.Contains("[ui] first") && text.Contains("[ui] other line"), text);
            Check("error log: repeats inside 1 s are dropped and counted",
                text.Split("[ui] first").Length == 2 && text.Contains("(repeated 3 more times)"), text);

            for (var i = 0; i < 100; i++) { clock = clock.AddSeconds(2); log.Write("engine", "fill " + i + new string('x', 60)); }
            var rotated = System.IO.Path.Combine(dir, "errors.1.log");
            Check("error log: rotates once at the cap", System.IO.File.Exists(rotated) && new System.IO.FileInfo(file).Length <= 2000
                && new System.IO.FileInfo(rotated).Length <= 2000 && !System.IO.File.Exists(System.IO.Path.Combine(dir, "errors.2.log")));

            var shared = new ErrorLog(() => file, 1_000_000, () => DateTime.Now);
            System.Threading.Tasks.Parallel.For(0, 200, i => shared.Write("ui", "thread " + i));
            var lines = System.IO.File.ReadAllLines(file).Count(l => l.Contains("thread "));
            Check("error log: parallel writes all land, none throw", lines == 200, lines.ToString());

            Check("error log: a bad path never throws", Throws(() => new ErrorLog(() => "\0:bad", 100, () => DateTime.Now).Write("ui", "x")) == false);
        }
        finally { try { System.IO.Directory.Delete(dir, true); } catch (System.IO.IOException) { } }
    }

}
