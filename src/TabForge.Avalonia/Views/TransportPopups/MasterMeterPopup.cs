using System;
using System.Linq;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using TabForge.Audio.Metering;
using TabForge.Avalonia.Platform;

namespace TabForge.Avalonia.Views.TransportPopups;

// Owns: the rows of the master meter's settings popup (its right-click): the Measure drop-down (Peak, RMS, Loudness), the Show number check box,
//     the Peak hold drop-down (1 s, 3 s, Off) and the Reset clip button. Every change applies at once and calls the change callback.
// Does not own: the popup's look and opening (TransportPopupFrame), the meter or its numbers (MasterMeterView, MasterMeterModel), the choices' wording (MasterMeterSettings).
// Tests: TabForge.Avalonia.Tests.MasterMeterBarTests.
public static class MasterMeterPopup
{
    public const string Title = "METER", MeasureLabel = "Measure", ShowNumberLabel = "Show number", PeakHoldLabel = "Peak hold", ResetClipLabel = "Reset clip";
    private const string CloseTip = "Close meter settings";
    private const double ResetTopMargin = 10;

    /// <param name="settings">The meter settings the rows read and write.</param>
    /// <param name="changed">Runs after every change (the caller refreshes the meter and saves).</param>
    /// <param name="resetClip">Runs when Reset clip is pressed.</param>
    public static TransportPopupFrame Create(MasterMeterSettings settings, Action changed, Action resetClip)
    {
        var frame = new TransportPopupFrame(Title, CloseTip, TransportPopupFrame.WideWidth);
        var modes = MasterMeterSettings.Modes;
        frame.AddChoice(MeasureLabel, modes.Select(MasterMeterSettings.ModeLabel), modes.ToList().IndexOf(MasterMeterSettings.ParseMode(settings.Mode)),
            index => { settings.Mode = modes[index]; changed(); }, 2);
        frame.AddCheck(ShowNumberLabel, settings.ShowNumber, value => settings.ShowNumber = value, changed);
        var holds = MasterMeterSettings.HoldChoices;
        frame.AddChoice(PeakHoldLabel, holds.Select(MasterMeterSettings.HoldLabel), holds.ToList().IndexOf(settings.HoldSeconds),
            index => { settings.HoldSeconds = holds[index]; changed(); });
        var reset = new Button { Content = ResetClipLabel, HorizontalAlignment = HorizontalAlignment.Left, };
        reset.WithMargin(0, ResetTopMargin, 0, 0);
        AutomationProperties.SetName(reset, ResetClipLabel);
        reset.Click += (_, _) => resetClip();
        frame.Rows.Children.Add(reset);
        return frame;
    }
}
