# ArtDock

A feature-rich, highly customizable application dock for Windows 11, inspired by macOS. Pin
applications, files and folders, and launch or switch between them from one place. Fast,
responsive and built to feel at home on Windows.

## Highlights

- **Steps aside for a window that fills the screen.** When the window in front is maximized
  or fullscreen on the dock's display, the dock slides off the edge as auto-hide would,
  even with auto-hide turned off. Hold the pointer against the bottom edge under it and it
  comes back up; once that window is no longer in front, the dock is back where it was.
- **An exclusion list for the programs it must never interrupt.** Games, mostly, which scroll
  when the pointer reaches the edge of the screen. While a listed program fills the display,
  the edge does nothing and the dock stays down; while one is in front at all, the dock's
  hotkeys are let go of, so their keys reach it. The list can be filled by scanning: the
  games installed by Steam, Battle.net, the EA app, Epic Games, GOG, Ubisoft Connect, the
  Xbox app, Riot Games and Rockstar Games, or the apps on the Start menu.
- **Settings that show on the dock as you change them.** Every slider, colour and switch in
  the settings dialog reaches the dock at once, so a change is judged on the dock itself
  rather than imagined. Nothing is written until Save, and Cancel puts the dock back the way
  it was found.
- **A handle that marks a hidden dock.** While the dock is out of sight — slid away, or under
  other windows — a slim bar just above the taskbar shows where it will come up. It inverts
  the colours behind it, so it stands out on any background.
- **Blur behind the bar.** The bar sits on a sheet of Windows acrylic, which blurs whatever
  is behind it — drawn with the bar and its shadow in one piece, so the three never come apart.

Each of these, and everything else the dock does, is described under
[What it does](#what-it-does).

## Running it

```bash
dotnet run --project src/ArtDock
```

The dock appears centred on the bottom edge, with a tray icon for settings. Right-click the
tray icon for **Settings…**, **Hide dock** — **Show dock** while the dock is out of sight,
auto-hide's slide included — and **Exit**. A dock hidden from there stays away until it is shown
again, but for the settings dialog, which brings it up to show its preview and puts it away
again when it closes. `ArtDock.exe --settings` opens the dialog directly, as the dock's
**Win+Ctrl+I** does, and `--keyboard` and `--toggle` do what its hotkeys for the keyboard
and for hiding do — take the keyboard, and hide or show the dock — whether the dock is running
already or not, so a mouse button, a macro pad or a script can do it without a key registered.
The dialog's About page has an **Exit** of its own, for when Windows has tucked the tray icon
away behind the overflow arrow. Either one discards anything unsaved in the dialog, the way
Cancel does.

## Installing and releasing

Released copies are installed by a setup, `ArtDock.App-win-Setup.exe`, built by
[Velopack](https://velopack.io). It installs per user into `%LOCALAPPDATA%\ArtDock.App` —
no administrator rights, then or on any update — with shortcuts on the Start menu and the
desktop, and an entry in Windows' installed apps to remove it by. The installed dock starts when you sign in; the
System page's *Start ArtDock when I sign in* turns that off, and an update leaves it as it
was. Settings stay in `%LOCALAPPDATA%\ArtDock`, apart from the install, so updating does
not touch them. Uninstalling asks: *Keep settings*, the default, leaves them for the next
install to find; *Delete settings* takes the folder, packs and all, so nothing of ArtDock is
left. A dock still running is closed before the question — the installed one by Velopack,
any other copy by the uninstaller asking it to exit. The question keeps them by itself if nobody answers within about 25 seconds, since
Velopack stops waiting for it at 30.

To make a release, bump `<Version>` in `src/ArtDock/ArtDock.csproj` and run

```powershell
.\tools\release.ps1
```

which builds the setup, the packages and the update feed into `artifacts\releases` to try.
`-Publish` also uploads them as a GitHub release of the public
`Far-Art/ArtDock-Releases`, which installed docks read their updates from; it needs a
token that can write there in `$env:GITHUB_TOKEN`. The details, and what Velopack does not
cover, are in [docs/downloads.md](docs/downloads.md).

## What it does

- **Magnifies** icons as the pointer approaches, on a cosine falloff, with neighbours lifting
  alongside the hovered one.
- **Launches pinned apps**, or raises them if they already have a window. Clicking an app
  that is already in front cycles through its windows. **An app still closing** — its window
  gone, its program not yet finished — has a hollow dot, and a click on it waits until it has
  gone and then opens it, rather than starting a second copy into the one on its way out; Rider
  refuses that with an error. A program that stays in the tray counts as closing for six
  seconds at most. An app opened from the dock gets the
  environment you signed in with, as from the taskbar, however the dock itself was started:
  a dock run from a terminal or an IDE does not hand that shell's variables to everything it
  opens.
- **Shows an app's windows, live, above its icon**: rest the pointer on a running app for the
  *Preview delay* (250 ms unless changed), and a panel of cards comes up over it, a picture of each window
  under its title — Windows' own thumbnails, which cost the dock nothing however fast the
  window changes. Click a card to go to that window; its close button, or a middle click,
  closes it, except a window of a program running as administrator, which the dock cannot
  close and offers no button for. The panel rises and fades in as it opens, slides across to
  another running app when the pointer rests on that one, and fades out as it closes, as
  Windows' does — none of it with *Reduce motion* or Windows' animations off. Too
  many windows to fit across the display are shrunk alike, then listed by title. The icon
  stays magnified under the panel while the pointer is on it, and a click on the icon still
  cycles. On by default, on the Behaviour page, with the delay beside it.
- **Shows a dot** under every app that has an open window, kept current by window events
  rather than polling. A pinned shortcut is matched by what it points at, so one dragged
  out of the Start menu lights up like anything else. **A folder has the dot while it is
  open**: a folder, a drive, This PC or the Recycle Bin, while a File Explorer window shows
  it in any of its tabs — the folder itself, not one inside it — and its previews are those
  windows. A window that goes to another folder takes the dot with it. A click goes to such a
  window, bringing the folder's tab to the front if another is in front of it, on to the next
  window if there are several, and opens the folder when none shows it; a preview's card brings
  its window forward on that tab too. The Add menu's *File Explorer* is lit by every folder
  window, as the taskbar's is.
- **Floats above other windows**, or not — a dock that can be covered like any other window
  is one setting away. Either way it is **never over a window that fills its display** — a
  game, a video, a presentation, borderless or not, or any window maximized there with the
  taskbar showing: while one is in front the dock **slides away, as auto-hide does**, pops
  back up over it when the pointer reaches the bottom edge, and slides off again once the
  pointer has left; when that window goes, the dock slides back. If the pointer is on the
  dock when the window takes the display — an icon just clicked that opens maximized — it
  waits until the pointer leaves. The taskbar, Alt+Tab, Start and the like coming to the
  front leave it as it is, so switching between windows does not bounce it. Only the window
  in front counts, so one left maximized while you work on another display has the dock back
  over it. A dock put away from the tray stays away through all of this.
- **Auto-hides** to the screen edge and returns when the cursor reaches the bottom — over
  whatever has the focus, *Always on top* or not. It used to slide up *under* the focused
  window when the dock did not float. With the setting off it still hides, as above, while a
  maximized or fullscreen window is in front. The bottom **under the dock**, that is: across
  the bar and no wider, so it lines up with a handle as wide as the dock, and the strip below a
  dock that is up keeps it up across the same width. Until 2026-09-30 it was the dock's whole
  window and 80 pixels more at each end — the window keeps room beside the bar for the wave —
  about half as wide again as the dock.
- **Marks itself with a handle while it is out of sight**, unless told not to on the Behaviour
  page: a slim bar just above the taskbar, centred under where the dock will come up — the
  phone's home indicator, on a desktop. Out of sight means slid away — by auto-hide, or for a
  maximized or fullscreen window in front — or on screen with none of the bar showing for the
  windows over it, as a dock that does not float can be; a dock only partly covered can be
  seen, and has none. So it works with auto-hide off as well, and the setting is no longer
  greyed without it. It fades in as the dock slides away or is covered, at the slide's pace,
  and out in a fraction of that as it comes back — gone before the rising bar has passed it,
  since the bar comes to rest just above it. **It is a mark only**: the pointer resting on it does nothing, and holding
  the edge is what brings the dock up — resting on it used to, and was taken away because it
  lies over the bottom rows of other windows, where the pointer goes on business of its own.
  Clicks go straight through it to the window underneath, which is usually the bottom row of
  something maximized. It is as wide as the dock, following it as
  icons come and go, or a width of its own. It **inverts the colours behind it**, so it stands
  out on anything; the inverse is pushed away from the colour behind, since plain inversion
  gives mid-grey back as mid-grey and the handle would vanish into a grey status bar. What is
  behind is read off the screen on a thread of its own: fifteen times a second while it keeps
  still, for about half a percent of one core, and every frame while it moves, so a page
  scrolling or a video playing under it is followed frame for frame. Reading every frame costs
  Windows' compositor about a tenth of one core, so it is kept for movement — two changes close
  together; a caret blinking under the handle does not count. The handle is kept out of
  screenshots and recordings — which is
  what keeps it out of its own reads (see *Known gaps*). There is no other look to choose: a
  checkbox offered the bar's colour instead, and was taken away as a choice not worth a
  setting. The bar's colour remains only as a fallback, where Windows will not keep a window
  out of captures, and as what *No GPU* gives it, where the screen is not read at all. It
  **floats over everything**, and climbs
  back to the top when a window that floats comes to the front over it — the dock's own
  included, so a dock set to float does not lay its bar's shadow across the handle while the
  settings dialog shows one under the other. **It is not drawn over anything fullscreen**: a
  video, a game or a presentation has the whole display, and the handle would sit on the
  picture. A dock hidden for a maximized window is still marked, and the edge brings the dock
  up over either. A program on the **Exclusions** page counts only when it fills the display,
  so it never has a handle over it either — and the dock does not come up over one at all.
  Fullscreen is the window in front covering the whole display, past the taskbar; where the
  taskbar hides itself a maximized window covers it too, and is told apart by its title bar —
  a game's borderless fullscreen is a maximized window without one. A dock hidden from the
  tray has none: it was put away on
  purpose, and only the tray brings it back for good. While the settings dialog is open it shows under
  the dock, so its width can be seen while it is set.
- **Blurs what is behind the bar**, with a sheet of Windows acrylic, which draws the bar and
  its shadow along with the blur, so the three move as one.
- **Follows Windows' *Transparency effects*** (Settings › Personalization › Colors). Off, Windows
  makes the taskbar solid, and the bar goes solid with it, with no blur behind it — painted as
  *No GPU* paints it, its colour at its opacity over grey, so the Opacity slider still does
  something. Only the bar: the shadows and the handle stay as they were. The blur keeps its
  setting, greyed on the Appearance page with a line saying why, and comes back when the
  switch does; the switch is heard while the dock runs, and the dialogs and window previews,
  which are on Windows' own materials, follow it as they always have.
- **Runs without a graphics card**, when told to: *No GPU* on the System page, for a virtual
  machine or a remote session, where everything the dock asks of the compositor falls to the
  processor. On, the dock is drawn in software, with no Direct3D device at all, and gives up
  what leans on one: the bar is solid, with no blur behind it — painted not in its colour
  alone, which came out far lighter than the bar had ever looked, but in its colour at its
  opacity over a fixed grey that stands in for the desktop, so it keeps about the look it was
  chosen for and the Opacity slider still does something; the handle takes the bar's
  colour instead of reading the screen to invert it, and is back in screenshots; and every
  dialog goes without Mica, on the theme's plain background. It also draws less: **neither the bar nor the
  icons cast a shadow**. The bar's is a stack of see-through rounded rectangles under a clip,
  drawn again on every frame of the wave, which on a graphics card is about a tenth of what the
  wave costs and in software was measured at most of it — the settings dialog's sweep at about
  85% of one core with the shadow and 14% without, at 120 frames a second; the icons' are one
  more picture to scale for every icon on every frame. The wave itself is drawn at the
  display's own pace, as it is everywhere: for an evening it was held to thirty frames a
  second, which with the shadow gone saved three points more for two frames in three, and was
  taken out again. Magnification itself stays — *Disable magnification* is beside it
  for a machine that cannot keep up even so. The blur and the icons' shadows keep their own
  values underneath, greyed on their pages with a line saying why, and come back when it is
  turned off. The
  dock turns it on by itself once only: at its first run, if WPF reports that it has no
  hardware to draw with, it starts with *No GPU* on and says so in a message — what it did, and
  that it is turned off in Settings, on the System page. A dock that already has settings is
  never switched under its owner; there the checkbox has a line under it that says when this
  machine looks like one it is for, and leaves the choice there. Its card is the one on
  the page drawn in Windows' caution colours — the theme's own, so they follow light and dark —
  since it is not a box to tick in passing.
- **Casts a faint shadow under each icon**, falling a little below it so the icons stand off
  the bar — *Shadow under the icons* on the Icons page, on unless turned off. Only the
  shadow is added: each icon is drawn over it exactly as it is drawn without one, and none of
  the shadow lies under the icon itself.
- **Pulses an icon twice** when a click actually launches something (not when it just
  raises a window that was already open).
- **The Items page is a picture of the dock**: every row shows its item's icon as the dock
  draws it — from the icon set chosen on the Icons page, a customized folder in its own colour,
  and the Recycle Bin full or empty as it is now, following it while the dialog is open — and a
  folder is marked at the row's far end with Windows' folder outline, since
  a folder drawn in a colour or by an icon set does not always look like one. The mark is on
  exactly the items the editor offers *Customize* for: a folder on disk, `shell:Downloads`
  included, and not This PC or the Recycle Bin. **Selecting a row holds that item up on the
  dock**, magnified and labelled as if the pointer were on it, and the wave travels from one
  item to the next as the selection moves down the list; the item stays held as the list is
  reordered, and one just added is held as soon as it is on the dock. Only while the Items page
  is open — the other pages go back to the middle icon, or to the sweep, but for the Icons page,
  which holds an item up with its label showing so the labels' lettering can be judged where it
  is used: the row selected on the Items page, or the item nearest the middle, and never a
  separator, which has no label to show. The pointer outranks
  it, as it outranks the sweep: on the dock it takes the wave and the label, and the selection
  has them back when it leaves. A sweep turned on stands aside for a selected item and picks
  up from it when the selection goes. **A click on nothing in particular puts the selection
  down** — on the page around the list, or the empty part of the list below its last row — and
  the dock lets go with it; the buttons beside the list keep it, since they act on it, and so
  do the gaps between rows, where a click is a near miss rather than a choice of neither. Those
  buttons carry the glyphs the dock's menus draw for the same commands — *Remove* the menu's
  unpin, *Item settings…* its pencil, *Add* its plus — with arrows for the moves and a broom
  for *Clear all*.
- **Reorder from either side**: dragging a row in the settings dialog lifts it out under the
  pointer, reorders the list around it as it moves, and moves the icon on the dock at the same
  time; dragging an icon on the dock reorders the dialog's list to match, while it is open.
  **Lock the order** on the Items page once it is the order you want, and neither drag will
  move anything — nor will *Move up* and *Move down*, which grey out with it. Adding,
  removing and editing items are unaffected: it fixes the arrangement, it does not close the
  list. A press on a locked dock still launches, so an icon held with an unsteady hand does
  not simply do nothing.
- **Lock modification**, the other half of that, on the same page: the dock closes to
  changes, and nothing can be added to it, taken off it or edited. *Item settings…*, *Add*,
  *Remove* and *Clear all* grey out in the dialog, and double-clicking a row no longer opens
  the editor either. On the dock itself the right-click menu drops its commands altogether
  rather than greying five of them, and says why: one greyed **Locked** where they were,
  then *Dock settings…* — which is never taken away, so the lock is always one right-click
  from being undone. A file dropped on the bar is refused with a notice. Reordering is
  untouched: this closes the list, it does not fix the arrangement.
- **Drag and drop**: drop a file or a folder onto the bar to pin it where you dropped it —
  an application, a shortcut, or a document, which opens in whatever owns its type. **Places
  with no file behind them drop too**: This PC, the Recycle Bin or Control Panel dragged off
  the desktop or Explorer's navigation pane becomes exactly the pin the Add menu makes for
  it, and any other place there — Network, Home, Gallery — is pinned by its shell name. Drag an
  icon sideways to reorder: the wave stays up while you do, because the magnified
  neighbours are what make the gap the icon will land in legible. A drag held over the dock
  **opens a slot and previews the icon in it**, faded, so what the
  drop will produce and where it will land are both visible before it happens — and the
  preview **grows into** the slot the row opens for it rather than appearing in it whole.
  Something already on the dock is not pinned twice, and a drop the dock will not take
  **says why**, in a bubble where a label would be, rather than doing nothing at all — which
  is also how a dock whose items are locked answers a drop. Icons **slide** into their new
  slots rather than jumping, whether they are making room for a
  drop or swapping places in a reorder. A dragged icon **stays on the bar**: it follows the
  pointer exactly, but only as far as the end slots, so a drag that wanders leaves the icon
  where it can still be dropped instead of stranding it beside the dock. Dragging an icon off the bar no longer removes it:
  the gesture fired on a drag that wandered rather than on one that meant it, and losing a
  pin to a slip of the hand is a poor trade for a shortcut the right-click menu and the
  settings dialog both already offer.
- **Right-click an icon** to edit it or remove it, add another item beside it, or open the
  dock's settings — the item's own entries first, then the dock's; or, while the items are
  locked, a greyed *Locked* and the settings alone. *Remove from dock* is
  drawn in the theme's critical colour, the same one the settings dialog's Remove button
  takes, so the entry that takes something away does not read like the ones that do not.
  **It asks first**, naming the item and showing its icon, while the dock holds that icon
  magnified with its label up; *Cancel* is the default, so Enter keeps it. The settings
  dialog's *Remove* does not ask, because nothing there is kept until Save. Editing covers its name,
  its icon and what it opens. *Reset*, beside the name, puts back the one the item would be given
  if what it opens were pinned now: a file's or a folder's own name, a program's description, the
  Add menu's name for one of its entries, Explorer's for another place or a Store app, and a web
  address's site. Its tooltip says which, and it is greyed while that is the name already, or
  where there is none to go back to — an `ms-settings:` address, say. **A pinned picture is drawn as itself**: its Windows thumbnail,
  fitted to keep its shape, unless the editor's *Use the file's icon instead of a thumbnail* is
  ticked; an image chosen for the item wins over both. **A folder can be drawn by the dock
  instead of with Windows' folder icon**, in the manner of Windows 11's own folders:
  *Customize* draws it, and opens under it a colour of its own — from swatches, a colour wheel
  or hex — and on its front either one of 32 symbols from Windows' own symbol font or up to six
  characters of text, toned (a deeper shade of the folder's colour, as Windows' own carry
  theirs), white or black. A symbol's lines are as heavy as those on Windows' own folders, and a
  white symbol, or white text, casts the same soft shadow their white symbols cast. *Use the
  default icon* puts Windows' folder back; choosing an image, drawing the folder and the
  default icon each undo the others. A folder already customized opens with all of that out,
  and the settings dialog's Items page shows it as the dock draws it, so the folders can be told
  apart there. The folder's shape comes from an SVG built into the program, sized
  and placed as all of Windows' folder icons are, so it sits level with them on the dock; it is
  drawn from the pin's settings each time the dock reads its pins and never kept as a picture,
  so a reinstall or an imported settings file brings it back exactly. The item being edited
  holds its label open on the bar, so its name can be judged where it is actually used.
- **Answers the keyboard, from anywhere in Windows**, every hotkey the Windows key and Ctrl with
  a key. **Win+Ctrl+A** brings the dock up — out from hiding, the tray's *Hide dock* included,
  and over whatever covers it — with its first item held up, magnified and labelled. Then Left
  and Right move along it, Home and End go to its ends, a letter goes to the next item whose
  name starts with it, and 1 to 9 go to the item in that place, separators not counted — held
  up, not opened; Enter or Space opens the item held up as a click would, the menu key or
  Shift+F10 opens its right-click menu above it, and Esc — or Win+Ctrl+A again — hands the
  keyboard back to the window that had it. Opening an app hands it to the app instead, and a
  click anywhere ends it as well. Esc out of the menu comes back to the dock, on the same item.
  **Win+Ctrl+H** hides the dock or shows it, as the tray's entry does. **Win+Ctrl+I** opens the
  settings dialog — I as in Win+I, Windows' own Settings — or, when it is open, brings it back
  to the front: restored if it was minimised, and to the item editor or a scan if one is open
  over it. **Win+Ctrl+Num 1** to **9**, on the numeric keypad with Num Lock on, open the item in
  that place without the keyboard being taken first; without a keypad, Win+Ctrl+A, the digit and
  Enter do it. **Holding the Windows key and Ctrl**, with nothing else, puts each item's number
  on its icon the moment they are down — a small disc in the accent colour, at the icon's top
  left, magnified with it — and brings a hidden dock up until they are let go: out of auto-hide,
  from behind a window that fills the display, or put away from the tray, which it goes back to.
  Numbered are the items whose keys the dock holds — quick launch on, the item not turned off,
  its keys not another program's — and none while a program on the Exclusions page is in front;
  an arrow, Shift or Alt pressed with them ends the hold until they are let go — the two were
  the start of another shortcut, Win+Ctrl and an arrow switching virtual desktops above all —
  and a dock that came up for them goes straight back, without the hide delay; one of the dock's
  own hotkeys ends it as well. Win+Ctrl+H goes by the dock as it was before the two brought it
  up. On the Hotkeys page, both on by default, *Show item numbers on Win+Ctrl* turns the numbers
  off and *Reveal the dock on Win+Ctrl* the bringing up; with only the second off, the numbers
  still come up on a dock in sight. The badge stands off the icon — a soft shadow below it, the
  accent a shade lighter at its top, a thin half-white rim — set a little out from the corner so
  it covers less of the picture. The keys are read as the pointer is, by looking — Ctrl about
  thirty times a second, the rest only while it is down — never by a hook. One family, so the
  keys are consistent, and two modifiers side by side, which one finger can hold. The places are
  on the keypad because Windows has the number row's digits with the Windows key alone and with
  Shift, Ctrl, Alt, and Ctrl and Shift, all for the taskbar's buttons; the one family it leaves
  free, Win+Ctrl+Alt, was tried for a few hours and was a key too many. None of the dock's
  hotkeys is Windows' own or PowerToys', and none was registered on the machine they were chosen
  on: every candidate was registered and let go of at once, across nine families of modifiers
  and three keyboard layouts — D for dock is taken in every one. For a screen reader, the dock
  while it has the keyboard is a list of its items, read one by one as the keys move along it:
  the dock draws its icons rather than being made of controls, and Narrator would otherwise have
  nothing to follow.
- **Hotkeys are set on the Hotkeys page**: click a box and press the keys — the Windows key,
  Ctrl or Alt, with one other key — Backspace or the × in the box to empty it, Esc to put back
  what it held. Opening the page puts the keyboard in no box: a box records while it has it.
  Each of the first nine places on the dock has two keys, side by side on its row — its first on
  the keypad, and a second, unset to begin with (Win+Ctrl+Alt with the digit on the number row
  is free, for one) — and on a dialog too narrow for both, the second goes under the first. Each
  row has the icon of the item in that place beside its name, from the dialog's own list, so a
  change on the Items page shows there before it is saved. **Enable quick launch**, on by
  default, turns all of the items' keys on or off at once: off, the dock lets go of them, so
  they are free for other programs, and keeps them for when it is on again; the keyboard's own 1
  to 9 work either way. **Each item's row has a checkbox of its own**, ticked to begin with,
  which does the same for that item's two keys alone; **Show item numbers on Win+Ctrl** and
  **Reveal the dock on Win+Ctrl** say what holding the two does. The page stands under Items,
  whose items most of its keys open. A hotkey works as soon as it is recorded, so it can be
  tried before Save, and Cancel puts the old one back; while a box records, the dock lets its
  own hotkeys go, so pressing the one being changed reaches the box rather than firing. Keys set
  on the page outrank the ones an action starts with — given one item's keys, another item has
  them, and the first item's row says so — and between two set on the page, a first key keeps
  them over a second, and otherwise the higher row. Beside each, the page says what is wrong
  with it: another program has it already, which Windows says only by refusing it — Explorer
  registers its own before the dock starts, so a key Windows takes in some later update would
  otherwise just stop working; another row has it, and keeps it; it is Ctrl+Alt, which on many
  keyboards is AltGr and would take a character from typing; or, without the Windows key,
  programs may want it for themselves. Combinations Windows uses in every window — Alt+F4,
  Alt+Tab, Alt+Space, Ctrl+Esc — are refused, since some of them could be registered and closing
  a window would stop working everywhere, and F12 is Windows' for debuggers. Keys are stored by
  the key, not by what it types, so a hotkey is the same key on every keyboard layout, and shown
  with each key named as it is printed. **While a program on the Exclusions page is in front,
  the hotkeys are let go of** — whatever the size of its window, and on either display — not
  merely ignored: a key the dock holds is one the game never sees. The edge stands down only for
  one that fills the dock's display, since only such a window reaches the edge; a key goes to
  the window in front wherever it is.
- **The labels' lettering is the dock's**: their font, emphasis and size are set once, on the
  settings dialog's Icons page, for every label at once — and for the drop notices, which are
  drawn as labels are. Labels are 14 pt and bold by default, because a label is read against
  whatever happens to be behind the dock. A font that is not installed — in a settings file
  brought from another machine — stays chosen, marked as not installed, and the labels fall back
  as WPF does for any missing font. Until 2026-10-01 the lettering was chosen in the item editor
  and written to every pin; a settings file from then gives it to the dock as it is read.
- **The Add menu offers places and actions, not only apps**: alongside *Browse…*, the two
  searches (below, with the scans they share) and a few
  of the machine's own applications, **This PC**, the **User folder**, **Downloads**, the
  **Recycle Bin** and **Start**. They are there because nothing else can supply them, or not as well. This
  PC and the Recycle Bin are shell namespace extensions rather than shortcuts, so the file
  dialog cannot pick them, and they can be dropped only from somewhere they are shown —
  Windows 11 puts only the Recycle Bin on a new desktop. Once pinned they
  need no special handling, since the shell resolves a `shell:` name for an icon and
  launches one exactly as it does a path. The user folder could be dropped, but it would
  then be pinned as `C:\Users\name`, which an imported settings file carries to a machine
  where that is someone else's folder or nobody's; the preset pins `shell:Profile`, which is
  whoever is signed in. It is named as Explorer names it, which is the account's full name
  rather than the folder's (*Artur Farmanov*, not *artur*). The name is read when the pin is
  made, like every label, so an imported file keeps the exporter's until it is renamed — or
  until the editor's *Reset* reads it again.
  Downloads is pinned as `shell:Downloads` for the same reason, which also follows the folder
  if it has been moved out of the user folder.
  **Settings** is among the machine's own apps, and is pinned as the Store app it is, by its
  AUMID: the `ms-settings:` address opens it just as well, but the shell draws it as a blank
  page.
  **Start is not a target at all**: it has no path, no AUMID and no entry in the shell
  namespace, so nothing can open it. It is asked for by posting the taskbar the same command
  its own button sends, and it carries a mark that ships with the dock, because Windows
  exposes no Start icon anywhere — see *Known gaps*.
- **Run as administrator**: right-click a program — an `.exe`, a shortcut to one, a `.bat`,
  `.cmd` or `.msc`, whatever Windows' own associations give the `runas` verb — and the entry
  is at the top, above the ones that act on the pin. It is greyed while the program is open,
  so it never starts a second copy beside the one a click would bring forward, and it
  survives *Lock modification*, being a launch. To have a pin always start that way, tick *Run this program
  as an administrator* in its item editor; a click on one whose program is already open still
  brings that window forward. Windows asks every time either way, and the icon only bounces
  when the answer was yes. Not for Store apps. **An app running as administrator has an amber
  dot** rather than a white one — however it was started, from the dock or not. Either dot has
  a dark rim, so it stands out on a pale bar as well as a dark one.
- **Empty the Recycle Bin from the dock**: right-click the pin and the entry is there, above
  the ones that act on the pin itself. It greys out when the bin is already empty, and it
  **asks first exactly when Explorer would** — the *Display delete confirmation dialog*
  setting in the bin's own properties governs it, because the dock does not decide: it hands
  the whole question to the shell rather than overriding it.
- **The Recycle Bin's icon follows the bin**, full or empty, whoever changed it: a file
  deleted into it or restored from it anywhere on the machine, or the bin emptied. The dock
  redraws on the same announcement Explorer's desktop redraws on, so the two change together,
  and decides full or empty by counting the bin itself, drawing Windows' own full or empty
  icon — including one changed in *Desktop icon settings*. The setting Windows used to switch
  between the two cannot be relied on since an update at the end of September 2026: Explorer
  left it saying full after an empty, and the dock, which read it, stayed full. After emptying
  from its own menu the dock also asks the shell to re-check the bin, because a bin emptied
  without that check can stay drawn full everywhere, Explorer's desktop included.
- **Separators**: a divider that occupies a slot and launches nothing. It holds its resting
  size while its neighbours magnify around it — it is punctuation, not a target. Added from
  the same Add menu as everything else, and shown in the settings list as the rule it is
  rather than as an entry named *Separator*.
- **Starts with a usable dock, once**: a first run — meaning no settings file at all — gives
  the bar **Start**, **This PC**, the **User folder**, **Downloads**, **Settings**, a
  separator and the **Recycle Bin**, so a new install is never an empty strip. Those are on every machine; the
  user's own apps are left to the user, rather than guessed at from what Windows happens to
  ship. The separator sets the bin off at the end, where a Mac keeps the Trash. Only that
  first run — or on purpose: **Reset all pages to defaults**, on the About page, puts the
  dock back to the same set.
  The Items page's **Clear all** empties the list for good, and a cleared dock stays cleared
  across restarts instead of being restocked.
- **Starts when you sign in, unless told not to**: the same first run registers the dock to
  start at sign-in, and so does the setup, so *Start ArtDock when I sign in* on the System
  page comes up checked. The first run leaves alone an entry some other copy already made;
  the setup takes it over, since installing is choosing that copy. The checkbox and Windows'
  own switch — Task Manager's *Startup apps*, or the Settings app's *Apps → Startup* — are one
  switch: unchecking it leaves ArtDock on Windows' list, switched off, rather than taking it off
  the list; one turned off in Windows shows unchecked — at once, if the settings dialog is open
  while it happens; and neither the first run nor the setup turns it back on. Resetting the
  System page checks it again.
- **An empty dock is still a dock**: with nothing pinned it draws one empty slot's worth of
  bar rather than disappearing. That is not decoration — the window is per-pixel transparent
  and Windows hit-tests it by what was painted, so a dock that drew nothing would be a window
  clicks fall straight through. The empty bar is what a drop lands on, and what you
  right-click to reach **Add**.
- **Moves along its edge**: the Position page slides the dock from the middle towards either
  end, on a slider that fills from its middle out to the thumb, since the middle is where the
  setting is measured from — and catches there when dragged to within a few pixels of it, so
  the centred dock is easy to get back to. The setting is a share of the room there is rather than a
  distance, so it means the same on every display and at every size of dock. A dock pushed to one end stays at that
  end: anything that changes its width — an icon added or removed, a drop preview opening
  its slot — grows it away from the end rather than into it. All the way along, it stops
  where its magnified wave still keeps the bottom margin's distance from the side of the
  screen, so at rest it sits a little further in than that, by the room the wave grows into.
- **Stays put while you configure it** — opening the settings dialog or an item's edit
  dialog holds the dock on screen, so auto-hide cannot slide it away mid-adjustment.
- **Comes back to the front** when the cursor is held against the screen edge under it, whatever the
  settings — the same gesture that would summon a hidden dock digs out a buried one, blur and
  all, including from under the window that has focus and from under a fullscreen one. Windows will not put a background
  program over that one, so there the dock joins the windows that float for as long as it is
  up, and leaves them when it goes back. That holds for a dock set to float
  above everything too, which another window that floats can still cover. How long to hold
  is the *Reveal delay*, which is therefore never greyed out. Move away and, after the *Hide
  delay* — also never greyed out — it goes back under the window it was lifted over, exactly
  as auto-hide would slide it away: the dock and the strip below it, as wide as the dock, keep
  it up. It only ever
  goes back *down*: a window you have brought forward since stays in front of it, and one it
  was already in front of stays behind.
- **Answers the pointer only where you can see it.** Moving the pointer onto the visible part
  of a partly covered dock brings it to the front at once — no *Reveal delay*, since arriving
  on the dock is already deliberate — and it goes back the same way the edge's lift does. A pointer on a window lying over the dock belongs
  to that window: the dock does not magnify or label anything underneath it, and a dock
  entirely under a window does not come up just because you are working in that window over
  where it is. A dock with nothing over it is left where it is — lifting it would change
  nothing and still redraw it. A dock that has not settled on screen — hidden, or sliding
  either way — answers the pointer nowhere and takes no clicks, so the wave starts only once
  a dock coming back has landed, and a hidden dock that something has moved back onto the
  screen — as a wake from sleep once did — is put back within a quarter of a second.
- **Stays down over the apps you exclude** — games, mostly, which scroll when the pointer
  reaches the edge of the screen and would otherwise bring the dock up over themselves every
  time the map moved. While a program on the **Exclusions** page is in front and fills the
  dock's display, fullscreen or borderless, holding the pointer against the edge does
  nothing: a hidden dock stays hidden, a covered one stays covered, and the strip under a
  dock that was already out stops holding it up. The handle is not drawn over it either. Any
  window filling the display sends the dock away, but only a listed one keeps it away: **the
  list outranks the pointer as well as *Always on top*** — the edge does not bring the dock
  back over such a program, and the dock goes under it as well as away, so its slide happens
  behind the game rather than over it. It comes back the moment the program is not in front,
  and one started, or restarted, while the game was in front stays away too — it used to come
  up over it. Programs are added from those open at the
  time, by browsing for the `.exe`, or by **scanning for games or apps**, and are recognised by file
  name, so a game that moves folders when it updates still counts. Only the edge and the
  hotkeys stand down — the tray icon, a second launch and the settings dialog bring the dock
  back as they always do — and the hotkeys whenever the program is in front, filling the
  display or not, since its keys are its own. The page was called *Fullscreen* at first, which
  read as a setting for going fullscreen rather than as a list of apps to stay out of the way
  of.
- **Finds your games and the programs they run as**: *Scan for games…* on the Exclusions
  page asks for nothing. It finds the games the launchers on the machine have installed —
  Steam (every library, on every drive), Battle.net, the EA app, Epic Games, GOG, Ubisoft
  Connect, the Xbox app, Riot Games and Rockstar Games, whichever are there, each read from
  its own record — looks through all of them for programs, and lists them under a heading per
  game, with each program's icon. **Tick a game** and every program in it is ticked; that is
  the answer for someone who knows the game and not its programs. *Add a folder…* looks
  through one more, for anything the launchers do not know about.
  It exists for the game nobody starts by hand — StarCraft II is started from Battle.net, and
  its folder holds a `StarCraft II.exe` at the top that is only a stub, with the game itself
  two folders down as `Versions\Base97563\SC2_x64.exe`. So within a game, programs that give
  the same name are one row, ticked together, and the game is on the list whichever of them it
  is. Copies of one program in several version folders are one program; installers,
  updaters, crash reporters and tools a game carries are left out altogether — none of them
  is the window a game plays in, and a switch to show them was only more to read past — which
  in the Steam library here leaves each game as the one program it runs as; a row already on
  the list says so — or how much of it is. It runs in the background, can be stopped with
  what it found so far kept, passes over folders it may not read and does not follow
  junctions. Here it finds fourteen game folders in about 50 ms and looks through them in
  under a second.
- **Scan for apps…**, beside it, is the same for everything that is not a game — a video
  player, a browser, a remote desktop. It lists the apps installed on the machine from their
  Start menu and desktop shortcuts, under the names the Start menu gives them, as one list
  without headings. A shortcut that runs a console or a control panel ("Administrative
  Tools" is `control.exe`) is left out, since listing it would stand the dock down over every
  console; so is anything inside a game's folder, which *Scan for games…* has. Here it finds
  seventy-three apps in about a second. Store apps are not among them — their Start menu
  entries are not shortcuts — though one that is open is on the *Add* menu.
- **Both scans pin, too**: the dock's Add menu, and the settings dialog's, have *Search apps…*
  and *Search games…* under *Browse…*, which open the same dialog to put what is ticked on the
  dock, after the item the menu was opened on. A row is one pin there, not every program in
  it: the one nearest the top of its folder, which for StarCraft II is the stub that goes
  through Battle.net rather than the game two folders down; then the one with an icon, then
  the larger. An app keeps the name the Start menu gives it. A row with any of its programs
  pinned already — by its whole path, since a pin opens one file — is shown *On the dock*.
- **Both scans put the likeliest first, and can be filtered**: *Most likely first* ranks a
  program that is open now above everything — a launcher counted open while the program it
  starts is, as on the dock — then one whose name is its game's, then one with
  an icon of its own — every game here has one and not one of the crash handlers and
  uploaders beside them does — then one that describes itself, then the largest in its game;
  Windows' own tools sit below installed apps. *By name* is there for someone who knows what
  they are looking for, and so is the filter above the list, which matches a name, a file
  name or a game's name. Typing into it does not untick anything it hides.
- **Program icons in the Exclusions page's lists**: each listed program shows its icon, and
  so does each entry of the *Add* menu's list of open programs, since a program is known by
  its icon first.
- **Every menu entry has an icon**, as in Windows 11's own right-click menus: a glyph from
  the system's symbol font for each command — the same one for *Settings…* on the tray and
  *Dock settings…* on the dock — and, in the *Add* menus, the icon of what each entry pins.
  *Remove from dock*'s glyph is red with its words, and a greyed entry's is greyed.
- **One dock to a session.** A second launch does not start a second dock. The running one
  says it is already running, lifts the dock above any windows covering it, and says where
  the dock is — or, when it cannot be seen (hidden from the tray menu, tucked away by
  auto-hide, or on no connected display), why not and how to get it back. It offers **Exit
  ArtDock**, plus **Show dock** where a button can bring the dock back. `--settings` keeps its
  meaning: launched with it, the running dock opens its settings instead. Two docks would
  overlap on the same edge, race each other for the settings file, and put two identical
  icons in the tray.
- **Configurable** live: icon size, magnification, influence range, gap, bar opacity, bar
  colour (hex, a colour wheel, a stock swatch, one you have saved, or whatever Windows is
  tinting the taskbar with), corner roundness, the blurred backdrop, which display the dock
  lives on — remembered by the monitor itself, so it stays put when Windows renames its
  displays — and where along its edge, always-on-top, hide/reveal delays, the excluded apps
  the edge and the hotkeys stand down for, pinned apps, the icon set, the icons' shadow, the
  labels' font, emphasis and size, the hotkeys, the language, run-at-login. The dialog is paged
  — Size, Appearance (the bar), Behaviour, Icons, Items, Hotkeys, Exclusions, Position, System,
  About — and
  each page of settings has its own reset; the two pages that hold lists of the user's own,
  Items and Exclusions, have none. About holds no settings, and so no page reset — it has the
  whole-application one instead, which puts the pinned items back to the set a new dock
  starts with and leaves the excluded apps and saved colours alone.
  Nothing is written until Save; Cancel puts the dock back the way it was found.
- **Translatable, with English built in**: every word the dock shows — the dialogs, both
  menus, the drop notices, the messages — comes from one string table,
  `Localization/en.json`, rather than from the XAML or the code. A translation is a copy of
  that file dropped into `%LOCALAPPDATA%\ArtDock\Packs\Languages\<name>\pack.json` (the System
  page's *Open folder* goes there), and the System page's *Language* picker then offers it,
  beside *Windows display language*, which is the default. Choosing one changes the open
  dialog there and then and is undone by Cancel, like every other setting. A translation may
  be partial — anything it leaves out is English — and a string in it that could break the
  dock, such as one using a placeholder English does not supply, is dropped in favour of the
  English one. Numbers are formatted in the language's own way (`16 %`, `1,40x` in German),
  and counts take the language's own plural forms, three of them in Russian. Only English
  ships so far, in two kinds: US English, which is the default and what every translation
  falls back to, and British English (`en-GB.json`), a translation of it that gives only the
  strings it spells or words differently — *colour*, *Behaviour*, *Tick* — and takes the rest
  from US English. Following Windows comes to British English only for a Windows set to it;
  any other English, Australian included, is US English.
- **Icon sets**: a folder of PNGs with a `pack.json` saying which image is drawn for what —
  an executable by name, a path, a Store app, a place such as the Recycle Bin (full and empty
  separately), a document type, any folder — installed in
  `%LOCALAPPDATA%\ArtDock\Packs\IconSets`, and chosen on the Icons page. The most
  specific match wins; an item the set has nothing for keeps its own icon; and an icon chosen
  for one item in its own settings still beats the set, as does a folder given a colour. The format is in
  [docs/packs.md](docs/packs.md). No set ships.
- **Settings can be moved between machines**: the System page exports the settings file and
  imports one back, pinned items included. An import fills in the pages and shows on the
  dock immediately, but like every other control here it is not written until Save, so
  Cancel still undoes it. A file that is not ArtDock settings is refused rather than
  imported as a silent reset to defaults, and so is one from a newer format than the build
  can read. Autostart is the one thing that does not travel — that checkbox reflects the
  Run key rather than the file, so an import cannot quietly register the dock to start on a
  machine you were only copying your tuning to.
- **The About page says which build is running**: the last page of the dialog carries the
  mark, the copyright, the version, whether this is a Debug or Release build, and where the
  settings file is. The same author, copyright and version are in the executable's own
  version resource, so Explorer's Details tab shows them, and Task Manager's Startup apps
  page names the author as the publisher of the autostart entry.
- **Updates itself, when asked**: the About page's *Check for updates* looks for a newer
  release and, if there is one, becomes *Update and restart* — which downloads it, closes the
  dock, and brings the new version back up with the dialog open on the same page. Nothing
  goes online until the button is pressed. A copy the setup did not install, such as one run
  from the source tree, says so and offers nothing. Autostart in an installed copy points at
  Velopack's launcher, which outlasts every update, and uninstalling takes the entry away.

Settings live in `%LOCALAPPDATA%\ArtDock\settings.json` and are written atomically. The
file opens with a `Version`, which is the format rather than the application's — it is 1,
and it exists so a later change to what a setting *means* can be migrated rather than
guessed at. Adding a setting does not move it: an unknown property is ignored on read and
a missing one falls back to its default, so the file can grow freely. The System page can
export a copy of it and import one back.

## Layout

```
src/ArtDock/
  Dock/        DockMagnify, DockLayout, DockMetrics, DockItem, DockEdge, BarPalette,
               BarShadow (the bar's shadow, for whichever window draws it),
               AutoHideController, DockHandle (where the dock's handle goes),
               HandlePace (how often it reads what is behind it),
               DockFront (what the dock does about a fullscreen window, and when the
               handle shows)
  Controls/    DockBar (the rendering surface), DockItemVisual, ColorWheel,
               AnimatedRowPanel
  Views/       DockWindow (the floating dock), BackdropWindow (the acrylic sheet, which
               draws the bar while the blur is on),
               MenuHost (owner for the context menus), MenuIcons (their glyphs and
               images, and the same glyphs on the settings dialog's buttons),
               PopupShadows (room for the shadows under drop-down lists and tooltips),
               SettingsWindow, EditPinWindow,
               AlreadyRunningWindow, ScanWindow, HandleWindow (the handle itself)
  Interop/     WindowChrome, NativeMethods, DesktopComposition, CompositionBackdrop,
               WindowsApi, ShellIcons, ShellLink, AppLauncher, Autostart, MonitorDpi,
               TaskbarColour, RecycleBin, RecycleBinWatch, ForegroundApp, ScreenCapture,
               ShellIdList (what Explorer drags besides paths), ExplorerWindows (which
               folder each File Explorer window shows, for the folders' dots)
  Services/    SettingsStore, DockSettings, AppTheme, PinnedAppsService,
               FolderArt (the folders the dock draws, from Assets/folder.svg),
               IconShadow (the shadow an icon casts onto the bar), SvgBlur (the blur
               both of those use), RunningAppsService, DockPresets, DockCommands, DroppedItems, TrayIcon, Screens,
               SingleInstance, MemoryTrim, FullscreenApps, ProgramScan,
               GameLibraries, InstalledApps, AppUpdater (the dock's own updates)
  Program.cs   the entry point, which hands Velopack's installer launches over first
  Localization/ en.json (every string, in US English), en-GB.json (British English),
               StringTable, LanguageLibrary, Localizer, Localized (formatted text in XAML),
               PluralRules, LanguagePackFile
  IconSets/    IconSet (matching and loading), IconSetLibrary, IconSetFile
  Packs/       PackKind, PackManifest, PackLocations — what every pack has in common
  Downloads/   interfaces only: the pack catalog, download, verify, install
tests/ArtDock.Tests/   DockMagnify, DockLayout, DockItemVisual, DockBar focus, metrics,
                       DockBar window size, empty bar, drag clamp, pin creation,
                       pin targets, settings portability, Recycle Bin icon,
                       moving along the edge, localization, icon sets, display
                       identity, excluded apps, scanning for programs, game
                       libraries, installed apps, the dock's handle, what
                       the dock does about a fullscreen window,
                       dropping shell places, the menus' glyphs, the updater's
                       install folder, the shell's images' transparency, the
                       folders the dock draws, the icons' shadow, which windows
                       light a folder
LICENSE                MIT with the Commons Clause: free to use and change, not to sell
docs/step-2-transparency.md
docs/packs.md          the language-pack and icon-set formats
docs/downloads.md      how the dock's updates are delivered and packs are to be, and why
tools/release.ps1      builds a release with Velopack, and publishes it
dotnet-tools.json      pins vpk, Velopack's packager, to the library's version
```

## Design notes

**It is WPF, not WinUI 3.** WinUI 3 was the first choice and was abandoned after measurement:
a WinUI 3 XAML window cannot be made per-pixel transparent on Windows App SDK 2.4, which the
dock's silhouette requires. Every route was tried and recorded in
[`docs/step-2-transparency.md`](docs/step-2-transparency.md). WPF's `AllowsTransparency` does
it natively, and brings text, tooltips and UI Automation along for free.

**Icons live in fixed slots.** Each icon owns a placeholder slot that never moves, and grows
about that slot's centre — so an icon's size depends on the pointer, and its position depends
on nothing at all. The obvious alternative, which this started with, is to lay icons out as a running total of their magnified widths. That makes
every icon's position depend on the widths of all the icons before it, so the row slides
sideways as the pointer crosses it, and the bar breathes in and out at the icon spacing — the
dock appears to contract. Slots remove the coupling entirely: magnification is a property of
one icon, not of the row. `DockLayout` and `DockMagnify` are pure functions with no UI
dependency, and are unit tested — including that an untouched icon's centre never moves.

**Nothing lays out during a wave.** Icons are placed once at their resting positions; each
frame only writes a scale and a translate transform, plus one redraw of the bar. No measure
or arrange pass runs while magnifying.

**The window is deliberately bigger than the bar.** It extends past the bar on every side —
above for magnified icons and tooltips, sideways for the bar's growth at peak magnification,
and below for the drop shadow. A layered window is hit-tested against its alpha channel, so
wherever the dock draws nothing the click passes straight through to whatever is underneath;
the corollary is that the shadow and the tooltip, being drawn, do absorb clicks.

**The launch pulse is the icon, not a plate over it.** It used to be a white rounded
rectangle drawn on top, which read as a square appearing over the icon rather than as the
icon reacting. Pulsing the icon's own opacity leaves nothing on screen that was not already
there.

**Icons use Fant resampling.** The shell returns 128px icons that are drawn into roughly a
31-41px box. WPF's default bilinear sampling reads a 2x2 neighbourhood, which at 3-4x
minification misses most of the source pixels — edges alias, and because the ratio changes
continuously as an icon magnifies, they crawl while the wave moves.
`BitmapScalingMode.HighQuality` averages over the whole footprint instead, at no measurable
cost here.

**The blur is a second window, and while it is on, that window draws the bar.**
`AllowsTransparency` puts the dock on a layered surface, and DWM composes no material behind
one — which is why the bar was a flat fill. `BackdropWindow` is a second window directly under
the dock, the same size and in the same place, created by hand with
`WS_EX_NOREDIRECTIONBITMAP` so that a composition visual can fill it: a host backdrop brush —
the blurred desktop behind it — cut to the bar's rounded shape by a composition clip, with a
light wash over it.

**It draws the bar as well as the blur, because two windows would not stay together.** Drawn
in the dock's own window, the bar reached the screen through WPF's render thread and
`UpdateLayeredWindow`, and the blur's clip straight from the UI thread through the compositor.
Captured frame by frame, the blur's edge ran a frame or two ahead of the bar's whenever the bar
grew or shrank, and by no fixed amount, so no delay could have held them together; over a white
window it showed, as a strip of blur past the bar's end or of bar with no blur under it. So with
the blur on, the sheet draws the bar's fill, its rim and its shadow in the same batch as the
blur, and the dock draws only the icons over them. The sheet is told of the bar's shape a frame
after the dock has drawn it (`DockBar.RenderedBarRect`), so that it arrives with the icons that
frame drew, which come the long way round.

**The dock still owns the bar's clicks.** Its window is hit-tested by the alpha of what it
paints, so with the bar drawn elsewhere it paints an invisible stand-in — alpha 1 — over the bar
and its shadow, which took clicks and drops before and still do. The sheet takes none: it is
layered as well as `WS_EX_TRANSPARENT`, which is what it takes for hit-testing to pass a window
by every time. `WS_EX_TRANSPARENT` alone does not, reliably, and an earlier sheet with only that
was found swallowing clicks beside both ends of the bar.

**Where composition is not available**, the sheet falls back to DWM's accent policy — the route
the taskbar is on, which blurs regardless of focus, where the documented
`DWMWA_SYSTEMBACKDROP_TYPE` materials go flat for a window that is never active, as this one
never is. That blur cannot be shaped: DWM rounds it to a radius of its own, and a window region
is accepted but composed straight over. So there the sheet is a rectangle inscribed inside the
bar, by exactly enough for its corners to clear — almost all of the inset on the horizontal,
since the bar is far wider than it is tall — carrying a sizing frame, collapsed in
`WM_NCCALCSIZE`, because DWM shadows only windows with one. The dock draws the bar over it, as
it always did, and on that path the blur can still trail the bar.

**The drawn shadow.** A real `DropShadowEffect` would blur again every frame the bar changes
width, which is every frame of a wave. Two stacked passes of rounded rectangles (a tight contact
shadow and a displaced key shadow, spaced on a power curve) cost nothing to redraw
(`BarShadow`). With the blur off the dock draws them, clipping the bar out, because a shadow is
not visible through the object casting it; with it on the sheet draws the same rings under the
blur, which is opaque and hides them where the bar is.

**The icons' shadow is a picture, worked out once.** An icon's shadow is made from its own
outline the first time the icon is drawn with one — moved down, drawn in a little so that it
falls below the icon rather than round it, blurred as SVG blurs, and taken out wherever the icon
covers — into a bitmap 64 pixels across that the icon draws under itself (`IconShadow`). A
`DropShadowEffect` would look the same, but WPF keeps nothing of an effect's output, so it would
draw every icon off-screen and blur it again on every frame of the wave; a bitmap costs the wave
nothing measurable. The icon goes over it unchanged, where merging the two into one picture
would have resampled the icon, and every measure is a share of the icon's size, so the wave
magnifies the shadow with it.

**The bar breathes, in fractions of a pixel.** The bar wraps whatever the icons occupy at that
instant. That is nearly constant — the falloff sums to a constant across the middle of the dock
— but it narrows by a few pixels near the ends, over a few hundred milliseconds: under a pixel a
frame. A window moves in whole pixels, and a sub-pixel drift rendered in whole pixels stutters,
so the sheet's window never follows the bar. The bar is drawn inside it, in fractions of a
pixel, measured as WPF draws and not through `PointToScreen`, which rounds to whole pixels.

**Every dimension is a multiple of the icon size.** Magnification was already a scale
factor; influence range now counts how many icons either side of the pointer lift, and the
gap is a share of an icon rather than a number of pixels. One number sets the dock's size and
everything else keeps its proportions.

Influence range is the one that mattered most. In pixels it covered a different
number of neighbours at every icon size, so changing the icon size quietly reshaped the wave
and made two sliders look like they had moved on their own. It was being rounded to a whole
number of icon pitches regardless, since that is the condition under which the falloff sums
to a constant and the dock spreads without changing width — so this is the count it always
was, said out loud.

A settings file written before the change is converted once — the range through the same
rounding the dock already did, the gap by dividing by the icon size — so a tuned dock comes
across looking exactly as it did. Both old values are kept and both new ones are nullable, so
that "absent" and "zero" stay different things: a dock that has never carried a pixel value
takes the current default rather than being migrated from a number it never had.

**The window only grows while the dock is being tuned.** Resizing it is the expensive
thing to get right: it is layered, so it keeps its previous bitmap until WPF renders again,
and that bitmap has the bar drawn at an offset measured from the old left edge. Composed
inside the new bounds, the dock appears somewhere it does not belong — and not for a frame
either, but for as long as the render takes to land.

Dragging the icon-size slider asks for a new window size on every tick, which is what first
made this visible. The answer that worked was not to make the resize look better but to stop
doing it: while the settings dialog is open the window never shrinks, so opening and closing
cost no resize at all, and a drag costs one only when it asks for more room than the window
already has. A window bigger than it needs to be is invisible — it is transparent wherever
the dock does not draw, and the bar lands in the same place on the screen either way (see
*A dock moved along its edge*, below).

Two attempts came before that one and are worth not repeating. Rendering immediately after
the resize loses a race it cannot win: the new bounds are already live and can be composed
before the render lands. Resizing off screen and moving in afterwards removes the
displacement but replaces it with an absence — measured at about 130ms of no dock at all,
which is worse.

**The dock demonstrates its own wave, and gets out of the way.** Magnification and influence
range describe the wave, and the wave only exists while a pointer is over the dock — so with
the settings dialog holding the pointer's attention, both sliders looked like they did
nothing. `DockBar` walks the pointer from one end of the row to the other and back on a
raised cosine, which eases to a stop at each end rather than reversing on the spot.

Whoever is actually pointing at the dock outranks the demonstration: the moment a real
pointer enters, the sweep stands down and the wave behaves exactly as it always does, and
when the pointer leaves the sweep picks up again — from the phase that puts it where the
pointer just was, so handing back is a continuation rather than a jump. This is the one place
a held item does not lock the real cursor out; a menu or an edit dialog still does, because
there the point is to keep hold of one particular icon while you reach the dialog.

The row selected on the Items page belongs to the demonstration, not to that kind of hold. With
the sweep off, the dialog holds the middle icon up; a selected row takes its place, the pointer
outranks it in the same way, and with the sweep on the sweep stands down for it as for the
pointer. The item the Icons page holds up while it is open is held the same way, by the same
route — it is announced to the dock as a selection is. A parked wave sent to another icon travels there with the same easing as a change of
hands, so it glides along the dock as the selection moves down the list. A menu or the item
editor holding an icon over it hands the dock back to it when it lets go — to the demonstration
as it then stands. Before that, an icon's menu opened on the dock while the dialog was up left
the dock flat when it closed, unless the sweep was on, with the dialog still there to
demonstrate it.

**The wave changes hands without jumping.** A driver taking over — the cursor arriving on a
dock that is demonstrating itself, and leaving it again — used to move the wave to its own
position on the frame it took it, across however much dock lay in between. The gap is now
carried as a lag that eases to nothing over about a fifth of a second. Carried as a lag, and
not as an eased position: the wave still tracks the cursor one pixel per pixel while the gap
closes underneath it, so the smoothing is never felt as the dock trailing the pointer.

**The context menus are WPF menus, owned by a window that exists only to own them.** A menu
needs an owner that can take the foreground, and neither place this app shows one from has
that: the dock is `WS_EX_NOACTIVATE`, which is what stops it stealing focus from the app you
are switching to, and the tray icon has no window at all. So the dock's menu was a Win32
`TrackPopupMenu` and the tray's was WinForms', and both looked like an older Windows than the
one they were running on. `MenuHost` is the missing window — one pixel, off screen, never
seen — and once a menu belongs to something that can be given the foreground, the objection
disappears and it gets themed like everything else. Its glyphs come the same way: they are
drawn in the theme's own `SymbolThemeFontFamily`, the font it draws a menu's check marks and
chevrons in, so they are Windows' glyphs rather than a set this project drew (`MenuIcons`).
The settings dialog's Items page draws the same glyphs on its buttons, chosen by command, so a
command looks the same on a button as in a menu.

**The dialogs are on WPF's own Fluent theme, not an imitation of it.** `App.xaml` sets
`ThemeMode`, which merges `PresentationFramework.Fluent` — Microsoft's WPF implementation of
the Windows 11 design system, in the box since .NET 9. The controls in the settings and edit
windows are therefore the real thing: WinUI-shaped sliders, accent buttons, the system's own
colour resource keys rather than hex this project invented. The settings window sits on Mica
via `DWMWA_SYSTEMBACKDROP_TYPE`, the same material the Settings app uses, and is laid out the
same way — left navigation, page title, one card per setting. The other dialogs are on Mica
too, by the theme's own doing: its window style has no background at all — a transparent one —
and lets DWM's material show through from behind. So under *No GPU* a dialog is taken off the
material by two things together (`WindowMaterial`): DWM is told to compose none, and the window
is given the theme's opaque background, `ApplicationBackgroundBrush`. Doing only the first left
the settings dialog black, and doing it only to the settings dialog left the rest on Mica. And
DWM is told twice for a window that is just opening, at its first layout and again once its
source is ready: the theme puts its backdrop on in between, which left the item editor with a
plain body under a title bar still on Mica.

The dialogs follow Windows' light or dark setting by default, and can be pinned to either
from **System → App theme**. `ThemeMode.System` is WPF's own switch for that, so nothing here
watches for the change; the one thing it does not cover is the window's Mica and title bar,
which are DWM's and are told separately from `AppTheme.IsDark`.

Two traps worth naming, both the same shape. The Fluent theme supplies its controls as
*implicit* styles, so an explicit `Style` on a control replaces it outright rather than adding
to it — every keyed control style here carries `BasedOn`, and without it the control silently
reverts to the old Aero look, which is what happened to the sliders. And `TextBlock` is not a
`Control`: no theme sets its `Foreground`, and its own default is literally black. Every text
style starts from one that names the system's text brush, and an implicit style catches the
rest. Missing that is what put black text on a dark window.

One fault is the theme's own. Its drop-down lists and its tooltips cast shadows, and their
popups are only the size of what casts them — an effect is no part of a layout — so the shadows
were cut off square at the edges. The theme's submenus, with the same shadow, inset the list in
the popup and move the popup back by the inset; `PopupShadows` does the same for every combo
box and tooltip in the app, reading the room from the shadow. A list is placed by its popup's
placement rectangle rather than its offsets, because the offsets are the same whichever way the
popup opens, and a list with no room below opens above the box: an offset that kept it under the
box left it a shadow's depth clear of the box when it opened over it. A tooltip is placed against
the pointer by rules WPF keeps to itself, so it is let open where WPF puts it, read back, and
placed at that very spot with the room round it, all before its first frame is drawn.

**The colour wheel is drawn, not templated.** WPF ships no colour picker, and the
alternative is a dependency for one control. `ColorWheel` generates the hue/saturation disc
once as a premultiplied bitmap and dims it with a black wash as the brightness comes down.
The wash is capped well short of opaque on purpose: an accurate one would be exact — HSV's
value scales all three channels linearly — but a dock bar is usually a dark colour, and an
accurate wheel at that brightness is a black disc with no hues left to pick from. The wheel
indicates the brightness; the bar beside it and the swatch above it show what was chosen.

**Labels are ClearType, on a window where WPF turns it off.** On an `AllowsTransparency`
window WPF drops ClearType, because subpixel coverage cannot be composited against a
per-pixel alpha surface. Its grayscale fallback smooths glyph edges only horizontally, so
the tops and bottoms of curves stay hard steps, which reads as aliased at label sizes.
`DockBar` sets `RenderOptions.ClearTypeHint` to `Enabled`, which tells WPF the text is on
an opaque background and brings ClearType back. That holds because every label sits on a
bubble at 94% opacity. The rendering mode is left alone, so the system's own font-smoothing
setting still decides. `TextHintingMode.Animated` also smooths in both directions, but it
is unhinted, and the labels came out soft and heavy. `TextFormattingMode` is `Ideal`, so
glyphs keep their sub-pixel positions as the label slides.

**The headroom above the bar is measured, not assumed.** A fully magnified icon reaches
exactly the top of the reserve the window keeps for the tooltip, so a fixed reserve is only
ever right for one label size — anything larger lost its top edge to the window's own
clipping. `DockBar` measures every label in the dock (and any font being previewed), sizes
the reserve to the tallest, and tells the window to grow. That is what `PreferredSizeChanged`
is for.

**A drop preview is a real item in a real slot.** Rather than drawing an insertion marker,
the dock rebuilds itself with the incoming item spliced in at the slot it would land in, at
reduced opacity — so the preview inherits the spreading, the sizing and the icon resolution
of the real thing for free. The wave is held flat while it is up, for the same reason a
reorder drag holds it flat: sizes changing under the pointer would move the slot boundaries
the drop is aiming at. The preview is the one thing in the dock that is born mid-life, so it
is also the one thing that grows in: it starts at nothing and eases up to full size, anchored
at the bottom centre like the wave, while its new neighbours slide apart around it.

**A pinned shortcut is stored as the shortcut and matched by its target.** A `.lnk` carries
arguments, a working directory and an icon its author chose, so replacing it with the
executable behind it would throw all three away — but no process on the desktop runs under
the shortcut's own path, so matching on it left the running dot dark for anything pinned from
the Start menu. `Interop/ShellLink.cs` resolves the target alongside the pin, and
`DockItem.RunningTarget` is what `RunningAppsService` compares. Plenty of shortcuts
legitimately resolve to nothing — `File Explorer.lnk` points at a shell folder rather than a
file — so the match falls back to the shortcut's own path rather than treating that as an
error.

**A launcher is matched to the program it starts.** A pin of `Battle.net Launcher.exe` would
never light: the launcher opens no window, starts `Battle.net.exe` beside it and exits, and
the two agree in neither path nor file name. So a pin whose program is named `X Launcher.exe`
is lit by `X.exe` in the same folder when there is one — that and no wider, since lighting a
pin by anything in its folder would light the wrong one wherever programs share a folder.

**Anything that exists can be pinned, and the dock does not sort it into kinds.** A
document launches through the same `ShellExecute` as an application, takes its icon from the
same shell call — the shell has one for every registered type, and a generic sheet for the
rest — and occupies the same slot. The single difference is the running dot, and that is
ruled out where it is decided rather than by refusing the pin: `DockItem.RunningTarget` is
null for anything that is not an executable, because what opens a document is its editor,
whose window belongs to whatever pin starts *that*. A folder is the one other thing with a
dot, and it is not matched to what opens it: every folder window is `explorer.exe`'s,
whichever folder it shows, so a folder is matched to the folders Explorer's windows show in
their tabs. The dock asks Explorer for those on a thread of its own, since one of Explorer's
windows hanging must not hang the dock, whenever a window comes, goes or changes its title —
which is all a window going to another folder or tab changes. A tab behind another is brought
to the front by UI Automation, the way Windows lets another program choose one. Documents were
turned away once, on the
reasoning that a dock of documents is not what anyone means by pinning — a judgement made on
the user's behalf and enforced by doing nothing at all when they dropped one.

**The sheet's shape is set once, not twice.** `BackdropWindow.Place` used to end by clipping
the sheet to its own window, which `SyncBackdrop` then immediately replaced with the bar's
rectangle. The first clip never survived to be seen on purpose — but it was seen by accident,
whenever DWM composed a frame between the two, as a flash of acrylic at the full width of a
window that is deliberately wider than the bar.

**The bar opens up rather than jumping open.** A slot appearing is worth a whole icon pitch,
and taking it all at once made the bar snap to its new width and leave its contents to catch
up — the icons already on the dock jumped half a pitch sideways while the newcomer slid in.
The bar is drawn as if it held a *fractional* number of slots, easing to the real one on the
same time constant as the slides, so the row spreads and the bar opens as one movement. The
wave's own contribution to the width stays instant: the bar has to keep wrapping the icons
exactly as it passes, and easing that would have it trail them. The acrylic sheet follows for
free, since it draws the bar in whatever shape the dock last drew — its window is the dock
window's twin, and does not move at all when a preview comes and goes.

**The window already owns the slot a drop opens.** The dock asks for a window one icon
wider than it has, always. Growing to fit a preview and shrinking again when the drag moved
on was two re-places of a layered window — which shows its old bitmap inside its new bounds
until it repaints — so waving a file over the bar made the whole dock flinch by half a pitch
each way, and dropping one did it twice inside a single message: preview down, window in,
pin added, window back out. The reserve costs nothing to look at, because the window is
transparent where the dock does not draw and the bar lands in the same place either way.

**A dock moved along its edge is anchored, and its wave is not.** Everything about the dock
used to be centred — the window in the work area, the bar in the window — and every change
of width was shared out half to each end. Moved towards one end, the row is anchored instead,
at the same fraction of its room as the setting: an icon arriving, a drop preview opening
its slot, the bar easing between the two all grow it away from the end it is nearest, so a
dock pushed to the left keeps its left end exactly where it was. Centred, that fraction is a
half, which is the dock as it always was.

The wave is the exception, and has to be. It still grows about the row's middle, because a
wave held against one end has to push the icon under the pointer away from it — by half the
wave's growth in the middle of the row, which at a strong magnification is more than half an
icon, and leaves the pointer over a different icon from the one magnified. So the row keeps
the wave's reach clear at both ends instead (`DockLayout.WaveReach`). That reach has a closed
form: the raised cosine sums to a constant over whole pitches, so a full wave adds exactly the
hovered icon's growth times the pitches the influence spans. It is taken from the metrics
rather than from the icon count on purpose — a room that grew with a short row would move a
pushed dock each time it gained an icon.

The window is placed by the same fraction along the screen as the row is along the window,
and that composes: the bar lands in the same place whatever size the window is. It has to,
because the settings dialog deliberately holds the window wider than the dock, and a row
placed any other way would wander along the edge while a slider was dragged. The
drop-preview reserve and the dialog's held size stay invisible for the same reason.

At either end the window reaches a little past the side of the screen — the slack it keeps
beside the bar for the shadow is wider than the margin the bar keeps from the edge — so labels
are kept on the screen as well as in the window (`DockBar.OnScreen`), or a long name on the
end icon is drawn across the edge, onto the display next door if there is one. Auto-hide's
reveal zone followed from it too while it was the window's width and more; it is the bar's
now, which the placement keeps on the screen, and is clipped to the dock's own display all the
same, for a bar with more icons than the display is wide — a cursor on the display next door
cannot summon it. Separately, because a move along the edge
shifts both the row inside the window and the window itself, the acrylic sheet is placed
once per settings change rather than after each: between the two it would be a whole slider
tick away from the bar.

**The icons are outside WPF's hit-test.** They have to be. The window never takes focus, so
WPF routes no mouse input to it at all — clicks are read from the window procedure and the
pointer is polled — and an icon that can be hit is an element an OLE drag can be *over*.
Splicing a drop preview into the row removes whatever the cursor was on, which raises
`DragLeave` from inside the handler that just added the preview; the leave-check then took
the preview down, and the next mouse move put it back, five times a second. With nothing
under the cursor but the bar, which no rebuild removes, the only leave left is a real one.

**Rebuilding the row carries the elements over.** `DockBar.RebuildVisuals` matches incoming
items to existing visuals by id and rebinds them, creating and destroying only what actually
changed. Clearing the canvas and building fresh elements — which is what it used to do — made
a moving drop preview flicker, because every icon in the dock was discarded and re-rastered
on each slot the preview passed through. It also meant the window was re-sized on every
rebuild; it is now re-sized only when the label headroom changes, or when the count it is
*sized* for does — which a preview never changes, because its slot is already reserved.

**Position is two displacements, added.** Each icon carries a slot it is heading for and a
slot it is currently at, and eases between them on an exponential decay — an exponential
rather than a tween because a slot gets re-aimed mid-flight, as the insertion point of a drag
moves, and a tween would have to be restarted, which is what makes re-aimed animations
stutter. The transform adds that slide to the wave's spreading. The two are independent, so a
row still settling from a reorder magnifies correctly while it settles, and a settled row
adds exactly nothing — the slide term is zero and the geometry is the same as it ever was.
The dragged icon is the one exception: it is snapped to the cursor rather than eased toward
it, so it tracks the pointer exactly, and it slides into its slot only once it is dropped.

**Input is polled, not evented.** The window is transparent and hit-tests only where the dock
draws, so WPF's mouse events cut out over the gaps between magnified icons. A `GetCursorPos`
per tick is cheap and never misses. The same reasoning applies to edge reveal, which polls
rather than installing a `WH_MOUSE_LL` hook — a stalled low-level hook degrades input for
every application on the desktop.

**The dock never takes focus, and that costs it WPF's mouse input.** `WS_EX_NOACTIVATE` keeps
clicking an icon from stealing the foreground, which is what lets "switch to a running app"
hand off cleanly, and `WS_EX_TOOLWINDOW` keeps the dock off the taskbar and out of Alt-Tab.
The catch is that WPF does not route mouse input to a non-activating window at all — no
`MouseEnter`, no `MouseLeftButtonDown`. The messages still reach the HWND, so clicks are read
from the window procedure (`DockWindow.OnWindowMessage`) and resolved against the hovered icon
the cursor poll already tracks.

**So the keyboard has a window of its own, and the hotkeys are registered, not hooked.**
`RegisterHotKey` against the dock's window, heard in its window procedure — for the reason
edge reveal polls rather than hooking: a low-level keyboard hook sits in the input path of
every program on the desktop. The dock's window must never take the foreground, so the keys go
to a second window (`Views/KeyboardHost`), invisible, click-through and laid over the bar, which
takes the foreground only while the dock has the keyboard — through `AppLauncher.Activate`,
which attaches to the foreground's input for the moment it takes, as the settings dialog does.
It turns keys into commands (`Dock/DockKeys`, unit-tested), and the dock holds its item up by
the same claim a menu takes (`DockBar.FocusItem`). It gives the foreground back only while it
still has it: a window raised from the keys has taken it and keeps it, and an app launched is
still starting, so the window the keys came from has it meanwhile and the app takes it from
there. The hotkeys in force are part of the settings in force (`DockWindow._applied`), so the
settings dialog previews them as it does everything else, and `Interop/HotkeyRegistry` asks
Windows for them only when they change or when it stops having to let them go: while a box on
the Hotkeys page records, and while an excluded program is in front.

**Fullscreen is judged by the window in front, and a program by its file name.** The edge
stands down only while the *foreground* window belongs to a listed program and covers the
dock's display; the hotkeys, whenever it belongs to one (`ForegroundApp.InFront`), whatever it
covers. The foreground rather than whichever window is topmost there, because
overlays — a frame counter, a voice chat's — are windows of their own laid over the game,
and none of them is ever the foreground; the cost is that a game left behind while another
display has focus no longer counts. Covering the display's bounds rather than its work area,
so a maximized window, which stops at the taskbar, is not taken for a fullscreen one — unless
the taskbar hides itself, when it does fill the screen and is counted. The desktop is ruled
out explicitly: its window covers every display at once (measured here as `(-3840,0)–(2560,2168)`
across both), and it is the foreground whenever the wallpaper was the last thing clicked.
Programs are matched on the executable's file name because games move — versioned install
folders, Steam libraries on another drive — and a match that broke would fail the way that
matters, with the dock rising over the game again. When a program's path cannot be read,
which a game's anti-cheat may refuse, the name comes from the system's process list instead,
which needs no handle to the process. The list consulted is the one in force
(`DockWindow._applied`), so the settings dialog's changes apply before Save. What is in front
is looked at four times a second and at every change of foreground (`DockWindow.CheckFront`),
since the dock hides for any window that fills its display. That test asks a different
question from the list's: whether the window in front covers the display's *work area*
(`ForegroundApp.Filling`), which a window maximized with the taskbar showing does and a
fullscreen one does too — geometry, and no program named. Only the handle tells the two
apart, since it is not drawn over a fullscreen window: one that covers the whole display is
fullscreen where the taskbar shows, and where it hides, unless it is maximized with a title bar
(`FullscreenApps.IsFullscreen`). That is the only time a window's style or state is read. The shell's passing
windows are not asked at all: while the taskbar, Alt+Tab's switcher, Start or the like is in
front, the dock holds still (`ForegroundApp.IsPassingShellInFront`) — Alt+Tab's switcher is
exactly the work area, and would otherwise slide the dock away and back on every switch. An
empty list — the default — stops the list's test before it reads any window at all. The hiding
is auto-hide's own path, run while such a window is in front whatever the setting
(`AutoHideController.Yield`), and the rules — hide, show, and when the handle shows — are
`Dock/DockFront`, apart from the windows, and unit-tested.

**Memory is given back when the dock goes quiet.** The garbage collector runs when an
allocation budget fills, not when a program goes idle, and a dock at rest allocates about
33 KB a second — so whatever a dialog, a menu or a run of the wave left behind stayed for as
long as the dock sat still. Opening the settings dialog once took it from 40 MB to 60, for
good. `Services/MemoryTrim` reads the allocation counter every 30 seconds, and once an
interval has been quiet after at least 8 MB of allocation, it collects, waits for the
finalizers, and collects again in aggressive mode. The finalizers are the part that matters:
two-thirds of what the dialog left was native memory — surfaces, bitmaps, render resources —
that only its objects' finalizers free, and an aggressive collection with the finalizers run
after it gave back only the managed third. It costs about 10 ms, on a pool thread, and
nothing is paged out to make the figure look smaller.

**The application icon is generated, not drawn once and downscaled.**
[tools/make-icon.py](tools/make-icon.py) lays the mark out separately at each of the nine
sizes in `Assets/ArtDock.ico`, on that size's own integer pixel grid, and builds the icon row
outward from the centre so symmetry is structural rather than arithmetic. The icon that ships
is the Acrylic treatment of that geometry — a graded ground, a glass tray and a highlight on
the magnified icon — drawn by [tools/make-brand.py](tools/make-brand.py) on the same grids and
copied from `brand/windows/ArtDock.ico`; `brand/` has the rest of the set, for the web and
for light backgrounds. A single 256px
drawing scaled down does not survive the trip: the icon this replaced spanned
`[-0.5, 255.5]` instead of `[0, 256]`, which left 50% alpha down the left and top edges
against a hard clip on the right and bottom — a grey ghost column and a visibly off-centre
mark at 16px — and its five-icon composition smeared into the small sizes. Below 32px the
outer icon pair is dropped and the widths are hand-set, because the ratios round to 2px there
and the magnification wave stops reading. The script asserts every frame is mirror-symmetric
before writing, and 20px and 40px frames exist so the tray at 125% and 150% has one to pick
rather than rescaling 24.

**Strings are keys into one table, and the XAML asks for them as `DynamicResource`s.**
`Localizer` keeps the current language merged into the application's resources as a
dictionary, so a key in XAML — `Text="{DynamicResource Settings.Size.Title}"` — resolves there,
and a change of language reaches windows that are already open, which is what lets the
language picker preview. The table is JSON rather than `.resx` because a `.resx` translation
compiles to a satellite DLL: installing one would mean installing code into the program
folder. It is not loose XAML either, because WPF's XAML reader constructs whatever types the
file names, which would make a language pack a way to run code. A JSON table is only ever
data. The read-outs beside the sliders were `StringFormat` bindings, which are fixed at
compile time, cannot be translated, cannot say "1 icon", and format in WPF's default of US
English whatever the language; `Localized.Key` and `Localized.Value` replace them, and are
told of a change of language by taking the culture as a resource of their own. A key the code
names and the table lacks shows up as the key itself — ugly and findable — and a test holds
the source against `en.json` in both directions, since the compiler cannot.

**Packs are folders of data under the user's local application data.** One folder per pack,
one per kind — `Packs\Languages`, `Packs\IconSets` — each described by a `pack.json` with a
common header (`PackManifest`): a format number, a kind, an id, a name, a version, the oldest
dock it needs. That header is what a download will be checked against, so the packs a user
copies in by hand and the ones the dock will one day fetch are the same thing to everything
that reads them. A pack that cannot be used is not silently absent: its picker says which
folder was skipped and why.

## Planned work

What is left: downloadable language packs and icon sets, whose contracts are written and whose
implementation is not; and, for the dock's own updates, the first release to publish them from,
and a signature of the project's own on the feed. Then the
smaller things: sharpening the acrylic backdrop, and the Exclusions page's blind spot for Store
apps.

Asked for, and not begun: starting an item as administrator with Ctrl+Shift+click, or with
Ctrl+Shift+Enter from the keyboard — the menu entry and the item editor's checkbox are built;
and the window previews from the keyboard, opened by pressing Up, as the taskbar's are. The previews themselves are built, and
not yet tried on screen.

Version 2 is planned: more than one dock, so a machine with
several displays can have one on each; colours for the item labels; running apps shown on the
dock as a Mac shows them, pinned or not; widgets, starting with a live clock and date;
subdocks, groups of items that open as a second dock above the one they are on; and built-in
translations beyond English. The blur keeping up with the bar was on this list, and is done.

## Known gaps

- **The blur is Windows' own.** Its strength is DWM's and cannot be set, and the bar's colour
  is a fill over it — over a fixed wash — rather than the colour of the blur itself.
- **Store apps can only be pinned by path.** Anything on disk can be pinned by browsing to
  it or dropping it; AUMID pinning is modelled end to end (`DockItem.Aumid`, `ShellTarget`)
  but there is no browser for `shell:AppsFolder` yet.
- **A folder can only be pinned by dropping it.** The Add menu's *Browse…* opens a file
  dialog, which cannot pick one.
- **Running-app matching falls back to file name.** Several Windows 11 apps are launcher
  stubs — pinned `notepad.exe` starts a process under `WindowsApps`. Matching on file name
  covers that, at the cost of a possible false positive between two different apps that share
  an executable name — which the window previews show plainly, with the other app's windows
  under the icon.
- **The window previews do not peek.** Resting on one of the taskbar's previews shows that
  window alone on the desktop; what does that is not public, and the dock does not use it. Nor
  are the pictures' corners rounded: DWM draws a thumbnail over everything in its rectangle.
- **No previews for a Store app pinned by its app id**, which has no running dot either, and
  none of windows on another virtual desktop, which Windows hides from the census.
- **A tab is chosen by its name.** Windows lets another program bring a File Explorer tab to
  the front only by its header, so of two tabs behind another with the same name — two folders
  called *Docs* — a click may bring the other one forward.
- **A window that goes to another folder of the same name may keep the first one's dot** until
  something else changes on the desktop. The dock hears a window change folders by its title
  changing, and two folders called *Docs* give it the same title.
- **The Add menu's Control Panel is never marked as open.** `control.exe` hands Control Panel to
  File Explorer and exits, so nothing runs under it for long enough to be seen.
- **The bottom edge only.** The Position page has no choice of edge: the dock's geometry is
  written across the screen rather than down it, so a side dock is a change to the whole
  layout rather than a setting. Left and right used to be offered greyed out and were taken
  off the page, and side edges are not planned. Choosing the *display* does work, on a
  mixed-DPI desktop as well.
- **A display chosen before the dock learned to recognise monitors is still found by name.**
  The dock now remembers the monitor itself, but a settings file written earlier holds only
  Windows' name for the display (`\\.\DISPLAY2`), and Windows can hand those names out
  afresh — on a wake from sleep, on this machine — which moves such a dock to the other
  screen by itself. Choosing the display once more on the Position page and saving stores
  the monitor, and from then on it stays.
- **A fullscreen Store app is seen as its frame, not as itself.** A packaged app built on
  the old UWP model draws inside a window that belongs to `ApplicationFrameHost.exe`, so that
  is the program the Exclusions page sees in front, and the one its menu offers — and listing
  it would stand the edge down for every such app at once. Games are nearly all ordinary
  programs, Game Pass ones included, and are unaffected.
- **Windows exposes no Start icon.** The taskbar's Start button is drawn by the shell's own
  XAML, compiled into its binaries; there is no image asset named for it anywhere under
  `SystemApps` or `SystemResources`, no parsing name that resolves to it, and the only
  Windows mark in `imageres.dll` is welded onto the system-drive icon. So the dock draws its
  own and ships it in `Assets/icons`. It will not follow a future change to the logo.
- **The dock's handle is missing from screenshots and recordings.** It inverts what is
  behind it, and to read that without reading itself it asks Windows to leave it out of every
  capture of the screen — the Snipping Tool, Print Screen and recorders included. It is on the
  monitor all the same. The price of having no other look — but for *No GPU*, under which it
  is the bar's colour, reads nothing, and is captured like any other window.
- **Only the window in front counts as fullscreen.** A video left playing fullscreen on the
  dock's display while you work on the other one has the dock back over it, if the dock
  floats, or under it with the handle drawn over the picture, if it does not. Overlays — a
  game's frame counter, NVIDIA's — are full-display windows that are never in front, and
  asking the window in front is what keeps them from counting.
- **Icons are read once and kept for as long as the dock runs**, not cached to disk. The
  Recycle Bin is the exception — never kept, and re-read whenever the shell says its icon
  changed. Invisible for most applications, whose icon does not change while they are
  pinned; one that ships a new icon in an update goes on showing the old one for as long
  as the dock is running.
- **English is the only language, and there is no icon set.** The support for both is built;
  the content is not.
- **Packs are not downloaded.** They are installed by copying them into place; there is no
  catalog to browse. `src/ArtDock/Downloads` is interfaces with nothing behind them.
- **Redraw is not cheap while the pointer is moving.** Sweeping the cursor across the dock
  continuously costs roughly half of one core; idle is about 2%. Most of that is inherent to
  `AllowsTransparency`, which re-blits the whole window each frame — the shadow accounts for
  only about a tenth of it. Not yet optimised. The frame is drawn on the GPU, not in
  software as this used to say: the Direct3D device is in the process, and forcing software
  rendering — which *No GPU* does — costs about half as much CPU again on a machine that has
  a graphics card, frame for frame — nearly all of it the bar's shadow, which *No GPU*
  therefore leaves out. Measured in a harness on a machine that has a graphics card: about
  14% of one core for the sweep as *No GPU* draws it, at 120 frames a second. What the wave
  costs on a machine with none has not been measured.
- **About 35–40 MB in Task Manager**, against 15.5 MB for ObjectDock, which is native code.
  Most of it is fixed cost — the runtime, WPF, and about 15 MB for the GPU driver's Direct3D
  device. Software rendering would bring the dock to about 21 MB at the price of CPU while the
  wave moves; that trade is not the dock's to make, and is the user's in *No GPU*, which is
  for machines with no such device to begin with. What it no longer does is keep what a burst
  of activity left behind — see *Memory is given back when the dock goes quiet*.

## Verifying

```bash
dotnet test tests/ArtDock.Tests
```

Nine hundred and thirty-two tests cover the cosine falloff (peak, range boundary,
monotonicity, zero range), the layout (prefix sums, bar width, non-overlap across a full pointer
sweep, empty and single-icon docks, the room the window keeps for the widest bar and its
shadow, and the hover span, which covers the bar wherever the wave is without moving when the
wave does), the bar while the acrylic sheet draws it (the shadow both draw, the invisible
stand-in that keeps it clickable, the shape the sheet is told once it has been drawn, drawn to
the fraction of a pixel in a window the twin of the dock's, and a sheet made on any thread),
what counts as click-through, held against Windows' own hit-testing off every display, and
the tuning values
being relative — that the wave is identical at any icon size, that the gap keeps its
proportions, and that a settings file written in pixels is carried over as the count and
share it always meant.

Later groups guard behaviour that was expensive to get right: that a separator holds its
resting size while its neighbours magnify, that a stale release cannot put away a held label
something else has since taken, that the item selected in the settings dialog is held up by its
id — found wherever a change to the list puts it, waited for when the dock does not have it yet,
and come back to when a menu or a dialog holding an item of its own lets go, with no claim
handed out for it — that a colour or opacity change produces metrics equal to the
ones in force — which is what lets the appearance path skip the layout, and what stopped
those sliders flickering — that an arriving drop preview grows in *under* the wave rather
than instead of it, and that two spellings of the same path are one pin, which is what stops
a second copy of an app being dropped onto the dock. The last group covers what may be
pinned at all — a document and a folder by their own names, nothing where there is no file,
and a pin that is not an executable never claiming to be running unless it is a folder,
which is lit by the File Explorer windows showing it — This PC included, and not a zip, a
folder to the shell that is a file as well — by the one name the shell gives a folder however
it is written. Four more cover those windows: each folder's in the census's order, a window
with tabs under every folder they show, once, only the census's windows, and Explorer asked
about them without failing. Thirteen more hold the item
editor's *Reset* to the names pins are made with: a document, a folder, a program and a target
that is not there as pinning names them, the Add menu's places, *Start* and *Settings* as the
menu names them, a web address by its site, and no name at all for an address that has none of its own.
And one pair guards the
room the window keeps for a drop preview — that it is there before the drop needs it, and
that a reorder does not ask for a different amount — because a window that resizes to fit a
preview flinches every time one comes and goes.

One small group covers the bar an empty dock draws — that it exists at all, that it is a
full-height target rather than a sliver, that emptying a populated dock leaves one slot
behind rather than the width it had, and, the one that matters, that it actually paints
pixels: correct geometry is no use if nothing is drawn, because it is the painted alpha that
Windows hit-tests a layered window by. Two more guard the seam between an empty dock and a
full one — that the first icon does not collapse the bar it is arriving into, and that a
second icon still opens a slot, so the floor that fixed the first did not switch the growth
off altogether.

A group of six holds a dragged icon inside the bar: that one dragged past either end stops
at the end slot, that the bound follows the dragged icon's own magnified width rather than
the resting pitch, that no pointer position anywhere on or off the screen puts it outside the
bar, and that a bar briefly narrower than the icon it holds does not throw — `Math.Clamp`
does exactly that when its bounds cross, which is reachable for a frame while the bar is
still opening over an icon that has just arrived.

The newest group covers moving settings between machines: that an export read back is the
same settings, that the file leads with its format version, that a file written before that
field existed is taken for format 1 and brought up to date rather than passed off as current,
and that the importer refuses what it
should — a newer format, a root that is not an object, something that is not JSON, and, the
one that matters most, a JSON file that is *not* ArtDock settings. That last case parses
happily and yields a full set of defaults, so without the check an import of the wrong file
would look like it had worked and would in fact have reset everything. Two more hold that every
pin comes in with an id of its own — a file written by hand can give two pins the same one, or
none, and the dock's menu finds the pin to remove or edit by it — and that pins which already
have their own keep them. Three hold the labels' lettering where it now lives: that a file from
when every pin carried it gives it to the dock, from the first pin that has any — a pin added
from the dock had none, and taking the first pin's would have put the labels back to the
default — that what a file says at the dock's level outranks what a pin still carries, and that
a file written now carries it on the dock and nothing on the pins.

Five more cover the Add menu's shell places — that *This PC*, the *User folder*, *Downloads*
and the *Recycle Bin* are offered at all, and that `DockPresets.Create` builds a pin for each despite
no file existing at its name. That last one is the whole point of them: routed back through
the existence check every other pin goes through, they would be turned down, and the menu
entry would fail by quietly doing nothing rather than by erroring. One beside them covers
*Settings*, which is pinned by AUMID rather than by target, and is offered too; four more that
the user folder's pin carries the name Explorer shows for it, and that a name the shell
cannot resolve comes back empty rather than as an exception. Two more cover
what a new dock starts with — the seven defaults, in order, with the user folder and Downloads
stored by name rather than as paths and the separator before the Recycle Bin — and that a dock which
already has pins is left alone. And two cover *Search apps…* and *Search games…*: that they come
straight after *Browse…*, and that `DockPresets.Create` makes nothing for them, the caller doing
the asking; and that a pin made from what a search found goes by the name the search showed, with
a program gone since left out.

Twenty-five cover dropping those places, which Explorer drags as a shell ID list and no
paths. One takes the data object the desktop itself hands to a drag of This PC and the Recycle
Bin, reads it through WPF's wrapper as a real drop is read, and gets the two preset pins back.
The rest: that a block built from the shell's own ID lists reads back as the places and files it
holds, and that a malformed one — a count past the end, an offset past the end, an ID list
cut short or one that would never advance — reads as nothing rather than being handed to the
shell. That This PC, the Recycle Bin and Control Panel under each of its three CLSIDs become
the very pins the Add menu makes, since everything from the Empty entry to icon sets keys on
the target; that another place is pinned by its shell name under Explorer's name for it, and
a CLSID registered nowhere is not pinned at all. That a mixed drag keeps its order and its
files, that a drag of files alone is still read from its paths, and that two drags of
different places are told apart. And that the drop answers with an effect the source
offered — a link, for these places, which offer nothing else — and never a move.

Four hold the line around the Recycle Bin: that the pin is recognised by its target rather
than by the preset key it was added from — since the key is never stored, and the item editor
takes free text — and that nothing near it is mistaken for it. A false positive there is an
*Empty Recycle Bin* entry offered on the wrong item, which is an offer to delete permanently
made about something else.

Twelve cover a launcher's pin being lit by the program it starts: `X Launcher.exe` by `X.exe`
beside it, written with a space, a hyphen, an underscore or nothing, and through a shortcut;
not when that program is not there, nor for `Launcher.exe` alone or a name that only starts
with the word; the pin still launching the launcher; and a scan's row of the launcher shown
open while the program is.

Sixteen cover which pins can be run as administrator, asked of the machine's associations as
the dock asks them: a program, a batch file and a console, by path or by a bare name on the
`PATH`; a shortcut by what it points at, and one that cannot be resolved not at all; and not a
folder, a document, a picture, a `shell:` place, a web address ending in `.exe`, or a Store
app. And that a pin is not run that way unless it asks to be, and one that asks is launched so.

Two more cover the bin's icon following the bin: that it is read afresh every time rather
than served from the cache every other icon comes from, and that an icon swapped in place is
actually drawn once the dock is told. The second is the half that belongs to WPF rather than
the shell — an element keeps what it drew, so a new icon handed to an item on screen changes
nothing until the dock repaints it. The shell's side, the announcement that the icon has
changed, is not unit-tested: the only ways to raise one are to change the bin or to
broadcast a fake one to every window on the machine. Ten more cover what the dock does with
the announcement since Windows stopped switching its setting reliably: a bin gone from full to
empty, or back, is a change though the setting has not moved — the case that update made — and
another program's image is not; an icon location is read as the shell reads it, variables
expanded and quotes off, a missing index being 0 and nonsense refused; and the bin full and
empty, read from this machine's own registry and `imageres.dll`, are two different pictures at
the size asked.

Twenty-one cover a pinned picture being drawn as itself, from real thumbnails of PNGs and JPEGs
written for the test in solid colours, so the middle of what comes back tells the
thumbnail from the shell's icon for the type. The picture comes back in its own shape and
opaque, which a JPEG's thumbnail had to be shown to be rather than assumed; ticking the
editor's box gets the square icon instead; an image chosen for the item beats both; and a
picture rewritten under its pin is read again rather than served from the cache. Which types
count is asked of Windows, and a web address ending in `.png` never counts, since asking the
shell for its thumbnail would be a download. One more renders a wide image on the dock and
finds empty rows above and below it — it was stretched to fill the square before. Two draw red
over blue and check which comes back on top: a solid colour reads the same either way up,
which is how the first build shipped every picture upside down with all the others passing.
The shell hands a thumbnail over with its rows the other way round from an icon's, and a
header that says otherwise.

Two more hold the shell's transparency to what it is: an icon and a thumbnail of grey at half
alpha, written for the test, must come back as that grey at half alpha. The shell's pixels are
straight rather than premultiplied, and read as premultiplied they came back white — which drew
the Recycle Bin's glass as a white box and put a white fringe round the Settings gear.

Twenty-eight cover the folders the dock draws. The design is read from an SVG built into the
program, which refuses anything it would not draw as a browser does, so the first test is
where a refusal would be met rather than on somebody's dock. Then: that it fills, to the pixel,
the box the shell's own folder icon fills at the size the dock asks for — drawn first a little
low, it was seen to sit under the Downloads folder beside it; that a folder has its colour at
the back and the same lightened in front; that a slanted edge is smoothed in every row it
crosses — the folder is drawn at four times the size and averaged down, and shrunk by drawing
again smaller it came out in steps, because `RenderTargetBitmap` reads 4 of every 16 pixels
whatever scaling mode it is given; that a symbol lands in the place the design gives it and
nowhere else, toned, white or black as asked; that its lines are as heavy as the lines on
Windows' own folders, and of one paint all through; that the Downloads arrow stands as tall as
Windows' own on its Downloads folder, 104 of the 256 units, where it once stood 14% the taller; that a white symbol casts a shadow and a
toned or black one none; that text takes the symbol's place inside a box of its own, six
letters across its width and one held to its height, and is cut to six characters without
splitting one that takes two; that one look is drawn once and shared; that
a stored colour, symbol and tone read back as written, a mangled colour still draws a folder,
and the hex box reads a colour only once one has been typed; that every symbol offered, and the
editor's tile for none, is in both of the theme's symbol fonts; that no two swatches read as one;
that a drawn folder beats an icon set and an image chosen for the item beats it; that the
settings dialog's item list shows every item's icon as the dock draws it — a folder drawn, or
the chosen set's — and none on a separator's row, and marks the folders and nothing else, in a
glyph both of the theme's symbol fonts have; that `shell:Downloads` counts as a folder on disk
while This PC, the Recycle Bin and a `.zip` do not;
and that a folder in an imported settings file is drawn from the file alone, with nothing on
disk beside it to have gone missing.

Nine cover the shadow an icon casts onto the bar. The one that matters is what it was asked
for with — a shadow that would not blur the icons: every pixel an icon covers whole is the same
with its shadow as without it, so the icon is drawn over the shadow untouched rather than merged
into one picture with it. The rest: that it falls below the icon, faint at its sides and next
to nothing above it, rather than as a ring round it; that it is faint at its darkest; that none
of it lies under the icon, where it would darken the icon's antialiased rim; that it is centred
on the icon and grows with it, a photo's thumbnail included; that an icon is shadowed once and
an empty picture not at all; that none is drawn when it is turned off; that the dock shadows
every icon, ones added later included, and takes them all away again; and that it is on for a
new dock, and for one whose settings were written before it existed.

Fifteen cover the room drop-down lists and tooltips are given for their shadows. One shows a
combo box in a window off every display and finds it fitted with nothing but the app-wide
handler asking — the rest call the fitting themselves, and passed while it never ran in the
dialogs, because WPF raises `Loaded` only on an element with a `Loaded` handler of its own. Two
hold the theme's templates to what the fix relies on — for the combo box a transparent popup, opened
below, a list with a drop shadow, placed against something the box's size; for the tooltip a
shadowed border and no margin of its own — since a theme that changed any of it would be left
alone, and the shadows would go back to being cut off without a test noticing. For the list:
that the room takes in the whole shadow at the sides and below; that the list still starts
under the box at the theme's gap when it opens below, and ends over the box at the same gap
when it opens above; that it lines up with the box at either end the popup is aligned to, and
is as wide as the box; that a shadow deeper than the box is tall gives up some of its room
rather than turning the placement inside out; and that fitting twice changes nothing. For the
tooltip, whose window cannot be opened off every screen, the arithmetic on the place WPF gave
it: that its room takes in its shadow all round, and a shadow's reach is its blur moved by its
depth; that the window starts that far up and to the left, so the tooltip inside it is where it
was; that the room stops at the edge of the display and of the taskbar, where going past would
have the whole tooltip pushed back; that its sides are mirrored in a right-to-left tooltip; and
that it is whole pixels at a scale that would have put the tooltip between two.

Nine cover the pins that act rather than open. The one that matters guards the other
direction: an ordinary target — an `.exe`, a `shell:` place, an AUMID, a web address, the
empty string — must never be mistaken for a command, because that check stands between every
pin in the dock and `ShellExecute`, and a target it claimed by mistake would be a pin that
silently stopped launching. The rest cover *Start* being offered and pinned as a command, a
command under the scheme that this build does not recognise being refused rather than guessed
at, and a command showing no running dot, since nothing runs under one.

Beside them, one test guards the two item locks against `DockSettings.Clone`:
`DockWindow.ContentsToSave` clones the stored settings before saving them, so a lock the
clone dropped would be switched off by the next reorder or drop — silently, and by the very
gestures the locks exist to govern. A second does the same for every stored setting at once,
by reflection over the file format — each is given a value other than its default and has to
survive the copy — so a setting added later is covered without anyone remembering to extend
it. The stakes are wider than the locks: a cancelled settings dialog saves a clone too, so a
property `Clone` forgets is reset on disk by pressing Cancel.

Forty-one cover moving the dock along its edge. Centred is exactly the dock as it was. Nothing
the dock can draw leaves its span at any alignment — every point of a wave crossing rows too
short to hold one and long enough to, at an ordinary magnification and the strongest there
is — and pushed all the way, the widest wave meets the end of the span exactly rather than
short of it or past it. The pushed end stays where it is as icons come and go, including
while the bar is still easing open over a new one. The wave's reach, taken in closed form,
agrees with the sweep that finds it the long way and bounds every row. A long name on the end
icon of a pushed dock stays on the screen, which its window does not quite — checked in
pixels, and checked first that without the fix the same label really does cross the edge.
And the one that matters most: the row lands in the same place on the screen whatever size
its window is, which is what lets the settings dialog hold the window wide without the dock
wandering along the edge under the slider.

Forty-four cover the string table. Three read the source: every key the XAML and the code
name is in `en.json`, every key in `en.json` is named somewhere, and every English string
formats — the first catches a typo that would show a key on screen, and the second keeps the
table from filling with strings nothing uses. The rest hold a translation to English: that it
falls back key by key; that a string using a placeholder English does not supply, or one that
is not a format string at all, is refused rather than shown — either would throw each time it
was drawn; that one leaving a placeholder out is allowed; that plurals are chosen by the
translation's own rules, Russian's three forms included; and that numbers take its culture.
Around them, the library: English always there and first, an installed pack listed and
loaded, six kinds of unusable pack reported rather than listed, and following Windows landing
on the nearest language there is — German for Austrian German, English for Japanese when there
is no Japanese. Five hold the two Englishes to each other: US English first and British beside
it, British only for a Windows set to it, no British spelling in `en.json`, every US spelling
in it given its British form in `en-GB.json`, and nothing in `en-GB.json` that says the same
as `en.json` — so a new string with *color* or *colour* in it cannot slip past the other file.

Twenty-two cover icon sets. Most are about which image a pin gets — an executable by name
wherever it lives, a shortcut by what it runs, a Store app by its id, a place by its target, a
document by its type, the Recycle Bin full and empty, the most specific match winning whatever
order the set lists them in, and the first of equals. The rest are about what a set may not
do, since it is a folder anyone can write to: an image path that climbs out of the folder, a
rooted one, a UNC one, one that is not a PNG, one that is not there, and a rule that matches
nothing are each dropped with the rest of the set kept; an id that could not be written safely
into the settings file is refused; and an image is decoded without the file being held open.

Seven cover finding the display the dock was put on once Windows has renamed the displays,
which on this machine happens across a wake from sleep. The monitor's path wins over a name
that now belongs to the other screen; a settings file from before paths were stored goes by
the name exactly as it did; a path that finds nothing falls through to the name, and so does
a monitor Windows gives no path for; nothing found is nothing, which the dock turns into the
main display; and two identical monitors, whose paths differ only by the connector, are told
apart.

Thirty-five cover the Exclusions page's two questions, the dock's own third, and the handle's
fourth. Nine are about
which program: one moved to another folder by an update is still matched, case is ignored, a
hand-written file name
matches as a path does — and nothing overreaches, so `mygame.exe` is not `game.exe`, a
folder named like the program is not the program, and a blank or null entry in a hand-edited
file matches nothing rather than everything. Nine are about filling the display,
against this machine's two displays at their real sizes: exactly, past the edges the way a
borderless window can, at negative coordinates, spread across both at once — and, the one
that matters, not a maximized window while the taskbar is showing, not one a row short, not
one on the other display, and nothing at all before the dock has a display to measure
against, since an empty rectangle is contained by everything. The settings round trip above
carries the list as well. Six more are what the dock goes under whatever *Always on
top* says, listed or not: a window in front covering the display's work area. The one that
matters is the maximize button with the taskbar showing, as Windows reports the window, its
invisible border hanging past the work area on every side; then a window that draws its own
frame and is the work area exactly, and a fullscreen one, which covers more. Not a snapped
half, not a window one row short of the taskbar, and not one maximized on the other display.
The last eleven hold the line between fullscreen and merely maximized, which only the handle
asks, since it is not drawn over a fullscreen window: anything over the taskbar is fullscreen
however it is styled, a maximized window is not, and where the taskbar hides itself the title
bar decides — StarCraft II's window, maximized with no title bar over the whole main display,
is the case that matters there. Seven held that line once before, and went when the dock came
to hide for both alike; it came back when the handle was taken off fullscreen windows.

Seventy-four cover the scans' arranging, and the case they are built around is StarCraft II's folder
as it really is on this machine — twelve programs, three of which call themselves
"StarCraft II". Those three have to come out as one row, the stub and the game together,
with the error reporter the only thing hidden and last. Two games that each ship a
`Launcher.exe` stay two rows, and games come in order of name with each one's helpers last.
Around that: copies of one program in
several version folders are one program, and the newest is the one kept; names that differ
only in case are one row; programs that describe themselves not at all are *not* lumped into
a row of the nameless; a helper sharing the game's name is kept apart from it, so hiding the
helpers cannot hide the game; and a program's name is read once however many copies there
are. Forty-one cases hold the line between helpers and apps by whole words rather than
substrings — `BlizzardError` is a helper and `Terror` is a game, `setup` is one and
`Setupper` is not — and the crash reporters the Steam library here turned up among its games
(`crs-handler`, `breakpad_server`, a bundled `7za`) are held to being helpers, while the games
beside them (`GoWR`, `tlou-i`, `RiftApart`) are held to not; so are `EAUpdater` and
`MySQLInstaller`, whose acronyms hid the word that gives them away until they were split off,
beside `MySQLWorkbench`, which the same split must not make a helper. Nine cover the order:
with StarCraft II's real programs, the game first and the one with no icon and no name last,
the game's name outweighing the larger editor, an open program ahead of all of it and a game
with one open ahead of the other games; a name that is the game's whatever its spacing and
trademark signs; installed apps ranked as one list, and Windows' tools below them but above a
program with no icon; and seven the filter, which matches a name, a file name or a game.
Two cover the one program a row becomes when *Search games…* pins it: StarCraft II's stub at
the top of its folder, not the game two folders down, which started by hand does not start;
and between programs as deep as each other, the one with an icon, then the larger.
The last four walk a real folder made for the test: programs at every
depth and nothing else — not a `.txt`, not a `game.exe.bak`, not a folder called `Folder.exe`
— the folders entered counted, none entered once stopped, and a junction pointing back at its
own ancestor not followed, which is the loop that would otherwise never end.

Thirty-three cover finding the games in the first place, from the records as they are on
this machine: both of Steam's libraries read out of `libraryfolders.vdf` with their
backslashes unescaped, an app manifest's name kept apart from its folder's ("Detroit: Become
Human" in "Detroit Become Human"), an Epic manifest read — and one that is broken, empty or
not a manifest at all costing only itself. A game publisher's program is its launcher's game,
the launcher itself is not, and the one that matters: "Patriot Memory" is nobody's, because
matching part of a publisher's name found "Riot" in it and offered its RGB software as a game.
A folder found twice is kept once under the first name given, one that is gone is dropped, a
quoted path is unquoted, and a game with no name takes its folder's.

Twenty-three cover *Scan for apps…* and the icon test it and the order lean on. A shortcut to
an app is kept; one to a help file, a document or nothing with a path is not, nor an
uninstaller — even one that runs the app's own program with an argument, which must not be
the shortcut the program is offered under — nor a helper's program, nor one that runs a host
(`control.exe`, `cmd.exe`, `wscript.exe`), nor a game's program, nor one that is gone. An app
named like a helper is still kept, since only the program's name is judged. Inside a folder
means under it, not beside it — "StarCraft II Beta" begins with "StarCraft II". And the icon
test reads real files: Explorer has an icon of its own, a file that is not a program and one
that is not there have none.

Twenty-two cover what the dock does about the window in front of its display, and when the handle
marks it (`DockFront`). Nothing filling the display has the dock where the settings have it; a
window filling it — maximized or fullscreen — has it hide; and a program on the Exclusions page
outranks both. Keeping a dock the pointer has brought up is not tested here: while the dock
hides for a window, that is auto-hide's own keeping of a revealed dock under the pointer,
reused whole. The handle comes as the dock hides and goes as it comes back, even from under a
window; marks a dock on screen that cannot be seen, hiding or not, and not one that can; is
not offered for a dock put away from the tray, which only the tray brings back; marks nothing
turned off; is never drawn over a fullscreen window — a listed program among them — even for a
hidden dock or with the settings dialog open; marks a dock hidden for a maximized window and
not one hidden for a fullscreen one; and shows under a dock in plain sight while that dialog is
open.

Eleven cover where across the screen the edge brings the dock up, and the strip below a dock
that is up keeps it there (`AutoHideController.IsUnderDock`), in physical pixels on both
displays: under the bar and no wider — not a pixel past either end, nor out in the room the
window keeps for the wave, where the edge answered until 2026-09-30 — so it lines up exactly
with a handle as wide as the dock; a bar wider than its display cut at the display's sides, so
a pointer on the display next door is not under it; and nothing under a dock not yet laid out.

Thirty-three cover the handle itself, most in physical pixels against this
machine's two displays at their real scales, since the handle is placed by `SetWindowPos` and
the scale is what is likeliest to go wrong. As wide as the dock it is the bar exactly; with a
width of its own it is centred on the bar wherever the dock sits along its edge, wider than the
bar included, and that width is in DIPs, so it is half as wide again in pixels on the 4K
display; it sits just above the taskbar and never on it, its thickness and lift scale too, and
every edge is a whole pixel. A width off the slider's range is held to it, and a scale or
width that is no number falls back rather than placing a window nowhere. (Three more held
where the pointer counted as on it, and went when resting on it stopped bringing the dock.)
Six more hold that the resting bar it is measured from is the bar a
dock at rest actually draws, at either end of its edge and empty. The last seven are the
inverted look: white comes back black and black white, a colour comes back as its opposite,
what is shown is opaque whatever the read said of alpha, and nothing past the last whole pixel
is touched — and the one that matters, that no grey from 0 to 255 comes back within fifty
levels of itself, which a plain inversion fails at mid-grey.

Twelve cover how often the handle reads what is behind it (`HandlePace`), against a simulated
display at 120 Hz where a read finishes at the next frame, as a real one does. Something still
is read about fifteen times a second; something moving every frame is read every frame, and a
video at 24 fps is followed within a frame once it has been seen to move; the first change of
something moving is seen at the quiet pace at worst; once it keeps still the reads slow down
again; a read that did not wait for a frame is held to one a frame, and a slower display is
read every frame of its own. And the one that matters for the cost: a caret blinking under the
handle does not keep it reading every frame, which reading fast after any change at all would.

Eleven cover an app counting as closing — its last window gone, its process not — measured on
Rider, whose process outlived its window by 5.2 s: that it is closing while any of its
processes runs, and not when they had already gone; that it ends when every one has exited,
not the first, when a window comes back, or at the six-second limit, for a program that stays
in the tray; that a click waits for it and is taken once, then, or when a window comes back;
that a click on one not closing is not held; that a launcher stub's pin finds its program by
file name, as the census does; and that clearing lets every process go and runs nothing.

Five cover the dock taking the environment the user signed in with as it starts: that a
variable of the shell that started it is taken out, one the shell changed is put back and one
it lacked added; that names compare without case, as Windows compares them; that a drive's
current directory (`=C:`), which is the process's own, is left alone; and that the
environment Windows builds is this user's. None adopts it — that would change the test
host's own.

Four cover what the project file tells the updater. The one that matters is that the folder
Velopack installs into is not the one the settings live in: Velopack treats its folder as its
own and its uninstaller removes it, so an install id of `ArtDock` would take the user's
settings and packs with the first uninstall. The others: updates come from a GitHub repository
over HTTPS, and a copy nobody installed — the test run itself — is not taken for an installed
one, and starts at sign-in from where it is, both before Velopack has looked for an
installation and after, which is the state a build run from the source tree is in whenever
the settings dialog asks.

Twenty-one cover starting at sign-in, under a scratch registry key and never the real Run key.
Task Manager's *Startup apps* page turns an entry off by writing a flag beside it rather than
deleting it, so an entry turned off there reads as registered but not on, and one turned back
on there reads as on — and so does one turned off in the Settings app's *Apps → Startup*, which
writes a different byte for off. The one that matters: ticking the System page's box over an
entry Windows turned off turns it on, where it used to rewrite the entry and leave it off. The
box writes the flags Task Manager writes: unticking keeps the entry and switches it off, with
the time, so the dock stays on Windows' list; with no entry it writes nothing. The setup and the
first run register without touching the flag, so a setup run over an installed copy leaves a
dock that was switched off off; and an uninstall takes the flag with an entry into its
installation, and leaves another copy's alone. The last three are the watch the settings dialog
keeps while it is open: it hears an entry written and a switch in Windows, and hears the second
of two in a row — a registration is good for one change, so that one is heard only if the
watch made it again — and hears nothing once it is stopped.

Three hold the settings dialog open to a screen reader, reading its XAML, since the dialog
cannot be built in the test host. The template that draws the page list names its content
`PART_SelectedContentHost`, the one name UI Automation finds a page's contents by — unnamed, a
screen reader reached the page names and not one setting; every slider, drop-down, text box,
checkbox and list has a name, its label's, where 39 of 64 had none; and both lists name their
rows, which were read as the type of the object each holds.

Eleven hold the dock to painting what its settings say, and the stock bar to the look it
always had. A dock just made, and one told the stock settings, paint the stock bar — which is
the one that failed: a dock's first fill used to be written out by itself, `#CCD2FF` at a half,
under settings that said `#EEF1FF` at 0.76, and telling the bar what it was already recorded
as holding changed nothing, so every dock on the stock settings painted the first until its
colour or opacity was touched and the second, a good deal lighter, from then until it was next
started. The stock values are now the ones that were on the screen, and the rest hold the move:
a file of the older format on the old stock pair is moved to the new one, so no dock changes
colour with the update; a file of this format holding the same pair is somebody's choice and
is left; so is an older file with a colour or an opacity of its own, or one matching the
taskbar, all of which were painted as asked all along; and migrating twice changes nothing.

Seventeen hold *No GPU*, and Windows' *Transparency effects*, to what they promise. With
transparency effects off the bar is solid and unblurred, painted as *No GPU* paints it, and the
shadows and the handle are left as chosen; the switch is the system's, on until the dock reads
it otherwise, and never written to the file. Off in a new dock and in a file written before it
existed; turned on by the dock itself at a first run with no hardware to draw with, and at no
other time — not with a graphics card, and not for a dock that already has settings; on, the
bar is solid, the blur off, the handle not inverting, neither the bar nor the icons casting a
shadow, whatever is stored; the solid bar is its colour at its opacity
over grey, from the grey itself to the colour itself; and what it overrides is kept underneath
through a save and a load, so turning it off puts back the dock that was there. The file
carries the setting and none of what follows from it. And the one read from the source: the
dock's window applies the blur, the opacity and the shadows in force and never the stored ones
— a line reading the stored blur would compile, work on every machine with a graphics card,
and bring the acrylic back on the ones the setting is for.

Four hold a dialog taken off Mica to having a background (`WindowMaterial`), on windows given a
handle and never shown: in the light theme and the dark its background is opaque, where the
theme's own is transparent and, with no material behind it, black; put back on the material it
has the theme's again; and a window that is transparent on purpose, as the dock's is, is left
alone.

Seventy-three cover the hotkeys. How one is written and read — the modifiers in Windows' order,
the keys by name, by their code where they have none, and by code throughout, so a key is the
same key on every layout — and what is refused: neither the Windows key, Ctrl nor Alt held; F12;
what Windows does in every window, Alt+F4 among it, which could be registered and would stop
windows closing everywhere; and what is not a key to take, a modifier or Caps Lock. That every
default is the Windows key and Ctrl, the places' on the keypad and their second keys unset, no
two sharing keys, and a place's second key opening the same place; that a hotkey cleared stays
cleared through the file, where one never set follows the default; that a file from before
hotkeys has the defaults, and one saved before the settings' hotkey and the places' defaults
came, a day after the others, has those as well; that the file leaves the defaults out and
carries a later version's actions through; that keys set on the page outrank a default, even one
higher on the page, while of two set on the page a first key keeps them over a second; that
quick launch is on for a new dock and for a file from before it, and off lets the items' keys go
— first and second, kept for later — with no other row told an item has its keys; and that every
item is on by itself too, for a new dock and a file from before, while one turned off lets its
own two keys go, kept for later, and has none to share; and that holding the Windows key and
Ctrl numbers the items and brings the dock up for a new dock and a file from before either. And,
with real registrations on the test's own thread of a combination no keyboard has, that Windows
gives it to one registration and tells the other it is taken; that the second, asking again once
it has been let go, is told it is free; that two actions can trade keys; and that the dock's are
let go of for real while the dialog records or a game is in front, and taken back after. One
reads the dialog's XAML for a row for every action, and an icon and a checkbox for every item,
which the dialog finds by name as it opens; and one the dock's source, to keep the hotkeys
standing down for a program on the Exclusions page in front at any size, rather than only for
one filling the display as the edge does — what is in front cannot be arranged in a test.

Thirty-five cover the keys along the dock: that they step over separators and stop at the ends
rather than going round; that the places are counted without the separators; that a letter
goes round to the next name starting with it, in the language's own sense of case; and that
each key means what it does anywhere in Windows — Esc and Alt+F4 give the keyboard back, the
menu key and Shift+F10 open the menu, Ctrl and Alt still held from the hotkey do not stop the
arrows or a digit going to its place, the Windows key does, and Ctrl+Shift+Enter, Windows' way
to start a program as administrator, does nothing yet.

Nine cover the Windows key and Ctrl held. That the hold counts the moment they are down, and
ends the moment they are let go; that anything else pressed with them — an arrow, Shift, Alt —
cuts it short, or keeps it from starting, until both are let go, and that one of the dock's own
hotkeys, which the keys cannot see, ends it without cutting it short; and, from the source, that
Win+Ctrl+H goes by the dock as it was before the two brought it up. That the items are numbered
as the hotkeys count them, without the separators, only those asked for and none past the ninth,
and that an item added while the numbers are up is numbered where it stands. And that the number
is drawn on the icon's top-left corner in the accent, shading darker, and nothing of it anywhere
else. The keys themselves are read from Windows and cannot be pressed in a test.

Twelve cover a dock put away from the tray while something holds it up
(`AutoHideController.HoldRevealed`), on the dock's own auto-hide over a window given a handle
and never shown. Held, it comes up; let go, it goes away again — when the last of two holds
lets go, not the first, and put away rather than merely hidden even with a window filling the
display in front, so that window going does not bring it back. A dock that was up stays up, and
one auto-hide slid away is auto-hide's again. A choice made meanwhile is kept: hidden from the
tray or by the hotkey while held, the dock stays away, and shown again it stays up. Auto-hide
turned on meanwhile has the dock from then on; turned off again before the hold lets go, the
dock is put away as before. And two read the source, to keep the hotkeys that hide the dock and
open the settings asking before the keyboard lets go of it — the other order puts the dock away
as the keys let go, and the toggle then brings it straight back, or the dialog a moment later.

Forty-three cover the window previews. Their layout: a window's shape kept inside the picture
box, whatever its size or a size that could not be read; a narrow window still given room for
its title; every part inside its card and every card inside the panel, in order and apart; the
panel centred on the icon, kept on the display at both ends and on the left-hand display in its
own coordinates, scaled by half again at 150%, and always standing clear of the dock; a row too
wide shrunk alike until it fits, then a list, and a list too tall cut to what fits. Their timing:
opening after the hover time and not before, the wait starting again on another icon, never
with a button down; staying open on the panel however long, closing after the leave time
elsewhere and not while on another icon; moving to another app only after resting on it, so
cutting across a neighbour on the way up does not; and a dismissed item staying shut until the
pointer has left it, while another still opens. The windows kept in the order first seen, a
new one last and a closed one forgotten; the held icon drawn where the wave puts it, carried
outward at the ends; and the setting on, and the delay 250 ms, for a new dock and a file from
before them, both carried through an export.

## Licence

ArtDock is free to use, change and share, under the MIT licence with the
[Commons Clause](https://commonsclause.com/) added: it may not be sold, and neither may any
product or service whose value comes entirely or substantially from it — hosting and paid
support included. The whole text is in [`LICENSE`](LICENSE).
