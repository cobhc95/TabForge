using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace TabForge.Avalonia.Views.TransportPopups;

// Owns: the small row builders the master scale panel and the host extras share: a wrapped check box and a labelled segmented button row.
// Does not own: the panel or the settings they change.
// Tests: TabForge.Avalonia.Tests.StatusBarScaleTests.
public static class ScalePanelParts
{
    /// <summary>A check box with wrapped text; a click calls <paramref name="store"/> unless <paramref name="building"/> says the panel is being built.</summary>
    public static CheckBox Check(string name, string text, bool isChecked, Action<bool> store, Func<bool>? building = null)
    {
        var box = new CheckBox { Name = name, Content = new TextBlock { Text = text, TextWrapping = global::Avalonia.Media.TextWrapping.Wrap }, IsChecked = isChecked };
        AutomationProperties.SetName(box, text);
        box.IsCheckedChanged += (_, _) => { if (building?.Invoke() != true) store(box.IsChecked == true); };
        return box;
    }

    /// <summary>A caps label over a row of segmented buttons of which one is on; picking another calls <paramref name="store"/> with its value.</summary>
    public static IEnumerable<Control> Segmented(string label, IReadOnlyList<(string Value, string Text)> choices, string current, string namePrefix, Action<string> store,
        Func<bool> building, IReadOnlyDictionary<string, string>? tips = null)
    {
        yield return ScalePopupLayout.SectionLabel(label);
        var buttons = choices.Select(c => ScalePopupLayout.Segment(namePrefix + c.Value, c.Text, c.Value == current)).ToList();
        for (var i = 0; i < buttons.Count; i++)
        {
            var (value, button) = (choices[i].Value, buttons[i]);
            AutomationProperties.SetName(button, label + ": " + choices[i].Text);
            if (tips is not null && tips.TryGetValue(value, out var tip)) ToolTip.SetTip(button, tip);
            button.IsCheckedChanged += (_, _) =>
            {
                if (building()) return;
                if (button.IsChecked != true) { if (value == current) button.IsChecked = true; return; }   // a segment cannot be switched off by itself
                store(value);
            };
        }
        yield return ScalePopupLayout.SegmentRow(buttons);
    }
}
