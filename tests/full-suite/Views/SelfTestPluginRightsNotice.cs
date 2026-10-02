using TabForge.Plugins;
using TabForge.Services;

namespace TabForge;

public static partial class SelfTest
{
    /// <summary>The plug-in trust wording keeps its key facts (permissions, files, crash-only isolation, trusted sources, where the option is).</summary>
    private static void TestPluginRightsNotice()
    {
        var t = PluginTrust.RightsNotice;
        Check("Plug-in notice: says a plug-in runs with your Windows permissions, can read and write your files, and to use trusted sources only",
            t.Contains("Windows permissions") && t.Contains("read and write your files") && t.Contains("sources you trust"), t);
        Check("Plug-in notice: says own-process running protects TabForge from a crash, not your files, is off by default, and where the option is",
            t.Contains("protects TabForge from a plug-in crash, not your files") && t.Contains("off by default") && t.Contains("Settings > Audio & Plug-ins"));
        var isolate = SettingsCatalog.Build(new AppSettings()).FirstOrDefault(s => s.Key == "vst.isolate");
        Check("Plug-in notice: the own-process setting's help says the same (crash, not files; Windows permissions; trusted sources)",
            isolate is not null && isolate.Description.Contains("not your files from the plug-in") && isolate.Description.Contains("Windows permissions") && isolate.Description.Contains("sources you trust"));
    }
}
