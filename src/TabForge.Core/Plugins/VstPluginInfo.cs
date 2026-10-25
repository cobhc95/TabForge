// Owns: the plug-in row data (name, path, format, vendor, role) that the Add plug-in window and its list presenter share.
// Does not own: scanning (VstScannerService), approval (PluginTrust) or any WPF type.
// Tests: no dedicated test class.
namespace TabForge.Plugins;

/// <param name="ClassId">A VST3 plug-in's class id (32 hex digits) when its bundle lists one, else "".</param>
/// <param name="Role">"Instrument", "Effect", or "" when not known yet (VST2 roles are probed when first used).</param>
public sealed record VstPluginInfo(string Name, string Path, string Format = "VST3", string Vendor = "", string Role = "", string ClassId = "")
{
    /// <summary>The format of a 32-bit VST2 DLL the scan found: listed so the user sees why it is not usable, never loaded.</summary>
    public const string Format32 = "32-bit (not supported)";

    public const string Text32 = "This is a 32-bit plug-in; TabForge loads 64-bit plug-ins only";

    public bool IsUnsupported32 => Format == Format32;

    public override string ToString() => $"{Name}  ({Format})";
}
