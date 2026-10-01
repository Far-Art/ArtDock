using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ArtDock.Dock;
using ArtDock.Interop;

namespace ArtDock.Views;

/// <summary>
/// The slim bar that marks the dock while it is out of sight — slid away by auto-hide, or under
/// the windows in front — the phone's home indicator, just above the taskbar, under where the
/// dock will come up.
/// </summary>
/// <remarks>
/// <para>
/// A window of its own, because the dock's is off the bottom of the screen, or under other
/// windows, while this is up.
/// Made by hand rather than as a WPF <see cref="Window"/>, like <see cref="BackdropWindow"/> and
/// for the same reason: its size is the dock's to set, in device pixels, and WPF's handling of
/// a change of scale would resize it. So <c>WM_DPICHANGED</c> is swallowed, and the pill is
/// drawn to fill whatever the window is, rounded from its own height — which makes whatever
/// scale WPF believes this window is at beside the point.
/// </para>
/// <para>
/// It never takes input, and nothing watches for the pointer on it: it is a mark and nothing
/// more. <c>WS_EX_TRANSPARENT</c> passes a click straight through to the window underneath,
/// which is usually the bottom row of something maximized — a status bar, a scroll bar. Resting
/// the pointer on it brought the dock up until 2026-09-30, when that was taken away on request;
/// the edge below the taskbar is the way up.
/// </para>
/// <para>
/// It shows the colours behind it <b>inverted</b> — read off the screen by a thread of its own,
/// since a read waits a whole frame for DWM (see <see cref="ScreenCapture"/>) — so it stands out
/// against anything, as the phone's does by turning dark over light and light over dark. There
/// is no choice of look; that was a setting once, and was taken away as a choice nobody needed.
/// The dock's colour is what it falls back on where Windows will not keep it out of captures of
/// the screen, since inverting then would mean inverting itself — and what it is given under
/// <c>DockSettings.NoGpu</c>, where the screen is not read at all.
/// </para>
/// </remarks>
public sealed class HandleWindow : IDisposable
{
    /// <summary>The same as the dock's slide, so the handle arrives as the dock goes.</summary>
    private static readonly Duration FadeInDuration = new(TimeSpan.FromMilliseconds(220));

    /// <summary>
    /// Far quicker the other way: gone before the dock coming back has risen past it.
    /// </summary>
    /// <remarks>
    /// It used to take the slide's time as well, and read as the handle going only once the
    /// dock had arrived — reported on 2026-09-30. The handle sits just under where the bar comes
    /// to rest (the bar's bottom at 1380 on the main display, the handle from 1381), so the
    /// rising bar covers it about 40 ms into the slide's ease and uncovers it again about
    /// 180 ms in, settling just above it — and a handle a fifth of the way from gone, in colours
    /// pushed away from whatever is behind, is plain to see. Eighty milliseconds has it all but
    /// gone before the bar reaches it.
    /// </remarks>
    private static readonly Duration FadeOutDuration = new(TimeSpan.FromMilliseconds(80));

    private readonly HwndSource _source;
    private readonly nint _hwnd;
    private readonly Border _pill;

    private (int X, int Y, int Width, int Height)? _placed;
    private Color? _color;

    /// <summary>
    /// The look in force: inverting, or the dock's colour for want of it — null before
    /// <see cref="SetLook"/> has been called.
    /// </summary>
    private bool? _inverting;

    /// <summary>
    /// False once Windows has refused to keep this window out of captures of the screen. It
    /// cannot invert then — each read would see its own inversion — so it takes the dock's colour.
    /// </summary>
    private bool _canInvert = true;

    /// <summary>True from <see cref="Show"/> until <see cref="Hide"/> — fading in, or up.</summary>
    private bool _wanted;

    /// <summary>True while the window is shown at all, which outlasts <see cref="_wanted"/> by the fade out.</summary>
    private bool _visible;

    /// <summary>Which fade is the latest, so an earlier one finishing late cannot hide the window.</summary>
    private int _fade;

    private bool _disposed;

    // ---- reading what is behind ----------------------------------------------
    //
    // Everything below _gate is shared with the reading thread and touched only under it.

    private readonly Lock _gate = new();

    /// <summary>What the reading thread reads: where the handle is, in physical pixels.</summary>
    private (int X, int Y, int Width, int Height) _region;

    /// <summary>The latest inverted read, waiting for the dock's thread to draw it.</summary>
    private byte[] _inverted = [];

    private int _invertedWidth;
    private int _invertedHeight;
    private bool _fresh;
    private bool _drawPosted;

    /// <summary>Wakes the reading thread early, when the handle has moved or has just come up.</summary>
    private readonly AutoResetEvent _wake = new(false);

    /// <summary>The run of the reading thread in progress, or null when nothing is being read.</summary>
    private CancellationTokenSource? _reading;

    /// <summary>What is behind, inverted, as drawn: made at the size of the first read to arrive.</summary>
    private WriteableBitmap? _behind;

    private readonly ImageBrush _behindBrush = new() { Stretch = Stretch.Fill };

    public HandleWindow()
    {
        _pill = new Border { Opacity = 0, SnapsToDevicePixels = true };
        _pill.SizeChanged += (_, _) => Shape();

        // Pixel for pixel when the read is the size of the window, which is always but for the
        // moment after a resize: then the last read is stretched until the next one lands,
        // rather than the handle going blank while it waits.
        RenderOptions.SetBitmapScalingMode(_pill, BitmapScalingMode.NearestNeighbor);

        _source = new HwndSource(new HwndSourceParameters("ArtDock.Handle")
        {
            WindowStyle = unchecked((int)NativeMethods.WS_POPUP),
            ExtendedWindowStyle = unchecked((int)(
                NativeMethods.WS_EX_TOOLWINDOW
                | NativeMethods.WS_EX_NOACTIVATE
                | NativeMethods.WS_EX_TRANSPARENT)),
            UsesPerPixelTransparency = true,
            PositionX = -32000,
            PositionY = -32000,
            Width = 1,
            Height = 1
        })
        {
            RootVisual = _pill
        };

        _hwnd = _source.Handle;
        _source.AddHook(OnWindowMessage);
    }

    public nint Hwnd => _hwnd;

    /// <summary>Where the handle was last put, in physical pixels; empty before it has been.</summary>
    public Rect Bounds => _placed is { } placed
        ? new Rect(placed.X, placed.Y, placed.Width, placed.Height)
        : Rect.Empty;

    /// <summary>Puts the handle where it goes, in physical pixels — only if it is not there already.</summary>
    public void Place(Rect bounds)
    {
        if (_disposed || bounds.IsEmpty || bounds.Width < 1 || bounds.Height < 1)
        {
            return;
        }

        var (x, y, width, height) =
            ((int)bounds.X, (int)bounds.Y, (int)bounds.Width, (int)bounds.Height);

        // Checked against where the window actually is as well as against what was asked for,
        // as every other window of the dock's is: re-placing a layered window where it already
        // is still re-composes it, and this runs on every frame of the dock's slide.
        if (_placed == (x, y, width, height)
            && NativeMethods.GetWindowRect(_hwnd, out var actual)
            && actual.Left == x && actual.Top == y
            && actual.Right - actual.Left == width
            && actual.Bottom - actual.Top == height)
        {
            return;
        }

        _placed = (x, y, width, height);
        NativeMethods.SetWindowPos(
            _hwnd, 0, x, y, width, height,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOZORDER);

        // Somewhere new has something new behind it; read it now rather than at the next turn.
        lock (_gate)
        {
            _region = (x, y, width, height);
        }

        _wake.Set();
    }

    /// <summary>
    /// Sets the handle's look: the colours behind it inverted — or, when that is not wanted or
    /// Windows will not keep it out of captures of the screen, <paramref name="fallback"/>, the
    /// dock's colour, outlined in whatever stands out against it.
    /// </summary>
    /// <param name="invert">
    /// False under <c>DockSettings.NoGpu</c>, where reading the screen back is work for a
    /// processor that is already doing the compositor's.
    /// </param>
    /// <remarks>
    /// <para>
    /// Inverting keeps the window out of screenshots and recordings, because that is what keeps
    /// it out of its own reads. Windows is asked for that on the way into inverting, before the
    /// first read; a refusal — before Windows 10 2004 — is kept, and the handle is drawn in the
    /// dock's colour from then on. On the way out it is let back into captures, since nothing
    /// is reading any more.
    /// </para>
    /// <para>
    /// The dock's colour nearly solid, where the bar is usually see-through. Nothing is blurred
    /// behind the handle the way the acrylic sheet is behind the bar, and a sliver at the bar's
    /// own opacity went into whatever it lay over. The hairline is in the opposite tone to the
    /// fill, so a light handle over a white window is still outlined, and a dark one over a
    /// dark window still has a light edge.
    /// </para>
    /// </remarks>
    public void SetLook(bool invert, Color fallback)
    {
        if (_disposed)
        {
            return;
        }

        invert &= _canInvert;

        if (invert && _inverting != true && !ScreenCapture.ExcludeFromCapture(_hwnd, exclude: true))
        {
            _canInvert = false;
            invert = false;
        }

        if (invert != _inverting)
        {
            var wasInverting = _inverting == true;

            _inverting = invert;
            _color = null;

            if (invert)
            {
                _pill.Background = _behindBrush;
                _pill.BorderBrush = null;
            }

            Shape();
            UpdateReading();

            // After the reading has been told to stop. A read already on its way may now find
            // the handle in it, and is drawn into a brush the pill no longer wears.
            if (wasInverting)
            {
                ScreenCapture.ExcludeFromCapture(_hwnd, exclude: false);
            }
        }

        if (invert || _color == fallback)
        {
            return;
        }

        _color = fallback;

        var luminance =
            ((0.2126 * fallback.R) + (0.7152 * fallback.G) + (0.0722 * fallback.B)) / 255;
        var edge = luminance > 0.5
            ? Color.FromArgb(0x59, 0x00, 0x00, 0x00)
            : Color.FromArgb(0x59, 0xFF, 0xFF, 0xFF);

        _pill.Background = Frozen(Color.FromArgb(0xE6, fallback.R, fallback.G, fallback.B));
        _pill.BorderBrush = Frozen(edge);
    }

    /// <summary>Brings the handle up, fading in, at the top of the topmost band.</summary>
    /// <remarks>
    /// Topmost whatever the dock's own setting. It is a mark for a dock that is out of sight,
    /// and one that the window in front could cover would be gone exactly when the dock is —
    /// which is the one time it is for. Being put there once is not being kept there, though;
    /// see <see cref="KeepOnTop"/>.
    /// </remarks>
    public void Show()
    {
        if (_disposed || _wanted)
        {
            return;
        }

        _wanted = true;

        if (!_visible)
        {
            _visible = true;

            // SW_SHOWNA rather than SW_SHOW: this window must never take the foreground.
            NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_SHOWNA);
        }

        NativeMethods.SetWindowPos(
            _hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);

        UpdateReading();
        Fade(1);
    }

    /// <summary>
    /// Puts a handle that is up back at the top of the topmost band, if it has lost its place
    /// there or one of <paramref name="dock"/>'s windows has come above it — and, when
    /// <paramref name="overOthers"/>, if a window of another program has come to lie over it.
    /// </summary>
    /// <param name="overOthers">True on a change of foreground; see the remarks.</param>
    /// <param name="dock">The dock's own windows — the dock and its acrylic sheet — or 0.</param>
    /// <remarks>
    /// <para>
    /// Over the dock's own windows always. A dock that floats enters the topmost band after the
    /// handle and lands above it, and the dock's window reaches down over the handle's strip
    /// because the dock draws the bar's shadow there itself (the acrylic sheet has none of its
    /// own on the composition path): while the settings dialog showed the handle under the
    /// dock, turning <em>Always on top</em> on laid that shadow across it and dimmed it, and
    /// turning it off put it back. Reported on 2026-09-30, and read off the z-order the same
    /// day: dock, then sheet, then the dialog, then the handle, all topmost.
    /// </para>
    /// <para>
    /// <see cref="Show"/> put it there once, and nothing kept it there: a window that floats
    /// and came to the front afterwards was drawn over it for as long as the handle stayed up,
    /// which with auto-hide can be all day. Reported on 2026-09-30 as the handle not being on
    /// top of other windows.
    /// </para>
    /// <para>
    /// Checked before acting, as every window here is, since re-asserting a z-order that is
    /// already right still re-composes the window. And over other windows only when asked —
    /// on a change of foreground. The shell's own passing popups float too, and sit just above
    /// the taskbar where the handle is — a button's thumbnails, a tooltip — and a handle that
    /// climbed back over whatever lay on it a few times a second would climb over those.
    /// </para>
    /// </remarks>
    public void KeepOnTop(bool overOthers, ReadOnlySpan<nint> dock)
    {
        if (_disposed || !_wanted)
        {
            return;
        }

        if (WindowChrome.IsTopmostWindow(_hwnd)
            && !WindowChrome.IsUnderAny(_hwnd, dock)
            && !(overOthers && WindowChrome.IsUnderAnother(_hwnd)))
        {
            return;
        }

        NativeMethods.SetWindowPos(
            _hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
    }

    /// <summary>Fades the handle out, and hides its window once it has gone.</summary>
    public void Hide()
    {
        if (_disposed || !_wanted)
        {
            return;
        }

        _wanted = false;
        Fade(0);
    }

    private void Fade(double to)
    {
        var generation = ++_fade;
        var animation = new DoubleAnimation(to, to > 0 ? FadeInDuration : FadeOutDuration);

        animation.Completed += (_, _) =>
        {
            // Only the latest fade may put the window away. A handle hidden while it was still
            // fading in would otherwise be put away by the fade in finishing, and one shown
            // again while fading out would vanish when the fade out did.
            if (generation != _fade || _wanted || !_visible || _disposed)
            {
                return;
            }

            _visible = false;
            NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_HIDE);
            UpdateReading();
        };

        // From wherever the last fade had got to, so reversing one mid-way does not jump.
        _pill.BeginAnimation(UIElement.OpacityProperty, animation);
    }

    /// <summary>
    /// Rounds the pill from its own height, and keeps its outline one device pixel wide — or
    /// takes the outline away, when the pill is what is behind it inverted.
    /// </summary>
    /// <remarks>
    /// Both in whatever units WPF lays this window out in, which is its idea of the scale — and
    /// that idea is stale by design, since the change-of-scale message is swallowed. Taken from
    /// the element rather than assumed, the pill is a pill and the hairline a hairline on either
    /// display whichever scale WPF thinks it is at.
    /// </remarks>
    private void Shape()
    {
        var dpi = VisualTreeHelper.GetDpi(_pill);
        _pill.CornerRadius = new CornerRadius(_pill.ActualHeight / 2);
        _pill.BorderThickness = new Thickness(_inverting == true ? 0 : 1 / dpi.DpiScaleY);
    }

    // ---- reading what is behind ----------------------------------------------

    /// <summary>
    /// Starts reading what is behind while the handle is inverting and on screen, and stops
    /// when it is neither.
    /// </summary>
    /// <remarks>
    /// A run per showing, so each one starts from nothing: the first read is always drawn,
    /// rather than skipped for matching the last read of the time before, which is no longer
    /// what the handle is showing if it has been put away since.
    /// </remarks>
    private void UpdateReading()
    {
        var wanted = _inverting == true && _visible && !_disposed;

        if (wanted && _reading is null)
        {
            _reading = new CancellationTokenSource();
            var stop = _reading.Token;

            new Thread(() => ReadLoop(stop))
            {
                IsBackground = true,
                Name = "ArtDock handle",
                Priority = ThreadPriority.BelowNormal
            }.Start();
        }
        else if (!wanted && _reading is not null)
        {
            // Not waited for. The thread is at most one read from noticing, and anything it
            // hands over meanwhile is drawn into a window that is hidden.
            _reading.Cancel();
            _reading = null;
        }
    }

    /// <summary>
    /// Reads what is behind the handle, and hands it over inverted whenever it has changed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Unchanged is the usual answer — a status bar, a scroll bar — and costs the dock's thread
    /// nothing: only a read that differs from the one before is inverted and drawn. How soon the
    /// next read comes is <see cref="HandlePace"/>'s to say: fifteen a second while what is
    /// behind keeps still, every frame while it moves.
    /// </para>
    /// <para>
    /// A read that fails — the secure desktop is up, the screen is locked — is waited on at the
    /// quiet pace, never retried at once. Anything thrown ends the reading and leaves the last
    /// read on the handle. A failure here must never take the dock down with it, and a background
    /// thread's exception would.
    /// </para>
    /// </remarks>
    private void ReadLoop(CancellationToken stop)
    {
        byte[] read = [];
        byte[] last = [];
        var lastSize = (Width: 0, Height: 0);
        var waits = new[] { stop.WaitHandle, _wake };
        var pace = new HandlePace();
        var clock = Stopwatch.StartNew();

        try
        {
            while (!stop.IsCancellationRequested)
            {
                (int X, int Y, int Width, int Height) region;
                lock (_gate)
                {
                    region = _region;
                }

                var wait = HandlePace.StillInterval;
                var length = region.Width * region.Height * 4;
                if (length > 0)
                {
                    if (read.Length != length)
                    {
                        read = new byte[length];
                    }

                    var started = clock.Elapsed;
                    if (ScreenCapture.TryCopy(region.X, region.Y, region.Width, region.Height, read))
                    {
                        var finished = clock.Elapsed;
                        var changed = lastSize != (region.Width, region.Height) || !read.AsSpan().SequenceEqual(last);

                        if (changed)
                        {
                            if (last.Length != length)
                            {
                                last = new byte[length];
                            }

                            read.CopyTo(last);
                            lastSize = (region.Width, region.Height);
                            HandOver(read, region.Width, region.Height);
                        }

                        wait = pace.Next(finished, changed, finished - started);
                    }
                }

                if (wait > TimeSpan.Zero)
                {
                    WaitHandle.WaitAny(waits, wait);
                }
            }
        }
        catch (Exception)
        {
            // See the remarks: the handle keeps what it last showed, and the dock carries on.
        }
    }

    /// <summary>Inverts a read and asks the dock's thread to draw it, once however many arrive first.</summary>
    private void HandOver(byte[] read, int width, int height)
    {
        lock (_gate)
        {
            if (_inverted.Length != read.Length)
            {
                _inverted = new byte[read.Length];
            }

            DockHandle.Invert(read, _inverted);
            (_invertedWidth, _invertedHeight) = (width, height);
            _fresh = true;

            if (_drawPosted)
            {
                return;
            }

            _drawPosted = true;
        }

        _source.Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(DrawBehind));
    }

    /// <summary>Draws the latest inverted read into the pill.</summary>
    private void DrawBehind()
    {
        lock (_gate)
        {
            _drawPosted = false;

            if (!_fresh || _disposed)
            {
                return;
            }

            _fresh = false;
            var (width, height) = (_invertedWidth, _invertedHeight);

            if (_behind is null || _behind.PixelWidth != width || _behind.PixelHeight != height)
            {
                _behind = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
                _behindBrush.ImageSource = _behind;
            }

            _behind.WritePixels(new Int32Rect(0, 0, width, height), _inverted, width * 4, 0);
        }
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private nint OnWindowMessage(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        // Swallowed, as the backdrop's is: this window is born off screen, possibly on a display
        // of another scale from the one it is sent to, and WPF's handling of the move would
        // resize it by the ratio between the two. Its size is the dock's to set.
        if (msg == NativeMethods.WM_DPICHANGED)
        {
            handled = true;
        }

        return 0;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        UpdateReading();
        _pill.BeginAnimation(UIElement.OpacityProperty, null);
        _source.Dispose();
    }
}
