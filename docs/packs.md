# Packs: languages and icon sets

A **pack** is add-on content for the dock: a translation of its text, or a set of icons.
Each is a folder holding a `pack.json` and, for an icon set, its images. Packs are data only —
nothing in one is ever run.

Today a pack is installed by copying its folder into place. Downloading them is planned and
not built; see [downloads.md](downloads.md). A downloaded pack will land in exactly the same
place, so everything here applies to both.

## Where they go

```
%LOCALAPPDATA%\ArtDock\Packs\
  Languages\
    <any folder name>\pack.json
  IconSets\
    <any folder name>\pack.json
    <any folder name>\*.png
```

The *Open folder* buttons beside the *Language* picker (System page) and the *Icon set*
picker (Icons page) open these folders, creating them if they are not there yet. The
pickers read the folders again whenever the settings window comes back to the front, so a
pack copied in while the window is open appears as soon as you switch back to it.

A pack that cannot be used is not silently left out: the picker's card says which folder
was skipped and why.

## What every `pack.json` has

The file is JSON, read forgivingly — property names in any case, `//` comments and trailing
commas allowed.

| Field | Required | Meaning |
| --- | --- | --- |
| `format` | yes | The file's layout. `1` today. A pack in a newer format than the dock reads is skipped, not guessed at. |
| `kind` | yes | `language` or `iconSet`. Must match the folder the pack is in. |
| `id` | yes | What the settings file stores to choose the pack, so it must not change between versions. A language's id is its language tag; an icon set's is a short name — letters, digits, `.`, `-`, `_`, at most 64. |
| `name` | yes | What the picker shows. |
| `version` | no | The pack's own version, `1.2.0`. Will be compared with a catalog's to offer updates. |
| `minAppVersion` | no | The oldest ArtDock the pack works with. An older dock skips it. |
| `author` | no | Who made it. |
| `license` | no | What it may be used and shared under — an SPDX identifier where there is one (`CC-BY-4.0`, `MIT`). |
| `description` | no | A sentence about it. |

## Language packs

A language pack is the dock's English string table, translated. The English table is
[`src/ArtDock/Localization/en.json`](../src/ArtDock/Localization/en.json), in US English — start
from a copy of it. British English,
[`en-GB.json`](../src/ArtDock/Localization/en-GB.json) beside it, is a built-in translation of
the smallest kind: it gives only the strings British English spells differently.

```jsonc
{
  "format": 1,
  "kind": "language",
  "id": "de",                 // a language tag Windows knows: de, ru, pt-BR, zh-Hans …
  "name": "Deutsch",          // the language's name in itself
  "englishName": "German",    // shown beside it, so the way back to English can be found
  "direction": "ltr",         // or "rtl"
  "version": "1.0.0",
  "author": "…",
  "license": "CC-BY-4.0",
  "strings": {
    "Common.Save": "Speichern",
    "Settings.Size.Influence.Value.one": "{0:0} Symbol",
    "Settings.Size.Influence.Value.other": "{0:0} Symbole"
  }
}
```

**A translation can be partial.** Any key it leaves out is shown in English, so a pack can be
used — and tried — long before it is finished.

**Placeholders.** `{0}`, `{1}` and so on are where the dock puts a value, sometimes with a
format after a colon: `{0:0}` a whole number, `{0:0.00}` two decimals, `{0:P0}` a percentage.
Keep them. A translation may leave one out (`"eine Sekunde"` for "1 second" is fine), but one
that uses a placeholder the English does not have, or that is not a valid format string, is
refused and the English shown instead — it would otherwise break every time it was drawn.
Numbers are formatted in the pack's own language: German gets `16 %` and `1,40x`.

**Plurals.** A string that varies with a number has a key per plural form:
`….one`, `….other`, and whichever of `zero`, `two`, `few`, `many` the language uses —
[CLDR's categories](https://cldr.unicode.org/index/cldr-spec/plural-rules). English uses
`one` and `other`; Russian `one`, `few`, `many` (and `other` for fractions); Japanese only
`other`. Always give `.other`: it is what any form you did not give falls back to. The rules
the dock knows are in
[`PluralRules.cs`](../src/ArtDock/Localization/PluralRules.cs); a language not listed there
takes the English rule.

**What stays in English whatever the pack says**: the product name, and anything Windows or
the shell draws itself — the file dialogs, the Recycle Bin's confirmation, the names of the
apps on the dock.

**Replacing a built-in language.** An installed pack with the same id as a language built
into the dock is used instead of it, so a correction to a shipped translation can be tried
without a build.

**Right to left.** `"direction": "rtl"` mirrors the dialogs and menus. It has not yet been
tried with a real right-to-left language.

## Icon sets

An icon set is a folder of PNG images and a `pack.json` saying which image is drawn for what.

```jsonc
{
  "format": 1,
  "kind": "iconSet",
  "id": "flat",
  "name": "Flat",
  "version": "1.0.0",
  "license": "CC-BY-4.0",
  "icons": [
    { "match": { "exe": "notepad.exe" },                              "file": "notepad.png" },
    { "match": { "path": "%WINDIR%\\explorer.exe" },                  "file": "explorer.png" },
    { "match": { "aumid": "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App" }, "file": "calc.png" },
    { "match": { "target": "shell:RecycleBinFolder", "state": "empty" }, "file": "bin-empty.png" },
    { "match": { "target": "shell:RecycleBinFolder", "state": "full" },  "file": "bin-full.png" },
    { "match": { "target": "shell:MyComputerFolder" },                "file": "this-pc.png" },
    { "match": { "target": "artdock:start" },                         "file": "start.png" },
    { "match": { "extension": ".pdf" },                               "file": "pdf.png" },
    { "match": { "folder": true },                                    "file": "folder.png" }
  ]
}
```

### What an image can be matched to

Every field given in `match` must fit; a field left out matches anything.

| Field | Matches |
| --- | --- |
| `exe` | An executable's file name, wherever it is installed — and a shortcut that runs it. |
| `path` | A full path, which may use environment variables. The pin's own target, or what a pinned shortcut points at. |
| `aumid` | A Store app, by its Application User Model ID. The *Settings* preset is one: `windows.immersivecontrolpanel_cw5n1h2txyewy!microsoft.windows.immersivecontrolpanel`. |
| `target` | A target exactly as the pin stores it: the places and actions with no file behind them — `shell:RecycleBinFolder`, `shell:MyComputerFolder`, `artdock:start` — or a web address. This PC, the Recycle Bin and Control Panel are stored the same whether added from the menu or dropped from Explorer; any other place that was dropped is stored as `shell:::{CLSID}`, Network as `shell:::{F02C1A0D-BE21-4350-88B0-7367FC96EF3C}`. |
| `extension` | A document by its type, `.pdf` (the dot is optional). |
| `folder` | `true` for any pinned folder. |
| `state` | `empty` or `full`, for the Recycle Bin. Without a state the bin is drawn the same either way. |

**The most specific match wins**, whatever order the rules are in: `path`, then `aumid` and
`target`, then `exe`, then `extension`, then `folder` — and a rule with a `state` beats the
same rule without one. Among equally specific rules the first listed wins. So a set can say
"every folder looks like this" and still give one folder its own image.

**What wins over the set**: an icon chosen for one item in its own settings (*Item
settings…* → *Choose image…*), and a folder given a colour of its own there, which the dock
draws itself. **What the set does not cover** keeps its own icon.

### The images

- **PNG only**, square, with transparency where the icon is not. The dock does not read SVG.
- **256 × 256 or larger.** The dock magnifies; at its largest, on a 150% display, an icon is
  well over 300 pixels. Larger images are scaled down to 384 when read, never up.
- **Inside the set's folder.** A path that leads out of it — `..\`, a drive, a network path —
  is refused, as is anything that is not a `.png`. A refused image is reported under the
  picker and the rest of the set is still used.

### Licensing

Icons are the part of this with a real cost. Most "macOS-style" icon packs found online are
not the downloader's to redistribute, and the logos of the apps they depict are trademarks.
Draw your own, or use sets whose licence allows redistribution, and say which in `license`.
