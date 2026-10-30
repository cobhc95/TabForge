using System.Collections.Generic;
using System.Linq;
using Avalonia.Automation;
using Avalonia.Controls;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Avalonia.Platform.Controls;

namespace TabForge.Avalonia.Views.TransportPopups;

// Owns: the part of the master scale panel under the shared sections: the host's extra sections (a caps title and the controls the host builds) and the muted
//   sections of the other hosts, each with its description and a link that opens that host's panel (disabled with a tip when that area is not on screen).
// Does not own: the shared sections (ScalePanel), what the extras change (the hosts).
// Tests: TabForge.Avalonia.Tests.ScalePanelHostTests.
// Threads: UI thread only.
public sealed partial class ScalePanel
{
    private const string WaitTip = "That area is not on screen right now.";

    private void AddHostSections(global::Avalonia.Controls.Controls rows)
    {
        var extras = new List<string>();
        foreach (var section in _host.Extras)
        {
            rows.Add(ScalePopupLayout.SectionLabel(section.Title));
            rows.Add(section.Build(Close));
            extras.Add(section.Title);
        }
        ExtraTitles = extras;
        var greyed = new List<string>();
        foreach (var section in _host.Greyed)
        {
            rows.Add(GreyedView(section));
            greyed.Add(section.Title);
        }
        GreyedTitles = greyed;
    }

    private Control GreyedView(ScaleGreyedSection section)
    {
        var box = new StackPanel { Name = "ScaleGreyed" + section.Id, Opacity = ScalePopupLayout.GreyedOpacity };
        box.Children.Add(ScalePopupLayout.SectionLabel(section.Title));
        box.Children.Add(TransportPopupFrame.Helper(section.Description));
        var can = section.CanOpen();
        var link = new Button { Name = "ScaleOpen" + section.Id, Content = can ? section.LinkText : section.WaitText, IsEnabled = can, HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Left }.WithMargin(ScalePopupLayout.LinkRowMargin);
        TextLinkButton.Apply(link);
        AutomationProperties.SetName(link, can ? section.LinkText : section.WaitText);
        if (!can) ToolTip.SetTip(link, WaitTip);
        link.Click += (_, _) => { Close(); section.Open(); };
        box.Children.Add(link);
        return box;
    }
}
