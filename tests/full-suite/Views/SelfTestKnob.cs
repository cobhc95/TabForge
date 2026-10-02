using TabForge.Views;

namespace TabForge;

public static partial class SelfTest
{
    /// <summary>Knob type-in: unit parsing, clamping and step, apply/cancel, double-click / Ctrl+click routes, and the menu item.</summary>
    private static void TestKnobTypeIn()
    {
        bool P(string s, double expect) => KnobValueParser.TryParse(s, out var v) && Math.Abs(v - expect) < 1e-9;
        Check("knob parse: plain number", P("2.1", 2.1) && P("-6", -6) && P("+3", 3) && P("  7 ", 7));
        Check("knob parse: dB, %, ms", P("-6 dB", -6) && P("-6dB", -6) && P("75%", 75) && P("75 %", 75) && P("100 ms", 100));
        Check("knob parse: ratio", P("2:1", 2) && P("2.0:1", 2) && P("3:2", 1.5) && P("4:", 4));
        Check("knob parse: comma decimal", P("2,5", 2.5) && P("-0,5 dB", -0.5));
        Check("knob parse: -inf", KnobValueParser.TryParse("-inf dB", out var inf) && double.IsNegativeInfinity(inf));
        Check("knob parse: rejects junk", !KnobValueParser.TryParse("", out _) && !KnobValueParser.TryParse("abc", out _)
            && !KnobValueParser.TryParse("2 volts", out _) && !KnobValueParser.TryParse("1:0", out _) && !KnobValueParser.TryParse("1.2.3", out _));
        Check("pan parse", KnobValueParser.ParsePan("Centre") == 64 && KnobValueParser.ParsePan("L 20") == 44 && KnobValueParser.ParsePan("R 5") == 69
            && KnobValueParser.ParsePan("-10") == 54 && KnobValueParser.ParsePan("+5") == 69 && KnobValueParser.ParsePan("x") is null);

        // Multiply-style knob: shown in tenths (DisplayScale 10), range 0..5.
        var started = 0; var ended = 0;
        var k = new KnobControl { Minimum = 0, Maximum = 50, DefaultValue = 10, DisplayScale = 10, Label = "Multiply", Format = v => (v / 10).ToString("0.0#") };
        k.EditStarted += (_, _) => started++;
        k.EditEnded += (_, _) => ended++;
        Check("knob apply: 2.1 sets 21 through EditStarted/EditEnded", k.ApplyTyped("2.1") && k.Value == 21 && started == 1 && ended == 1, $"{k.Value} {started} {ended}");
        Check("knob clamp: above max", k.ApplyTyped("99") && k.Value == 50);
        Check("knob clamp: below min", k.ApplyTyped("-4") && k.Value == 0);
        k.ApplyTyped("2.1");
        started = 0;
        Check("knob invalid entry keeps the old value", !k.ApplyTyped("nonsense") && k.Value == 21 && started == 0);
        Check("knob same value is not an edit", k.ApplyTyped("2.1") && started == 0);

        // Step: dB knob in tenths.
        var db = new KnobControl { Minimum = -60, Maximum = 12, Step = 0.1, Format = v => $"{v:+0.0;-0.0;0.0} dB" };
        Check("knob step: -6.04 dB rounds to -6.0", db.ApplyTyped("-6.04 dB") && Math.Abs(db.Value + 6.0) < 1e-9, db.Value.ToString());
        Check("knob step: -6.25 dB", db.ApplyTyped("-6,25") && Math.Abs(Math.Abs(db.Value + 6.25) - 0) <= 0.05 + 1e-9, db.Value.ToString());
        Check("knob: -inf goes to the minimum", db.ApplyTyped("-inf") && db.Value == -60);
        Check("knob: displayed text is what the editor is pre-filled with", db.DisplayText() == "-60.0 dB", db.DisplayText());

        // Percent knob over 0..127 and ratio knob.
        var vol = new KnobControl { Minimum = 0, Maximum = 127, FromDisplay = p => p * 1.27 };
        Check("knob percent: 50% is 64 (rounded), 200% clamps", vol.ApplyTyped("50%") && vol.Value == 64 && vol.ApplyTyped("200") && vol.Value == 127);
        var ratio = new KnobControl { Minimum = 10, Maximum = 200, DisplayScale = 10 };
        Check("knob ratio: 2:1 is 20, 2.0:1 is 20", ratio.ApplyTyped("2:1") && ratio.Value == 20 && ratio.ApplyTyped("4.5:1") && ratio.Value == 45);
        var pan = new KnobControl { Minimum = 0, Maximum = 127, Parse = KnobValueParser.ParsePan };
        Check("knob pan: L 20 is 44", pan.ApplyTyped("L 20") && pan.Value == 44 && pan.ApplyTyped("centre") && pan.Value == 64);

        // Editor lifecycle: double-click opens, Esc cancels, finish applies.
        var e = new KnobControl { Minimum = 0, Maximum = 100, DefaultValue = 50, Value = 30, Label = "Volume", Format = v => $"{v:0}%" };
        e.HandleLeftDown(1, false);
        Check("single click does not open the editor", !e.IsEditing);
        e.HandleLeftDown(2, false);
        Check("double-click opens the editor, pre-filled with the displayed value and named",
            e.IsEditing && e.Editor!.Text == "30%" && System.Windows.Automation.AutomationProperties.GetName(e.Editor) == "Volume value" && e.Value == 30);
        e.Editor!.Text = "80";
        e.FinishEdit(false);
        Check("Esc cancels", !e.IsEditing && e.Value == 30);
        e.BeginTypeValue();
        e.Editor!.Text = "80";
        e.FinishEdit(true, keepOpenIfInvalid: true);
        Check("Enter applies", !e.IsEditing && e.Value == 80);
        e.BeginTypeValue();
        e.Editor!.Text = "zzz";
        e.FinishEdit(true, keepOpenIfInvalid: true);
        Check("Enter on an invalid entry keeps the editor open", e.IsEditing && e.Value == 80);
        e.FinishEdit(true);
        Check("clicking away on an invalid entry closes it and keeps the value", !e.IsEditing && e.Value == 80);
        var eStarted = 0; var eEnded = 0;
        e.EditStarted += (_, _) => eStarted++;
        e.EditEnded += (_, _) => eEnded++;
        e.HandleLeftDown(1, true);
        Check("Ctrl+click resets to the default as one edit gesture (undo, dirty, engine)", e.Value == 50 && !e.IsEditing && eStarted == 1 && eEnded == 1, $"{e.Value} {eStarted} {eEnded}");
        e.HandleLeftDown(1, true);
        Check("Ctrl+click on the default value is not an edit", eStarted == 1 && eEnded == 1);
        e.HandleLeftDown(2, false);
        e.FinishEdit(false);

        // Unloading the knob (rows rebuilt, window closed) cancels an open editor.
        e.Value = 40;
        e.HandleLeftDown(2, false);
        e.Editor!.Text = "90";
        e.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.FrameworkElement.UnloadedEvent));
        Check("knob unload closes the editor without applying", !e.IsEditing && e.Value == 40);

        // FX Volume (0.1 dB Step): wheel / arrow keys still move in whole dB as before.
        var fx = new KnobControl { Minimum = -60, Maximum = 12, Step = 0.1 };
        Check("fine-Step knob keeps whole-unit coarse steps", fx.CoarseStep(false) == 2 && fx.CoarseStep(true) == 1
            && new KnobControl { Minimum = 0, Maximum = 127 }.CoarseStep(false) == 127.0 / 32, fx.CoarseStep(false).ToString());

        // A knob with its own context menu: "Type value…" becomes the first item, once.
        var menu = new System.Windows.Controls.ContextMenu();
        menu.Items.Add(new System.Windows.Controls.MenuItem { Header = "Centre pan" });
        var withMenu = new KnobControl { ContextMenu = menu };
        withMenu.PrepareMenu();
        withMenu.PrepareMenu();
        Check("knob context menu: Type value… is first, added once, existing items kept",
            menu.Items.Count == 3 && menu.Items[0] is System.Windows.Controls.MenuItem { Header: "Type value…" }
            && menu.Items[2] is System.Windows.Controls.MenuItem { Header: "Centre pan" });
    }
}
