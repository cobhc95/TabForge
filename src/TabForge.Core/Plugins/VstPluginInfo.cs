// Owns: the plug-in row data (name, path, format, vendor, role) that the Add plug-in window and its list presenter share.
// Does not own: scanning (VstScannerService), approval (PluginTrust) or any toolkit type.
// Tests: no dedicated test class.
namespace TabForge.Plugins;

/// <param name="ClassId">A VST3 plug-in's class id (32 hex digits) when its bundle lists one, else "".</param>
/// <param name="Role">"Instrument", "Effect", or "" when not known yet (VST2 roles are probed when first used).</param>
public sealed record VstPluginInfo(string Name, string Path, string Format = "VST3", string Vendor = "", string Role = "", string ClassId = "")
{
    /// <summary>The format of a 32-bit VST2 DLL: hosted through the 32-bit helper process, so it is a normal selectable plug-in.</summary>
    public const string Format32 = PluginFormats.Bridged32;
    /// <summary>The format of a 64-bit Windows VST2 DLL hosted through the 64-bit helper under Wine on Linux.</summary>
    public const string Format64 = PluginFormats.Bridged64;

    /// <summary>True for a 32-bit VST2 DLL the 32-bit helper hosts (the old "not supported" label is migrated by the scanner and validators).</summary>
    public bool IsBridged32 => Format == Format32;
    /// <summary>True for a 64-bit Windows VST2 DLL the 64-bit helper hosts under Wine.</summary>
    public bool IsBridged64 => Format == Format64;

    public override string ToString() => $"{Name}  ({Format})";
}
