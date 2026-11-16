// Owns: the master list of user-facing captions that repeat across dialogs and presenters (OK, Cancel, Apply, Close, Yes, No, Delete...).
// Does not own: one-off texts (they stay next to the code that shows them, usually as a presenter constant) or key texts (HotkeyCatalog, FixedKeys).
// Tests: TabForge.Avalonia.Tests.HotkeyLiteralGuardTests (a common caption written as a literal outside this file fails).
namespace TabForge;

/// <summary>Repeated button captions and short prompts. Read these instead of writing "OK" or "Cancel" again.</summary>
public static class CommonText
{
    public const string Ok = "OK", Cancel = "Cancel", Apply = "Apply", Close = "Close";
    public const string Yes = "_Yes", No = "_No";
    public const string Delete = "Delete", Save = "Save", Load = "Load", Clear = "Clear", Convert = "Convert", Reset = "Reset";
    public const string Browse = "Browse…";
}
