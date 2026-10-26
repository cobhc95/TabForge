// Owns: nothing; this file is an intentional tombstone/pointer (no-delete rule).
// Does not own: any code; see the note below for where the behaviour lives now.
// Tests: none.
namespace TabForge.Avalonia;

// Stage 2 removed the transient pass picker: pass clips are ordinary clips and the saved active pass is chosen by the
// Make active command in MainWindowTakeGroupActive. This file is kept so the project item list stays stable.
