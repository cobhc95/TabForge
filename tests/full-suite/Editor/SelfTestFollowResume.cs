using TabForge.Models;
using TabForge.Presets;

namespace TabForge;

public static partial class SelfTest
{
    /// <summary>
    /// Playback follow resumes after a hand scroll when the playhead jumps (loop wrap, seek) or the playing song is shown
    /// again; contiguous playback does not. Headless: a 48-bar score in a 900x320 viewport, Jump style.
    /// </summary>
    private static void TestFollowResumesAfterSeek()
    {
        var (scroll, editor, layout) = FollowScene();
        var playhead = 10;
        var follow = FollowFor(scroll, editor, () => playhead);
        follow.ResetForPlayback();
        follow.OnPlayheadBar(playhead);
        layout();

        HandScroll(scroll, follow, layout);
        Check("a manual scroll pauses following", !follow.IsFollowing);
        playhead = 11;
        Settle(60);
        follow.OnPlayheadBar(playhead);
        layout();
        Check("contiguous playback keeps a paused follow paused", !follow.IsFollowing);

        playhead = 0;
        Settle(60);
        follow.OnPlayheadBar(playhead);
        layout();
        Check("a loop wrap resumes following", follow.IsFollowing);
        Check("after a loop wrap the playhead bar is in view", BarInView(editor, scroll, playhead),
            $"systemTop={editor.SystemTopForMeasure(playhead):0} offset={scroll.VerticalOffset:0}");

        HandScroll(scroll, follow, layout);
        Check("a second manual scroll pauses following", !follow.IsFollowing);
        playhead = 30;
        Settle(60);
        follow.OnPlayheadBar(playhead);
        layout();
        Check("a forward seek resumes following", follow.IsFollowing);
        Check("after a seek the playhead bar is in view", BarInView(editor, scroll, playhead),
            $"systemTop={editor.SystemTopForMeasure(playhead):0} offset={scroll.VerticalOffset:0}");

        // A playing song shown again (tab switch back) re-arms following, as ReattachFollow does.
        HandScroll(scroll, follow, layout);
        Check("a manual scroll before a tab switch pauses following", !follow.IsFollowing);
        follow.ResetForPlayback();
        follow.OnPlayheadBar(playhead);
        layout();
        Check("showing the playing song again resumes following", follow.IsFollowing);
        Check("after showing the song again the playhead bar is in view", BarInView(editor, scroll, playhead));
    }

    /// <summary>
    /// A programmatic scroll the viewer clamps to its extent is not a hand scroll, even with a gesture flag stuck on (a scrollbar
    /// drag whose release was lost). A relayout during playback keeps following and the playhead in view.
    /// </summary>
    private static void TestFollowIgnoresClampedScroll()
    {
        // A four-bar score fits the viewport, so the extent allows no scrolling: the viewer clamps every programmatic
        // target to zero while the target itself lies past it.
        var (shortScroll, shortEditor, shortLayout) = FollowScene(bars: 4);
        var shortPlayhead = 3;
        var shortFollow = FollowFor(shortScroll, shortEditor, () => shortPlayhead, bars: 4, tune: s =>
        {
            s.StopAtEnd = false;
            s.VerticalTriggerPercent = 40;
        });
        shortLayout();                        // settles the initial extent before the scroll under test
        shortFollow.ResetForPlayback();
        shortFollow.SetScrollBarDrag(true);   // stuck gesture: every scroll now looks like the user's
        shortFollow.OnPlayheadBar(shortPlayhead);
        Settle(200);                          // the scroll's ScrollChanged arrives after the own-scroll settle window
        shortLayout();
        Check("a programmatic scroll clamped by the extent does not pause following", shortFollow.IsFollowing,
            $"offset={shortScroll.VerticalOffset:0} max={Math.Max(0, shortScroll.ExtentHeight - shortScroll.ViewportHeight):0}");
        shortFollow.SetScrollBarDrag(false);

        // A relayout during playback on the long score keeps following and the playhead in view.
        var (scroll, editor, layout) = FollowScene();
        var playhead = 20;
        var follow = FollowFor(scroll, editor, () => playhead);
        follow.ResetForPlayback();
        follow.OnPlayheadBar(playhead);
        layout();
        editor.InvalidateScoreLayout();
        editor.InvalidateMeasure();
        layout();
        playhead = 24;
        Settle(60);
        follow.OnPlayheadBar(playhead);
        layout();
        Check("a relayout during playback keeps following", follow.IsFollowing);
        Check("after a relayout during playback the playhead bar is in view", BarInView(editor, scroll, playhead),
            $"systemTop={editor.SystemTopForMeasure(playhead):0} offset={scroll.VerticalOffset:0}");
    }

    /// <summary>While playing, a track switch (relayout) puts the playing bar in view in one scroll step, with no glide.</summary>
    private static void TestTrackSwitchSnapsFollow()
    {
        var (scroll, editor, layout) = FollowScene();
        editor.Project!.Tracks.Add(new TrackModel { Name = "Bass", Measures = TemplateFactory.Measures(48) });
        var playhead = 40;
        var settings = new Services.FollowSettings { Mode = Services.FollowModes.Smooth };
        var follow = new Views.ScoreFollowCoordinator(scroll, editor, () => true, () => false, () => playhead, () => 0.0, () => 48, () => settings);
        follow.ApplySettings(settings);
        follow.ResetForPlayback();
        editor.SelectedTrackIndex = 1;
        layout();
        follow.OnPlayheadBar(playhead);
        follow.PumpForTest();
        layout();
        Check("after a track switch the playing bar is in view after one step", BarInView(editor, scroll, playhead),
            $"systemTop={editor.SystemTopForMeasure(playhead):0} offset={scroll.VerticalOffset:0}");
    }

    /// <summary>A vertical turn to a system far below the viewport lands within 250 ms at 30, 60 and 144 fps.</summary>
    private static void TestVerticalFollowGlideLands()
    {
        foreach (var fps in new[] { 30, 60, 144 })
        {
            double offset = 0;
            for (var t = 0.0; t < 0.25; t += 1.0 / fps) offset = Views.ScoreVerticalFollow.GlideStep(offset, 900, 1.0 / fps);
            Check($"vertical glide reaches a 900 px turn within 250 ms at {fps} fps", Math.Abs(900 - offset) < 4,
                $"offset={offset:0.0}");
        }
    }

    /// <summary>A score of <paramref name="bars"/> bars in a 900x320 viewport, laid out; returns the viewer, the editor and a layout pass.</summary>
    private static (System.Windows.Controls.ScrollViewer Scroll, Views.TabEditorControl Editor, Action Layout) FollowScene(int bars = 48)
    {
        var editor = new Views.TabEditorControl();
        var project = new SongProject { Tempo = 120 };
        project.Tracks.Add(new TrackModel { Name = "Guitar", Measures = TemplateFactory.Measures(bars) });
        editor.Project = project;
        editor.SelectedTrackIndex = 0;
        var scroll = new System.Windows.Controls.ScrollViewer
        {
            Width = 900, Height = 320, Content = editor,
            VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto
        };
        void Layout()
        {
            scroll.Measure(new System.Windows.Size(900, 320));
            scroll.Arrange(new System.Windows.Rect(0, 0, 900, 320));
            scroll.UpdateLayout();
        }
        Layout();
        return (scroll, editor, Layout);
    }

    /// <summary>A Jump-style follow for a playing score; <paramref name="tune"/> adjusts the settings of one test.</summary>
    private static Views.ScoreFollowCoordinator FollowFor(System.Windows.Controls.ScrollViewer scroll, Views.TabEditorControl editor,
        Func<int> playhead, int bars = 48, Action<Services.FollowSettings>? tune = null)
    {
        var settings = new Services.FollowSettings { Mode = Services.FollowModes.Jump };
        tune?.Invoke(settings);
        var follow = new Views.ScoreFollowCoordinator(scroll, editor, () => true, () => false, playhead, () => 0.0,
            () => bars, () => settings);
        follow.ApplySettings(settings);
        scroll.ScrollChanged += (_, e) => follow.OnScrollChanged(e); // as MainWindow wires it
        return follow;
    }

    /// <summary>A genuine hand scroll: a wheel gesture, then the viewer moves by 120 px.</summary>
    private static void HandScroll(System.Windows.Controls.ScrollViewer scroll, Views.ScoreFollowCoordinator follow, Action layout)
    {
        Settle(400);
        follow.NoteUserScrollGesture();
        scroll.ScrollToVerticalOffset(scroll.VerticalOffset + 120);
        layout();
    }

    /// <summary>The bar's system starts inside the visible part of the viewer.</summary>
    private static bool BarInView(Views.TabEditorControl editor, System.Windows.Controls.ScrollViewer scroll, int bar)
    {
        var top = editor.SystemTopForMeasure(bar);
        var offset = scroll.VerticalOffset;
        return top >= offset - 0.5 && top < offset + scroll.ViewportHeight;
    }

    private static void Settle(int milliseconds) => System.Threading.Thread.Sleep(milliseconds);
}
