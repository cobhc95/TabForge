namespace TabForge.Plugins;

// Owns: the one spelling of a VST3 class id (32 upper-case hex digits) used by the scanner, the settings and the chain importer.
// Does not own: reading ids from bundles (VstScannerService) or from chain files (ReaperChainImporter).
// Tests: TabForge.Avalonia.Tests.RfxImportMatchTests.
public static class Vst3ClassId
{
    /// <summary>The id without dashes or braces, upper case; "" when it is not 32 hex digits.</summary>
    public static string Normalize(string? text)
    {
        var id = (text ?? "").Trim().Replace("-", "").Replace("{", "").Replace("}", "");
        return id.Length == 32 && id.All(Uri.IsHexDigit) ? id.ToUpperInvariant() : "";
    }
}
