# Audit-7 Dead-Verb Plan — PLAN ONLY (no wiring changes; build offline)

Date: 2026-10-19. Data: tasks 32+33+34. Gate: headers intact (run after write).
No feature removed. No id deleted.

## Findings
- Core HotkeyCatalog 220/221 bound. Verbs 42/44 catalogued (2 intentional).
- Avalonia 271 ids: both 170 / menu-only 27 / hot-only 45 / neither 29 → 24 resolved
  (16 timeline/section + Section.ToggleLock + Transport.LoopSection + Selection.Clear
  via Views/ContextMenuRows.cs; 4 Tool.* via MainWindowBarTools Ask();
  Transport.LiveInput via TransportBarView toolbar).
- Engine EngineCommand 38/38 handled + SetTrack=2 reserved; round-trips 1:1
  (20 + 9 shapes); no default: = forward-compat ignore. No action.

## True orphans (6, all with handlers — DO NOT DELETE)
1. File.CancelImport (Core + Avalonia agree): HotkeyCatalog row, empty gesture, zero
   commands.Add; StatusBar Cancel button wired by Click (StatusBarView.cs:72-78),
   tests use button Name. FIX (needs build): route button Click through the
   command id so rebinding works; fallback if risky: document as click-only.
2-6. Transport.EscapeStop (TransportMenu), Transport.CountInSettings,
   Transport.LoopSettings, Transport.MetronomeSettings (TransportToggles),
   Transport.ClearLoopArea (SelectionLoopLink): registered, handlers exist, zero
   invokers by id. FIX (needs build): verify direct-call reachability first
   (handler may be called as method, not by id); if truly dead, bind to menu
   or mark API-only — never delete the handler blind.

## Intentional (confirm, don't "fix")
- Note.Longer/Note.Shorter: fixed-keymap pair (menu + numpad aliases + catalog consts).
  No catalog rows by design.
- TrackRow.Copy/Cut/Duplicate/Paste: registered via Core TrackRowMenus.* constants.
- Engine SetTrack=2 / EngineEvent.States=4 reserved; TestHang TEST_HOOKS-gated.

## Gate
- After any wiring fix: Owns counts + tutorial refs + `dotnet build` touched
  projects (blocked 2026-10-19 NU1301 offline; re-run with network) + relevant
  SelfTests (StatusBarViewTests, transport toggle tests).
