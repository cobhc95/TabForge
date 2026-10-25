using System;

namespace TabForge.Avalonia.Platform;

// Owns: the two numeric rules of the transport bar's fit: whether the wrapped groups sit in one row (their wrapped height is no taller than the
//     tallest group), and the next smaller button size to try while they do not (never below the named minimum).
// Does not own: the layout itself (TransportBarView's WrapPanel and its groups), the button sizes (TransportBarView constants).
// Tests: TabForge.Avalonia.Tests.TransportWrapRulesTests.
public static class TransportWrapRules
{
    /// <summary>True when a wrapped height is at most the tallest group's height, so every group shares the one row.</summary>
    public static bool FitsOneRow(double wrappedHeight, double tallestGroupHeight) => wrappedHeight <= tallestGroupHeight + 0.5;

    /// <summary>The next button size down a step, never below <paramref name="minimumSize"/>.</summary>
    public static double NextSmallerSize(double size, double minimumSize, double step) => Math.Max(minimumSize, size - step);
}
