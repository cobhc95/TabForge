# 0002. .gp import runs in a limited worker process

Status: accepted

## Context

A song file is untrusted input. The parser (alphaTab) can use a lot of memory or get stuck on a crafted file, and a failure inside the window process would take the editor with it.

## Decision

An import runs in a worker process (`Source/src/TabForge.Core/Services/ImportWorker.cs`, the same executable). The worker is confined by a Job Object with a committed-memory limit (2 GiB by default). The project it converts comes back in a compact, size-capped form, so the UI never parses the original file. `ImportGuard` (`Source/src/TabForge.Core/Services/GuitarProImportGuard.cs`) adds a time budget (60 seconds by default) and cancellation; a worker that does not answer within a short grace period is killed. Imports are queued and run one at a time (the WPF app's `ScoreImportQueue`, git HEAD only). Input limits (file size, tracks, bars, cells) are in `Source/src/TabForge.Core/Services/InputLimits.cs`.

If the worker cannot be started or confined, the file is imported in the UI process only with the user's consent for that file.

## Consequences

- A hostile or broken file can fail or be killed without affecting open songs.
- An import costs a process start and a copy of the converted result.
- The limits are constants in one place, and tests have seams that simulate a hung worker and a Job Object failure.
- The worker limits memory and time; it is not a security sandbox.

See `ARCHITECTURE.md`, section 6.
