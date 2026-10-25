namespace TabForge.Presenters;

/// <summary>
/// The logic of the compact mode strip: when the strip and the window's menu row show, each pill's tooltip and which pill is lit.
/// Owns: the visibility rules, the pill tooltip text and the lit-pill rule.
/// Does not own: the strip's layout and wiring (CompactMode/CompactModeStrip), the pill looks (TrackPillStrip) or the track selection.
/// Tests: no dedicated test class.
/// </summary>
public static class CompactModeStripPresenter
{
    public const string Caption = "COMPACT MODE";

    /// <summary>The strip shows while compact mode is on, unless another header (the Band view's) takes its place.</summary>
    public static bool StripShown(bool on, bool otherHeaderShown) => on && !otherHeaderShown;

    /// <summary>The menu row is hidden whenever compact mode is on, whichever header shows.</summary>
    public static bool MenuRowShown(bool on) => !on;

    public static string PillTooltip(string trackName) => "Select " + trackName;

    /// <summary>Exactly the selected track's pill is lit.</summary>
    public static bool PillLit(int pillIndex, int selectedIndex) => pillIndex == selectedIndex;
}
