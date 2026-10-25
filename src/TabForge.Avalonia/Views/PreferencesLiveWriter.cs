using System.Collections.Generic;
using System.Linq;
using TabForge.Services;

namespace TabForge.Avalonia.Views;

// Owns: which edits of the Settings window the live settings have not taken yet, and writing them: just the edited rows of a scalar kind (switch, choice,
//   number, colour, text), else a full copy.
// Does not own: when the write runs (PreferencesWindow coalesces it to once per frame), the preview notice, or the rows (SettingsCatalog).
// Tests: TabForge.Avalonia.Tests.PreferencesLiveWriterTests.
// Threads: UI thread only. Keys: the edited rows' keys, ceiling the catalogue's rows.
internal sealed class PreferencesLiveWriter
{
    private readonly AppSettings _saved;
    private readonly List<string> _keys = new();
    private bool _all;
    // The catalogue rows bound to the live settings: built on the first per-row write (the section objects keep their identity through every copy).
    private Dictionary<string, SettingDescriptor>? _savedRows;

    internal PreferencesLiveWriter(AppSettings saved) => _saved = saved;

    /// <summary>True while an edit waits to be written.</summary>
    internal bool Pending => _all || _keys.Count > 0;

    /// <summary>Notes that <paramref name="row"/> was edited; a row that holds a list or a button action asks for a full copy.</summary>
    internal void Note(SettingDescriptor row)
    {
        if (row.Kind is SettingKind.FolderList or SettingKind.Button) _all = true;
        else { _keys.Remove(row.Key); _keys.Add(row.Key); }   // a re-edit moves to the end: the last edit wins, as in the working copy
    }

    /// <summary>Writes the noted edits from <paramref name="rows"/> (the rows of the working settings) into the live settings; a full copy of <paramref name="working"/> when one was asked for.</summary>
    internal void WritePending(AppSettings working, IReadOnlyList<SettingDescriptor> rows, bool includeWorkspace)
    {
        _savedRows ??= PreferencesCatalog.Build(_saved).ToDictionary(r => r.Key, System.StringComparer.OrdinalIgnoreCase);
        var edited = _keys.Select(key => rows.FirstOrDefault(r => r.Key == key)).ToList();   // in edit order: a theme pick after a colour edit wins, as in the working copy
        if (_all || includeWorkspace || edited.Any(r => r is null || !_savedRows.ContainsKey(r.Key))) { WriteAll(working, includeWorkspace); return; }
        foreach (var row in edited) _savedRows[row!.Key].Set(row.Get());
        _keys.Clear();
    }

    /// <summary>Copies all of <paramref name="source"/> into the live settings (the dock layout only when <paramref name="includeWorkspace"/>) and forgets the noted edits.</summary>
    internal void WriteAll(AppSettings source, bool includeWorkspace)
    {
        PreferencesCatalog.CopyInto(PreferencesCatalog.Copy(source), _saved, includeWorkspace);
        _keys.Clear();
        _all = false;
    }
}
