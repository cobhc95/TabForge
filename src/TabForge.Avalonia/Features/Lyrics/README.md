# Lyrics: the Lyrics dock pane

The lyric rules and edits live in Core (`LyricsController`, `DocumentEdits`); this folder wires them into one dock pane and one small window.
`LyricsPaneFlow` places the pane and keeps it on the shown tab's song.

| Class | Job |
|---|---|
| `LyricsSongPane` | The Lyrics dock pane as the WPF app has it: one text box of the shown song's lyrics (`Project.Lyrics`). Leaving the box commits the text through `DocumentEdits` (one undo step, one dirty change); a button opens the per-bar syllable editor. |
| `LyricsPaneView` | The per-bar syllable editor: one row per bar (bar number, section name, a text box of the bar's syllables). Enter commits the bar and moves on, leaving the box commits it, and the bar under the playhead takes the AccentSoft fill. |
| `LyricsByBarWindow` | The window that shows `LyricsPaneView` for one song, opened from the Lyrics pane's "Edit by bar" button. |
| `LyricsPaneFlow` | Placing the Lyrics pane in the dock and keeping it on the active tab's song. |

Rules: edits go through `DocumentEdits` (undoable, dirty-marked); the per-bar rows follow the playhead but never write it.

Tests: `LyricsPaneTests`, `LyricsSongPaneTests`. Run the Avalonia test project locally with
`--filter "FullyQualifiedName~LyricsSongPaneTests"`.
