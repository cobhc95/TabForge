using System.Collections.Generic;
using Avalonia.Controls;
using TabForge.Audio.Contracts;
using TabForge.AudioEngine.Builtin.Delay;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Models;
using TabForge.Plugins;

namespace TabForge.Avalonia.Views.Fx;

// Owns: the delay window: sync to the song tempo (a note length from 1/1 to 1/32, plain, dotted or triplet) or a time in milliseconds, feedback, mix, ping-pong,
//   low cut and high cut on the repeats, and bypass. Only the time control that applies is shown.
// Does not own: the poll and the engine talk (BuiltinEffectPanel), the sound and the note lengths (DelayEffect, DelayDivision).
// Tests: BuiltinEditorWindowTests.
public sealed class DelayEditor : BuiltinEffectPanel
{
    private readonly ParamChoice _division;
    private readonly ParamSlider _time;

    public DelayEditor(IFxChainHost host, TrackModel track, PluginSlot slot)
        : base(host, track, slot, "Delay", DelayEffect.Descriptors, new Size2(DialogMetrics.DelayWidth, DialogMetrics.DelayHeight, DialogMetrics.DelayMinWidth, DialogMetrics.DelayMinHeight))
    {
        _division = Choice(DelayEffect.DivisionId, "Note length", "The echo time as a note length at the tempo of the song: 1/4 is a beat, a dot adds half, T is a triplet.", DelayDivision.Names);
        _time = Slider(DelayEffect.TimeId, "Time", "The echo time in milliseconds (used while Sync is off).", ParamScale.Log);
        Frame(new StackPanel
        {
            Children =
            {
                Switch(DelayEffect.SyncId, "Sync to tempo", "On: the time follows the tempo of the song. Off: the time is in milliseconds.").Spaced(DialogMetrics.FxRowGap),
                _division.Spaced(DialogMetrics.FxRowGap),
                _time.Spaced(DialogMetrics.FxRowGap),
                Slider(DelayEffect.FeedbackId, "Feedback", "How much of each echo is fed back to make the next one.").Spaced(DialogMetrics.FxRowGap),
                Slider(DelayEffect.MixId, "Mix", "Level of the echoes; the dry sound is not changed.").Spaced(DialogMetrics.FxRowGap),
                Slider(DelayEffect.LowCutId, "Low cut", "Takes the bass out of the echoes (left at the lowest value it does nothing).", ParamScale.Log, text: ParamSlider.Hertz).Spaced(DialogMetrics.FxRowGap),
                Slider(DelayEffect.HighCutId, "High cut", "Takes the brightness out of the echoes (left at the highest value it does nothing).", ParamScale.Log, text: ParamSlider.Hertz).Spaced(DialogMetrics.FxRowGap),
                Row(Switch(DelayEffect.PingPongId, "Ping-pong", "The echoes bounce between left and right."), Switch(DelayEffect.BypassId, "Bypass", "Hear the sound without the delay (a short fade, no click).")),
            },
        });
        ShowTimeControls(true);
    }

    private void ShowTimeControls(bool sync)
    {
        _division.IsVisible = sync;
        _time.IsVisible = !sync;
    }

    protected override void ShowValues(IReadOnlyDictionary<uint, PluginParameterInfo> byId) =>
        ShowTimeControls(!byId.TryGetValue(DelayEffect.SyncId, out var sync) || sync.Value >= 0.5);
}
