using System.Globalization;
using System.Runtime.InteropServices;
using System.Security;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ArtDock.Dock;
using ArtDock.Interop;
using ArtDock.Localization;
using Microsoft.Win32;

namespace ArtDock.Views;

/// <summary>One window as the previews show it.</summary>
/// <param name="Window">The window itself.</param>
/// <param name="Title">Its title as it was last read.</param>
/// <param name="Icon">The icon drawn before the title: the item's own.</param>
/// <param name="CanClose">Whether the dock can close it, and so whether it gets a close button.</param>
internal sealed record PreviewEntry(nint Window, string Title, ImageSource? Icon, bool CanClose);

/// <summary>
/// The panel the previews of an app's windows are shown in, above the dock: a card for each
/// window, its title in a row above a live picture of it.
/// </summary>
/// <remarks>
/// <para>
/// An ordinary, unlayered window on the accent policy's acrylic, with Windows' own rounded
/// corners — the taskbar's route, and the one that blurs in a window that is never active, as
/// this one never is (<c>WS_EX_NOACTIVATE</c>). Measured before choosing (TODO.md, *Resolved*,
/// "What a window holding window previews can be"). Under *No GPU*, or with Windows'
/// transparency effects off, it is a solid sheet of the theme's colour instead.
/// </para>
/// <para>
/// <b>Made where it is shown, in physical pixels, and never moved to another display.</b> WPF
/// takes a window's scale from where it is created and keeps its size in DIPs across a change of
/// scale; a window made off screen and moved onto the 150% display was resized under it, as the
/// acrylic sheet was. So it is created hidden at its final rectangle, shown once WPF has drawn it,
/// and a panel wanted on another display is a new one.
/// </para>
/// <para>
/// <b>It slides and fades, as Windows' does</b>: up into place and in as it opens, sideways to
/// the next item when the pointer moves along the dock, and out as it closes. The slide moves the
/// window, which carries the pictures, being in its client area; and only ever above the dock's
/// hover zone — the rise starts at the floor <see cref="PreviewLayout"/> keeps it above, since a
/// panel over the zone even for a frame tells the dock it is covered and drops the wave. Off with
/// *Reduce motion* or Windows' animations.
/// </para>
/// <para>
/// <b>The fade is the whole window's, and only while it fades.</b> WPF's opacity never reaches
/// DWM's pictures and the blur cannot fade, but a layered window's single alpha
/// (<c>SetLayeredWindowAttributes</c>) is applied by DWM to everything it composes for the window:
/// the blur, what WPF drew and the pictures alike — measured 2026-10-03, at both scales, with the
/// blur intact at full alpha and no flash when the style comes off again. WPF will not have the
/// style, though: <c>HwndSource</c> takes <c>WS_EX_LAYERED</c> back off a window it does not draw
/// per pixel, every time, so <c>WM_STYLECHANGING</c> is answered here and the style kept or left
/// as <see cref="_layered"/> says. Layered only for the fade, since that is all that was measured.
/// </para>
/// <para>
/// The mouse comes to the window procedure, as the dock's does — clicks there, marked handled,
/// with the press and the release on the same card. WPF hears it too (measured), but nothing in
/// the panel is an element to route to: it is drawn, and its parts are found from
/// <see cref="PreviewLayout"/>'s rectangles.
/// </para>
/// </remarks>
internal sealed class PreviewWindow : IDisposable
{
    private const int WS_POPUP = unchecked((int)0x8000_0000);
    private const int WS_EX_TOPMOST = 0x0000_0008;
    private const int WS_EX_TOOLWINDOW = 0x0000_0080;
    private const int WS_EX_NOACTIVATE = 0x0800_0000;

    private const int WM_MOUSEACTIVATE = 0x0021;
    private const int WM_MOUSEMOVE = 0x0200;
    private const int WM_MBUTTONDOWN = 0x0207;
    private const int WM_MBUTTONUP = 0x0208;
    private const int MA_NOACTIVATE = 3;
    private const int SW_SHOWNA = 8;

    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private readonly HwndSource _source;
    private readonly PreviewPanel _panel;
    private readonly List<DwmThumbnail?> _thumbnails = [];

    /// <summary>How long the rise into place takes as the panel opens, in milliseconds.</summary>
    private const double OpenMs = 200;

    /// <summary>How long the slide to another item takes.</summary>
    private const double SwitchMs = 167;

    /// <summary>How long the fade out takes as it closes.</summary>
    private const double CloseMs = 120;

    private const int WM_STYLECHANGING = 0x007C;

    private readonly bool _animate;
    private readonly int _rise;

    private PreviewArrangement _arrangement;
    private IReadOnlyList<PreviewEntry> _entries;

    /// <summary>Where the window is now, which during a slide is on the way.</summary>
    private Int32Rect _placed;

    /// <summary>Where it is going: the arrangement's panel.</summary>
    private Int32Rect _target;

    private Int32Rect _slideFrom;
    private long _slideStart;
    private double _slideMs;
    private bool _sliding;

    /// <summary>Whether the window is to be layered — only while it fades. See WM_STYLECHANGING.</summary>
    private bool _layered;

    private bool _fadingIn;
    private bool _fading;
    private long _fadeStart;
    private double _fadeMs;

    /// <summary>True once it is fading out to close: nothing more is shown in it.</summary>
    private bool _closing;
    private bool _shown;
    private bool _disposed;
    private bool _thumbnailsPending;

    /// <summary>The card a button went down on, and which button; -1 for none.</summary>
    private int _pressed = -1;

    private bool _pressedClose;
    private bool _pressedMiddle;

    /// <param name="arrangement">Where the panel goes and what is in it.</param>
    /// <param name="entries">The windows, one to a card, in the arrangement's order.</param>
    /// <param name="solid">Whether to paint a solid sheet rather than blur — *No GPU*.</param>
    /// <param name="animate">Whether it slides, or simply appears and moves.</param>
    /// <param name="rise">How far below its place it starts as it opens, in pixels: no further than
    /// the floor it stands above.</param>
    public PreviewWindow(PreviewArrangement arrangement, IReadOnlyList<PreviewEntry> entries, bool solid, bool animate, int rise)
    {
        _arrangement = arrangement;
        _entries = entries;
        _animate = animate;
        _rise = Math.Max(0, rise);

        var panel = arrangement.Panel;
        var parameters = new HwndSourceParameters("ArtDockPreviews")
        {
            WindowStyle = WS_POPUP,
            ExtendedWindowStyle = WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST,
            PositionX = panel.X,
            PositionY = panel.Y,
            Width = Math.Max(1, panel.Width),
            Height = Math.Max(1, panel.Height),
        };

        _source = new HwndSource(parameters);
        _placed = panel;
        _target = panel;
        _source.AddHook(OnWindowMessage);

        var dark = !UsesLightTheme();
        var blurred = !solid && TransparencyEffects();

        // Left to right whatever the language: the cards' rectangles are DWM's too, and a mirrored
        // panel would draw each title over another window's picture. Only the text reads the
        // language's way.
        _panel = new PreviewPanel(dark, Localizer.FlowDirection);
        _source.RootVisual = _panel;

        if (blurred && DesktopComposition.EnableAcrylic(Hwnd, dark ? 0xA0202020 : 0xA0F3F3F3))
        {
            _source.CompositionTarget.BackgroundColor = Colors.Transparent;
        }
        else
        {
            _source.CompositionTarget.BackgroundColor = dark ? Color.FromRgb(0x2C, 0x2C, 0x2C) : Color.FromRgb(0xEE, 0xEE, 0xEE);
        }

        DesktopComposition.SetRoundedCorners(Hwnd, rounded: true);

        foreach (var entry in entries)
        {
            _thumbnails.Add(arrangement.IsList ? null : DwmThumbnail.Register(Hwnd, entry.Window));
        }

        _panel.Set(arrangement, entries, DeviceToDips());
        _source.ContentRendered += OnFirstRender;
        ScheduleThumbnails();
    }

    /// <summary>A card was clicked: go to its window.</summary>
    public event EventHandler<nint>? Chosen;

    /// <summary>A card's close button, or a middle click on it: close its window.</summary>
    public event EventHandler<nint>? CloseRequested;

    public nint Hwnd => _source.Handle;

    /// <summary>Where the panel is, or is sliding to, in physical screen pixels.</summary>
    public Int32Rect Bounds => _target;

    /// <summary>The windows shown, in order.</summary>
    public IReadOnlyList<PreviewEntry> Entries => _entries;

    /// <summary>
    /// Shows other windows, or the same ones laid out again: re-placed, re-drawn, and pictured
    /// anew only where the window in a card has changed. Each part is left alone where it is
    /// already right.
    /// </summary>
    public void Update(PreviewArrangement arrangement, IReadOnlyList<PreviewEntry> entries)
    {
        if (_disposed || _closing)
        {
            return;
        }

        var listChanged = arrangement.IsList != _arrangement.IsList;
        for (var i = 0; i < Math.Max(entries.Count, _thumbnails.Count); i++)
        {
            var wanted = i < entries.Count && !arrangement.IsList ? entries[i].Window : 0;
            var current = i < _thumbnails.Count ? _thumbnails[i] : null;
            if (!listChanged && (current?.Source ?? 0) == wanted && (wanted == 0 || current is not null))
            {
                continue;
            }

            current?.Dispose();
            var replacement = wanted == 0 ? null : DwmThumbnail.Register(Hwnd, wanted);
            if (i < _thumbnails.Count)
            {
                _thumbnails[i] = replacement;
            }
            else
            {
                _thumbnails.Add(replacement);
            }
        }

        while (_thumbnails.Count > entries.Count)
        {
            _thumbnails[^1]?.Dispose();
            _thumbnails.RemoveAt(_thumbnails.Count - 1);
        }

        _arrangement = arrangement;
        _entries = entries;
        Place(arrangement.Panel);
        _panel.Set(arrangement, entries, DeviceToDips());
        ScheduleThumbnails();
    }

    /// <summary>Each window's size as DWM pictures it, in pixels, for laying the cards out.</summary>
    public IReadOnlyList<Size> SourceSizes() =>
        [.. _thumbnails.Select(thumbnail => thumbnail?.SourceSize() ?? Size.Empty)];

    /// <summary>Lights the card under a point on the screen, in physical pixels; none when it is off the panel.</summary>
    public void PointAt(int screenX, int screenY)
    {
        var x = screenX - _placed.X;
        var y = screenY - _placed.Y;
        var card = CardAt(x, y);
        _panel.SetHover(card, card >= 0 && Contains(_arrangement.Cards[card].Close, x, y));
    }

    /// <summary>Keeps the panel above the dock, its sheet and its handle: topmost, and at the top of the topmost band.</summary>
    public void BringToTop()
    {
        if (!_disposed)
        {
            NativeMethods.SetWindowPos(
                Hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _source.ContentRendered -= OnFirstRender;
        CompositionTarget.Rendering -= OnRenderingThumbnails;
        CompositionTarget.Rendering -= OnRenderingSlide;
        CompositionTarget.Rendering -= OnRenderingFade;
        foreach (var thumbnail in _thumbnails)
        {
            thumbnail?.Dispose();
        }

        _thumbnails.Clear();
        _source.Dispose();
    }

    /// <summary>
    /// Closes the panel: fades it out and then gets rid of it, or at once when it does not
    /// animate or is not up yet. It shows nothing new and answers no clicks meanwhile.
    /// </summary>
    public void FadeAway()
    {
        if (_disposed || _closing)
        {
            return;
        }

        _closing = true;
        if (!_shown || !_animate)
        {
            Dispose();
            return;
        }

        StopSlide();
        SetLayered(true, alpha: CurrentAlpha());
        Fade(fadeIn: false, CloseMs);
    }

    /// <summary>The alpha a fade in has reached, or full.</summary>
    private byte CurrentAlpha() =>
        _fading && _fadingIn ? (byte)Math.Round(255 * FadeProgress()) : (byte)255;

    private double FadeProgress()
    {
        var t = _fadeMs <= 0 ? 1 : Math.Clamp((Environment.TickCount64 - _fadeStart) / _fadeMs, 0, 1);
        return 1 - Math.Pow(1 - t, 3);
    }

    private void Fade(bool fadeIn, double ms)
    {
        // A fade out taking over a fade in starts from where that had got to, rather than from full.
        var reached = CurrentAlpha() / 255.0;
        _fadingIn = fadeIn;
        _fadeMs = fadeIn ? ms : ms * reached;
        _fadeStart = Environment.TickCount64;
        if (!_fading)
        {
            _fading = true;
            CompositionTarget.Rendering += OnRenderingFade;
        }
    }

    private void OnRenderingFade(object? sender, EventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        var progress = FadeProgress();
        var alpha = _fadingIn ? progress : 1 - progress;
        NativeMethods.SetLayeredWindowAttributes(Hwnd, 0, (byte)Math.Round(255 * Math.Clamp(alpha, 0, 1)), NativeMethods.LWA_ALPHA);

        if (progress < 1)
        {
            return;
        }

        _fading = false;
        CompositionTarget.Rendering -= OnRenderingFade;

        if (_fadingIn)
        {
            // Back to the window that was measured and seen working, now that it is all there.
            SetLayered(false, alpha: 255);
        }
        else
        {
            Dispose();
        }
    }

    /// <summary>Makes the window layered at an alpha, or not layered.</summary>
    private void SetLayered(bool layered, byte alpha)
    {
        _layered = layered;
        var style = NativeMethods.GetWindowLongPtr(Hwnd, NativeMethods.GWL_EXSTYLE);
        var wanted = layered
            ? style | (nint)NativeMethods.WS_EX_LAYERED
            : style & ~(nint)NativeMethods.WS_EX_LAYERED;
        if (wanted != style)
        {
            NativeMethods.SetWindowLongPtr(Hwnd, NativeMethods.GWL_EXSTYLE, wanted);
        }

        if (layered)
        {
            NativeMethods.SetLayeredWindowAttributes(Hwnd, 0, alpha, NativeMethods.LWA_ALPHA);
        }
    }

    /// <summary>
    /// Sends the panel to a new place: at once before it is up or without animation, and
    /// otherwise by a slide from where it is — at its new size from the start, its middle where
    /// the old middle was and its bottom where the old bottom was, so the new contents arrive in
    /// the old place and travel to the new one.
    /// </summary>
    private void Place(Int32Rect panel)
    {
        if (panel == _target)
        {
            return;
        }

        _target = panel;
        if (!_shown || !_animate)
        {
            StopSlide();
            Move(panel);
            return;
        }

        Move(new Int32Rect(
            _placed.X + ((_placed.Width - panel.Width) / 2),
            _placed.Y + _placed.Height - panel.Height,
            panel.Width,
            panel.Height));
        SlideTo(SwitchMs);
    }

    /// <summary>Puts the window somewhere, when it is not there already.</summary>
    private void Move(Int32Rect rect)
    {
        if (rect == _placed)
        {
            return;
        }

        var flags = NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOZORDER;
        if (rect.Width == _placed.Width && rect.Height == _placed.Height)
        {
            flags |= NativeMethods.SWP_NOSIZE;
        }

        NativeMethods.SetWindowPos(Hwnd, 0, rect.X, rect.Y, Math.Max(1, rect.Width), Math.Max(1, rect.Height), flags);
        _placed = rect;
    }

    /// <summary>Starts a slide from where the window is to <see cref="_target"/>.</summary>
    private void SlideTo(double ms)
    {
        _slideFrom = _placed;
        _slideStart = Environment.TickCount64;
        _slideMs = ms;
        if (!_sliding)
        {
            _sliding = true;
            CompositionTarget.Rendering += OnRenderingSlide;
        }
    }

    private void StopSlide()
    {
        if (_sliding)
        {
            _sliding = false;
            CompositionTarget.Rendering -= OnRenderingSlide;
        }
    }

    /// <summary>One frame of a slide: a cubic ease out, Windows' deceleration, in whole pixels.</summary>
    private void OnRenderingSlide(object? sender, EventArgs e)
    {
        if (_disposed)
        {
            StopSlide();
            return;
        }

        var t = _slideMs <= 0 ? 1 : Math.Clamp((Environment.TickCount64 - _slideStart) / _slideMs, 0, 1);
        var eased = 1 - Math.Pow(1 - t, 3);
        Move(new Int32Rect(
            (int)Math.Round(_slideFrom.X + ((_target.X - _slideFrom.X) * eased)),
            (int)Math.Round(_slideFrom.Y + ((_target.Y - _slideFrom.Y) * eased)),
            _target.Width,
            _target.Height));

        if (t >= 1)
        {
            StopSlide();
        }
    }

    /// <summary>
    /// Moves the pictures as WPF's frame with the new cards is drawn rather than now: DWM would
    /// put them in their new places a frame or two before the cards around them arrive.
    /// </summary>
    private void ScheduleThumbnails()
    {
        if (_thumbnailsPending || _disposed)
        {
            return;
        }

        _thumbnailsPending = true;
        CompositionTarget.Rendering += OnRenderingThumbnails;
    }

    private void OnRenderingThumbnails(object? sender, EventArgs e)
    {
        CompositionTarget.Rendering -= OnRenderingThumbnails;
        _thumbnailsPending = false;
        if (_disposed)
        {
            return;
        }

        for (var i = 0; i < _thumbnails.Count; i++)
        {
            var where = i < _arrangement.Cards.Count ? _arrangement.Cards[i].Picture : Int32Rect.Empty;
            _thumbnails[i]?.Show(where);
        }
    }

    private void OnFirstRender(object? sender, EventArgs e)
    {
        _source.ContentRendered -= OnFirstRender;
        if (_disposed || _shown)
        {
            return;
        }

        _shown = true;

        // Up from the floor into place and in from nothing, as the taskbar's previews come.
        if (_animate)
        {
            SetLayered(true, alpha: 0);
            if (_rise > 0)
            {
                Move(new Int32Rect(_target.X, _target.Y + _rise, _target.Width, _target.Height));
            }
        }

        NativeMethods.ShowWindow(Hwnd, SW_SHOWNA);
        BringToTop();

        if (_animate)
        {
            if (_rise > 0)
            {
                SlideTo(OpenMs);
            }

            Fade(fadeIn: true, OpenMs);
        }
    }

    /// <summary>WPF's own scale for this window, which is the one its drawing is in.</summary>
    private double DeviceToDips() =>
        _source.CompositionTarget?.TransformFromDevice.M11 is { } m and > 0 ? m : 1;

    private int CardAt(int x, int y)
    {
        for (var i = 0; i < _arrangement.Cards.Count; i++)
        {
            if (Contains(_arrangement.Cards[i].Card, x, y))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool Contains(Int32Rect rect, int x, int y) =>
        x >= rect.X && y >= rect.Y && x < rect.X + rect.Width && y < rect.Y + rect.Height;

    private nint OnWindowMessage(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        // WPF's own handling would take WS_EX_LAYERED off again: the style is this window's to say.
        if (msg == WM_STYLECHANGING && (int)wParam == NativeMethods.GWL_EXSTYLE)
        {
            // STYLESTRUCT: the old style, then the new one.
            var wanted = Marshal.ReadInt32(lParam, 4);
            wanted = _layered ? wanted | (int)NativeMethods.WS_EX_LAYERED : wanted & ~(int)NativeMethods.WS_EX_LAYERED;
            Marshal.WriteInt32(lParam, 4, wanted);
            handled = true;
            return 0;
        }

        if (_closing && msg != WM_MOUSEACTIVATE)
        {
            return 0;
        }

        switch (msg)
        {
            case WM_MOUSEACTIVATE:
                handled = true;
                return MA_NOACTIVATE;

            case WM_MOUSEMOVE:
                if (NativeMethods.GetCursorPos(out var cursor))
                {
                    PointAt(cursor.X, cursor.Y);
                }

                break;

            case NativeMethods.WM_LBUTTONDOWN:
            case WM_MBUTTONDOWN:
            {
                var (x, y) = ClientPoint(lParam);
                _pressed = CardAt(x, y);
                _pressedMiddle = msg == WM_MBUTTONDOWN;
                _pressedClose = _pressed >= 0 && !_pressedMiddle
                    && _entries[_pressed].CanClose && Contains(_arrangement.Cards[_pressed].Close, x, y);
                if (_pressed >= 0)
                {
                    NativeMethods.SetCapture(hwnd);
                }

                handled = true;
                break;
            }

            case NativeMethods.WM_LBUTTONUP:
            case WM_MBUTTONUP:
            {
                var (x, y) = ClientPoint(lParam);
                var card = CardAt(x, y);
                var pressed = _pressed;
                var close = _pressedClose;
                var middle = _pressedMiddle;
                _pressed = -1;
                NativeMethods.ReleaseCapture();
                handled = true;

                if (pressed < 0 || card != pressed || middle != (msg == WM_MBUTTONUP) || pressed >= _entries.Count)
                {
                    break;
                }

                var window = _entries[pressed].Window;
                if (middle || close)
                {
                    // A middle click closes, as on the taskbar — where the dock can.
                    if (close || _entries[pressed].CanClose)
                    {
                        CloseRequested?.Invoke(this, window);
                    }
                }
                else if (!Contains(_arrangement.Cards[pressed].Close, x, y) || !_entries[pressed].CanClose)
                {
                    Chosen?.Invoke(this, window);
                }

                break;
            }

            case NativeMethods.WM_CAPTURECHANGED:
                _pressed = -1;
                break;
        }

        return 0;
    }

    private static (int X, int Y) ClientPoint(nint lParam)
    {
        var value = (int)(lParam.ToInt64() & 0xFFFF_FFFF);
        return ((short)(value & 0xFFFF), (short)((value >> 16) & 0xFFFF));
    }

    /// <summary>Whether Windows is in light mode — the taskbar's and Start's setting, which its flyouts follow.</summary>
    private static bool UsesLightTheme() => ReadPersonalize("SystemUsesLightTheme", fallback: false);

    /// <summary>Whether Windows' transparency effects are on; its flyouts go solid without them.</summary>
    private static bool TransparencyEffects() => ReadPersonalize("EnableTransparency", fallback: true);

    private static bool ReadPersonalize(string name, bool fallback)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue(name) is int value ? value != 0 : fallback;
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException)
        {
            return fallback;
        }
    }

    /// <summary>
    /// Draws the panel's cards: the title row of each, the hover's highlight and the close
    /// button. The pictures are DWM's, over the top.
    /// </summary>
    private sealed class PreviewPanel(bool dark, FlowDirection reading) : FrameworkElement
    {
        private static readonly Typeface TitleFace = new(
            new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

        private static readonly Typeface SymbolFace = new(
            new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

        /// <summary>ChromeClose, in both symbol fonts.</summary>
        private const string CloseGlyph = "";

        private const double TitleSize = 12;
        private const double IconSize = 16;
        private const double CardRadius = 6;

        private readonly Brush _text = Frozen(dark ? Color.FromRgb(0xFF, 0xFF, 0xFF) : Color.FromRgb(0x1A, 0x1A, 0x1A));
        private readonly Brush _hover = Frozen(dark ? Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x12, 0x00, 0x00, 0x00));
        private readonly Brush _closeHover = Frozen(Color.FromRgb(0xC4, 0x2B, 0x1C));

        private PreviewArrangement? _arrangement;
        private IReadOnlyList<PreviewEntry> _entries = [];
        private double _toDips = 1;
        private int _hovered = -1;
        private bool _hoveredClose;

        public void Set(PreviewArrangement arrangement, IReadOnlyList<PreviewEntry> entries, double toDips)
        {
            _arrangement = arrangement;
            _entries = entries;
            _toDips = toDips;
            if (_hovered >= entries.Count)
            {
                _hovered = -1;
            }

            InvalidateVisual();
        }

        public void SetHover(int card, bool onClose)
        {
            if (card == _hovered && onClose == _hoveredClose)
            {
                return;
            }

            _hovered = card;
            _hoveredClose = onClose;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            if (_arrangement is not { } arrangement)
            {
                return;
            }

            var pixelsPerDip = _toDips > 0 ? 1 / _toDips : 1;
            for (var i = 0; i < arrangement.Cards.Count && i < _entries.Count; i++)
            {
                var card = arrangement.Cards[i];
                var entry = _entries[i];
                var hovered = i == _hovered;

                if (hovered)
                {
                    drawingContext.DrawRoundedRectangle(_hover, null, Dips(card.Card), CardRadius, CardRadius);
                }

                var title = Dips(card.Title);
                var textLeft = title.Left;
                if (entry.Icon is { } icon)
                {
                    drawingContext.DrawImage(icon, new Rect(title.Left, title.Top + ((title.Height - IconSize) / 2), IconSize, IconSize));
                    textLeft += IconSize + 8;
                }

                var closeShown = hovered && entry.CanClose;
                var textRight = closeShown ? Math.Min(title.Right, Dips(card.Close).Left - 4) : Dips(card.Card).Right - (title.Left - Dips(card.Card).Left);
                var text = new FormattedText(
                    string.IsNullOrWhiteSpace(entry.Title) ? " " : entry.Title,
                    CultureInfo.CurrentUICulture,
                    reading,
                    TitleFace,
                    TitleSize,
                    _text,
                    pixelsPerDip)
                {
                    MaxTextWidth = Math.Max(1, textRight - textLeft),
                    MaxLineCount = 1,
                    Trimming = TextTrimming.CharacterEllipsis,
                };

                drawingContext.DrawText(text, new Point(textLeft, title.Top + ((title.Height - text.Height) / 2)));

                if (closeShown)
                {
                    var close = Dips(card.Close);
                    if (_hoveredClose)
                    {
                        drawingContext.DrawRoundedRectangle(_closeHover, null, close, 4, 4);
                    }

                    var glyph = new FormattedText(
                        CloseGlyph,
                        CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight,
                        SymbolFace,
                        10,
                        _hoveredClose ? Brushes.White : _text,
                        pixelsPerDip);
                    drawingContext.DrawText(glyph, new Point(close.Left + ((close.Width - glyph.Width) / 2), close.Top + ((close.Height - glyph.Height) / 2)));
                }
            }
        }

        private Rect Dips(Int32Rect pixels) =>
            new(pixels.X * _toDips, pixels.Y * _toDips, pixels.Width * _toDips, pixels.Height * _toDips);

        private static SolidColorBrush Frozen(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }
}
