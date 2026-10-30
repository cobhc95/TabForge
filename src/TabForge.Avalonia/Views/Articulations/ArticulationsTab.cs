using System;
using Avalonia.Controls;
using TabForge.Avalonia.Views.Midi;

namespace TabForge.Avalonia.Views.Articulations;

// Owns: putting the Articulations tab beside the processors in the MIDI processing window (the one hook the window calls for an instrument plug-in).
// Does not own: the tab content (ArticulationsPane) or the window layout it wraps.
// Tests: no dedicated test class.
public static class ArticulationsTab
{
    /// <summary>
    /// Replaces the window top row and body (rows 0 and 1 of <paramref name="root"/>) with two tabs: Processors (what they were) and Articulations
    /// (<paramref name="makePane"/>, built the first time its tab is shown). Returns the tab control.
    /// </summary>
    public static TabControl Install(Grid root, Control top, Control body, Func<ArticulationsPane> makePane)
    {
        root.Children.Remove(top);
        root.Children.Remove(body);
        var processors = new Grid { RowDefinitions = RowDefinitions.Parse("Auto,*") };
        processors.Children.Add(top);
        processors.Children.Add(body);
        var articulations = new TabItem { Header = "Articulations", Name = "ArticulationsTab" };
        var tabs = new TabControl { Name = "MidiTabs", Items = { new TabItem { Header = "Processors", Content = processors, Name = "ProcessorsTab" }, articulations } };
        MidiSharedRows.Tip(articulations, "Which MIDI a note technique (palm mute, harmonic, slide ...) sends to this instrument: keyswitches, controllers, velocity or another pitch");
        tabs.SelectionChanged += (_, e) =>
        {
            if (e.Source == tabs && ReferenceEquals(tabs.SelectedItem, articulations) && articulations.Content is null) articulations.Content = makePane();
        };
        Grid.SetRow(tabs, 0);
        Grid.SetRowSpan(tabs, 2);
        root.Children.Insert(0, tabs);
        return tabs;
    }

    /// <summary>Installs the same two tabs when the Processors tab has no processor panes (a track without a plug-in).</summary>
    public static TabControl Install(Grid root, Control processorsContent, Func<ArticulationsPane> makePane)
    {
        var articulations = new TabItem { Header = "Articulations", Name = "ArticulationsTab" };
        var tabs = new TabControl { Name = "MidiTabs", Items = { new TabItem { Header = "Processors", Content = processorsContent, Name = "ProcessorsTab" }, articulations } };
        MidiSharedRows.Tip(articulations, "Which MIDI a note technique (palm mute, harmonic, slide ...) sends to this instrument: keyswitches, controllers, velocity or another pitch");
        tabs.SelectionChanged += (_, e) =>
        {
            if (e.Source == tabs && ReferenceEquals(tabs.SelectedItem, articulations) && articulations.Content is null) articulations.Content = makePane();
        };
        Grid.SetRow(tabs, 0);
        Grid.SetRowSpan(tabs, 2);
        root.Children.Add(tabs);
        return tabs;
    }
}
