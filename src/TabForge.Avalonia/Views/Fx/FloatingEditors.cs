using System;
using System.Collections.Generic;
using System.Linq;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Plugins;

namespace TabForge.Avalonia.Views.Fx;

// Owns: the plug-ins whose editor floats in its own window, per track, and the song each belongs to: hiding them with their song's tab, showing them again, closing them
//   with the song, the plug-in or the track. They outlive the FX window that opened them, so nothing about them lives in the window or its controller.
// Does not own: the native windows (the engine), the FX windows (FxChainWindows drives this registry on tab switch, song close and chain edits).
// Tests: TabForge.Avalonia.Tests.FloatingEditorsTests.
// Threads: UI thread only.
public static class FloatingEditors
{
    /// <summary>The floating plug-ins of one track.</summary>
    public sealed class Set
    {
        private readonly HashSet<PluginSlot> _slots = new(ReferenceEqualityComparer.Instance);
        public Set(TrackModel track) => Track = track;
        public TrackModel Track { get; }
        /// <summary>The song the track belongs to (null: a track of no song, never hidden).</summary>
        public SongDocument? Document { get; private set; }
        /// <summary>The engine that holds the windows.</summary>
        public IFxChainEngine? Engine { get; private set; }
        public bool OnTop { get; private set; }
        /// <summary>The windows are closed because the song's tab is not shown; they open again with it.</summary>
        public bool Hidden { get; set; }
        public int Count => _slots.Count;
        public IReadOnlyCollection<PluginSlot> Slots => _slots;
        public bool Contains(PluginSlot slot) => _slots.Contains(slot);

        public void Add(PluginSlot slot, SongDocument? document, IFxChainEngine? engine, bool onTop)
        {
            if (!ReferenceEquals(Document, document)) Document?.ViewAttachments.Remove(this);
            (Document, Engine, OnTop) = (document, engine, onTop);
            if (_slots.Add(slot) && document is not null && !document.ViewAttachments.Contains(this)) document.ViewAttachments.Add(this);
        }

        public bool Remove(PluginSlot slot)
        {
            var removed = _slots.Remove(slot);
            if (_slots.Count == 0) Clear();
            return removed;
        }

        public void Clear() { _slots.Clear(); Document?.ViewAttachments.Remove(this); Hidden = false; }
    }

    public static Set Of(TrackModel track) => track.ViewAttachment as Set ?? (Set)(track.ViewAttachment = new Set(track));

    private static List<Set> OfSong(SongDocument song) => song.ViewAttachments.OfType<Set>().ToList();

    /// <summary>Closes the windows of the song's floating plug-ins and forgets them (the song's tab closes).</summary>
    public static void CloseSong(SongDocument song)
    {
        foreach (var set in OfSong(song)) CloseAll(set);
    }

    /// <summary>Closes the windows of the plug-ins that left their track, and of tracks that left the song.</summary>
    public static void Prune(SongDocument song)
    {
        foreach (var set in OfSong(song))
        {
            if (set.Track.IsBus) continue;
            if (!song.Project.Tracks.Contains(set.Track)) { CloseAll(set); continue; }
            foreach (var slot in set.Slots.Where(s => !set.Track.Rig.Plugins.Contains(s)).ToList())
            {
                if (!set.Hidden) set.Engine?.CloseEditor(set.Track, slot);
                set.Remove(slot);
            }
        }
    }

    /// <summary>The songs of <paramref name="held"/> other than <paramref name="shown"/> hide their floating windows; the shown song's hidden ones open again over <paramref name="owner"/>.</summary>
    public static void SongShown(IEnumerable<SongDocument> held, SongDocument shown, IntPtr owner, bool dark)
    {
        foreach (var song in held)
            foreach (var set in OfSong(song))
                if (!ReferenceEquals(song, shown)) Hide(set);
                else Show(set, owner, dark);
    }

    private static void Hide(Set set)
    {
        if (set.Hidden || set.Engine is not { } engine) return;
        foreach (var slot in set.Slots) engine.CloseEditor(set.Track, slot);
        set.Hidden = true;
    }

    private static void Show(Set set, IntPtr owner, bool dark)
    {
        if (!set.Hidden || set.Engine is not { } engine) return;
        var failed = false;
        foreach (var slot in set.Slots.ToList())
            if (!engine.OpenEditor(set.Track, slot, owner, dark, docked: false, onTop: set.OnTop)) failed = true;   // the engine has not taken the song yet: a later call retries
        set.Hidden = failed;
    }

    private static void CloseAll(Set set)
    {
        if (!set.Hidden) foreach (var slot in set.Slots.ToList()) set.Engine?.CloseEditor(set.Track, slot);
        set.Clear();
    }
}
