# Releasing

A release takes under 4 minutes from one command.

1. Set `<Version>` in `Directory.Build.props`.
2. Write the release notes in `docs/releases/<version>.md` and add a section to `docs/CHANGELOG.md`.
3. Commit to `main`.
4. Run `tools\SHIP.cmd`.

The command pushes `main` and the `v<version>` tag to the public repository. The `release` workflow then builds the Windows and Linux packages and publishes the GitHub release, marked Latest, using those notes.

## What is checked

- The basic self-test (about 5 seconds) runs on the exact Windows folder that gets packaged.
- Packaging refuses to continue if native helper hashes, licence files or the demo song are wrong or missing.
- `tools\SHIP.cmd` refuses to push if you are not on `main`, there are uncommitted changes, the notes are missing, the tag already exists or private files are tracked.

The slower release test set (`--areas release`) and the full suite are not part of a release. Run the release set when you want it with `tools\Package-Release.ps1 -FullGate`.

## Other workflows

Only `release` runs on a tag. `portable-ci` runs on code pushes to `main`, does not block a release and does not need to be watched. Everything else is started by hand from the Actions page.
