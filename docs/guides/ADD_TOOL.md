# Add a palette tool (one page)

The Tools pane is data-driven: a row in PaletteToolCatalog becomes a button, and the row's id maps to an existing command. The pieces are:

- Source/src/TabForge.Core/Presenters/PaletteToolCatalog.cs: the rows, their tabs, ids and labels, and CommandIdFor.
- Source/src/TabForge.Avalonia/Assets/Icons/Tools/<Group>/<name>.svg: the icon (32 x 32, currentColor).
- The command itself: a Commands row in the owning MainWindow*.cs (see docs/guides/COMMANDS.md).

## Steps

1. **Command.** Register the action where its feature lives, e.g. new Command("My.Tool", MyTool) in the matching MainWindow partial. Give it a handler that edits the document through DocumentEdits (one undo step) and refreshes the view.
2. **Row.** Add a new PaletteTool("Group", "my:tool", "Group/my_tool", "My tool") to PaletteTools (or the matching tab array: StructureTab, RhythmTab, LayoutTab).
3. **Mapping.** Add "my:tool" => "My.Tool" to CommandIdFor. Tools with no dedicated command can use the Tool. prefix fallback (ToolCommandPrefix), but a real command is clearer.
4. **Icon.** Add the SVG under Assets/Icons/Tools/Group/; the build copies Assets/Icons/Tools/**/*.svg to Assets/ToolIcons/ automatically. A missing file draws the fallback glyph, so this step cannot break the build.
5. **Editor tools (optional).** If the score editor performs the action directly instead of opening a dialog, add the id to EditorToolIds; ScorePreviewEditor.RunPaletteTool then receives it.
6. **Tests.** Extend ToolsPaneViewTests (the row appears and maps to a command) and, for an editor tool, ScorePreviewEditor tests.

Keep labels and tooltips short; colours come from theme keys, sizes from DialogMetrics / the layout tokens.
