using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia;
using TabForge.Avalonia.Platform.Controls;
using TabForge.Avalonia.Transport;
using TabForge.Avalonia.Views;
using TabForge.Platform;
using TabForge.Platform.Theme;
using TabForge.Menus;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia.Platform;

/// <summary>
/// The transport bar of the WPF app: the live input button, the playback group (rewind to beginning, stop, record, play / pause, next section, a
/// divider, then count-in, metronome and loop), then the timeline zoom pair, the global tuning fork, the snap magnet, the mixer button and, at the
/// far right, the master volume knob. Each button is drawn through IDrawContext in the WPF playbar colours, inside the bar's rounded panel. The preview has no playback
/// engine, so the playback and live input buttons are disabled (drawn with the transport look's Disabled opacity) and their tooltip says why; the
/// toggles show unlit. The tuning fork and the snap magnet work without an engine.
/// Owns: layout and drawing of the transport buttons, their tooltips, the recording state the Record button shows and the snap settings the magnet holds.
/// Does not own: playback, the song, the engine, the recording itself (the window's MainWindowTransportRecord sets the state).
/// The drawn button and divider controls are the partial file TransportBarView.TransportButton.cs.
/// Tests: TabForge.Avalonia.Tests.TransportBarViewTests.
/// </summary>
public sealed partial class TransportBarView : Border
{
    public const string NotAvailable = "Playback is not available in the preview";

    // The square transport buttons (34 DIP) and the stacked timeline zoom pair (26 x 17 DIP each), sized as the WPF playbar sizes them.
    public const double TransportButtonSize = UiMetrics.TransportButtonSize;

    /// <summary>The smallest button size the transport bar shrinks to so its groups fit one row; below it the bar wraps.</summary>
    public const double MinButtonSize = 26;

    /// <summary>How much a button shrinks each step while the groups do not fit one row.</summary>
    private const double ButtonShrinkStep = 1;

    /// <summary>The zoom pair's buttons: WPF's 26 x 17 DIP, stacked flush (no vertical margin) and 1 DIP apart sideways.</summary>
    private const double ZoomButtonWidth = 26;
    private const double ZoomButtonHeight = 17;
    private static readonly Thickness ZoomButtonMargin = new(1, 0);

    /// <summary>The bar's margin outside its border: 1 DIP from the list's left edge and 5 DIP from the splitter; no vertical margin, so one row is the ruler's height.</summary>
    private static readonly Thickness BarMargin = new(1, 0, 5, 0);

    /// <summary>The bar's padding inside its border: 1 DIP, so one row of 34 DIP buttons (with their 1 DIP margins) and the border is the ruler's 40 DIP.</summary>
    private static readonly Thickness BarPadding = new(1);

    // The master volume knob at the far right after the mixer button: the WPF knob's label, a 0-100 % range, sized to the 34 DIP buttons so the bar keeps its height.
    public const string MasterVolumeLabel = "Master volume";
    private const double MasterVolumeKnobSize = 34;
    private const double MasterVolumeKnobLeftMargin = 4;
    private const double MasterVolumeKnobRightMargin = 6;

    // The lit toggle's fill and accent border (the loop colours of the WPF playbar); the loop button takes the fill; the snap magnet keeps its plain fill and takes only the accent border.
    private const string PlayItemName = "Play or pause";
    // The soft grey hover shade of the theme (the same key the menus and lists hover with).
    private const string HoverFillKey = HoverShade.IconPaletteKey;
    private const string LoopItemName = "Loop enable";
    private const string RecordItemName = "Record";

    /// <summary>One button: automation name, WPF tooltip, command id for the key text (or null), icon (null: the record dot), fill and border palette keys.</summary>
    public sealed record Item(string Name, string ToolTip, string? CommandId, string? Icon, string Fill, string Border, bool Toggle);

    /// <summary>The playback buttons in WPF order; the divider sits before the first toggle.</summary>
    public static readonly IReadOnlyList<Item> Items = new Item[]
    {
        new("Rewind to beginning", "Rewind to beginning", null, "rewind_to_beginning", "PlaybarRewind", "PlaybarRewindBorder", false),
        new("Stop playback", "Stop playback", "Transport.Stop", "stop", "PlaybarStop", "PlaybarStopBorder", false),
        new(RecordItemName, "Record every armed track (the red button on a track row) while the song plays; press again to stop", "Transport.Record", null, "PlaybarStop", "PlaybarStopBorder", false),
        new("Play or pause", "Play / pause", "Transport.PlayPause", "play", "PlaybarPlay", "PlaybarPlayBorder", false),
        new("Next section", "Next section", NextSectionId, "next_section", "PlaybarNext", "PlaybarNextBorder", false),
        new("Click and beat count-in", "Count-in · click to toggle, right-click for count-in settings", MainWindowTransportToggles.CountInId, "click_beat", "PlaybarSurface", "PlaybarBorder", true),
        new("Metronome", "Metronome · click to toggle, right-click for metronome settings", MainWindowTransportToggles.MetronomeId, "metronome", "PlaybarSurface", "PlaybarBorder", true),
        new("Loop enable", "Loop · click to toggle, right-click for loop settings", "Transport.Loop", "loop", "PlaybarSurface", "PlaybarBorder", true),
    };

    /// <summary>The timeline zoom pair, top to bottom: zoom in, then zoom out (the WPF header zoom buttons).</summary>
    public static readonly IReadOnlyList<Item> ZoomItems = new Item[]
    {
        new("Zoom in timeline", "Zoom in timeline", MainWindowTimelineZoom.ZoomInId, "zoom_plus", "PlaybarSurface", "PlaybarBorder", false),
        new("Zoom out timeline", "Zoom out timeline", MainWindowTimelineZoom.ZoomOutId, "zoom_minus", "PlaybarSurface", "PlaybarBorder", false),
    };

    /// <summary>The mixer button after the tuning fork and the snap magnet (the WPF header mixer button).</summary>
    public static readonly Item MixerItem = new("Mixer", "Mixer: track and group levels, pan, pitch, sound source and FX chains", "View.Mixer", "mixer", "PlaybarSurface", "PlaybarBorder", false);

    /// <summary>The live input button, left of the playback group (the WPF left-most header button): a press runs Transport.LiveInput, a right press opens the input menu; lit while live input is on.</summary>
    public static readonly Item LiveInputItem = new("Live input", "Live input: hear your instrument through the live FX chain", "Transport.LiveInput", "mic", "PlaybarSurface", "PlaybarBorder", false);

    /// <summary>The global tuning fork after the zoom pair (the WPF tuning button); it opens the tuning window.</summary>
    public static readonly Item TuningItem = new("Global tuning", "Global tuning: click to open the tuning window · double-click to type a semitone shift · right-click for quick options (tune up or down a semitone, back to original)", null, "tuning_fork", "PlaybarSurface", "PlaybarBorder", false);

    /// <summary>The snap magnet after the tuning fork, lit while snapping is on (the WPF snap button); right-click opens the snap settings.</summary>
    public static readonly Item SnapItem = new("Snap", "Snapping: clips snap to the grid, other clips and the playhead. Hold Alt while dragging to bypass. Right-click for the snap settings.", "Timeline.Snap", "snap_magnet", "PlaybarSurface", "PlaybarBorder", true);

    /// <summary>The playback commands a bound session answers, by button name; the other buttons stay disabled.</summary>
    private static readonly IReadOnlyDictionary<string, Action<AvaloniaPlaybackSession>> WiredCommands = new Dictionary<string, Action<AvaloniaPlaybackSession>>
    {
        ["Rewind to beginning"] = session => session.Seek(0, 0),
        ["Stop playback"] = session => session.Stop(),
        ["Play or pause"] = session => session.PlayOrPause(),
    };

    // The command the Next section button runs: the score's Section.Next (the same id the Section and Bar menus use).
    private const string NextSectionId = "Section.Next";

    /// <summary>The window command each timeline zoom button, the mixer button, the Next section button the two toggles and Record run, by button name: the score's zoom rows, the Mixer command, Section.Next and the Transport toggles.</summary>
    private static readonly IReadOnlyDictionary<string, string> WindowCommandIds = new Dictionary<string, string>
    {
        [RecordItemName] = MainWindowTransportRecord.CommandId,
        [LiveInputItem.Name] = LiveInputItem.CommandId!,
        ["Zoom in timeline"] = MainWindowTimelineZoom.ZoomInId,
        ["Zoom out timeline"] = MainWindowTimelineZoom.ZoomOutId,
        ["Mixer"] = MixerItem.CommandId!,
        ["Next section"] = NextSectionId,
        ["Metronome"] = MainWindowTransportToggles.MetronomeId,
        ["Click and beat count-in"] = MainWindowTransportToggles.CountInId,
        [LoopItemName] = "Transport.Loop",   // the window's loop link: the score or timeline selection, the no-area scope and the status line
    };

    private readonly List<TransportButton> _buttons = new();

    /// <summary>Builds the live input button's right-click menu at each opening (the window's live input sets it); null: no menu.</summary>
    public Func<ContextMenu?>? LiveInputMenu { get; set; }

    /// <summary>Whether live input is on, read for the button's lit state (the window's live input sets it); null: never lit.</summary>
    public Func<bool>? LiveInputOn { get; set; }

    /// <summary>Repaints the lit toggles (live input turned on or off in any window).</summary>
    public void RefreshLiveInput() => RefreshLit();

    /// <summary>True while this bar's window is capturing: the Record button takes the red active state (WPF's REC pulse).</summary>
    public bool Recording { get; private set; }

    /// <summary>Shows or clears the Record button's recording state; called when a recording starts, is stopped or refused, and when a new bar is made.</summary>
    public void ShowRecording(bool recording)
    {
        if (Recording == recording) return;
        Recording = recording;
        RefreshLit();
    }

    private AvaloniaPlaybackSession? _session;

    /// <summary>The snap settings the snap magnet toggles and the snap settings window edits; the preview has no timeline, so the bar holds them.</summary>
    public SnapSettings Snap { get; } = new();

    /// <summary>The master volume knob (0-100 %) at the far right after the mixer button; no engine is bound to it in the preview yet.</summary>
    public KnobControl MasterVolumeKnob { get; }

    public TransportBarView()
    {
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        var keys = new HotkeySettings();
        var firstToggle = Items.First(item => item.Toggle);
        row.Children.Add(Group(BuildButton(LiveInputItem, keys, TransportButtonSize, TransportButtonSize)));
        row.Children.Add(Group(Items.TakeWhile(item => item != firstToggle).Select(item => (Control)BuildButton(item, keys, TransportButtonSize, TransportButtonSize)).ToArray()));
        row.Children.Add(Group(Items.SkipWhile(item => item != firstToggle).Select(item => (Control)BuildButton(item, keys, TransportButtonSize, TransportButtonSize))
            .Prepend(new Divider()).ToArray()));
        MasterVolumeKnob = new KnobControl
        {
            Minimum = 0, Maximum = 100, DefaultValue = 100, Origin = 0, Value = 100,
            Width = MasterVolumeKnobSize, Height = MasterVolumeKnobSize, Label = MasterVolumeLabel, Format = v => $"{v:0}%",
            Margin = new Thickness(MasterVolumeKnobLeftMargin, 0, MasterVolumeKnobRightMargin, 0), VerticalAlignment = VerticalAlignment.Center
        };
        global::Avalonia.Automation.AutomationProperties.SetName(MasterVolumeKnob, MasterVolumeLabel);
        row.Children.Add(Group(BuildLocalButton(TuningItem, keys, OpenTuningWindow, null), BuildLocalButton(SnapItem, keys, ToggleSnap, OpenSnapSettings),
            BuildButton(MixerItem, keys, TransportButtonSize, TransportButtonSize), MasterVolumeKnob));
        row.Children.Add(Group(BuildZoomPair(keys)));   // the zoom pair closes the row, after the master volume knob
        RefreshLit();
        Child = row;
        SizeChanged += (_, e) => FitButtonsToOneRow(row, e.NewSize.Width - BorderThickness.Left - BorderThickness.Right);
        // The spacing density scales only the sideways gaps (the bar keeps the ruler's height); the resource changes once per Apply.
        this.GetResourceObservable(UiDensityResources.Factor).Subscribe(new global::Avalonia.Reactive.AnonymousObserver<object?>(factor => ApplyGaps(row, factor is double f ? f : 1.0)));
        Name = "TransportControls";
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(6);
        Padding = BarPadding;
        Margin = BarMargin;
        HorizontalAlignment = HorizontalAlignment.Left;
        global::Avalonia.Automation.AutomationProperties.SetName(this, "Transport controls");
        this.FollowTheme(Paint);
        Paint();
    }

    /// <summary>
    /// Wires the playback buttons to a session: rewind, stop and play or pause are enabled and press their command; the buttons the window's
    /// commands run (record, next section, count-in, metronome, loop, zoom, mixer) keep their wiring, and the toggles light from the session.
    /// Null (no engine) puts the session buttons back to the preview state.
    /// </summary>
    public void Bind(AvaloniaPlaybackSession? session)
    {
        if (_session is not null) _session.StateChanged -= RefreshLit;
        _session = session;
        if (session is not null) session.StateChanged += RefreshLit;
        foreach (var button in _buttons)
        {
            if (button.Local) continue; // local buttons work without a session
            var command = session is null ? null : WiredCommands.GetValueOrDefault(button.Item.Name);
            var wired = session is not null && command is not null;
            button.IsEnabled = wired;
            button.Pressed = wired ? () =>
            {
                // A rewind is a user seek: while this window records it does nothing, as the timeline's bar click.
                if (Recording && button.Item.Name == "Rewind to beginning") return;
                command!(session!);
            } : null;
            ToolTip.SetTip(button, wired ? button.BaseTip : button.BaseTip + " (" + NotAvailable + ")");
        }
        RefreshLit();
    }

    /// <summary>
    /// Wires the timeline zoom pair and the mixer button to the window's commands: each press runs its command through the host, and the
    /// buttons work without a playback session. Null leaves them disabled. The host is read at press time, so the window may set its
    /// command runner after the bar is built.
    /// </summary>
    public void BindCommands(ContextMenuHost? contextMenus)
    {
        foreach (var button in _buttons)
        {
            if (!WindowCommandIds.TryGetValue(button.Item.Name, out var commandId)) continue;
            button.Local = true;
            button.IsEnabled = contextMenus is not null;
            // The metronome, count-in and loop buttons toggle on a left click and open their own settings popup above the button on a right click.
            var settingsId = button.Item.Name switch
            {
                "Metronome" => MainWindowTransportToggles.MetronomeSettingsId,
                "Click and beat count-in" => MainWindowTransportToggles.CountInSettingsId,
                LoopItemName => MainWindowTransportToggles.LoopSettingsId,
                _ => null,
            };
            if (settingsId is null)
            {
                button.Pressed = contextMenus is null ? null : () => RunIfHandled(contextMenus, commandId);
                button.SecondaryPressed = contextMenus is null ? null
                    : button.Item.Name == LiveInputItem.Name ? () => { if (LiveInputMenu?.Invoke() is { } menu) menu.Open(button); }
                    : null;
            }
            else
            {
                button.Pressed = contextMenus is null ? null : () => RunIfHandled(contextMenus, commandId);
                button.SecondaryPressed = contextMenus is null ? null : () => RunIfHandled(contextMenus, settingsId);
            }
            ToolTip.SetTip(button, contextMenus is null ? button.BaseTip + " (" + NotAvailable + ")" : button.BaseTip);
        }
    }

    private static void RunIfHandled(ContextMenuHost host, string commandId)
    {
        if (host.Commands.Handles(commandId)) host.Commands.Run(commandId);
    }

    private TransportButton BuildButton(Item item, HotkeySettings keys, double width, double height)
    {
        var text = item.CommandId is null ? item.ToolTip : HotkeyCatalog.TooltipWithKey(keys, item.ToolTip, item.CommandId);
        var button = new TransportButton(item, text, width, height) { IsEnabled = false };
        ToolTip.SetTip(button, text + " (" + NotAvailable + ")");
        ToolTip.SetShowOnDisabled(button, true);
        global::Avalonia.Automation.AutomationProperties.SetName(button, item.Name);
        _buttons.Add(button);
        return button;
    }

    private StackPanel BuildZoomPair(HotkeySettings keys)
    {
        var pair = new StackPanel { Orientation = Orientation.Vertical, VerticalAlignment = VerticalAlignment.Center };
        foreach (var item in ZoomItems)
        {
            var button = BuildButton(item, keys, ZoomButtonWidth, ZoomButtonHeight);
            button.Margin = ZoomButtonMargin;
            pair.Children.Add(button);
        }
        return pair;
    }

    // A run of controls that wraps as one unit: its members always share a row.
    private static StackPanel Group(params Control[] children)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        panel.Children.AddRange(children);
        return panel;
    }

    // Shrinks the buttons step by step, down to MinButtonSize, until the groups sit in one row of this width; the bar wraps only below that size.
    private void FitButtonsToOneRow(WrapPanel wrap, double width)
    {
        var size = TransportButtonSize;
        while (true)
        {
            ApplyButtonSize(size);
            wrap.Measure(new Size(width, double.PositiveInfinity));
            var tallestGroup = wrap.Children.Max(group => group.DesiredSize.Height);
            if (size <= MinButtonSize || TransportWrapRules.FitsOneRow(wrap.DesiredSize.Height, tallestGroup)) return;
            size = TransportWrapRules.NextSmallerSize(size, MinButtonSize, ButtonShrinkStep);
        }
    }

    // Sideways gaps at the density factor: the button, divider and knob margins and the bar's side padding. Comfortable (1.0) is today's exact layout.
    private void ApplyGaps(WrapPanel row, double factor)
    {
        foreach (var button in _buttons)
            if (!ZoomItems.Contains(button.Item)) button.WithMargin(factor, 1, factor, 1);
        foreach (var group in row.Children.OfType<Panel>())
            foreach (var child in group.Children)
            {
                if (child is Divider) child.WithMargin(3 * factor, 0, 3 * factor, 0);
                else if (ReferenceEquals(child, MasterVolumeKnob)) child.WithMargin(MasterVolumeKnobLeftMargin * factor, 0, MasterVolumeKnobRightMargin * factor, 0);
            }
        this.WithPadding(factor, BarPadding.Top);
    }

    // The zoom pair keeps its own size; every other button takes the given square size.
    private void ApplyButtonSize(double size)
    {
        foreach (var button in _buttons)
        {
            if (ZoomItems.Contains(button.Item)) continue;
            button.Width = size;
            button.Height = size;
        }
    }

    // A button that works in the preview without a session: enabled, its left press runs pressed and its right press secondary.
    private TransportButton BuildLocalButton(Item item, HotkeySettings keys, Action pressed, Action? secondary)
    {
        var button = BuildButton(item, keys, TransportButtonSize, TransportButtonSize);
        button.Local = true;
        button.IsEnabled = true;
        button.Pressed = pressed;
        button.SecondaryPressed = secondary;
        ToolTip.SetTip(button, button.BaseTip);
        return button;
    }

    /// <summary>Timeline.Snap (and the snap magnet button): turns clip snapping on or off.</summary>
    public void ToggleSnap()
    {
        Snap.Enabled = !Snap.Enabled;
        RefreshLit();
    }

    private void OpenSnapSettings() => new SnapSettingsWindow(Snap, RefreshLit).ShowDialog(OwnerWindow());

    /// <summary>The Global tuning button was clicked: the window that owns the song's tuning opens the tuning window and applies its choice.</summary>
    public event Action? GlobalTuningRequested;

    private void OpenTuningWindow() => GlobalTuningRequested?.Invoke();

    private Window OwnerWindow() => TopLevel.GetTopLevel(this) as Window ?? throw new InvalidOperationException("The transport bar is not in a window.");

    // The loop button is lit while the bound session loops and the snap magnet while snapping is on; the other buttons stay unlit.
    private void RefreshLit()
    {
        foreach (var button in _buttons)
        {
            var lit = button.Item.Name switch
            {
                LoopItemName => _session?.Loop == true,
                "Metronome" => _session?.Metronome == true,
                "Click and beat count-in" => _session?.CountIn == true,
                "Snap" => Snap.Enabled,
                "Live input" => LiveInputOn?.Invoke() == true,
                RecordItemName => Recording,
                _ => false,
            };
            var playing = button.Item.Name == PlayItemName && _session is { IsPlaying: true, IsPaused: false };
            if (button.Lit == lit && button.Playing == playing) continue;
            button.Lit = lit;
            button.Playing = playing;
            button.InvalidateVisual();
        }
    }

    private void Paint()
    {
        var light = ActualThemeVariant == ThemeVariant.Light;
        Background = new SolidColorBrush(AvaloniaPalette.ColorOf(TransportButton.Fill("PlaybarSurface", light)));
        BorderBrush = new SolidColorBrush(AvaloniaPalette.ColorOf(Palette.Get("PlaybarBorder", light)));
    }

}
