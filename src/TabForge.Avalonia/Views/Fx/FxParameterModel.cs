using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TabForge.Audio.Contracts;

namespace TabForge.Avalonia.Views.Fx;

// Owns: the rules of the generic plug-in parameter panel with no UI in them: the search filter, the value text with its units, how many rows show, the values the
//   panel holds (a poll never moves the one being dragged), and the undo and redo history of the user's changes.
// Does not own: the controls (FxParameterPanel), the polling and the engine calls (FxParameterPane), the parameters themselves (the plug-in, through the engine).
// Tests: TabForge.Avalonia.Tests.FxParameterTests.
// Threads: UI thread only.
public sealed class FxParameterModel
{
    /// <summary>How many rows show at first and each time "Show more" is pressed (a plug-in may list over a thousand parameters).</summary>
    public const int PageSize = 100;
    /// <summary>The most changes the undo history keeps.</summary>
    public const int MaxHistory = 200;

    private readonly Dictionary<uint, double> _values = new();
    private readonly List<(uint Id, double Before, double After)> _undo = new();
    private readonly List<(uint Id, double Before, double After)> _redo = new();
    private IReadOnlyList<PluginParameterInfo> _all = Array.Empty<PluginParameterInfo>();
    private string _search = "";
    private int _shown = PageSize;

    /// <summary>Every parameter the plug-in listed, in its order.</summary>
    public IReadOnlyList<PluginParameterInfo> All => _all;
    /// <summary>The search text (case does not matter; a parameter matches when its name or module contains every word).</summary>
    public string Search { get => _search; set { if (_search == value) return; _search = value; _shown = PageSize; } }
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    /// <summary>Forgets everything (another plug-in is shown): the list, the values, the search and the history.</summary>
    public void Reset()
    {
        _all = Array.Empty<PluginParameterInfo>();
        _values.Clear();
        _undo.Clear();
        _redo.Clear();
        _search = "";
        _shown = PageSize;
    }

    /// <summary>The parameters that match the search, in the plug-in's order.</summary>
    public IReadOnlyList<PluginParameterInfo> Matches()
    {
        var words = _search.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return words.Length == 0 ? _all : _all.Where(p => words.All(w => p.Name.Contains(w, StringComparison.OrdinalIgnoreCase) || p.Module.Contains(w, StringComparison.OrdinalIgnoreCase))).ToList();
    }

    /// <summary>The rows to show now: the first <see cref="PageSize"/> matches, more after <see cref="ShowMore"/>.</summary>
    public IReadOnlyList<PluginParameterInfo> Visible() => Matches() is var m && m.Count <= _shown ? m : m.Take(_shown).ToList();

    /// <summary>How many matches are not shown yet.</summary>
    public int Hidden => Math.Max(0, Matches().Count - _shown);

    public void ShowMore() => _shown += PageSize;

    /// <summary>Takes the plug-in's list. True when the set of parameters changed (the rows are built again); otherwise only the values moved.
    /// A value in <paramref name="held"/> (the one being dragged) keeps what the panel has.</summary>
    public bool Load(IReadOnlyList<PluginParameterInfo> list, uint? held = null)
    {
        var same = list.Count == _all.Count && list.Select(p => p.Id).SequenceEqual(_all.Select(p => p.Id));
        _all = list;
        if (!same) { _values.Clear(); _undo.Clear(); _redo.Clear(); _shown = PageSize; }
        foreach (var p in list)
            if (held != p.Id || !_values.ContainsKey(p.Id)) _values[p.Id] = p.Value;
        return !same;
    }

    /// <summary>The value the panel holds for a parameter (the plug-in's last reading, or the user's change).</summary>
    public double ValueOf(PluginParameterInfo p) => _values.TryGetValue(p.Id, out var v) ? v : p.Value;

    /// <summary>The parameter changed live (a drag): only the held value moves, no history.</summary>
    public void Hold(uint id, double value) => _values[id] = value;

    /// <summary>One finished change of the user (a drag ended, a key press): it goes to the undo history and the redo history is dropped. False when nothing changed.</summary>
    public bool Commit(uint id, double before, double after)
    {
        _values[id] = after;
        if (before.Equals(after)) return false;
        _undo.Add((id, before, after));
        if (_undo.Count > MaxHistory) _undo.RemoveAt(0);
        _redo.Clear();
        return true;
    }

    /// <summary>Takes back the last change: the parameter and the value to set, or null.</summary>
    public (uint Id, double Value)? Undo()
    {
        if (_undo.Count == 0) return null;
        var step = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _redo.Add(step);
        _values[step.Id] = step.Before;
        return (step.Id, step.Before);
    }

    /// <summary>Does the last undone change again: the parameter and the value to set, or null.</summary>
    public (uint Id, double Value)? Redo()
    {
        if (_redo.Count == 0) return null;
        var step = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        _undo.Add(step);
        _values[step.Id] = step.After;
        return (step.Id, step.After);
    }

    /// <summary>The value to show for <paramref name="value"/>: the plug-in's own text with its units while the value is what it last read, else the number with those units.</summary>
    public static string ValueText(PluginParameterInfo p, double value)
    {
        if (p.Text.Length > 0 && value.Equals(p.Value)) return p.Text;
        var number = p.Stepped ? Math.Round(value).ToString("0", CultureInfo.CurrentCulture) : value.ToString(Magnitude(p) < 10 ? "0.###" : "0.##", CultureInfo.CurrentCulture);
        var units = Units(p.Text);
        return units.Length == 0 ? number : $"{number} {units}";
    }

    /// <summary>The units in a plug-in's value text: what follows the number ("-6.0 dB" gives "dB"); empty when there is none.</summary>
    public static string Units(string text)
    {
        var i = 0;
        var t = text.Trim();
        while (i < t.Length && (char.IsDigit(t[i]) || t[i] is '-' or '+' or '.' or ',' or '−')) i++;
        return i == 0 ? "" : t[i..].Trim();
    }

    private static double Magnitude(PluginParameterInfo p) => Math.Max(Math.Abs(p.Min), Math.Abs(p.Max));

    /// <summary>The slider's value for a keyboard or wheel step: one unit for a stepped parameter, else a hundredth of the range.</summary>
    public static double StepOf(PluginParameterInfo p) => p.Stepped ? 1 : Math.Max((p.Max - p.Min) / 100, double.Epsilon);
}
