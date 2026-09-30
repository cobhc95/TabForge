using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using TabForge.Controllers;
using TabForge.Diagnostics;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Plugins;
using TabForge.Services;
using TabForge.Visualization;

namespace TabForge.Views;

/// <summary>Pure horizontal playhead-follow thresholds, independent of the current timeline zoom.</summary>
internal static class ArrangementFollowGeometry
{
    public static double RightTriggerDistance(double viewportWidth, double threeBarSpan)
        => Math.Min(Math.Max(0, threeBarSpan), Math.Max(0, viewportWidth) * 0.45);

    public static bool ShouldAdvance(double playheadX, double horizontalOffset, double viewportWidth,
        double threeBarSpan)
        => playheadX >= horizontalOffset + viewportWidth - RightTriggerDistance(viewportWidth, threeBarSpan);

    public static double AdvanceOffset(double horizontalOffset, double viewportWidth, double scrollableWidth)
        => Math.Min(Math.Max(0, scrollableWidth), horizontalOffset + Math.Max(0, viewportWidth) * 0.5);

    public static double OffsetForBackwardSeek(double playheadX, double viewportWidth)
        => Math.Max(0, playheadX - Math.Max(0, viewportWidth) * 0.5);
}
