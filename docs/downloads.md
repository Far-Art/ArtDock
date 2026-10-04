# Downloadable content: packs and updates

Three kinds of content are downloadable: **language packs**, **icon sets**, and **the dock
itself**. The first two are data and are described in [packs.md](packs.md). The third is
code, which is why it is handled apart from them.

**Status.** Updates of the dock are **built**, with [Velopack](https://velopack.io): an
installer, and an update check on the About page — see *Updates of the dock* below. Packs are
**designed, not built**: the contracts are in
[`src/ArtDock/Downloads/`](../src/ArtDock/Downloads) — interfaces and data records, with
nothing implementing them and nothing in the running dock calling them.

## Constraint: free

The dock makes no money, so everything here is chosen to cost nothing, with one optional
exception.

| Piece | Choice | Cost |
| --- | --- | --- |
| The dock's releases | Velopack's packages and feed, as GitHub releases of the **public** `Far-Art/ArtDock-Releases` | Free |
| Installer and updater | Velopack (MIT), `Velopack` from NuGet and `vpk` as a local tool | Free |
| Pack catalog | `catalog.json` on GitHub Pages, from a **public** repository of its own | Free |
| Pack files | Release assets on that repository (up to 2 GB each) | Free |
| Signing | The project's own ECDSA P-256 key, verified by code built into the dock | Free |
| Address | Optionally, a domain of the project's own in front of the host | ~$10–15 a year |

Public repositories rather than the main one: the main repository is private, GitHub Pages
on a private repository is a paid feature, and releases and raw files in one can only be
fetched with a token — which the app cannot carry without handing it to everybody. The packs
may share the releases repository or have one of their own; that is still open.

The domain is the one thing worth paying for, and only because every build that ships keeps
asking the catalog's address for as long as it is installed. An address the project owns
can be pointed at another host later; `someone.github.io/…` cannot. Decide before the first
build that asks.

Not chosen: the Microsoft Store's add-ons, which need the app packaged as MSIX; and paid
object storage or a CDN, which solve a scale problem the dock does not have. Cloudflare Pages
or R2 are the free fallback if GitHub's limits are ever reached.

## The catalog

One file for every pack, so there is one thing to fetch, one signature to check and one place
to publish to. Sketch of `schema` 1 — the records it is read into are in
[`ContentCatalog.cs`](../src/ArtDock/Downloads/ContentCatalog.cs):

```jsonc
{
  "schema": 1,
  "published": "2026-10-01T12:00:00Z",
  "packs": [
    {
      "kind": "language", "id": "de", "name": "Deutsch", "version": "1.2.0",
      "format": 1, "minAppVersion": "0.9.0", "author": "…", "license": "CC-BY-4.0",
      "file": { "url": "https://…/lang.de-1.2.0.zip", "size": 18342, "sha256": "…" }
    }
  ]
}
```

The catalog itself is signed too — a detached signature published beside it — because it is
what says which hashes are genuine. A catalog that could be swapped would make every other
check worthless.

## The pipeline

```
IContentCatalogSource   fetch catalog.json, check its signature
        │
IContentDownloader      fetch one file over HTTPS, never past its stated size
        │
IContentVerifier        size, SHA-256, and the signature when there is one
        │
IPackInstaller          unpack a pack into Packs\<kind>\<id>
        │
IPackManager            what the settings page talks to for packs
```

There was a fifth branch, `IAppUpdater`, for the dock's own releases through this same
catalog. It was removed when Velopack took that job; the catalog lost its release list with it.

Each step is a separate interface so that nothing can reach the disk through a path that
skipped verification: the installer is handed a verified download, and does not download.

### Rules every implementation keeps

- **Only when asked.** The dock contacts nothing unless told to. No check at start-up or on a
  timer until there is a setting that says the user wants one, off by default. The settings
  page for downloads fetches the catalog when it is opened.
- **HTTPS only**, a size limit taken from the catalog, conditional requests (`ETag`) so a
  repeated check costs the host almost nothing.
- **Packs are data.** A pack's zip may hold `pack.json`, a licence or readme as text, and for
  an icon set `.png` files — nothing else. Not an executable, not a script, not a DLL, and
  not XAML, which WPF's reader will turn into arbitrary objects.
- **No zip slip.** Every entry must land inside the pack's folder; an entry with `..`, a
  drive or a rooted path refuses the whole pack. Entry count and unpacked size are limited
  as the archive is read, not as it claims.
- **Checked by the same code that will read it.** A pack is unpacked beside its destination,
  loaded through the same loader the dock uses, and only then swapped in — so an unreadable
  pack never replaces a working one, and a pack in use is never half-updated.
- **A pack installs under the id it was offered as**, and cannot overwrite another.

## Updates of the dock

Built, with Velopack, in `Services/AppUpdater.cs`, `Program.cs` and `tools/release.ps1`. The
plan above had the dock's releases going through the same catalog, downloader and verifier as
the packs, with an `IAppUpdater` of its own to stage a release, hand over to it and switch.
Velopack does that part — the part with the risk in it — and brings the installer it needs,
so it was taken instead and that interface removed.

**How a copy gets installed.** `tools/release.ps1` publishes the app and packages it with
`vpk` into a setup (`ArtDock.App-win-Setup.exe`), a portable zip, a full package, a delta
from the last release, and the feed installed docks read. The setup installs per user into
`%LOCALAPPDATA%\ArtDock.App`, so neither it nor any update asks for administrator rights,
adds shortcuts on the Start menu and the desktop, and an entry in Windows' installed apps to
uninstall it by. The setup, the shortcuts and that entry all carry `Assets/ArtDock.ico`, and
while it installs the setup shows the brand's dark lockup from `brand/installer/splash.png`,
with its progress bar in the magnified icon's blue. The desktop shortcut came later than the
rest. A copy installed without it gets it at its next update, because Velopack makes any
shortcut location a new version adds. It never re-makes a shortcut the user has deleted, and
uninstalling removes both.

**The install id is not "ArtDock", and must never be.** Velopack installs into
`%LOCALAPPDATA%\<install id>` and treats that folder as its own — its uninstaller removes it.
The settings and the packs live in `%LOCALAPPDATA%\ArtDock`. The obvious id would put the two
in one folder, and the first uninstall would take the user's settings with it. The id is
`ArtDockInstallId` in the project file, a test holds it apart from the settings folder, and it
cannot change after the first release: a changed id is a different app, which an installed
dock cannot update into.

**How an update happens.** The About page's *Updates* card has one button. First it checks —
Velopack reads the releases of `ArtDockReleasesRepository` through GitHub's API — and then,
if a newer version is there, it says so and becomes *Update and restart*. Pressing that
downloads the version, a delta where there is one, checked against the feed's SHA-256; starts
Velopack's `Update.exe`, which waits for the dock to exit; and closes the dock the way Exit
does. `Update.exe` swaps the application folder and starts the new dock with the settings
dialog open on the About page, so the new version number is the first thing shown. A copy
the setup did not install — a build run from the source tree, as `ArtDock.cmd` runs it —
says so on the card and offers nothing.

**Skipped versions are skipped.** A copy any number of versions behind is offered the newest
release and goes straight to it; nothing in between is installed, and a new user's setup is
the newest one's. Velopack reads the ten newest releases and merges their feeds, so a copy up
to ten versions behind can chain the releases' deltas, each from the release before it, when
the chain is smaller than the full package; further behind, or when a delta fails to apply, it
downloads the full package. That holds only while nothing in the dock depends on having run a
particular version: no work is done in an update hook, and the settings are brought up to date
when they are read (`DockSettings.Migrate`), one `Version < n` step after another, every step
kept for good — so a file of any age reaches the current format in one start. A step removed,
or an upgrade done once on update rather than on read, breaks every copy that skipped the
version doing it.

**Never unannounced.** The dock sits on screen all day. Nothing goes online until the button
is pressed, nothing is applied until it is pressed again for the version found, and the card
says before then that the dock will close. A setting to check by itself is not built, and
when it is it will be off by default.

**Autostart follows the install.** The setup turns it on — Velopack's after-install hook,
`AppUpdater.OnInstalled`, not the dock's first run, which a reinstall is not (the settings
outlive the uninstall) and a silent install never reaches. It takes the Run entry over from
any other copy; updates do not run that hook, so a user who turned autostart off keeps it
off. In an installed copy the Run key points at Velopack's launcher in the install folder
rather than at the executable, which lives in a folder every update replaces; the setup puts
the launcher there before it calls the hook. The uninstaller removes the entry on its way
out — only if it points into that installation, so uninstalling one copy cannot turn
autostart off for another.

**Uninstalling asks about the settings.** They live outside the installation, so they outlive
it — which is what lets a reinstall bring the dock back as it was. The same before-uninstall
hook, `AppUpdater.OnUninstalling`, asks with `Views/UninstallWindow`: *Keep settings*, the
default, or *Delete settings*, which removes `%LOCALAPPDATA%\ArtDock`, packs included, and the
folder of the app's former name, `ImsDock`, which a later install would otherwise adopt as
its settings. The window is WPF, themed and in the user's language, made in a process that
has no `App` — Velopack runs the hook before `Program.Main` gets that far and exits after it.
Velopack kills the hook 30 seconds after starting it, so the question counts down and
answers Keep at 25; Enter, Escape, the close box and anything going wrong on the way to
asking all answer Keep too. (The 1.2.158 uninstaller's log says it waits 60 seconds; the
documented 30 is the figure relied on.)

**Nothing Windows wrote down is left either.** After an uninstall on 2026-10-04 the registry
still held, about the installed copy: its tray icon (`Control Panel\NotifyIconSettings`, which
keeps ArtDock listed in Settings' *Other system tray icons*), `MuiCache`'s names for its
executables, `UserAssist`'s launch counts (names in ROT13), `FeatureUsage`'s, the Program
Compatibility Assistant's record of each executable, Start's tile properties and jump-list
timestamp, two entries in Start's cloud store, Windows' note that it had announced the startup
entry (`RunNotification`), Windows Backup's queued events for the install and the uninstall
(`AppListBackup`) — and on disk the jump list (`Recent\AutomaticDestinations\ddc8f37c9e9e9633…`,
a CRC-64 of the app id) and Velopack's log, `%LOCALAPPDATA%\velopack\velopack_ArtDock.App.log`.
The hook removes them after the question, whatever its answer (`Interop/WindowsTraces`;
`RunNotification` with the Run entry, in `Autostart`). An entry is the installation's when it
names a path inside the install folder, the app id `velopack.ArtDock.App`, or the setup by its
file name; anything about a copy run from elsewhere stays.

Some of it is written after the hook: Velopack logs until it exits; Explorer writes the jump
list, its timestamp and the launch counts as the question's window and Velopack's closing message
go; Windows Backup queues the uninstall once Velopack has removed the entry in installed apps. So
the hook also leaves a hidden Windows PowerShell behind — plain `-Command` text, every name in a
literal single-quoted string — that waits for Velopack's `Update.exe` to exit, five seconds more,
and then deletes all it found, what it can name ahead, the backup queue's events for the app
(matched as text: Windows writes them as JSON with unescaped backslashes), the log, and
Velopack's folder if nothing else is in it. Waiting for `Update.exe` rather than for a time
because an interactive uninstall ends on Velopack's *Uninstall Complete* box, which stays until
dismissed. Machine-wide records — Prefetch, the Amcache, the background activity moderator — need
administrator rights, which the uninstaller does not have, and stay.

**No dock is left running.** Velopack stops every process running from the install folder
before it runs the hook — its log says *Killing process* — so the copy being uninstalled is
gone, though not by its own Exit. A copy running from anywhere else, such as a build from the
source tree, it does not touch, and that copy shares the settings. So the hook, before it
asks, signals the running dock to exit (`SingleInstance.CloseRunning`, a third named event
beside the relaunch ones) and waits up to five seconds for the single-instance claim to be
let go of. The dock answers it with the same `Quit` its Exit uses.

**What Velopack does not give, that this design wanted:**

- **The project's own signature.** Velopack checks a download against its feed's hashes, and
  the feed comes from GitHub over HTTPS — so the trust is in the GitHub account. Whoever
  took that over could publish a release every installed dock would take. The plan's answer
  still applies, and fits Velopack's `IUpdateSource`: sign the feed with the project's
  ECDSA P-256 key, and have a source that checks the signature before handing the feed on.
  Not built.
- **Automatic rollback.** A bad release is recovered from by publishing a fixed one. So a
  release that raises the settings format (`DockSettings.CurrentVersion`) must still write
  files an older build can read, or going back a version by hand would cost the user their
  settings.
- **Authenticode.** Nothing is signed, so the setup meets SmartScreen's warning when it is
  downloaded in a browser. Updates the dock fetches itself do not. `vpk pack` takes
  `--signParams` for a certificate, or `--azureTrustedSignFile` for Azure Trusted Signing,
  when there is one; a certificate costs a few hundred dollars a year.

## Publishing

**The dock:** bump `<Version>` in the project file, then

```powershell
.\tools\release.ps1                                  # build into artifacts\releases, to try
.\tools\release.ps1 -Publish -ReleaseNotes notes.md  # build and publish
```

Publishing needs `$env:GITHUB_TOKEN`: a token that can write to the releases repository — a
fine-grained one with *Contents: read and write* on that repository alone is enough. The
script refuses a working tree with uncommitted changes unless given `-AllowDirty`, reads
every name and address from the project file, and keeps `vpk` at the version of the
`Velopack` package, which `dotnet-tools.json` pins; raise the two together.

**Packs:** by hand is how a catalog ends up with a hash that matches nothing. A small tool
should zip a pack folder, hash it, sign it, upload it, and regenerate and sign the catalog,
in one step. Not written.
