using System.Windows;
using System.Windows.Media;
using TabForge.Models;

namespace TabForge.Views.Score;

/// <summary>The page metrics, appearance and cursor state shared by the score drawing and the playback overlay.</summary>
internal interface IScorePageHost
{
    TrackModel? Track { get; }
    NotationMode Notation { get; }
    ScoreAppearance Appearance { get; }
    double StaffGap { get; }
    double StringGap { get; }
    double StaffTop(int system);
    double TabTop(int system);
    bool HideCursor { get; }
    int SelectedMeasure { get; }
    int SlotsFor(int measure);
    ScoreLayoutEngine Layout { get; }
    double Zoom { get; }

    /// <summary>Drops the retained system drawings and repaints.</summary>
    void RepaintAll();
}

/// <summary>What the score drawing reads from the editor that owns it.</summary>
internal interface IScoreRenderHost : IScorePageHost
{
    SongProject? Project { get; }

    // ---- page metrics ----
    double FretFontSize { get; }
    double GridLeft { get; }
    double GridWidth { get; }
    double HeaderCentreX { get; }

    // ---- cursor, selection and hover ----
    int SelectedCell { get; }
    int SelectedString { get; }
    bool HasSelection { get; }
    (int m1, int c1, int m2, int c2) SelectionRange();
    int HoverMeasure { get; }
    int HoverCell { get; }
    int ActiveVoiceIndex { get; }

    // ---- playback ----
    bool PlaybackActive { get; }
    int PlaybackMeasure { get; }
    int PlaybackCell { get; }
    double PlaybackFraction { get; }
    HashSet<(int bar, int cell, int s)> SoundingNotes { get; }
    HashSet<(int bar, int cell, int s)> StruckNotes { get; }
    (Rect Rect, Brush Brush)? PlayingBarBand(TrackModel track, ScoreSystemPosition system, ScoreMeasurePosition position);

    // ---- one-line mode ----
    bool InHorizontalBand(ScoreMeasurePosition measure);
}
