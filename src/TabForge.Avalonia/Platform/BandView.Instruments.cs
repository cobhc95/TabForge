using System;
using TabForge.Services;

namespace TabForge.Avalonia.Platform;

// Owns: the Band view's cards' fretboards: the score and fretboard menus, the instrument look and view, and the fretboard size, split from BandView.cs.
// Does not own: the cards or the view's layout and follow (BandView).
public sealed partial class BandView
{
    /// <summary>The cards' score right-click menu with the band's own score settings; set by the window, bound to every card made after and now.</summary>
    public BandScoreMenu? ScoreMenu
    {
        get => _scoreMenu;
        set
        {
            _scoreMenu = value;
            if (value is not null) foreach (var panel in _current.Panels.Values) value.Bind(panel);
        }
    }

    private BandScoreMenu? _scoreMenu;

    /// <summary>
    /// The window's main-mode fretboard menu, taken by every card's board: a right-click there opens the same rows as the fretboard pane, and
    /// "Show this track as" changes that card's own track. Set by the window; null leaves the cards without a menu.
    /// </summary>
    internal FretboardContextMenu? FretboardMenu
    {
        get => _fretboardMenu;
        set
        {
            if (ReferenceEquals(_fretboardMenu, value)) return;
            if (_fretboardMenu is not null && _attached) _fretboardMenu.TrackViewChanged -= ApplyInstrumentViews;
            _fretboardMenu = value;
            if (value is not null && _attached) value.TrackViewChanged += ApplyInstrumentViews;
            foreach (var panel in _current.Panels.Values)
            {
                BindInstrumentMenu(panel);
                ApplyInstrumentView(panel);
            }
        }
    }

    private FretboardContextMenu? _fretboardMenu;

    /// <summary>What the menu's "Find scale…" row runs (the window's scale finder); set by the window with the menu.</summary>
    internal Action? FindScale { get; set; }

    /// <summary>What every card's board draws (the Fretboard settings); set by the window and applied to the cards made now and later.</summary>
    internal FretboardSettingsBinding.Look InstrumentLook
    {
        get => _instrumentLook;
        set
        {
            _instrumentLook = value;
            foreach (var panel in _current.Panels.Values) ApplyInstrumentView(panel);
        }
    }

    private FretboardSettingsBinding.Look _instrumentLook = FretboardSettingsBinding.Look.From(new TabForge.Services.AppSettings());

    // Takes the fretboard menu on a card's board; the card names its own track in it.
    private void BindInstrumentMenu(BandTrackPanel panel)
    {
        if (_fretboardMenu is not { } menu || panel.TrackIndex >= _document.Project.Tracks.Count) return;
        menu.Attach(panel.Instrument, () => panel.Instrument.Kind, FindScale, () => _document.Project.Tracks[panel.TrackIndex]);
    }

    // Gives a card the board of its own track: the view the fretboard menu holds for that track, else the settings' default view.
    private void ApplyInstrumentView(BandTrackPanel panel)
    {
        if (panel.TrackIndex < 0 || panel.TrackIndex >= _document.Project.Tracks.Count) return;
        var track = _document.Project.Tracks[panel.TrackIndex];
        var view = _fretboardMenu is { } menu ? menu.ViewFor(track, _instrumentLook.InstrumentView) : _instrumentLook.InstrumentView;
        panel.SetInstrumentLook(_instrumentLook);
        panel.SetInstrumentKind(FretboardSettingsBinding.KindFor(track, view));
        panel.RefreshInstrumentCursor();
    }

    /// <summary>Re-gives every card the board its track's instrument now calls for (called when an instrument changes, live or on commit).</summary>
    internal void RefreshInstrumentViews() => ApplyInstrumentViews();

    private void ApplyInstrumentViews()
    {
        foreach (var panel in _current.Panels.Values) ApplyInstrumentView(panel);
    }

    /// <summary>The size the user gave the fretboards (saved with the band settings); set by the window. A view without a window keeps it for the session.</summary>
    internal BandFretboardSize FretboardSize
    {
        get => _fretboardSize;
        set
        {
            _fretboardSize = value;
            ApplyFretboardSize();
            ShareBars();
        }
    }

    private BandFretboardSize _fretboardSize;

    // Gives each card the user's fretboard size (the automatic one when the user chose none); the fit then sizes the automatic ones.
    private void ApplyFretboardSize()
    {
        foreach (var panel in _current.Panels.Values) panel.SetInstrumentSize(_fretboardSize.UserHeight(panel.InstrumentShape), panel.InstrumentAutoMaxHeight);
    }

    // The user dragged a fretboard's bottom edge: every fretboard takes the height, and the cards fit again (the staff zoom gives way, or grows back).
    private void ResizeFretboards(BandTrackPanel panel, double height)
    {
        _fretboardSize.Choose(height, panel.InstrumentShape);
        ApplyFretboardSize();
        FitCards();
        SlideCards();
    }
}
