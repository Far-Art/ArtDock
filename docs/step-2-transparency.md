# Step 2 — why the dock window is WPF and not WinUI 3

The dock's silhouette depends on per-pixel window transparency: icons magnify *above* the
bar, so the window has to be taller than the visible bar with the surplus fully
see-through. This note records what was measured, so the decision is not re-litigated.

## Measured on Windows App SDK 2.4.0 (Windows 11 26200)

Every candidate was built into one binary behind `--chrome-mode=N` and captured with a
screen-diff harness: shoot the strip above the bar with the dock running and again with it
closed, then count identical pixels. Identical = genuinely transparent.

| Route | Result |
| --- | --- |
| `Microsoft.UI.Xaml.Media.TransparentBackdrop` | **Does not exist** in 2.4.0 — metadata carries only `MicaBackdrop` and `DesktopAcrylicBackdrop` |
| Custom `SystemBackdrop` writing a transparent brush into `ICompositionSupportsSystemBackdrop.SystemBackdrop` | `OnTargetConnected` fires, brush installs, window still opaque |
| `SetWindowCompositionAttribute` — `ACCENT_ENABLE_TRANSPARENTGRADIENT` | `rc=1`, 0% see-through |
| …`ACCENT_ENABLE_BLURBEHIND` | `rc=1`, 0% see-through |
| …`ACCENT_ENABLE_ACRYLICBLURBEHIND` | `rc=1`, 0% see-through |
| …`ACCENT_ENABLE_HOSTBACKDROP` | `rc=1`, 0% see-through |
| `DwmExtendFrameIntoClientArea` with `-1` margins (sheet of glass) | `hr=0`, 0% see-through |
| `WS_EX_LAYERED` + `SetLayeredWindowAttributes(LWA_COLORKEY)` | Key colour **rendered, not punched out** |

All of them were combined with `DWMWA_SYSTEMBACKDROP_TYPE = DWMSBT_NONE` and a transparent
XAML root. The conclusion is that WinUI 3's DirectComposition-presented content is composited
opaquely and ignores the window-level transparency mechanisms Win32 offers.

Worth noting what *did* work on WinUI 3, since it transfers to any host: `WS_EX_TOOLWINDOW`
(off the taskbar), `WS_EX_NOACTIVATE` (no focus theft), `AppWindow.IsShownInSwitchers = false`
(off Alt-Tab) and `HWND_TOPMOST` all behaved correctly.

## Decision

The dock window is **WPF** (`AllowsTransparency="True"`, `WindowStyle="None"`), which supports
per-pixel transparency natively — measured at **100% see-through** on the same harness. The
settings dialog is WPF too, so the app is one framework end to end.

WPF also hands us text, layout, input and UI Automation for free, all of which a
Composition-rendered window would have cost us (the runner-up option was raw
`Windows.UI.Composition` on a `WS_EX_NOREDIRECTIONBITMAP` window, which does support
transparency but has no text and no automation).

## Known consequence

`AllowsTransparency="True"` puts WPF on a layered window, which rules out a Windows blur or
acrylic *behind* the bar — the web dock's `backdrop-filter: blur(18px)` has no direct
equivalent. The bar is therefore a translucent flat fill. If real blur becomes important, the
way to get it is a second, non-transparent window behind the dock carrying the accent-policy
acrylic, with the transparent dock window on top; that is deliberately not built yet.
