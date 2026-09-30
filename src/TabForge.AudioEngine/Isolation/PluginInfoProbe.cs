using TabForge.AudioEngine.Plugins;

namespace TabForge.AudioEngine.Isolation;

/// <summary>
/// `TabForge.exe --plugin-info &lt;path&gt;`: loads one plug-in in a throwaway process and prints
/// "role|vendor|name" (role = Instrument or Effect). A plug-in that crashes here only ends this process.
/// Like the plug-in scan, this tells instruments from effects without trusting file names.
/// </summary>
public static class PluginInfoProbe
{
    public static int Run(string[] args)
    {
        if (args.Length < 2 || !File.Exists(args[1]) && !Directory.Exists(args[1])) return 2;
        var path = args[1];
        try
        {
            // VST3: prefer the bundle's instrument class (a bundle with one reports it); VST2 reports its own role.
            using var plugin = PluginFactory.Create(path, "", isInstrument: true, 48000, 256);
            var role = plugin.IsInstrument ? "Instrument" : "Effect";
            Console.Out.WriteLine(plugin is Vst2Plugin vst2 ? $"{role}|{Clean(vst2.Vendor)}|{Clean(vst2.ProductName)}" : $"{role}||");
            Console.Out.Flush();
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.GetBaseException().Message);
            return 1;
        }
    }

    private static string Clean(string text) => text.Replace('|', '/').Replace('\n', ' ').Replace('\r', ' ').Trim();
}
