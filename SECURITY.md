# Security model

TabForge is a desktop application that runs as the current user. It does not register services, tasks,
drivers or startup entries (the manifest requests `asInvoker`, `uiAccess=false`), and it never downloads
or executes code at run time. This page describes what the shipped system does and, just as important,
what it does not protect against. Process layout and IPC details are in `ARCHITECTURE.md`.

## Process model

- **UI process.** Owns the song, documents, settings and undo. It never loads plug-ins and is not on the
  audio callback path.
- **Audio engine process.** A separate process (`TabForge.exe --audio-engine ...`) that owns the audio
  device, mixer and plug-in hosting. It talks to the UI over a named pipe created with
  `PipeOptions.CurrentUserOnly`, plus a shared-memory block (default security descriptor) for MIDI, meters
  and a breadcrumb naming the plug-in call in progress. A watchdog ends a hung engine and the UI restarts it
  with the plug-in named by the breadcrumb switched off (quarantined for the session).
- **Plug-in host processes (optional).** A plug-in can run in its own process (`--plugin-host`, control pipe
  also `CurrentUserOnly`), so a crash or hang there loses only that plug-in.

**Process isolation is crash isolation, not a sandbox.** Engine and plug-in host processes run as the same
user with the same rights as TabForge, with no restricted token, AppContainer or file / network limits. A
malicious native plug-in can read and write anything the user can. Load only plug-ins you trust; a separate
process is used for crashes and hangs, not for safety against hostile code.

## Plug-ins

A plug-in is native code. It runs with your Windows permissions and can read and write your files, like any
program you start. Running plug-ins in their own process protects TabForge from a plug-in crash or hang; it
does not protect your files from the plug-in. The option is "Run each plug-in in its own process" in
Settings > Audio & Plug-ins (off by default). Only use plug-ins from sources you trust. The Review plug-ins
prompt and that setting's help say the same.

## Plug-in trust gate

Songs, presets, rigs and auto-load chains only name plug-in paths; they cannot make native code run.
`PluginTrust` decides what is sent to the engine as loadable; everything else is sent as `Skip` and shown to
the user as blocked, with an explicit Allow action.

- **What an approval covers: this binary.** Outside Program Files / Common Files, a scan or an explicit
  approval records the file's size, last-write time, SHA-256 and signing identity. It does not approve a
  folder or a publisher. A file whose content differs is "changed since you approved it" and is not loaded
  until approved again.
- **Where the hash is checked.** Routine checks in the UI compare size and time and hash only on a
  difference. The engine hashes the exact file before every real load, through a handle that denies
  writers and stays open until the plug-in is created, so a same-size, same-time replacement is caught. A
  mismatch blocks the plug-in and is never turned into a new baseline; only the user's approval is.
  Residual gap: a writer that already held the file open for writing, or a driver-level change, is not
  excluded, and a plug-in that is already loaded is not re-checked until it is loaded again.
- **Publisher updates.** A changed file is accepted, and becomes the baseline, only when WinVerifyTrust
  accepts its signature (no UI, no revocation lookup) and the signing key is the same (SHA-256 of the
  certificate's public key; the subject text is display only). A new key needs a new approval.
- **Location trust** applies only under Program Files / Common Files on a local fixed drive, judged on the
  file's final path (links and junctions are resolved), and assumes the default administrator-only write
  ACLs there. Files in those folders are not hashed.
- **Network (UNC) and removable or optical locations** always need an explicit approval, which records the
  hash when the user approves; they are not read during routine checks, and the engine checks the hash at
  each real load, so a changed file does not inherit its path's approval.
- **Linked audio** (clips in a song) is checked by the same idea (`MediaPathPolicy` / `MediaAccess`): device paths and `\\?\` / `\\.\`
  forms, reserved device names, alternate streams and non-audio extensions are refused; network (UNC, mapped) and removable
  or optical folders are not touched until the user approves the folder for that song (revocable, stored in the settings);
  links are resolved to their real target before judging. The only network access TabForge starts on its own is the update
  check; linked media on a network path is read only after that approval. Waveform reading is bounded (2 GiB, 2 h,
  queue of 64, time limit, memory budget) and cancellable, and the engine refuses non-plain clip paths as a last line.
- An unreadable or in-use binary is refused with a message saying so (nothing is loaded on a failed check);
  a missing one is reported by the loader as not found.
- Quarantined plug-ins (blamed for an engine crash or hang) are also sent as `Skip`.
- Plug-in discovery is passive (names and paths); the metadata probe runs in a short-lived helper process.

Plug-in state blobs are size-limited (`PluginStateLimits`, 16 MiB) and stored as SHA-verified chain-state
files that are garbage-collected.

## File input limits and validation

- Project, score and settings files are read through bounded readers (`InputLimits`: e.g. 128 MiB for
  `.tforge` and score files, 2 MiB for settings JSON, JSON depth 64) and are deserialised into fixed
  model types with `System.Text.Json`. Track, bar, cell, note, curve-point, plug-in and text counts have
  hard caps, and values (tempo, frets, strings, colours, hotkeys, workspace layouts) are validated or
  clamped before use. Invalid data fails closed with an error; the existing file is kept.
- Score metadata and embedded paths are never passed to a process or shell. There is no BinaryFormatter and
  no assembly loading from user files.
- User-selected paths are normalised and extension-checked, keeping Unicode and long-path support; Windows
  reserved device names are rejected in generated file names.
- Writes go through `FilePathPolicy`: a complete flushed temporary file, then an atomic replace. The
  `.gp` + `.tfaudio` pair is committed with a "save in progress" marker; if the process dies between the two
  commits, the next open restores the last complete pair from the backup and says so. A failed restore is
  reported with both paths and never deletes the backup. Leftover temp/backup files older than 24 hours are
  swept.
- Settings are normalised on load (`SettingsMigration`), bounded by `SettingsValidator`, and written
  atomically; a bad settings file is preserved rather than overwritten.
- Diagnostics are size-capped and do not include score contents unless an audit report is requested.

## Build and dependencies

The build uses the installed .NET 8 SDK selected by `global.json`; no SDK bootstrapper is downloaded.
NuGet versions are locked (`packages.lock.json`, locked restore) with NuGet audit enabled and warnings
treated as errors. CI actions are pinned by commit SHA. A software bill of materials is generated by
`tools/Write-Sbom.ps1` into `docs/SBOM.md`. Release builds run the self-test on the exact binary that is
packaged. To update a dependency, change its explicit version, regenerate the lock with
`dotnet restore src/TabForge/TabForge.csproj -r win-x64 -p:RestoreLockedMode=false`, and review the diff and
`dotnet list package --vulnerable --include-transitive`.

## Not covered

- Malicious or compromised plug-ins that the user has approved (see above).
- Other software running as the same user: shared memory uses default ACLs, and only the pipes are
  restricted to the current user.
- Confidentiality of songs or settings on disk; nothing is encrypted.

## Reporting

Report suspected vulnerabilities privately to the repository owner rather than in a public issue.
