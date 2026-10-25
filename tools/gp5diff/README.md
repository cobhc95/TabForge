# GP5 differential harness

Runs the same human-like editing sequences in Guitar Pro 5 (the reference) and in TabForge, and reports the first action after
which the two scores differ. Not part of the product; nothing here ships.

| File | Owns |
|---|---|
| `actions.json` | The action vocabulary: each action's GP5 keys or menu path and TabForge keys or command id, and how faithful the mapping is. |
| `gen.py` | Seeded generator of 20-80-action sequences (`python gen.py <run> <first seed> <count>`; `--trivial` for hand-checkable ones). |
| `Gp5Driver/` | C# console tool. `run <desktop> <run> <k>/<n>` drives one GP5 on its own hidden desktop: File > New, the actions, a save after every action (`steps/<id>/NNN.gp5`), the final file (`out/<id>.gp5`). `do <desktop> <ops>` is the exploration tool (keys, menus, captures). |
| `gp5.ps1` | Starts N drivers in parallel (one hidden desktop and one GP5 each), with a timeout. |
| `tf.ps1` | Runs `TestGp5DiffReplay` (tests/full-suite/Editor/SelfTestGp5Diff.cs) in a build of the app: `replay` writes TabForge's song after every action (`tf/<id>.json`), `import` reads GP5's files with TabForge's importer (`gp5/<id>.json`). |
| `gpdump.py`, `dumpgp.py` | pyguitarpro readings of GP5's files (the comparator's reference; a quick bar-by-bar print). |
| `compare.py` | First divergent action per sequence, and where TabForge's importer reads a GP5 file differently from pyguitarpro. |
| `cluster.py` | Groups divergences by action and kind of difference, keeps the shortest repro of each. |
| `x.ps1` | Exploration helper: ops on a desktop plus a capture. |

## How GP5 is driven
- Each GP5 runs on its own desktop made with `CreateDesktop` and started with `STARTUPINFO.lpDesktop`, so several run at once and none
  appears on the screen. The driver thread joins that desktop (`SetThreadDesktop`) and attaches to GP5's input queue
  (`AttachThreadInput`) so `SetKeyboardState` gives GP5 the Ctrl / Shift / Alt state of a posted `WM_KEYDOWN`.
- GP5 reads score keys through an off-screen `TEdit` on the score panel; the driver gives it the focus (the window is never
  activated on a hidden desktop). Every key waits for GP5 to handle it (sent no-op messages after the key-down and the key-up).
- Menus are Toolbar2000 menus: Alt+F, then Right / Down counts, then Enter (disabled items count; they are not skipped).
- Dialogs an action opens (bend, grace, bar copy and paste) are accepted with their default button. GP5's own crash reports
  (access violations) are logged; the comparator stops at that step.
- After every action Ctrl+S writes the score's file and the driver copies it. GP5 does not write after an undo or redo (it does not
  count the score as modified), so then File > Save As writes a fresh work file. Saving changes neither the cursor, the selection,
  the undo history nor a pending two-digit fret.
- A killed GP5 asks to restore its backup at the next start; the driver answers No.

## Running
```
python gen.py <run> 1 1000
powershell -File gp5.ps1 <run> 12          # GP5 side (12 instances is about what one PC handles)
powershell -File tf.ps1 <run> replay 4     # TabForge side, 4 processes
powershell -File tf.ps1 <run> import 6
python compare.py <run>; python cluster.py <run>
```
The TabForge side needs a build of the app (`dotnet build Source/src/TabForge.Avalonia/TabForge.Avalonia.csproj -c Release -o <dir>`).
Without `TABFORGE_GP5DIFF_DIR` the test replays a short built-in sequence as its own check.
