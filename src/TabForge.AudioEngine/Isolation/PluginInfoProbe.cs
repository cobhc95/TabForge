using TabForge.AudioEngine.Plugins;

namespace TabForge.AudioEngine.Isolation;

/// <summary>
/// `TabForge.exe --plugin-info &lt;path&gt; &lt;approved sha256&gt;`: loads one plug-in in a throwaway process and prints
/// "role|vendor|name" (role = Instrument or Effect). A plug-in that crashes here only ends this process.
/// Like the plug-in scan, this tells instruments from effects without trusting file names. This executes the plug-in, so it
/// only ever runs for an approved file: the hash is required (fail closed) and is checked through the shared
/// <see cref="TabForge.Audio.Contracts.PluginIdentity"/> rule before the file is loaded. Exit 5 = refused (missing or changed).
/// </summary>
public static class PluginInfoProbe
{
    /// <param name="create">Test seam only: replaces the real load (a counter proves an unapproved file is never executed).</param>
    public static int Run(string[] args, Func<string, IPluginInstance>? create = null)
    {
        if (args.Length < 2 || !File.Exists(args[1]) && !Directory.Exists(args[1])) return 2;
        var path = args[1];
        try
        {
            // VST3: prefer the bundle's instrument class (a bundle with one reports it); VST2 reports its own role.
            using var plugin = PluginFactory.CreateVerified(path, "", isInstrument: true, 48000, 256, args.Length > 2 ? args[2] : "", required: true, create);
            var role = plugin.IsInstrument ? "Instrument" : "Effect";
            Console.Out.WriteLine(plugin is Vst2Plugin vst2 ? $"{role}|{Clean(vst2.Vendor)}|{Clean(vst2.ProductName)}" : $"{role}||");
            Console.Out.Flush();
            return 0;
        }
        catch (TabForge.Audio.Contracts.PluginChangedException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 5;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.GetBaseException().Message);
            return 1;
        }
    }

    private static string Clean(string text) => text.Replace('|', '/').Replace('\n', ' ').Replace('\r', ' ').Trim();
}
