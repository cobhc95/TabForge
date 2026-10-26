// Owns: nothing; this file is an intentional tombstone/pointer (no-delete rule).
// Does not own: any code; see the note below for where the behaviour lives now.
// Tests: none.
namespace TabForge.Avalonia.Platform;

// Retired in 0.8.1: the triangle resize knobs above the ruler were replaced by grabbing the section chip edges directly (TimelineView.SectionEdgeAt, edge highlight in DrawSections).
// The file is kept as a tombstone; nothing draws or hit-tests knobs any more.
internal static class SectionKnobs
{
}
