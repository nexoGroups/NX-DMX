---
name: release
description: Publish a new NX-DMX version. Updates the README, screenshots, version and release notes, pushes to GitHub, tags it so the installer and update packages are built and published, and checks that installed copies can update. Use when the user asks to release, publish, ship or bring out a new version of NX-DMX.
---

# Releasing NX-DMX

A release is a git tag `vX.Y.Z`. Pushing the tag starts `.github/workflows/release.yml`, which runs the tests, builds the
self-contained app, packs it with Velopack (`tools/pack.ps1`) and publishes the GitHub release with `NxDmx-win-Setup.exe`,
the full and delta packages and the update feed. Installed copies find that release themselves and show the green update
button. Nothing is uploaded by hand.

Pushing to GitHub and tagging are public actions. Being asked to release (this skill) is the go-ahead for the version
you agreed on, nothing more. If the version number, the scope or anything unusual is unclear, ask first.

Work in `C:\Users\camer\Documents\NX-DMX`. Windows PowerShell 5.1. In a shell that was open before the installs, refresh
the path first: `$env:Path = [Environment]::GetEnvironmentVariable('Path','Machine') + ';' + [Environment]::GetEnvironmentVariable('Path','User')`.

## 1. Check the starting point

- `git status` must show only the changes that belong in this release (or nothing); the branch is `main`.
- `gh auth status` must show the `nexoGroups` account. If not, run `gh auth login --web --scopes repo,workflow` in the
  background and tell the user the one-time code; they confirm it in the browser. Never enter credentials yourself.
- `dotnet test NxDmx.slnx -c Release` must pass. Do not release with failing tests.

## 2. Pick the version

Semantic versioning. Look at `git describe --tags --abbrev=0` and `git log <last-tag>..HEAD --oneline`.
Pre-1.0: a new feature is a minor bump (0.2.0 to 0.3.0), a fix-only release is a patch bump. If the user named a version,
use it. The tag is `v` + version, the version number has no leading `v`.

## 3. Prepare the release commit

1. **Version:** set `<Version>` in `src/NxDmx.App/NxDmx.App.csproj`. (CI takes the version from the tag, this is the
   default for local builds.)
2. **Release notes:** create `docs/release-notes/<version>.md` in German, like the existing ones: a heading
   `## NX-DMX <version>: <short title>`, then **Neu**, **Hinweise** (and **Behoben** when there are fixes). Base it on the
   commits since the last tag, written for operators, not as a commit list. Pack picks this file up automatically and it
   becomes the text of the GitHub release.
3. **README.md** (German): keep it true to what the app does now.
   - "Was schon geht" and the "Stand" paragraph: add new features, drop things that are no longer missing.
   - Command table, "Schnellstart" and the matching sections if commands or keys changed.
   - "Roadmap": tick finished items, add new ones.
   - Anything that mentions a version number or a limitation that changed (for example the update section).
   Re-read the whole README against the code. Do not leave a claim in that you did not check.
4. **Screenshots** (only if the UI changed): `tools/screenshots.ps1 -Exe src\NxDmx.App\bin\Debug\net10.0-windows\NX-DMX.exe`
   after `dotnet build NxDmx.slnx`. The script runs the app with its own data folder (`NXDMX_DATA`), so the user's real
   autosave and settings are not touched. It writes `overview.png`, `patch.png`, `profiles.png`, `universes.png` and
   `remote.png` to `docs/images`; look at them afterwards. The app window comes to the front while it runs.
5. If the Velopack package version in the csproj changed, change `VPK_VERSION` in `release.yml`, the version in
   `tools/pack.ps1`'s comment and the README to match. The `vpk` tool and the NuGet package must have the same version.

## 4. Commit and push

- Commit with the noreply identity (already set in the repo): `user.email` is `267294461+nexoGroups@users.noreply.github.com`.
  Never use the user's private mail address.
- Write the message to a file and use `git commit -F <file>` (a here-string piped to `-F -` does not work in PowerShell
  5.1). End it with `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`.
- Git has no global credential helper. Push with the GitHub CLI's token for this one command:
  `git -c credential.helper= -c "credential.helper=!gh auth git-credential" push origin main`
- Wait for CI on that commit: `gh run list --repo nexoGroups/NX-DMX --limit 3 --json workflowName,status,conclusion,headBranch`
  until the CI run for `main` is `completed` / `success`. If it fails, fix it first. Never force-push.

## 5. Tag and publish

```powershell
git tag -a vX.Y.Z -m "NX-DMX X.Y.Z"
git -c credential.helper= -c "credential.helper=!gh auth git-credential" push origin vX.Y.Z
$id = gh run list --repo nexoGroups/NX-DMX --workflow Release --limit 1 --json databaseId --jq '.[0].databaseId'
gh run watch $id --repo nexoGroups/NX-DMX --exit-status
```

When the run is green, check the result: `gh release view vX.Y.Z --repo nexoGroups/NX-DMX --json assets,isDraft,url`.
Expected assets: `NxDmx-win-Setup.exe`, `NxDmx-X.Y.Z-full.nupkg`, `NxDmx-win-Portable.zip`, `RELEASES`,
`releases.win.json`, plus `NxDmx-X.Y.Z-delta.nupkg` from the second release on. It must not be a draft.
If the workflow failed halfway, read the log (`gh run view <id> --log-failed`), fix on `main`, then move the tag only
if no release for it exists yet. Ask the user before deleting a published release or tag.

## 6. Prove that updating works

The point of the whole setup is that people who installed the previous version get this one. Test it for real once the
release is public:

1. Download the **previous** version's installer from its release into a new empty folder, for example
   `gh release download vOLD --repo nexoGroups/NX-DMX --pattern NxDmx-win-Setup.exe --dir <new-folder>`.
2. Run `powershell -File tools/test-update.ps1 -Setup <that Setup.exe> -ExpectVersion X.Y.Z`. It installs the old version
   into a temp folder, lets the app find the new release on GitHub, clicks the update button and the dialog, and checks
   that the new version is installed and restarted. It uninstalls everything afterwards. Also run it once with
   `-Answer no` when the update code changed (that path installs when the app closes).
   It needs port 6454 free (no other Art-Net software running). It removes `%AppData%\NX-DMX` afterwards unless that
   folder existed before; if the user uses NX-DMX for real, their autosave there is never touched by the cleanup, but the
   test app does write to it while it runs, so ask the user to close their own NX-DMX first.
3. Before publishing, the same test works against local packages: `tools/pack.ps1 -Version A -OutputDir artifacts\feedtest`
   twice (A < B), keep the Setup of A, and pass `-Feed artifacts\feedtest`.
4. When the old installer is not available (first release), install the new `Setup.exe` into a temp folder the same way and
   at least check that the check reports "up to date".

Pitfalls found while building this: WPF message box buttons appear as panes (click them with `BM_CLICK` on the window
handle, `Invoke` does not work); UI Automation `Invoke` returns before the click is handled; the updater applies a
downloaded update automatically at the next start, so never reuse an install folder between test runs; use `Update.exe
--silent uninstall` and wait before deleting a folder because it removes it itself after a few seconds.

## 7. Clean up and report

- Remove temp folders, `artifacts\`, `Releases\`, and `%AppData%\NX-DMX` if a test run created it. Nothing of the tests
  may stay installed on the user's PC (no desktop or start menu shortcut, no entry in "Apps").
- `git status` should be clean and `main` equal to `origin/main`.
- Tell the user, in German: the version, the link to the release, the direct installer link, what changed, what was
  verified (CI, release assets, update test) and what was not.
- Update the memory file `nx-dmx-project.md` when the process itself changed.

## Things never to do

- No manual upload of installer files, no re-tagging an existing version that people may already have installed.
- No release without green tests and a green CI run on the release commit.
- No force-push, no `--no-verify`, no changing global git config (credential helper) without asking.
- Do not put tokens, the private email address or local paths into commits, release notes or the README.
