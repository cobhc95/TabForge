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
- `tools\SHIP.cmd` refuses to push if you are not on `main`, there are uncommitted changes, the notes are missing, the tag already exists, the version is lower than an existing tag or private files are tracked. The update check compares each part of a version as a whole number (0.8.75 is higher than 0.8.8), so a lower number would never be offered to anyone on the higher one.

The slower release test set (`--areas release`) and the full suite are not part of a release. Run the release set when you want it with `tools\Package-Release.ps1 -FullGate`.

## Other workflows

Only `release` runs on a tag. `portable-ci` runs on code pushes to `main`, does not block a release and does not need to be watched. Everything else is started by hand from the Actions page.

## Release conventions

These apply to every release. The `release` workflow publishes `docs/releases/<version>.md` as the GitHub release body, so that file must be right before `tools\SHIP.cmd` runs.

**Release notes** (`docs/releases/<version>.md`)

- Start with `# TabForge <version>`, then one sentence on what the release is: stable, beta, or what it supersedes.
- Then `## 🎸 New since <previous major>` with bold-labelled bullets, the screenshots, and `## 🛠️ Fixes and performance`.
- Screenshots go in a pipe table with an emoji caption in the header row and `<img>` tags at `width="400"` (560 for a very wide image). Use absolute `raw.githubusercontent.com` URLs pinned to the tag, for example `https://raw.githubusercontent.com/cobhc95/TabForge/v<version>/docs/media/screenshots/<version>/name.jpg`. Never use `../` relative links: they break on the GitHub release page.
- No Downloads table. The release's Assets list shows the files.
- End with the unsigned-build and Linux-experimental note, then the complete-changelog and compatibility-notes links, both absolute at the tag.

**Assets**

Use exactly these names, so GitHub's alphabetical list keeps Linux and Windows files together:

- `TabForge-<version>-linux-x64.tar.gz`
- `TabForge-<version>-linux-x86_64.AppImage`
- `TabForge-<version>-win-setup.exe`
- `TabForge-<version>-win-x64-portable.zip`

Do not attach checksum files or source zips. GitHub's automatic source archives are the source download. The SoundTouch.Net source is linked upstream in `docs/THIRD_PARTY.md` and covered by its written offer.

**README** (`docs/README.md`)

- The title and download links name the current version.
- "What's new" covers only the current version. Older history goes in the collapsed "Earlier in…" block.
- Verify downloads with `gh attestation verify`, not checksum files.
- Do not move or rename files in `docs/media/screenshots/`. Older releases link to them on `main`.

**Publishing**

- Pushing to the public repository and changing a public release need the owner's go-ahead for that release.
