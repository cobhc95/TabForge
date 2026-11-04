// Stage 15 test-only probe (never shipped): prepare one Wine prefix with the production WineRuntime code, in its own
// process, so the two-copy fault test can start two real processes against the same prefix and count wineboot calls.
using TabForge.AudioEngine.Isolation;

if (args.Length != 3 || args[0] != "prepare")
{
    Console.Error.WriteLine("usage: Stage15Probe prepare <wine> <prefix>");
    return 2;
}

var helper = Path.Combine(Path.GetTempPath(), "stage15-probe-tfvst32.exe");
var helper64 = Path.Combine(Path.GetTempPath(), "stage15-probe-tfvst64.exe");
var runtime = new WineRuntime(args[1], helper, args[2], helperPath64: helper64);
try
{
    runtime.EnsureReady(5000, 10000);
    Console.WriteLine($"ready {runtime.Version}");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.GetBaseException().Message);
    return 1;
}
