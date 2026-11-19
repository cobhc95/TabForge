using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Platform.Controls;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Models;
using TabForge.Platform.Theme;
using TabForge.Services;
namespace TabForge.Avalonia.Features.LeftStrip;

public sealed partial class FocusTrackSettingsWindow
{
    /// <summary>One amount: three modes as a segmented choice, a slider and the value.</summary>
    private sealed class RuleEditor
    {
        private readonly Button[] _segments;
        private readonly Slider _slider;
        private readonly TextBlock _value = new() { VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeight.SemiBold, TextAlignment = TextAlignment.Right };
        private readonly bool _lower;
        private FocusBoostMode _mode;
        private bool _loading;

        /// <param name="lower">A lowering, not a raise: its percentage tops out at 100, its sample amounts are smaller, its value reads with a minus.</param>
        public RuleEditor(bool lower = false)
        {
            _lower = lower;
            _slider = CreateModernSlider();
            _segments = Modes.Select(m => CreateSegmentButton(m.Label)).ToArray();
            var strip = new UniformGrid { Rows = 1 };
            for (var i = 0; i < Modes.Count; i++)
            {
                var mode = Modes[i].Mode;
                _segments[i].Click += (_, _) => { _mode = mode; Sync(resetAmount: true); Changed?.Invoke(); };
                strip.Children.Add(_segments[i]);
            }
            var segBox = new Border
            {
                Child = strip,
                CornerRadius = new CornerRadius(ChromeMetrics.RadiusXLarge),
                BorderThickness = DialogMetrics.HairlineAll,
            }.WithPadding(DialogMetrics.FocusStripPad);
            segBox[!Border.BackgroundProperty] = new DynamicResourceExtension("WindowBackground");
            segBox[!Border.BorderBrushProperty] = new DynamicResourceExtension("BorderSoftBrush");

            var valueBadge = new Border
            {
                Child = _value,
                MinWidth = ChromeMetrics.FocusValueMinWidth,
                CornerRadius = new CornerRadius(ChromeMetrics.RadiusLarge),
                BorderThickness = DialogMetrics.HairlineAll,
                VerticalAlignment = VerticalAlignment.Center,
            }.WithPadding(DialogMetrics.FocusValuePad).WithMargin(DialogMetrics.FocusValueGap);
            valueBadge[!Border.BackgroundProperty] = new DynamicResourceExtension("WindowBackground");
            valueBadge[!Border.BorderBrushProperty] = new DynamicResourceExtension("BorderSoftBrush");

            _slider.ValueChanged += (_, _) => { if (_loading) return; _value.Text = Text(); Changed?.Invoke(); };
            var amount = new DockPanel { LastChildFill = true }.WithMargin(DialogMetrics.FocusSectionTop);
            DockPanel.SetDock(valueBadge, Dock.Right);
            amount.Children.Add(valueBadge);
            amount.Children.Add(_slider);
            Root = new StackPanel { Spacing = Tight, Children = { segBox, amount } };
        }

        private static Button CreateSegmentButton(string label) => new Button
        {
            Content = label,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            CornerRadius = new CornerRadius(ChromeMetrics.RadiusLarge),
            MinHeight = ChromeMetrics.FocusSegmentMinHeight,
            FontSize = ChromeMetrics.TextSmall,
            Template = new FuncControlTemplate<Button>((btn, scope) =>
            {
                var presenter = new ContentPresenter
                {
                    Name = "PART_ContentPresenter",
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                presenter.Bind(ContentPresenter.ContentProperty, btn.GetObservable(ContentControl.ContentProperty), BindingPriority.Template);
                presenter.Bind(ContentPresenter.ContentTemplateProperty, btn.GetObservable(ContentControl.ContentTemplateProperty), BindingPriority.Template);
                presenter.Bind(ContentPresenter.ForegroundProperty, btn.GetObservable(TemplatedControl.ForegroundProperty), BindingPriority.Template);
                presenter.RegisterInNameScope(scope);

                var pill = new Border
                {
                    Child = presenter,
                    CornerRadius = btn.CornerRadius,
                };
                pill.Bind(Decorator.PaddingProperty, btn.GetObservable(TemplatedControl.PaddingProperty), BindingPriority.Template);
                pill.Bind(Border.BackgroundProperty, btn.GetObservable(TemplatedControl.BackgroundProperty), BindingPriority.Template);
                return pill;
            }),
        }.WithPadding(DialogMetrics.FocusSegmentPad);

        private static Slider CreateModernSlider() => new()
        {
            Minimum = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
            Height = ChromeMetrics.FocusSliderHeight,
            MinHeight = ChromeMetrics.FocusSliderHeight,
            Background = AvaloniaPalette.TransparentBrush,
            Template = new FuncControlTemplate<Slider>((slider, scope) =>
            {
                var less = new RepeatButton
                {
                    Name = "PART_DecreaseButton",
                    Focusable = false,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Template = new FuncControlTemplate<RepeatButton>((_, _) =>
                    {
                        var fill = new Border { Height = ChromeMetrics.FocusSliderTrack, CornerRadius = new CornerRadius(ChromeMetrics.RadiusSmall, 0, 0, ChromeMetrics.RadiusSmall), VerticalAlignment = VerticalAlignment.Center };
                        fill[!Border.BackgroundProperty] = new DynamicResourceExtension("AccentBrush");
                        return fill;
                    }),
                };
                var more = new RepeatButton
                {
                    Name = "PART_IncreaseButton",
                    Focusable = false,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Template = new FuncControlTemplate<RepeatButton>((_, _) =>
                    {
                        var rest = new Border { Height = ChromeMetrics.FocusSliderTrack, CornerRadius = new CornerRadius(0, ChromeMetrics.RadiusSmall, ChromeMetrics.RadiusSmall, 0), VerticalAlignment = VerticalAlignment.Center };
                        rest[!Border.BackgroundProperty] = new DynamicResourceExtension("BorderSoftBrush");
                        return rest;
                    }),
                };
                var thumb = new Thumb
                {
                    Name = "thumb",
                    Width = ChromeMetrics.FocusSliderThumb,
                    Height = ChromeMetrics.FocusSliderThumb,
                    Template = new FuncControlTemplate<Thumb>((_, _) =>
                    {
                        var dot = new Border
                        {
                            Width = ChromeMetrics.FocusSliderThumb,
                            Height = ChromeMetrics.FocusSliderThumb,
                            CornerRadius = new CornerRadius(ChromeMetrics.FocusSliderThumb / 2),
                            BorderThickness = DialogMetrics.FocusThumbRing,
                        };
                        dot[!Border.BackgroundProperty] = new DynamicResourceExtension("AccentBrush");
                        dot[!Border.BorderBrushProperty] = new DynamicResourceExtension("PanelBackground");
                        return dot;
                    }),
                };
                var track = new Track
                {
                    Name = "PART_Track",
                    Orientation = Orientation.Horizontal,
                    Thumb = thumb,
                    DecreaseButton = less,
                    IncreaseButton = more,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                track.Bind(RangeBase.MinimumProperty, slider.GetObservable(RangeBase.MinimumProperty), BindingPriority.Template);
                track.Bind(RangeBase.MaximumProperty, slider.GetObservable(RangeBase.MaximumProperty), BindingPriority.Template);
                track.Bind(RangeBase.ValueProperty, new Binding { Path = nameof(RangeBase.Value), Source = slider, Mode = BindingMode.TwoWay });
                track.RegisterInNameScope(scope);
                thumb.RegisterInNameScope(scope);
                less.RegisterInNameScope(scope);
                more.RegisterInNameScope(scope);
                return new Grid { Height = ChromeMetrics.FocusSliderHeight, Background = AvaloniaPalette.TransparentBrush, Children = { track } };
            }),
        };

        public Control Root { get; }

        /// <summary>Raised when the mode or the amount changed by the user.</summary>
        public event Action? Changed;

        public void Show(FocusBoost rule)
        {
            _mode = rule.Mode;
            _loading = true;
            Sync(resetAmount: false);
            _slider.Value = _lower ? rule.ClampedLowerAmount : rule.ClampedAmount;
            _loading = false;
            _value.Text = Text();
        }

        public FocusBoost Read() => new(_mode, Math.Clamp(_slider.Value, 0, MaxOf(_mode)));

        private double MaxOf(FocusBoostMode mode) => _lower && mode == FocusBoostMode.Percent ? 100 : Modes.First(m => m.Mode == mode).Max;

        private string Text() => _lower ? LowerSummary(Read()) : Summary(Read());

        // The segments show the chosen mode; the slider takes the mode's range (and, on a change of mode by the user, its usual starting amount).
        private void Sync(bool resetAmount)
        {
            var wasLoading = _loading;
            _loading = true;
            var info = Modes.First(m => m.Mode == _mode);
            _slider.Maximum = MaxOf(_mode);
            _slider.SmallChange = info.Step;
            _slider.TickFrequency = info.Step;
            _slider.IsSnapToTickEnabled = true;
            if (resetAmount) _slider.Value = _lower
                ? _mode switch { FocusBoostMode.Decibels => 3, FocusBoostMode.Percent => 30, _ => FocusBoost.MaxLevel * 0.6 }
                : _mode switch { FocusBoostMode.Decibels => 6, FocusBoostMode.Percent => 50, _ => FocusBoost.MaxLevel * 0.8 };
            for (var i = 0; i < _segments.Length; i++)
            {
                var on = Modes[i].Mode == _mode;
                if (on)
                {
                    _segments[i][!Button.BackgroundProperty] = new DynamicResourceExtension("AccentBrush");
                    _segments[i][!Button.ForegroundProperty] = new DynamicResourceExtension("OnAccentBrush");
                }
                else
                {
                    _segments[i].Background = AvaloniaPalette.TransparentBrush;
                    _segments[i][!Button.ForegroundProperty] = new DynamicResourceExtension("TextMuted");
                }
                _segments[i].FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal;
            }
            _loading = wasLoading;
            _value.Text = Text();
        }
    }

    /// <summary>A group's row: its name and what happens to its other tracks; "Lowered by…" and "Raised by…" open an amount.</summary>
    private sealed class GroupRow
    {
        private readonly ComboBox _choice = new() { ItemsSource = Choices.Select(c => c.Label).ToList(), HorizontalAlignment = HorizontalAlignment.Right };
        private readonly RuleEditor _lowerEditor = new(lower: true);
        private readonly RuleEditor _raiseEditor = new();
        private readonly Action _changed;
        private bool _loading;

        public GroupRow(string group, Action changed)
        {
            Group = group;
            _changed = changed;
            var name = new TextBlock { Text = group, VerticalAlignment = VerticalAlignment.Center };
            var head = new DockPanel { LastChildFill = false };
            DockPanel.SetDock(_choice, Dock.Right);
            head.Children.Add(_choice);
            head.Children.Add(name);
            var headBox = DialogMetrics.Padded(new Border { Child = head }, Gap, Tight);
            var amounts = new Border
            {
                BorderThickness = DialogMetrics.FocusAmountRule,
                Child = new StackPanel { Children = { _lowerEditor.Root, _raiseEditor.Root } },
            }.WithPadding(DialogMetrics.FocusAmountPad).WithMargin(DialogMetrics.FocusAmountMargin);
            amounts[!Border.BorderBrushProperty] = new DynamicResourceExtension("AccentBrush");
            _choice.SelectionChanged += (_, _) => { Sync(); if (!_loading) _changed(); };
            _lowerEditor.Changed += () => { if (!_loading) _changed(); };
            _raiseEditor.Changed += () => { if (!_loading) _changed(); };
            Root = new StackPanel { Children = { headBox, amounts } };
            Sync();
        }

        public string Group { get; }

        public Control Root { get; }

        private FocusOtherMode Mode => Choices[Math.Max(0, _choice.SelectedIndex)].Mode;

        public FocusOther Read() => Mode switch
        {
            FocusOtherMode.Lower => new FocusOther(Mode, _lowerEditor.Read()),
            FocusOtherMode.Raise => new FocusOther(Mode, _raiseEditor.Read()),
            _ => new FocusOther(Mode),
        };

        public void Show(FocusOther other)
        {
            _loading = true;
            _choice.SelectedIndex = Choices.ToList().FindIndex(c => c.Mode == other.Mode);
            var amount = other.Amount ?? new FocusBoost(FocusBoostMode.Decibels, 3);
            _lowerEditor.Show(amount);
            _raiseEditor.Show(amount);
            Sync();
            _loading = false;
        }

        private void Sync()
        {
            var hasCustom = Mode is FocusOtherMode.Lower or FocusOtherMode.Raise;
            _lowerEditor.Root.IsVisible = Mode == FocusOtherMode.Lower;
            _raiseEditor.Root.IsVisible = Mode == FocusOtherMode.Raise;
            if (Root is StackPanel sp && sp.Children.Count > 1)
                sp.Children[1].IsVisible = hasCustom;
        }
    }

    private sealed class MixBar
    {
        private const double BarHeight = ChromeMetrics.FocusMixBarHeight;
        private readonly Border _fill;
        private readonly TextBlock _value;

        public MixBar(string label, bool isAccent)
        {
            _fill = new Border
            {
                CornerRadius = new CornerRadius(ChromeMetrics.RadiusSmall),
                VerticalAlignment = VerticalAlignment.Bottom,
                Height = BarHeight,
            };
            _fill[!Border.BackgroundProperty] = new DynamicResourceExtension(isAccent ? "AccentBrush" : "TextMuted");

            var track = new Border
            {
                Height = BarHeight + 4,
                CornerRadius = new CornerRadius(ChromeMetrics.RadiusSmall + 2),
                BorderThickness = DialogMetrics.HairlineAll,
                Child = _fill,
            }.WithPadding(DialogMetrics.FocusMixTrackPad);
            track[!Border.BackgroundProperty] = new DynamicResourceExtension("WindowBackground");
            track[!Border.BorderBrushProperty] = new DynamicResourceExtension("BorderSoftBrush");

            var name = new TextBlock
            {
                Text = label,
                FontSize = ChromeMetrics.TextSmall,
                FontWeight = isAccent ? FontWeight.SemiBold : FontWeight.Normal,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            name[!TextBlock.ForegroundProperty] = new DynamicResourceExtension(isAccent ? "AccentBrush" : "TextPrimary");

            _value = new TextBlock
            {
                Text = "100",
                FontSize = ChromeMetrics.TextSmall,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            _value.UseMutedText();

            Root = new StackPanel
            {
                Spacing = 3,
                Children = { track, name, _value },
            }.WithMargin(DialogMetrics.FocusMixColumnGap);
        }

        public Control Root { get; }

        public void SetLevel(int level)
        {
            _value.Text = level.ToString(CultureInfo.InvariantCulture);
            _fill.Height = Math.Max(3, BarHeight * MidiLimits.Fraction(level));
        }
    }
}
