using System.Windows;
using System.Windows.Media;
using TabForge.Models;
using RenderDraw = TabForge.Visualization.Draw;
using TabForge.Views.Score;

namespace TabForge.Views;

/// <summary>
/// Dynamics markings (ppp..fff) engraved as bold italic letters under the staff (under the TAB when the
/// notation staff is hidden). A marking appears on the first note of the track and then only where the dynamic
/// changes. The marks are computed once per score generation (with the palm-mute passages), never per frame,
/// and they take part in the bar's width and in the palm-mute lane stacking so nothing overlaps.
/// </summary>
public sealed partial class TabEditorControl
{

}
