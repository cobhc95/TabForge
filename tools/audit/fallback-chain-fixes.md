# Fallback-Chain Fixes A–F — verbatim from core task-73

## (A) Fallback CHAIN with varying PARENTS (core recommendation)
Replace the single `FallbackWritableFolder(kind)` (%LocalAppData%\TabForge\<kind>,
probed) with an ordered, varying-parent chain — e.g.
`%LocalAppData%\TabForge\<kind>` → `%Temp%\TabForge\<kind>` → song folder → null —
each level write-probed before use. Kills the common-mode parent failure:
today's fallback shares ancestors with nothing else, but a single level still
dies with its parent (quota/redirect/CFA on that tree).

## (B) Empty-root → null (HIGH severity edge, one line)
If `Environment.GetFolderPath` returns "" (service/session-0/unloaded profile),
`Path.Combine("", ...)` yields a RELATIVE path that would create folders under
CWD (Program Files → denied; elsewhere → litter). Treat empty root as null
(fallback level skipped) instead of probing a relative path.

## (C) Drop flushToDisk in the probe (one-word change)
`TryProbeWritableFolder` writes 1 byte + `Flush(flushToDisk: true)`. A 1-byte
probe needs no durability; the flush doubles AV-filter exposure and can fail on
SMB/redirected stacks where create+write succeed. Flush plain (no forced disk
flush). Also halves the UI-thread stall surface (probe runs synchronously).

## (D, optional) Single 100ms retry on Unauthorized only
One delayed retry for transient AV-indexer locks. Optional; keep single-shot if
the team prefers predictability.

## (E) Trace which chain level won
Per-level `Trace` stage lines already exist for validate/create/probe; extend the
same idiom to each chain level (`fallback-1/fallback-2…`) so errors.log names
the exact hop that succeeded or failed.

## (F) Docs note for enterprise CFA-on-AppData
If Controlled Folder Access (or AV ransomware shields) covers AppData, no
on-machine fallback can succeed — document the allow-listing step in
troubleshooting docs. Code cannot fix policy.
