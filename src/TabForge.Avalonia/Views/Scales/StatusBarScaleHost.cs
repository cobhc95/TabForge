using System.Collections.Generic;
using Avalonia.Controls;
using TabForge.Services;

namespace TabForge.Avalonia.Views.TransportPopups;

// Owns: the status bar's view of the master scale panel: its extras (keep my choice for this section, musical key names, prefer common scales) and the muted
//   fretboard and piano roll sections with their links. A pick is kept in the song only when "Keep my choice" is ticked, else for this session.
// Does not own: the shared sections (ScalePanel), the readout (StatusScaleReadout).
// Tests: TabForge.Avalonia.Tests.ScalePanelHostTests, StatusBarScaleTests.
internal sealed class StatusBarScaleHost : ReadoutScaleHost
{
    private const string OptionsTitle = "OPTIONS";
    private const string PreferText = "Prefer common scales when it is close", KeepText = "Keep my choice for this section", MusicalText = "Use musical key names (E♭ instead of D#)", ShowText = "Show on the fretboard (scale highlight)";
    public const string Footnote = "Your pick sticks to this section and track; Auto returns to detection.";

    public StatusBarScaleHost(StatusScaleReadout readout, ScaleHostLinks links) : base(readout, links) { }

    public override void Pick(ScaleMatch scale) => Readout.PickFor(scale, keep: Readout.KeepChoice, fromFretboard: false);

    public override IReadOnlyList<ScaleExtraSection> Extras => new[] { new ScaleExtraSection(OptionsTitle, _ => Options()) };

    public override IReadOnlyList<ScaleGreyedSection> Greyed => new[] { FretboardLink(), PianoRollLink() };

    private Control Options()
    {
        var panel = new StackPanel();
        // One setting with the fretboard look's "Show the scale on the fretboard: On / Off".
        panel.Children.Add(ScalePanelParts.Check("ScaleShowOnFretboard", ShowText, Settings.ShowOnFretboard, v =>
        {
            Settings.ShowOnFretboard = v;
            if (!v && Readout.Editing is { } editing) editing.ScaleHighlight = null;
            SettingChanged();
        }));
        panel.Children.Add(ScalePanelParts.Check("ScaleKeepChoice", KeepText, Readout.KeepChoice, v => Readout.KeepChoice = v));
        panel.Children.Add(ScalePanelParts.Check("ScaleMusicalNames", MusicalText, Settings.MusicalKeyNames, v => { Settings.MusicalKeyNames = v; SettingChanged(); }));
        panel.Children.Add(ScalePanelParts.Check("ScalePreferCommon", PreferText, Settings.PreferCommon, v => { Settings.PreferCommon = v; SettingChanged(); }));
        var note = new TextBlock { Name = "ScaleOptionsFootnote", Text = Footnote, TextWrapping = global::Avalonia.Media.TextWrapping.Wrap };
        note[!TextBlock.ForegroundProperty] = new global::Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("TextMuted");
        panel.Children.Add(note);
        return panel;
    }
}
