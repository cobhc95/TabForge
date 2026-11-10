using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Platform.Controls;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Presenters;
using TabForge.Platform.Theme;
using TabForge.Services;
using Avalonia.Automation;

namespace TabForge.Avalonia.Views;

/// <summary>
/// The "Stretch Chorus by 4 bars?" prompt: a muted line (where the section and the next one end up), four clickable radio cards (the backward prompt has a fifth, add a new named section second, with name and colour fields inside it) (add empty bars, default; repeat the section's last bars; add a repeat sign,
/// disabled with a reason line when a repeat is in the way; stop at the next section), "Remember my choice" with its hint and Cancel / Stretch. Enter stretches, Esc cancels.
/// <see cref="Answer"/> is null on cancel.
/// Owns: the window's controls and reading them back. Does not own: the words (SectionStretchPromptPresenter), the edit (SectionStretch) or the stored setting.
/// Tests: TabForge.Avalonia.Tests.SectionStretchTests.
/// </summary>
public sealed class SectionStretchPromptWindow : Window
{
    private static SectionStretchMode[] ForwardModes => new[] { SectionStretchMode.AddEmpty, SectionStretchMode.RepeatLast, SectionStretchMode.AddRepeatSign, SectionStretchMode.Stop };
    private static SectionStretchBackMode[] BackModes => new[] { SectionStretchBackMode.AddEmpty, SectionStretchBackMode.NewSection, SectionStretchBackMode.RepeatFirst, SectionStretchBackMode.TakeFromPrevious, SectionStretchBackMode.Stop };
    private readonly List<RadioCard> _cards = new();
    private readonly CheckBox _remember;
    private int _choice = -1;

    /// <summary>The answer the window closed with; null on Cancel / Esc / close (or for the backward prompt).</summary>
    public SectionStretchAnswer? Answer => _choice < 0 || _back ? null : new SectionStretchAnswer(ForwardModes[_choice], _remember.IsChecked == true);

    /// <summary>The backward prompt's answer; null on Cancel / Esc / close (or for the forward prompt).</summary>
    public SectionStretchBackAnswer? BackAnswer => _choice < 0 || !_back ? null : BackAnswerOf(BackModes[_choice]);

    private readonly bool _back;
    private TextBox? _sectionName;
    private ColourButton? _sectionColour;
    private Control? _sectionFields;

    private SectionStretchBackAnswer BackAnswerOf(SectionStretchBackMode mode) => mode == SectionStretchBackMode.NewSection
        ? new SectionStretchBackAnswer(mode, false, _sectionName?.Text, _sectionColour?.Value)
        : new SectionStretchBackAnswer(mode, _remember.IsChecked == true);

    public SectionStretchPromptWindow(SectionStretchQuestion question) : this(false, SectionStretchPromptPresenter.Title(question), SectionStretchPromptPresenter.Body(question), new (string, string, string, string?)[]
    {
        ("Add", SectionStretchPromptPresenter.AddTitle(question), SectionStretchPromptPresenter.AddHint, null),
        ("Repeat", SectionStretchPromptPresenter.RepeatTitle(question), SectionStretchPromptPresenter.RepeatHint(question), null),
        ("Sign", SectionStretchPromptPresenter.SignTitle, SectionStretchPromptPresenter.SignHint(question), question.SignConflict),
        ("Stop", SectionStretchPromptPresenter.StopTitle(question), SectionStretchPromptPresenter.StopHint, null),
    }) { }

    public SectionStretchPromptWindow(SectionStretchBackQuestion question) : this(true, SectionStretchBackPromptPresenter.Title(question), SectionStretchBackPromptPresenter.Body(question), new (string, string, string, string?)[]
    {
        ("Add", SectionStretchBackPromptPresenter.AddTitle(question), SectionStretchBackPromptPresenter.AddHint, null),
        ("NewSection", SectionStretchBackPromptPresenter.NewSectionTitle(question), SectionStretchBackPromptPresenter.NewSectionHint(question), null),
        ("Repeat", SectionStretchBackPromptPresenter.RepeatTitle(question), SectionStretchBackPromptPresenter.RepeatHint(question), null),
        ("Take", SectionStretchBackPromptPresenter.TakeTitle(question), SectionStretchBackPromptPresenter.TakeHint(question), SectionStretchBackPromptPresenter.TakeConflict(question)),
        ("Stop", SectionStretchBackPromptPresenter.StopTitle(question), SectionStretchBackPromptPresenter.StopHint, null),
    }) { }

    // The shared chrome: the muted line, one radio card per option (the first selected; a card with a reason is disabled and the reason shown under it), Remember and Cancel / Stretch.
    private SectionStretchPromptWindow(bool back, string title, string text, IReadOnlyList<(string Name, string Title, string Hint, string? Reason)> options)
    {
        _back = back;
        Title = title;
        Width = DialogMetrics.SectionAudioWidth;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        DialogMetrics.CentreOnOwner(this);
        ShowInTaskbar = false;
        this[!BackgroundProperty] = new DynamicResourceExtension("PanelBackground");
        this[!ForegroundProperty] = new DynamicResourceExtension("TextPrimary");
        FontSize = DialogMetrics.FontSize;

        var body = DialogMetrics.Inset(new StackPanel());
        var message = new TextBlock { Name = "MessageText", Text = text, TextWrapping = TextWrapping.Wrap }.WithMargin(DialogMetrics.SectionAudioQuestionGap);
        message[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextMuted");
        body.Children.Add(message);
        var group = new RadioCardGroup("SectionStretchChoice");
        foreach (var option in options)
        {
            var card = new RadioCard(group, option.Name, option.Title, option.Hint, selected: _cards.Count == 0, preview: option.Name == "NewSection" ? NewSectionFields() : null);
            _cards.Add(card);
            body.Children.Add(card);
            if (option.Reason is not { } reason) continue;
            card.IsEnabled = false;
            var why = new TextBlock { Name = option.Name + "Reason", Text = reason, TextWrapping = TextWrapping.Wrap, FontSize = DialogMetrics.MediaSmallText }.WithMargin(DialogMetrics.SectionAudioHintIndent);
            why[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextMuted");
            body.Children.Add(why);
        }

        _remember = new CheckBox { Name = "RememberBox", Content = SectionStretchPromptPresenter.RememberText };
        var hint = new TextBlock { Name = "RememberHint", Text = SectionStretchPromptPresenter.RememberHint, FontSize = DialogMetrics.MediaSmallText, VerticalAlignment = VerticalAlignment.Center };
        hint[!ForegroundProperty] = new DynamicResourceExtension("TextMuted");
        body.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { _remember, hint } }.WithMargin(DialogMetrics.SectionAudioRememberGap));

        var cancel = new Button { Name = "CancelButton", Content = SectionStretchPromptPresenter.CancelText, MinWidth = DialogMetrics.PromptButtonMinWidth, IsCancel = true };
        ToolTip.SetTip(cancel, SectionStretchPromptPresenter.CancelToolTip);
        cancel.Click += (_, _) => Close();
        var ok = new Button { Name = "OkButton", Content = SectionStretchPromptPresenter.StretchText, MinWidth = DialogMetrics.PromptButtonMinWidth, IsDefault = true };
        ok.Classes.Add(AvaloniaDefaultLooks.AccentLook);
        ToolTip.SetTip(ok, SectionStretchPromptPresenter.StretchToolTip);
        ok.Click += (_, _) => Accept();
        body.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { cancel, ok } }.WithMargin(DialogMetrics.FooterGap));
        Content = body;
        if (_sectionName is not null)
        {
            // The name and colour show only while the new-section card is selected; the button then says what it does, and Remember does not apply (a name and colour are asked each time).
            void Sync()
            {
                var isNew = _cards.Find(card => card.Name == "NewSectionCard")?.IsSelected == true;
                _sectionFields!.IsVisible = isNew;
                ok.Content = isNew ? SectionStretchBackPromptPresenter.NewSectionButton : SectionStretchPromptPresenter.StretchText;
                _remember.IsEnabled = !isNew;
            }
            foreach (var card in _cards) card.Radio.IsCheckedChanged += (_, _) => Sync();
            Sync();
            _cards.Find(card => card.Name == "NewSectionCard")!.Radio.IsCheckedChanged += (_, _) => { if (_cards.Find(card => card.Name == "NewSectionCard")!.IsSelected) _sectionName.Focus(); };
        }

        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Enter) Accept();
            else if (e.Key == Key.Escape) Close();
            else return;
            e.Handled = true;
        }, RoutingStrategies.Tunnel);
        Opened += (_, _) => _cards[0].Focus();
    }

    // The new-section card's name box and colour button, in one row under its hint.
    private Control NewSectionFields()
    {
        _sectionName = new TextBox { Name = "NewSectionName", Text = SectionStretchBackPromptPresenter.NewSectionName, Width = DialogMetrics.ColourSettingWidth, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(_sectionName, "New section name");
        _sectionColour = new ColourButton(NamedColourSwatches.SectionDefaultColour, new List<string>()) { Name = "NewSectionColour", Width = DialogMetrics.ColourSettingWidth, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(_sectionColour, "New section colour");
        _sectionColour.Show(NamedColourSwatches.SectionDefaultColour);
        return _sectionFields = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _sectionName, _sectionColour } };
    }

    /// <summary>Shows the prompt over <paramref name="owner"/>; null when the user cancelled.</summary>
    public static async Task<SectionStretchAnswer?> AskAsync(Window? owner, SectionStretchQuestion question) =>
        await DialogLauncher.AskFrom<SectionStretchPromptWindow, SectionStretchAnswer?>(owner, () => new SectionStretchPromptWindow(question), window => window.Answer);

    /// <summary>Shows the backward prompt over <paramref name="owner"/>; null when the user cancelled.</summary>
    public static async Task<SectionStretchBackAnswer?> AskAsync(Window? owner, SectionStretchBackQuestion question) =>
        await DialogLauncher.AskFrom<SectionStretchPromptWindow, SectionStretchBackAnswer?>(owner, () => new SectionStretchPromptWindow(question), window => window.BackAnswer);

    private void Accept()
    {
        // A disabled card (a repeat or a too-short previous section in the way) left selected by the arrow keys counts as the default.
        var chosen = _cards.FindIndex(card => card.IsSelected);
        _choice = chosen < 0 || !_cards[chosen].IsEnabled ? 0 : chosen;
        Close();
    }
}
