namespace TabForge.Views;

// Owns: the bar-and-slot span of a palm-mute run and of a fade, as the score layout derives and the drawing reads them.
// Does not own: finding the spans (ScorePassages) or drawing them (ScoreRenderer.Passages). Tests: TestNotationLayout.

internal readonly record struct PalmMutePassage(
    int FirstMeasure, double FirstStartSlots,
    int LastMeasure, double LastEndSlots,
    int EventCount);

internal readonly record struct FadePassage(
    int FirstMeasure, double FirstStartSlots,
    int LastMeasure, double LastEndSlots,
    bool IsFadeOut);
