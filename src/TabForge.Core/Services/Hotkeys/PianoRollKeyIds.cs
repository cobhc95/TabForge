// Owns: the ids of the piano roll commands that the shortcut catalogue lists (each shared by a menu row or a key-only command), in one
//   place the Services layer can read without depending on the Menus namespace.
// Does not own: the menu rows that carry them (Menus/PianoRollMenus.cs, which re-exports these ids) or the catalogue rows (HotkeyCatalog.MenuCommands.cs).
// Tests: TabForge.Avalonia.Tests.PianoRollViewTests.
namespace TabForge.Services;

public static class PianoRollKeyIds
{
    public const string Duplicate = "PianoRoll.Duplicate", Quantize = "PianoRoll.Quantize", Mute = "PianoRoll.Mute", Legato = "PianoRoll.Legato",
        InvertSelection = "PianoRoll.InvertSelection", TransposeDialog = "PianoRoll.TransposeDialog", ZoomToFit = "PianoRoll.ZoomToFit",
        EditTool = "PianoRoll.EditTool", EraseTool = "PianoRoll.EraseTool", SplitTool = "PianoRoll.SplitTool", MuteTool = "PianoRoll.MuteTool", GlueTool = "PianoRoll.GlueTool";
}
