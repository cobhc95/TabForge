# 0003. alphaTab is built from source with minimal patches

Status: accepted

## Context

TabForge reads and writes .gp files through alphaTab (MPL-2.0). Upstream has a hard-coded bar limit for older .gp files that no setting can change, loses precision of the mixer volume and balance of newer .gp files, and does not write the trill speed. A global, mutable setting would let two imports running at the same time affect each other.

## Decision

TabForge ships its own build of one pinned upstream release as a different package (`TabForge.AlphaTab`), so it cannot be mistaken for the upstream one. The change is a small, ordered patch set kept in `Source/vendor/alphatab/`; each patch is documented there. `Source/tools/Build-AlphaTab.ps1` clones the pinned commit, applies the patches and builds; CI rebuilds it and compares the result. The package is consumed from a local NuGet source (`nuget.config`) and is locked in `packages.lock.json`.

The rest of the app reaches alphaTab through one class, `AlphaTabBoundary` (`Source/src/TabForge.Core/Services/AlphaTabBoundary.cs`). It checks at run time that the patched build is the one loaded, and sets the per-import limit on that import's own settings object.

## Consequences

- Behaviour that upstream does not offer is available without global state.
- We carry the patches. Moving to a new upstream version means re-applying and re-checking each one.
- The build is reproducible and its provenance is public (`Source/vendor/alphatab/README.md`, `Source/docs/REPRODUCIBLE_BUILDS.md`).
- A patch must be small and justified. Anything that can be done outside the library is done in `AlphaTabBoundary` instead.
