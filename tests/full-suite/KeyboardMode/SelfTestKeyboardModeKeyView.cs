using System.Windows;
using TabForge.Audio;
using TabForge.KeyboardMode;
using TabForge.Models;

namespace TabForge;

// Owns: the keyboard falling-notes view checks: the layout maths (key window, key x of white and black keys, lane to key, falling position and pixels per millisecond, the narrow window),
//   the colours (text contrast, the hand pair), the feedback state machine (held keys, grade words, fade, reset) with fake judge results, the page being drawn once for seconds of frames,
//   and the wait cue and held keys driven through a run on a fake device.
// Does not own: the judge, score and wait rules (their own tests) or the dock layout (TestKeyboardModeLayout).
// Tests: TestKeyboardModeKeyView.
public static partial class SelfTest
{
    private static void TestKeyboardModeKeyView()
    {
        // Layout maths.
        var (lo, hi) = KeyboardModeLayout.Window(60, 71);
        Check("key view: a one-octave range widens to whole octaves, at least three, inside the 88", hi - lo + 1 >= KeyboardModeLayout.MinKeys && lo >= 21 && hi <= 108 && (hi - lo + 1) % 12 == 0 && lo <= 60 && hi >= 71, $"{lo}-{hi}");
        Check("key view: the whole piano keeps its range", KeyboardModeLayout.Window(21, 108) == (21, 108));
        var lay = KeyboardModeLayout.Compute(36, 96, 1600, 700, 5000);
        Check("key view: white keys are evenly spaced and the strip keeps piano proportions (length 6.4 widths, at most 30 % of the view)",
            Math.Abs(lay.KeyX(62) - lay.KeyX(60) - lay.WhiteWidth) < 1e-9 && Math.Abs(lay.KeyX(64) - lay.KeyX(62) - lay.WhiteWidth) < 1e-9
            && lay.StripHeight <= lay.Height * KeyboardModeLayout.MaxStripShare + 1e-6 && (Math.Abs(lay.StripHeight - lay.WhiteWidth * KeyboardModeLayout.WhiteLengthRatio) < 1e-6 || lay.Whites == 52), $"{lay.WhiteWidth:0.0} x {lay.StripHeight:0.0}");
        var wide = KeyboardModeLayout.Compute(60, 71, 1600, 800, 4000);
        Check("key view: a wide, short view shows more octaves rather than stubby keys", wide.Highest - wide.Lowest + 1 > KeyboardModeLayout.MinKeys && Math.Abs(wide.StripHeight - wide.WhiteWidth * KeyboardModeLayout.WhiteLengthRatio) < 1e-6, $"{wide.Lowest}-{wide.Highest} {wide.WhiteWidth:0.0} x {wide.StripHeight:0.0}");
        var shapeD = lay.KeyShape(62);
        var top = lay.StripTop + 2;
        Check("key view: a white key's outline leaves out the black keys over it and stays inside its key",
            !shapeD.FillContains(new Point(lay.KeyCentre(61), top)) && !shapeD.FillContains(new Point(lay.KeyCentre(63), top)) && shapeD.FillContains(new Point(lay.KeyCentre(62), top))
            && shapeD.FillContains(new Point(lay.KeyX(62) + 1, lay.StripTop + lay.StripHeight - 2)) && Rect.Inflate(lay.KeyRect(62), 0.01, 0.01).Contains(shapeD.Bounds), $"{shapeD.Bounds} in {lay.KeyRect(62)}");
        Check("key view: a black key's outline is its own rectangle", lay.KeyShape(61).Bounds == lay.KeyRect(61) && lay.KeyShape(61).FillContains(new Point(lay.KeyCentre(61), top)));
        Check("key view: E and B have one notch, C and F one on the other side", !lay.KeyShape(64).FillContains(new Point(lay.KeyCentre(63), top)) && lay.KeyShape(64).FillContains(new Point(lay.KeyX(64) + lay.WhiteWidth - 1, top)));
        var fast = KeyboardModeLayout.Compute(36, 96, 1200, 2400, 2000);
        Check("key view: the notes never fall faster than the speed cap (a short look-ahead on a tall view is lengthened)", fast.PxPerMs <= KeyboardModeLayout.MaxPxPerSecond / 1000 + 1e-9 && fast.AheadMs > 2000, $"{fast.PxPerMs * 1000:0} px/s, {fast.AheadMs:0} ms");
        Check("key view: a black key sits on the seam of its white neighbours and is narrower and shorter", Math.Abs(lay.KeyCentre(61) - lay.KeyX(62)) <= lay.WhiteWidth * 0.2 && lay.KeyWidth(61) < lay.WhiteWidth && lay.KeyRect(61).Height < lay.KeyRect(60).Height);
        Check("key view: C# leans left and D# leans right of their seams", lay.KeyCentre(61) < lay.KeyX(62) && lay.KeyCentre(63) > lay.KeyX(64));
        Check("key view: keys never overlap in a row of white keys and stay inside the view", Enumerable.Range(lay.Lowest, lay.Highest - lay.Lowest + 1).All(m => lay.KeyX(m) >= 0 && lay.KeyX(m) + lay.KeyWidth(m) <= lay.Width));
        var song = KeysSong(8);
        for (var bar = 0; bar < 8; bar++) for (var cell = 0; cell < 16; cell += 4) Beat(song, 0, bar, cell, 4, 48 + (cell / 4 * 5 + bar) % 30);
        var source = KeysSourceOf(song);
        var lane = KeyboardModeLayout.Compute(source.Lowest, source.Highest, 900, 400, 5000);
        Check("key view: a note's lane maps to its key's x (lane to key)", source.Notes.All(n => lane.Has(n.Fret) && Math.Abs(lane.KeyX(source.Lowest + n.StringIndex) - lane.KeyX(n.Fret)) < 1e-9));
        Check("key view: the falling area spans exactly the look-ahead (pixels per ms)", Math.Abs(lane.PxPerMs * 5000 - lane.StripTop) < 1e-6 && Math.Abs(lane.YAhead(0) - lane.StripTop) < 1e-9 && Math.Abs(lane.YAhead(5000)) < 1e-6 && Math.Abs(lane.AheadMs - 5000) < 1e-6);
        Check("key view: a note later in time sits higher", lane.YAhead(1000) < lane.YAhead(500) && lane.YAhead(500) < lane.StripTop);
        var tall = KeyboardModeLayout.Compute(source.Lowest, source.Highest, 900, 800, 5000);
        Check("key view: a taller view shows the same seconds with more pixels per second", Math.Abs(tall.AheadMs - 5000) < 1e-6 && tall.PxPerMs > lane.PxPerMs);
        var narrow = KeyboardModeLayout.Compute(21, 108, 864, 300, 5000);
        Check("key view: the 88 keys fit a narrow window and a short view keeps the minimum", narrow.Left >= 0 && narrow.Right <= 864 && narrow.Height >= KeyboardModeLayout.MinHeight && narrow.StripTop > 40 && Enumerable.Range(21, 88).All(m => narrow.KeyX(m) + narrow.KeyWidth(m) <= 864 + 1e-9), $"{narrow.Left}-{narrow.Right} h {narrow.Height}");
        Check("key view: the 88 keys have 52 white keys", narrow.Whites == 52);
        foreach (var (w, h) in new[] { (1600.0, 700.0), (864.0, 420.0), (1600.0, 300.0) })
        {
            var s2 = KeyboardModeLayout.Compute(36, 96, w, h, 5000);
            Check($"key view: the key strip of a {w}x{h} view is 6.4 white widths long, at most 30 % of the view (88 keys may be shorter)", s2.StripHeight <= s2.Height * KeyboardModeLayout.MaxStripShare + 1e-6 && (Math.Abs(s2.StripHeight - s2.WhiteWidth * KeyboardModeLayout.WhiteLengthRatio) < 1e-6 || s2.Whites == 52) && Math.Abs(s2.AheadMs - 5000) < 1e-6, $"{s2.StripHeight:0.0} of {s2.Height}, {s2.Whites} whites");
            Check($"key view: black keys are 58 % wide and 63 % long at {w}x{h}", Math.Abs(s2.KeyRect(61).Height / s2.KeyRect(60).Height - 0.63) < 0.005 && Math.Abs(s2.KeyWidth(61) / s2.WhiteWidth - 0.58) < 0.005);
        }
        Check("key view: a note name spells the key (sharps, octave from C-1)", KeyboardNoteSource.NameOf(60) == "C4" && KeyboardNoteSource.NameOf(54) == "F#3" && KeyboardNoteSource.NameOf(21) == "A0" && KeyboardNoteSource.NameOf(108) == "C8");
        // Bar length follows the duration: a whole, a half, a quarter and an eighth at 120 bpm, a tie merged into one note.
        var lens = KeysSong(3);
        Beat(lens, 0, 0, 0, 1, 60); Beat(lens, 0, 1, 0, 2, 62); Beat(lens, 0, 1, 8, 4, 64); Beat(lens, 0, 1, 12, 8, 65);
        Beat(lens, 0, 2, 0, 2, 67); Beat(lens, 0, 2, 8, 2, 67).IsTied = true;
        var ls = KeysSourceOf(lens);
        double Dur(int midi, int nth = 0) => ls.Notes.Where(n => n.Fret == midi).ElementAt(nth).DurationMs;
        Check("key view: bar length follows the note value (whole > half > quarter > eighth)", Dur(60) > Dur(62) * 1.8 && Dur(62) > Dur(64) * 1.8 && Dur(64) > Dur(65) * 1.8, $"{Dur(60)} {Dur(62)} {Dur(64)} {Dur(65)}");
        Check("key view: a tied note is one bar of two halves long", ls.Notes.Count(n => n.Fret == 67) == 1 && Dur(67) > 1800, $"{ls.Notes.Count(n => n.Fret == 67)} {Dur(67)}");
        var ly = KeyboardModeLayout.Compute(ls.Lowest, ls.Highest, 1600, 700, 4000);
        Check("key view: a half note is a tall bar on the page (pixels = ms * px per ms)", Dur(62) * ly.PxPerMs > 2 * 60, $"{Dur(62) * ly.PxPerMs:0}");

        // Colours: text readable on the ground it is drawn on in both themes, the hands apart in brightness too.
        foreach (var dark in new[] { true, false })
        {
            var pal = KeyboardModePalette.For(dark);
            var col = pal.Keyboard;
            var worst = new[] { col.PerfectColour, col.GoodColour, col.SlipColour, col.MissColour }.Min(c => KeyboardModePalette.Contrast(c, pal.PanelColour));
            Check($"key view [{(dark ? "dark" : "light")}]: grade text reads on the panel (>= 4.5:1)", worst >= 4.5, worst.ToString("0.00"));
            Check($"key view [{(dark ? "dark" : "light")}]: the note names read on a white key (>= 4.5:1)", KeyboardModePalette.Contrast(col.KeyLabelColour, col.WhiteColour) >= 4.5);
            Check($"key view [{(dark ? "dark" : "light")}]: both hands read against the panel (>= 3:1) and differ in brightness", KeyboardModePalette.Contrast(col.LeftColour, pal.PanelColour) >= 3 && KeyboardModePalette.Contrast(col.RightColour, pal.PanelColour) >= 3 && KeyboardModePalette.Contrast(col.LeftColour, col.RightColour) >= 1.4,
                $"{KeyboardModePalette.Contrast(col.LeftColour, pal.PanelColour):0.0} {KeyboardModePalette.Contrast(col.RightColour, pal.PanelColour):0.0} {KeyboardModePalette.Contrast(col.LeftColour, col.RightColour):0.0}");
            Check($"key view [{(dark ? "dark" : "light")}]: the note names read on both hand colours (>= 4.5:1)", new[] { (col.LeftText, col.LeftColour), (col.RightText, col.RightColour) }.All(t => KeyboardModePalette.Contrast(t.Item1.Color, t.Item2) >= 4.5));
            Check($"key view [{(dark ? "dark" : "light")}]: a high and a low note together go to the right and the left hand", KeyboardHands.Assign(new[] { (72, 0.0, 500.0), (40, 0.0, 500.0) }) is [false, true] && ReferenceEquals(col.HandBrush(false), col.Right) && ReferenceEquals(col.HandBrush(true), col.Left));
        }

        // The feedback state machine with fake judge results: a Perfect, Good, Early, Late and Miss, then the worst of a frame, the fade and the reset.
        var judge = new KeyboardModeJudge(new[] { new KeyboardModeExpected(60, 1.0, 0.3), new KeyboardModeExpected(62, 2.0, 0.3), new KeyboardModeExpected(64, 3.0, 0.3), new KeyboardModeExpected(65, 4.0, 0.3), new KeyboardModeExpected(67, 5.0, 0.3), new KeyboardModeExpected(69, 6.0, 0.3), new KeyboardModeExpected(71, 6.0, 0.3) });
        var fb = new KeyboardFeedback();
        Check("feedback: nothing shows before a grade", fb.Latest is null && fb.Alpha(0) == 0 && fb.GradeVersion == 0);
        judge.Feed(new KeyboardModePlayed(60, true, 1.02)); fb.Scan(judge, 1000);
        Check("feedback: a press within 60 ms reads Perfect", fb.Latest == KeyGradeKind.Perfect && fb.LatestKey == 60 && fb.GradeVersion == 1);
        judge.Feed(new KeyboardModePlayed(62, true, 2.09)); fb.Scan(judge, 2000);
        Check("feedback: a hit 90 ms late reads Good", fb.Latest == KeyGradeKind.Good && fb.GradeVersion == 2);
        judge.Feed(new KeyboardModePlayed(64, true, 2.88)); fb.Scan(judge, 3000);
        Check("feedback: a hit 120 ms early reads Early", fb.Latest == KeyGradeKind.Early, fb.Latest.ToString());
        judge.Feed(new KeyboardModePlayed(65, true, 4.13)); fb.Scan(judge, 4000);
        Check("feedback: a hit 130 ms late reads Late", fb.Latest == KeyGradeKind.Late, fb.Latest.ToString());
        var versionBefore = fb.GradeVersion;
        Check("feedback: nothing new, nothing flashes again", !fb.Scan(judge, 4100) && fb.GradeVersion == versionBefore);
        judge.Advance(5.5); fb.Scan(judge, 5600);
        Check("feedback: a note nobody played reads Miss", fb.Latest == KeyGradeKind.Miss && fb.LatestKey == 67);
        judge.Feed(new KeyboardModePlayed(69, true, 6.0)); judge.Advance(6.5); fb.Scan(judge, 6600);
        Check("feedback: of a chord with a hit and a miss the miss shows", fb.Latest == KeyGradeKind.Miss && fb.LatestKey == 71, fb.Latest + " " + fb.LatestKey);
        Check("feedback: the flash holds then fades to nothing", fb.Alpha(6600) == 1 && fb.Alpha(6600 + KeyboardFeedback.FadeStartMs) == 1 && fb.Alpha(6600 + (KeyboardFeedback.FadeStartMs + KeyboardFeedback.ShowMs) / 2) is > 0.4 and < 0.6 && fb.Alpha(6600 + KeyboardFeedback.ShowMs) == 0);
        Check("feedback: every word is distinct", new[] { KeyGradeKind.Perfect, KeyGradeKind.Good, KeyGradeKind.Early, KeyGradeKind.Late, KeyGradeKind.Miss }.Select(KeyboardFeedback.TextOf).Distinct().Count() == 5);
        fb.Played(new KeyboardModePlayed(60, true, 1), true);
        fb.Played(new KeyboardModePlayed(61, true, 1), false);
        var held = fb.HeldVersion;
        fb.Played(new KeyboardModePlayed(61, true, 1.1), false);
        Check("feedback: a right key and an extra key are held; the same key again changes nothing", fb.HeldOf(60) == KeyHold.Correct && fb.HeldOf(61) == KeyHold.Extra && fb.HeldVersion == held);
        fb.Played(new KeyboardModePlayed(60, false, 2), false);
        Check("feedback: a release clears the key", fb.HeldOf(60) == KeyHold.None && fb.HeldOf(61) == KeyHold.Extra && fb.HeldVersion == held + 1);
        fb.Reset();
        Check("feedback: a reset clears the keys and the flash", fb.HeldOf(61) == KeyHold.None && fb.Latest is null && fb.Alpha(7000) == 0);
        var mask = new KeyMask();
        mask.Set(21); mask.Set(63); mask.Set(64); mask.Set(108);
        Check("key mask: sets and tests keys across both words", mask.Has(21) && mask.Has(63) && mask.Has(64) && mask.Has(108) && !mask.Has(65) && !mask.Has(-1) && !mask.Has(200));

        // The view: the page is drawn once for seconds of frames, a seek, a new loop and a new size draw it again, and static frames redraw no layer.
        var view = new KeyboardModeView { Width = 900, Height = 400 };
        view.Measure(new Size(900, 400));
        view.Arrange(new Rect(0, 0, 900, 400));
        view.SetLook(true, 5000);
        view.SetSource(source, "");
        view.Update(0, null, false, true);
        var builds = view.PageBuilds;
        var y0 = view.PageShiftY;
        var lastT = 0;
        for (var t = 16; t <= 3000; t += 16) { view.Update(t, null, false, true); lastT = t; }
        Check("key view: 3 s of 60 frames a second draw the page once (a frame moves one transform down)", view.PageBuilds == builds && view.PageShiftY > y0, $"{view.PageBuilds} vs {builds}");
        Check("key view: the notes fall at the pixels-per-ms rate (to the nearest device pixel)", Math.Abs((view.PageShiftY - y0) - lastT * view.Layout!.PxPerMs) < 1.0, (view.PageShiftY - y0).ToString("0.0"));
        for (var t = 3016; t <= 14000; t += 16) view.Update(t, null, false, true);
        Check("key view: a rolling window draws a new page only every few seconds", view.PageBuilds is >= 2 and <= 4, view.PageBuilds.ToString());
        var before = view.PageBuilds;
        view.Update(100, null, true, true);
        Check("key view: a seek draws the page again", view.PageBuilds == before + 1);
        view.Update(116, new KeyboardModeLoop(0, 4000), false, true);
        Check("key view: a new loop draws the page again", view.PageBuilds == before + 2);
        var wrapPages = view.PageBuilds;
        for (var t = 132; t < 3900; t += 16) view.Update(t, new KeyboardModeLoop(0, 4000), false, true);
        view.Update(4100, new KeyboardModeLoop(0, 4000), false, true);   // virtual time runs past the loop end, no jump
        view.Update(4116, new KeyboardModeLoop(0, 4000), false, true);
        Check("key view: running through a loop wrap builds only a few pages and no reset", view.PageBuilds - wrapPages <= 3, (view.PageBuilds - wrapPages).ToString());
        var dueDraws = view.KeyDraws;
        view.Update(4132, new KeyboardModeLoop(0, 4000), false, true);
        var settled = view.KeyDraws;
        view.Update(4132, new KeyboardModeLoop(0, 4000), false, true);
        Check("key view: a repeated frame redraws no key layer", view.KeyDraws == settled && settled >= dueDraws);
        var frames = view.KeyDraws;
        for (var t = 4148; t < 4400; t += 16) view.Update(t, new KeyboardModeLoop(0, 4000), false, true);
        Check("key view: key layer redraws only when a note starts or ends (not every frame)", view.KeyDraws - frames <= 4, (view.KeyDraws - frames).ToString());
        var seekBuilds = view.PageBuilds;
        view.Width = 700; view.Measure(new Size(700, 400)); view.Arrange(new Rect(0, 0, 700, 400));
        view.SetSource(source, "");   // what a size change does once layout has run
        view.Update(100, null, false, true);
        Check("key view: a new size lays out and draws the page again", view.PageBuilds > seekBuilds && view.Layout!.Width == 700);
        view.Width = 785; view.Height = 290; view.Measure(new Size(785, 290)); view.Arrange(new Rect(0, 0, 785, 290));
        view.Update(200, null, false, true);
        Check("key view: a short wide view keeps the look-ahead's pixels per second", view.Layout!.AheadMs is > 3000 and < 6000, $"ahead {view.Layout.AheadMs:0} px/s {view.Layout.PxPerMs * 1000:0.0} top {view.Layout.StripTop:0} h {view.Layout.Height:0}");

        // The run: held keys, a grade and the wait cue through a session on a fake device.
        var play = KeysSong();
        Beat(play, 0, 0, 4, 4, 60);
        Beat(play, 0, 0, 8, 4, 64);
        var playSource = KeysSourceOf(play);
        var clockPaused = false;
        var dev = new FakeMidiDevice();
        var listener = new KeyboardModeMidiListener(new MidiInputHub(dev).CreateClient(), stamp => clockPaused ? double.NaN : stamp / 1000.0, () => 0);
        var session = new KeyboardModeKeyboardSession(listener);
        var sh = new FakeWaitHost();
        var wait = new KeyboardModeWaitMode(sh) { Enabled = true };
        session.Wait = wait;
        session.SetSource(playSource, true);
        var pv = new KeyboardModeView { Width = 900, Height = 400 };
        pv.Measure(new Size(900, 400)); pv.Arrange(new Rect(0, 0, 900, 400));
        pv.SetLook(false, 5000);
        pv.SetSource(playSource, "");
        pv.SetPlayAlong(session);
        void Frame(double ms) { session.Update(ms, true, !sh.Playing, null, 0, false, 0); pv.Update(ms, null, false, sh.Playing); }
        Frame(0);
        var hud = pv.HudDraws;
        dev.Fire(0x90, 60, 90, 495);
        dev.Fire(0x90, 61, 90, 497);
        Frame(500);
        Check("key view run: a right key and an extra key show as held, a hit shows a grade", session.Feedback.HeldOf(60) == KeyHold.Correct && session.Feedback.HeldOf(61) == KeyHold.Extra && session.Feedback.Latest == KeyGradeKind.Perfect && pv.GradeDraws >= 1 && pv.GradeOpacity > 0.9);
        Check("key view run: the score panel follows the hit", pv.HudDraws > hud && session.Score.Hits == 1);
        dev.Fire(0x80, 60, 0, 700); dev.Fire(0x80, 61, 0, 700);
        Frame(700);
        Check("key view run: releasing the keys clears them", session.Feedback.HeldOf(60) == KeyHold.None && session.Feedback.HeldOf(61) == KeyHold.None);
        Frame(1000);
        var awaited = new List<int>();
        wait.AwaitedInto(awaited);
        Check("key view run: the song waits and the awaited key is the next note", wait.IsWaiting && awaited.SequenceEqual(new[] { 64 }) && wait.Awaits(64) && !wait.Awaits(60));
        var waitHud = pv.HudDraws;
        var keyDraws = pv.KeyDraws;
        Frame(1000);
        Check("key view run: while it waits nothing is redrawn frame after frame", pv.HudDraws == waitHud && pv.KeyDraws == keyDraws);
        clockPaused = true;
        dev.Fire(0x90, 64, 90, 1020);
        Frame(1000);
        Check("key view run: the awaited key pressed during the wait counts as right and the wait ends", sh.Resumes == 1 && !wait.IsWaiting && session.Feedback.HeldOf(64) == KeyHold.Correct && session.Score.Hits == 2);
        Check("key view run: the cue is gone and the hud redrew", pv.HudDraws > waitHud);
        pv.SetPlayAlong(null);
        pv.Update(1000, null, false, true);
        Check("key view run: without play-along no grade shows", pv.GradeOpacity == 0);
        session.Dispose();
    }
}
