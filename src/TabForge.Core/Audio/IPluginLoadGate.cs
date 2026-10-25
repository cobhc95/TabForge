using TabForge.Audio.Contracts;
using TabForge.Plugins;
using TabForge.Services;

namespace TabForge.Audio;

// Owns: the two trust questions the engine client asks before and after a chain loads (which slots may load, which file changed).
// Does not own: any trust rule (PluginTrust in Core/Plugins keeps all of it) or the operating-system checks (IPluginFileTrust).
// Tests: no dedicated test class.
/// <summary>
/// The engine client's view of plug-in trust. An implementation fails closed: an untrusted, unknown or unreadable plug-in is
/// returned with <see cref="PluginSpec.Skip"/> set, so the engine never loads it.
/// </summary>
public interface IPluginLoadGate
{
    /// <summary>One spec per slot; quarantined and untrusted plug-ins are Skip, trusted ones carry the hash the engine checks at load.</summary>
    List<PluginSpec> BuildSpecs(IEnumerable<PluginSlot> plugins, ICollection<string> quarantined, PluginSettings settings);

    /// <summary>The engine found the file at <paramref name="path"/> different from its approval: untrusted until approved again.</summary>
    void MarkChanged(string path);
}
