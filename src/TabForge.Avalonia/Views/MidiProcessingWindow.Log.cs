using System;
using System.Collections.Generic;
using TabForge.Audio;
using TabForge.Services;

namespace TabForge.Avalonia.Views;

// Owns: the MIDI log half of the MIDI processing window (MidiProcessingWindow): watching the engine log while a log
//   processor is on or Learn listens, decoding lines, and stopping the watch on close.
// Does not own: the layout, the list wiring, or the parameter controls.
// Tests: TabForge.Avalonia.Tests.MidiProcessingWindowTests.
public sealed partial class MidiProcessingWindow
{
    private const int MaxLogLines = 300, RawInputStage = 250;

    private void UpdateWatch()
    {
        _logPanel.IsVisible = HasLog;
        var want = HasLog || _learn.Active;
        if (want == _watching) return;
        _watching = want;
        _host.Engine?.WatchMidiLog(_track, want);
    }

    private void OnLog(int engineSlot, IReadOnlyList<MidiLogLine> lines)
    {
        if (!_watching || engineSlot != _host.Engine?.SlotOf(_track)) return;
        foreach (var l in lines)
        {
            if (l.Stage == RawInputStage)   // raw input the chain saw: only used by Learn
            {
                if (_learn.Active && (l.Status & 0xF0) == 0x90 && l.Data2 > 0) _learn.Sink?.Invoke(l.Data1);
                continue;
            }
            if (HasLog) _logLines.Add(Describe(l));
        }
        if (!HasLog) return;
        if (_logLines.Count > MaxLogLines) _logLines.RemoveRange(0, _logLines.Count - MaxLogLines);
        _log.Text = string.Join('\n', _logLines);
        _log.CaretIndex = _log.Text.Length;
    }

    /// <summary>One log line: song time, channel, decoded message, and the position of the log processor that saw it.</summary>
    public static string Describe(MidiLogLine l)
    {
        var kind = l.Status & 0xF0; var ch = (l.Status & 0x0F) + 1;
        string note(int n) => $"{MusicTheoryService.NoteName(n)} ({n})";
        var what = l.Status >= 0xF0 ? $"System {l.Status:X2} {l.Data1} {l.Data2}" : kind switch
        {
            0x90 when l.Data2 > 0 => $"ch{ch,-2} Note on   {note(l.Data1)} vel {l.Data2}",
            0x90 or 0x80 => $"ch{ch,-2} Note off  {note(l.Data1)}",
            0xA0 => $"ch{ch,-2} Poly pressure {note(l.Data1)} {l.Data2}",
            0xB0 => $"ch{ch,-2} CC {l.Data1} = {l.Data2}",
            0xC0 => $"ch{ch,-2} Program {l.Data1}",
            0xD0 => $"ch{ch,-2} Channel pressure {l.Data1}",
            0xE0 => $"ch{ch,-2} Pitch bend {(l.Data2 << 7 | l.Data1) - 8192}",
            _ => $"{l.Status:X2} {l.Data1} {l.Data2}",
        };
        return $"{l.Time,8:0.000}s  {what}   @{l.Stage + 1}";
    }

    private void OnClosed()
    {
        _searchDebounce.Stop();
        try { if (_watching) _host.Engine?.WatchMidiLog(_track, false); }
        finally
        {
            if (_host.Engine is { } engine) { engine.MidiLogReceived -= OnLog; engine.ChainLoaded -= UpdateWatch; }
        }
    }
}
