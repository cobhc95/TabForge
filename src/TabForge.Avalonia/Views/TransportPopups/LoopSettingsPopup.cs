using System;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using TabForge.Playback;
using TabForge.Services;

namespace TabForge.Avalonia.Views.TransportPopups;

// Owns: the rows of the loop settings popup: the loop area line, "Loop button loops what is playing" and its scope,
//     "Forget the area when loop is turned off", the number of loops, the count-in before every loop, simple loop or speed trainer with its tempo
//     boxes, and the Clear loop area button. A box is read when it loses focus or on Enter, and shown again normalised.
// Does not own: the popup's look and opening (TransportPopupFrame), the box rules (LoopSettingsRules), the loop area and its clearing
//     (SelectionLoopLink, through the callback), saving and the engine (the change callback).
// Tests: TabForge.Avalonia.Tests.TransportSettingsPopupTests.
public static class LoopSettingsPopup
{
    public const string Title = "LOOP SETTINGS";
    private const string CloseTip = "Close loop settings";
    private const string LoopPowerName = "Loop on or off";
    private const string LoopButtonText = "Loop button loops what is playing (no area needed)";
    private const string ScopeText = "When that is on, loop";
    private const string ClearOnDisableText = "Forget the area when loop is turned off";
    private const string CountText = "Number of loops";
    private const string CountTip = "How many times to play the loop. Empty or ∞ = forever.";
    private const string CountInText = "Count-in clicks before every loop";
    private const string SimpleText = "Simple loop";
    private const string TrainerText = "Speed trainer";
    private const string TempoText = "Tempo";
    private const string ToText = " %  to ";
    private const string PercentText = " %";
    private const string StepText = "Increase tempo each loop by ";
    public const string ClearAreaText = "Clear loop area";
    private const string HelpText = "Drag across bars in the arrangement to pick an area; right-click a section to loop it.";
    private const string ModeGroup = "LoopMode";

    private static readonly string[] ScopeLabels = { "The current section", "The current bar", "The whole song" };
    private static readonly string[] ScopeValues = { LoopSettingsRules.SectionScope, LoopSettingsRules.BarScope, LoopSettingsRules.SongScope };

    private const double AreaBottomMargin = 6;
    private const double LoopButtonCheckMargin = 2;
    private const double LoopButtonCheckBottomMargin = 6;
    private const double ScopeLabelTopMargin = 2;
    private const double SectionTopMargin = 8;
    private const double CountRowTopMargin = 10;
    private const double CountBoxWidth = 60;
    private const double ModeTopMargin = 10;
    private const double SecondModeTopMargin = 4;
    private const double TrainerIndent = 18;
    private const double TrainerTopMargin = 4;
    private const double TempoLabelWidth = 44;
    private const double PercentBoxWidth = 40;
    private static readonly Thickness ClearButtonPadding = new(10, 3);

    /// <param name="audio">The audio settings the rows read and write.</param>
    /// <param name="areaText">The loop area line (LoopSettingsRules.AreaText).</param>
    /// <param name="loopOn">Whether looping is on now; the popup's switch shows it.</param>
    /// <param name="setLoopOn">The switch's change: the caller turns looping on or off.</param>
    /// <param name="changed">Runs after every change (the caller pushes the loop behaviour to the engines and saves).</param>
    /// <param name="clearArea">Clear loop area: turns looping off and drops the area; the popup closes after it.</param>
    public static TransportPopupFrame Create(AudioSettings audio, string areaText, Func<bool> loopOn, Action<bool> setLoopOn, Action changed, Action clearArea)
    {
        var frame = new TransportPopupFrame(Title, CloseTip, TransportPopupFrame.WideWidth);
        frame.AddPower(LoopPowerName, loopOn(), setLoopOn, changed);
        var area = new TextBlock { Text = areaText, Margin = new Thickness(0, 0, 0, AreaBottomMargin) };
        area[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextMuted");
        frame.Rows.Children.Add(area);
        frame.AddCheck(LoopButtonText, audio.LoopButtonLoopsSection, value => audio.LoopButtonLoopsSection = value, changed, LoopButtonCheckMargin)
            .Margin = new Thickness(0, LoopButtonCheckMargin, 0, LoopButtonCheckBottomMargin);
        var scope = Array.IndexOf(ScopeValues, audio.LoopDefaultScope);
        frame.AddChoice(ScopeText, ScopeLabels, scope < 0 ? 0 : scope, index => { audio.LoopDefaultScope = ScopeValues[index]; changed(); }, ScopeLabelTopMargin);
        frame.AddCheck(ClearOnDisableText, audio.LoopClearAreaOnDisable, value => audio.LoopClearAreaOnDisable = value, changed, SectionTopMargin);

        var count = Box(CountText, CountBoxWidth, LoopSettingsRules.CountText(audio.LoopCount));
        ToolTip.SetTip(count, CountTip);
        count.HorizontalAlignment = HorizontalAlignment.Right;
        DockPanel.SetDock(count, Dock.Right);
        frame.Rows.Children.Add(new DockPanel { Margin = new Thickness(0, CountRowTopMargin, 0, 0),
            Children = { count, new TextBlock { Text = CountText, VerticalAlignment = VerticalAlignment.Center } } });
        Commit(count, () =>
        {
            audio.LoopCount = LoopSettingsRules.ParseCount(count.Text);
            count.Text = LoopSettingsRules.CountText(audio.LoopCount);
            changed();
        });

        frame.AddCheck(CountInText, audio.LoopCountInEachLoop, value => audio.LoopCountInEachLoop = value, changed, SectionTopMargin);
        var simple = new RadioButton { Content = SimpleText, GroupName = ModeGroup, IsChecked = !audio.LoopSpeedTrainer, Margin = new Thickness(0, ModeTopMargin, 0, 0) };
        var trainer = new RadioButton { Content = TrainerText, GroupName = ModeGroup, IsChecked = audio.LoopSpeedTrainer, Margin = new Thickness(0, SecondModeTopMargin, 0, 0) };
        AutomationProperties.SetName(simple, SimpleText);
        AutomationProperties.SetName(trainer, TrainerText);
        frame.Rows.Children.Add(simple);
        frame.Rows.Children.Add(trainer);

        var from = Box(TempoText + " from", PercentBoxWidth, audio.LoopTrainerFrom.ToString());
        var to = Box(TempoText + " to", PercentBoxWidth, audio.LoopTrainerTo.ToString());
        var step = Box(StepText.Trim(), PercentBoxWidth, audio.LoopTrainerStep.ToString());
        var trainerRows = new StackPanel
        {
            Margin = new Thickness(TrainerIndent, TrainerTopMargin, 0, 0), IsEnabled = audio.LoopSpeedTrainer,
            Children =
            {
                Row(new TextBlock { Text = TempoText, Width = TempoLabelWidth, VerticalAlignment = VerticalAlignment.Center }, from, Label(ToText), to, Label(PercentText)),
                Row(Label(StepText), step, Label(PercentText)),
            },
        };
        ((Control)trainerRows.Children[1]).Margin = new Thickness(0, TrainerTopMargin, 0, 0);
        frame.Rows.Children.Add(trainerRows);
        trainer.IsCheckedChanged += (_, _) =>
        {
            audio.LoopSpeedTrainer = trainer.IsChecked == true;
            trainerRows.IsEnabled = audio.LoopSpeedTrainer;
            changed();
        };
        Commit(from, () => { audio.LoopTrainerFrom = LoopSettingsRules.ParsePercent(from.Text, audio.LoopTrainerFrom); from.Text = audio.LoopTrainerFrom.ToString(); changed(); });
        Commit(to, () => { audio.LoopTrainerTo = LoopSettingsRules.ParsePercent(to.Text, audio.LoopTrainerTo); to.Text = audio.LoopTrainerTo.ToString(); changed(); });
        Commit(step, () => { audio.LoopTrainerStep = LoopSettingsRules.ParseStep(step.Text, audio.LoopTrainerStep); step.Text = audio.LoopTrainerStep.ToString(); changed(); });

        var clear = new Button { Content = ClearAreaText, HorizontalAlignment = HorizontalAlignment.Left, Padding = ClearButtonPadding, Margin = new Thickness(0, CountRowTopMargin, 0, 0) };
        AutomationProperties.SetName(clear, ClearAreaText);
        clear.Click += (_, _) => { clearArea(); frame.Close(); };
        frame.Rows.Children.Add(clear);
        frame.AddHelper(HelpText, SectionTopMargin);
        return frame;
    }

    private static TextBox Box(string name, double width, string text)
    {
        var box = new TextBox { Width = width, Text = text, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(box, name);
        return box;
    }

    private static TextBlock Label(string text) => new() { Text = text, VerticalAlignment = VerticalAlignment.Center };

    private static StackPanel Row(params Control[] children)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.AddRange(children);
        return row;
    }

    // A box's text is read when focus leaves it or on Enter, as the boxes do (LostFocus, KeyDown Enter).
    private static void Commit(TextBox box, Action read)
    {
        box.LostFocus += (_, _) => read();
        box.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            read();
            e.Handled = true;
        };
    }
}
