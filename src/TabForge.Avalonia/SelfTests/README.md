# SelfTests: headless verification suite (no window, no Avalonia app)

Run: `TabForge.exe --selftest <log> [--areas basic|release|all|a,b] [--only TestA,TestB] [--require ci] [--timing <csv>]`

| Area set | What runs |
|---|---|
| (none: basic) | `architecture`, `hygiene`, `engine` — the default gate |
| `release` | basic plus the release-gated long tests (needs fixtures/audio) |
| `ci` (`--require`) | `gp-fixtures`, `synthetic-fixtures`, `source-hygiene`, `installer-parity`, `fuzz`, `architecture` |
| named (`--areas a,b`, `--only`) | just those areas/tests; unknown names fail before running |

Named areas include `fuzz` (malformed-input), `long-import` (Guitar Pro), `source-hygiene`/`installer-parity` (repo checks).
A skip inside a group must pass the group name (`Skip("reason", "source-hygiene")`); `--timing` records per-test ms.
Test methods are named `TestX` and mapped in `AreaOf`; `--core-shard n` splits the untagged core tests.
The log file gets one line per check; a failure prints the check name, the reason, and the area.

Add a test in 3 steps:
1. Write `TestX` in `Release/<Area>/SelfTest<Area>.cs` using only `Check`/`Eq`/`Skip` (generated fixtures only — never `Tabs/`).
2. Map it in `AreaOf` (`SelfTest.cs`) and register it: `GuardGroup("<group>", TestX)` in `SelfTest.Run`.
3. Run `--only TestX`, then the gate: `--areas basic` and `--require ci`.
