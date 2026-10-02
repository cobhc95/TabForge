namespace TabForge;

/// <summary>
/// Process entry. The audio engine mode (--audio-engine) runs without WPF at all, so the engine process stays
/// small; every other start is the normal TabForge application.
/// </summary>
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--audio-engine") return TabForge.AudioEngine.EngineHost.Run(args);
        if (args.Length > 0 && args[0] == "--plugin-host") return TabForge.AudioEngine.Isolation.PluginHostMain.Run(args);
        if (args.Length > 0 && args[0] == "--probe-gm") return TabForge.AudioEngine.Synth.GmSynthProbe.Run(args);
        // A5-07: the out-of-process Guitar Pro import worker (no WPF, no settings; the parent kills it on Cancel or timeout).
        if (args.Length > 0 && args[0] == TabForge.Services.ImportWorker.Argument) return TabForge.Services.ImportWorker.Run(args);
        if (args.Length > 0 && args[0] == "--plugin-info") return TabForge.AudioEngine.Isolation.PluginInfoProbe.Run(args);
        // Self-test child: a .gp + .tfaudio pair save that stops at one stage so the self-test can kill the process there (no WPF, no settings).
#if FULL_SUITE
        if (args.Length > 0 && args[0] == "--pair-save-probe") return SelfTest.PairSaveProbe(args);
#endif
        TabForge.Services.UserPaths.ApplyProfileArgument(args);
        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }
}
