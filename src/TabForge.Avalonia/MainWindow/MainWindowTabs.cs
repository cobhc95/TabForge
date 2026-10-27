using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.Fx;
using TabForge.Documents;
using TabForge.Presenters;
using TabForge.Services;

using TabForge.Avalonia.Playback;

namespace TabForge.Avalonia;

// Owns: the Avalonia window's open songs, which one is shown, and new / switch / close / detach (a tab moving to another window) tab with the save-changes question
//   (SavePromptPresenter) for an unsaved song; the same question for every unsaved song when the window closes, where Save saves first.
// Does not own: saving (MainWindowSaving, reached through the save function), the strip drawing (TabStripView), the question window
//   (SavePromptWindow, reached through the ask function).
// Tests: TabForge.Avalonia.Tests.MainWindowLifetimeTests, MainWindowSaveTests.
/// <summary>The window's document tabs. UI thread only.</summary>
public sealed class MainWindowTabs : IDisposable
{
    private const string LightPaper = "Light";
    private readonly List<SongDocument> _songs = new();
    private readonly Func<SavePrompt, Task<PromptButton>> _ask;
    private readonly Func<SongDocument, Task<bool>> _save;
    private readonly Func<Task>? _whenSaved;
    private readonly TabStripView? _strip;
    private readonly double _stripWidth;
    private AppSettings _settings;
    private readonly TabForge.Avalonia.Transport.AvaloniaPlaybackSessions? _playback;

    /// <param name="ask">Shows a save-changes question and returns the button pressed.</param>
    /// <param name="save">Saves a song (Save, or Save As when it has no file); true when it was written and is clean.</param>
    /// <param name="whenSaved">Completes when no save is running; closing waits on it, so the unsaved question sees the song as the save leaves it. Null: nothing saves in the background.</param>
    /// <param name="strip">The tab strip to redraw after every change; null in tests.</param>
    /// <param name="settings">The tab look, the last-tab rule, the settings a new tab follows (its position, the notation and track grouping of new songs); null uses the defaults.</param>
    public MainWindowTabs(SongDocument first, Func<SavePrompt, Task<PromptButton>> ask, Func<SongDocument, Task<bool>> save, TabStripView? strip = null, double stripWidth = 1320, AppSettings? settings = null, Action<string>? report = null, TabForge.Avalonia.Transport.AvaloniaPlaybackSessions? playback = null, TabForge.Audio.WaveformService? waveforms = null, Func<Task>? whenSaved = null)
    {
        Waveforms = waveforms ?? new TabForge.Audio.WaveformService();
        _ask = ask;
        _save = save;
        _whenSaved = whenSaved;
        _strip = strip;
        _stripWidth = stripWidth;
        _playback = playback;
        if (playback is not null) playback.AnyStateChanged += Redraw;
        _settings = settings ?? SettingsDefaults.Fallback();
        IconSizing.Apply(_settings.Appearance);
        UiMotion.Configure(_settings.Appearance.ReduceAnimations, _settings.Appearance.AnimationSpeed);
        if (strip is not null)
        {
            strip.TabPressed += Switch;
            strip.NewTabRequested += New;
            strip.DuplicateRequested += index => Duplicate(index);
            // A strip is only given with a status report (the app passes both); the check runs when a close is requested.
            strip.CloseRequested += index => { Switch(index); Platform.UiTasks.Forget(CloseActive(), "Closing the tab", report ?? throw new InvalidOperationException("A tab strip needs a status report.")); };
        }
        Add(first, 0);
        Redraw();
    }

    public IReadOnlyList<SongDocument> Songs => _songs;

    /// <summary>The waveform reader the window's tabs draw with; a closing tab's requests are forgotten here.</summary>
    public TabForge.Audio.WaveformService Waveforms { get; }

    /// <summary>True while this window is a tab drag's new window: it shows a song the source window still lists, so only the source drives the loop and transport for it.</summary>
    public bool IsTearCopy { get; private set; }

    /// <summary>Marks this window as a drag's copy (true) or, when the drag ends with the song staying here, as the owner (false: the shown song is applied again).</summary>
    public void SetTearCopy(bool value)
    {
        if (IsTearCopy == value) return;
        IsTearCopy = value;
        if (!value) ActiveChanged?.Invoke();
    }
    /// <summary>True when Open replaces the shown tab (the open-in-current-tab setting).</summary>
    public bool OpenReplacesActive => _settings.Tabs.OpenInCurrentTab;
    public int ActiveIndex { get; private set; }
    public SongDocument Active => _songs[ActiveIndex];
    public bool HasUnsaved => _songs.Any(s => s.HasUnsavedChanges);

    /// <summary>A new empty song, clean (as the app's File > New).</summary>
    public static SongDocument NewSong()
    {
        var song = new SongDocument(() => null) { Project = SongDocument.BlankProject() };
        song.MarkClean();
        return song;
    }

    /// <summary>Raised when the shown song changes: a new, opened, switched-to or remaining tab. Not raised by edits to the shown song.</summary>
    public event Action? ActiveChanged;

    /// <summary>Raised when the last tab closes and the last-tab setting says the window closes; the window then closes itself.</summary>
    public event Action? CloseWindowRequested;

    /// <summary>Raised with a song just before its tab leaves the window (closed or replaced); its playback and its FX chain windows go with it.</summary>
    public event Action<SongDocument>? SongClosing;

    private void Released(SongDocument song)
    {
        SongClosing?.Invoke(song);
        _playback?.Close(song);
        Waveforms.Forget(song.Media);   // reading stops and the outlines leave the memory budget
    }

    /// <summary>Takes new settings (the tab look, the last-tab rule, new-song defaults) and redraws the strip with them.</summary>
    public void ApplySettings(AppSettings settings)
    {
        _settings = settings;
        IconSizing.Apply(settings.Appearance);   // Icon size: read once per Apply
        UiMotion.Configure(settings.Appearance.ReduceAnimations, settings.Appearance.AnimationSpeed);   // the one shared motion policy: tab, button and timeline animations read it
        // A song still on the default paper follows the theme: switching to the light theme turns its score page light (and back).
        foreach (var song in _songs.Where(s => s.PaperIsDefault)) song.ApplyDefaultPaper(DefaultPaperIsDark);
        Redraw();
    }

    /// <summary>Names how the window currently paints (true = light): the paper a song on the default paper follows unless the user chose a score paper in Settings.</summary>
    public Func<bool>? EffectiveThemeIsLight { get; set; }

    /// <summary>Gives every song still on the default paper the paper of the effective theme again (the theme changed by any route: startup, Settings, the OS).</summary>
    public void RefreshDefaultPaper()
    {
        foreach (var song in _songs.Where(s => s.PaperIsDefault)) song.ApplyDefaultPaper(DefaultPaperIsDark);
    }

    // The paper the user named in Settings when they chose one; otherwise the effective theme's paper (Light, Dark, or System = the OS).
    private bool DefaultPaperIsDark => _settings.Appearance.IsOverridden(nameof(AppearanceSettings.ScorePaper)) || EffectiveThemeIsLight is null
        ? !string.Equals(_settings.Appearance.ScorePaper, LightPaper, StringComparison.OrdinalIgnoreCase)
        : !EffectiveThemeIsLight();

    /// <summary>Opens an empty song where the new-tab position setting puts it (after the shown tab, or at the end) and shows it.</summary>
    public void New()
    {
        var song = NewDefaultSong();
        ShowAt(song, DocumentRoutingDecision.NewTabIndex(_settings.Tabs.NewTabPosition == NewTabPositions.AtEnd, _songs.Count, ActiveIndex));
    }

    private SongDocument NewDefaultSong()
    {
        var song = NewSong();
        NewSongDefaults.Apply(song, _settings);
        return song;
    }

    /// <summary>Adds <paramref name="song"/> after the shown one and shows it.</summary>
    public void Open(SongDocument song) => ShowAt(song, ActiveIndex + 1);

    /// <summary>Adds <paramref name="song"/> at tab <paramref name="index"/> (clamped to the end) and shows it.</summary>
    public void InsertAt(SongDocument song, int index) => ShowAt(song, Math.Clamp(index, 0, _songs.Count));

    /// <summary>Adds an unsaved copy of the tab at <paramref name="index"/> after it and shows the copy (Tab.Duplicate, the tab menu and middle-click).</summary>
    public SongDocument Duplicate(int index)
    {
        if (index < 0 || index >= _songs.Count) throw new ArgumentOutOfRangeException(nameof(index));
        var copy = SongDuplicate.Copy(_songs[index], () => null);
        ShowAt(copy, index + 1);
        return copy;
    }

    /// <summary>Moves the tab at <paramref name="from"/> to <paramref name="to"/> in the tab order; the shown song stays shown.</summary>
    public void Reorder(int from, int to)
    {
        var shown = Active;
        var song = _songs[from];
        _songs.RemoveAt(from);
        _songs.Insert(to, song);
        ActiveIndex = _songs.IndexOf(shown);
        Redraw();
    }

    /// <summary>Puts <paramref name="song"/> in place of the shown tab after the save question; false when the person kept the shown song.</summary>
    public async Task<bool> ReplaceActive(SongDocument song)
    {
        var replaced = Active;
        if (!await MayDiscard(replaced, SavePromptPresenter.SaveChanges(replaced.Project.Title))) return false;
        Add(song, ActiveIndex);
        Remove(replaced);
        Released(replaced);
        ActiveIndex = _songs.IndexOf(song);
        Redraw();
        ActiveChanged?.Invoke();
        return true;
    }

    private void ShowAt(SongDocument song, int index)
    {
        // Adding a tab never touches the other tabs' playback, whatever the switch preference says: only Switch applies it.
        Add(song, index);
        ActiveIndex = index;
        Redraw();
        ActiveChanged?.Invoke();
    }

    public void Switch(int index)
    {
        if (index < 0 || index >= _songs.Count) throw new ArgumentOutOfRangeException(nameof(index));
        if (index == ActiveIndex) return;
        TabPlaybackSwitchPolicy.Apply(_playback, _songs, _songs[index], _settings.Tabs.PlaybackOnTabSwitch);
        ActiveIndex = index;
        Redraw();
        ActiveChanged?.Invoke();
    }

    /// <summary>
    /// Closes the shown tab after the save question; false when the person kept it. When the last tab closes, the last-tab setting decides:
    /// the window closes, or a new empty tab replaces it.
    /// </summary>
    public async Task<bool> CloseActive()
    {
        var closing = Active;
        if (!await MayDiscard(closing, SavePromptPresenter.SaveChanges(closing.Project.Title))) return false;
        Remove(closing);
        Released(closing);
        if (_songs.Count == 0)
        {
            if (_settings.Tabs.LastTabClosed == LastTabActions.CloseWindow)
            {
                CloseWindowRequested?.Invoke();
                return true;
            }
            Add(NewDefaultSong(), 0);
        }
        ActiveIndex = Math.Min(ActiveIndex, _songs.Count - 1);
        Redraw();
        ActiveChanged?.Invoke();
        return true;
    }

    /// <summary>Closes the given tabs (the tab menu's close others and close to the right), asking about each unsaved one; the shown tab stays
    /// shown when it is kept, otherwise the nearest tab takes its place.</summary>
    public async Task CloseMany(IReadOnlyList<SongDocument> closing)
    {
        var shown = Active;
        foreach (var song in closing)
        {
            if (!await MayDiscard(song, SavePromptPresenter.SaveChanges(song.Project.Title))) continue;
            Remove(song);
            Released(song);
        }
        var shownIndex = _songs.IndexOf(shown);
        ActiveIndex = shownIndex >= 0 ? shownIndex : Math.Clamp(ActiveIndex, 0, _songs.Count - 1);
        Redraw();
        ActiveChanged?.Invoke();
    }

    /// <summary>Takes the song at <paramref name="index"/> out with no save question, because it moves to another window. With songs left
    /// the nearest one is shown; with none left nothing is shown and the caller closes the window.</summary>
    public SongDocument Detach(int index)
    {
        if (index < 0 || index >= _songs.Count) throw new ArgumentOutOfRangeException(nameof(index));
        var song = _songs[index];
        Remove(song);
        if (_songs.Count == 0) return song;
        if (index < ActiveIndex || ActiveIndex >= _songs.Count) ActiveIndex--;
        Redraw();
        ActiveChanged?.Invoke();
        return song;
    }

    /// <summary>True while a save or export is running (a window with nothing unsaved still waits for it before it closes).</summary>
    public bool IsSaving => _whenSaved?.Invoke().IsCompleted == false;

    // Bounded: a save claim that never ends must not keep a window or tab from closing; after the wait the question goes on.
    private async Task Settled()
    {
        if (_whenSaved?.Invoke() is not { IsCompleted: false } pending) return;
        try { await pending.WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (TimeoutException) { }   // swallow: the bounded wait ended, close goes on
    }

    /// <summary>Asks about each unsaved song while the window closes; false as soon as one is kept or fails to save.</summary>
    public async Task<bool> MayCloseWindow()
    {
        await Settled();
        var unsaved = _songs.Where(s => s.HasUnsavedChanges).ToList();
        foreach (var song in unsaved)
            if (!await MayDiscard(song, SavePromptPresenter.SaveChangesOnWindowClose(song.Project.Title, song == Active, unsaved.Count))) return false;
        return true;
    }

    private async Task<bool> MayDiscard(SongDocument song, SavePrompt prompt)
    {
        await Settled();
        if (!song.HasUnsavedChanges) return true;
        if (!_settings.General.ConfirmOnClose) return true;   // the person's choice: no question (autosave and recovery copies still exist)
        return SavePromptPresenter.Answer(await _ask(prompt)) switch
        {
            DiscardAnswer.Close => true,
            DiscardAnswer.SaveFirst => await _save(song),   // a cancelled or failed save keeps the song open
            _ => false,
        };
    }

    private void Add(SongDocument song, int index)
    {
        // The score page is per song and session-only: a song joining a window takes the preferences default once (a song moved from
        // another window keeps the paper it already has).
        if (!song.PaperSet) song.ApplyDefaultPaper(DefaultPaperIsDark);
        FxDefaultChains.Apply(_settings.Plugins, song);
        _songs.Insert(index, song);
        song.DisplayStateChanged += OnDisplayStateChanged;
    }

    private void Remove(SongDocument song)
    {
        song.DisplayStateChanged -= OnDisplayStateChanged;
        _songs.Remove(song);
    }

    private void OnDisplayStateChanged(object? sender, EventArgs e) => Redraw();

    /// <summary>Releases the playback state subscription; the window calls it on close.</summary>
    public void Dispose()
    {
        if (_playback is not null) _playback.AnyStateChanged -= Redraw;
    }

    private void Redraw()
    {
        if (_strip is null) return;
        _strip.Settings = _settings.Tabs;
        _strip.IsVisible = _settings.Appearance.ShowTabStrip;
        _strip.Show(_songs.Select(s => new TabStripDocument(s.LabelPath, s.Project.Title, s.HasUnsavedChanges, _playback?.Existing(s) is { IsPlaying: true, IsPaused: false }, s.Path is not null)).ToList(), ActiveIndex, _stripWidth);
    }
}
