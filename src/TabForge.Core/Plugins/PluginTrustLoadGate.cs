using TabForge.Audio;
using TabForge.Audio.Contracts;
using TabForge.Platform;
using TabForge.Services;

namespace TabForge.Plugins;

// Owns: answering the engine client's IPluginLoadGate with PluginTrust, over the file-trust port it is given.
// Does not own: the trust rules (PluginTrust), the operating-system checks (IPluginFileTrust) or the engine I/O (AudioEngineClient).
// Tests: no dedicated test class.
/// <summary><see cref="IPluginLoadGate"/> over <see cref="PluginTrust"/>. Holds only its file-trust port; every call asks PluginTrust again.</summary>
public sealed class PluginTrustLoadGate : IPluginLoadGate
{
    private readonly IPluginFileTrust files;

    /// <summary>The trust state this gate asks; the app's windows ask the same one, so a file the engine found changed is untrusted for them too.</summary>
    public PluginTrust Trust { get; }

    public PluginTrustLoadGate(IPluginFileTrust files, PluginTrust trust)
    {
        this.files = files ?? throw new ArgumentNullException(nameof(files));
        Trust = trust ?? throw new ArgumentNullException(nameof(trust));
    }

    public List<PluginSpec> BuildSpecs(IEnumerable<PluginSlot> plugins, ICollection<string> quarantined, PluginSettings settings) =>
        Trust.BuildSpecs(plugins, quarantined, settings, files);

    public void MarkChanged(string path) => Trust.MarkChanged(path);
}
