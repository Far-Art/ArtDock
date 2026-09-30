using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using ArtDock.Interop;
using ArtDock.Views;

namespace ArtDock.Tests;

/// <summary>
/// Covers what makes a window click-through — which the dock's acrylic sheet got wrong for as
/// long as it existed, swallowing clicks meant for the windows behind it.
/// </summary>
/// <remarks>
/// <para>
/// The windows these make are real, but put off every display — twenty thousand pixels up and
/// to the left — over a backstop window of the test's own, so what <c>WindowFromPoint</c> finds
/// there is either the window under test or the backstop, whatever is on the screen. On it, a
/// fullscreen game in front answered for every point, above even a topmost window; and a window
/// that appears over a game's picture, however briefly, can cost it its direct path to the
/// display. Off it, nothing is on anyone's screen, and nothing takes the foreground.
/// </para>
/// <para>
/// Only the layered-and-transparent case is asked of Windows, because it is the only one with a
/// steady answer. A window that is only <c>WS_EX_TRANSPARENT</c> went both ways: the dock's old
/// sheet took the hit, asked from another process and from its own thread, and off screen the
/// same test found it in one run and passed it by in the next — sixty times running, once, with
/// what decided it never found. Layered as well, it was passed by in every one of dozens of
/// trials, from every thread, in every order.
/// </para>
/// </remarks>
public class ClickThroughTests
{
    private const uint Transparent = 0x0000_0020;
    private const uint Layered = 0x0008_0000;
    private const uint NoRedirection = 0x0020_0000;
    private const uint ToolWindow = 0x0000_0080;
    private const uint NoActivate = 0x0800_0000;
    private const uint Popup = 0x8000_0000;

    private const int OffScreen = -20000;
    private const int Size = 40;

    private static void OnStaThread(Action body)
    {
        ExceptionDispatchInfo? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                body();
            }
            catch (Exception ex)
            {
                failure = ExceptionDispatchInfo.Capture(ex);
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        failure?.Throw();
    }

    [Theory]
    [InlineData(0u, false)]
    [InlineData(Transparent, false)]
    [InlineData(Layered, false)]
    [InlineData(Layered | Transparent, true)]
    [InlineData(Layered | Transparent | NoRedirection | ToolWindow | NoActivate, true)]
    public void OnlyLayeredAndTransparentTogether_AreClickThrough(uint exStyle, bool clickThrough) =>
        Assert.Equal(clickThrough, WindowChrome.IsClickThrough(exStyle));

    /// <summary>
    /// What the rule calls click-through, Windows passes by: <c>WindowFromPoint</c>, the hit-test
    /// a click takes, reaches the window behind — made before it or after.
    /// </summary>
    [Theory]
    [InlineData(Transparent | Layered, true)]
    [InlineData(Transparent | Layered | NoRedirection, true)]
    [InlineData(Transparent | Layered | NoRedirection, false)]
    public void ALayeredTransparentWindow_IsPassedBy(uint exStyle, bool behindIsOlder) => OnStaThread(() =>
    {
        Assert.True(WindowChrome.IsClickThrough(exStyle));

        var older = behindIsOlder ? Backstop.Make() : null;
        var hwnd = MakeWindow(exStyle);
        var behind = older ?? Backstop.Make();
        try
        {
            Assert.False(behind.IsTakenBy(hwnd));
        }
        finally
        {
            behind.Dispose();
            DestroyWindow(hwnd);
        }
    });

    /// <summary>
    /// The sheet lies under the whole of the dock's window, most of which is transparent, so it
    /// must pass every click on to whatever is behind — however old that is. The dock paints what
    /// takes the bar's own clicks.
    /// </summary>
    [Fact]
    public void TheSheet_PassesClicksToTheWindowsBehindIt() => OnStaThread(() =>
    {
        using var behind = Backstop.Make();
        using var sheet = new BackdropWindow();
        var exStyle = (uint)GetWindowLongPtr(sheet.Hwnd, -20);

        if (!sheet.DrawsBar)
        {
            // The accent-policy fallback: DWM blurs behind no layered window, so it is not one —
            // and its sheet is kept inside the bar, where the dock takes every click itself.
            Assert.False(WindowChrome.IsClickThrough(exStyle));
            return;
        }

        Assert.True(WindowChrome.IsClickThrough(exStyle), $"the sheet's styles are 0x{exStyle:X}");
        Assert.False(behind.IsTakenBy(sheet.Hwnd), "a click on the sheet would land on the sheet");
    });

    /// <summary>A bare popup of the test's own class, drawing nothing.</summary>
    private static nint MakeWindow(uint exStyle)
    {
        var hwnd = MakeRaw(ToolWindow | NoActivate | exStyle);
        Assert.NotEqual(0, hwnd);

        if ((exStyle & Layered) != 0)
        {
            Assert.True(SetLayeredWindowAttributes(hwnd, 0, 0xFF, 0x2));
        }

        return hwnd;
    }

    /// <summary>
    /// A window off every display, on a thread of its own, for a window under test to be put
    /// over — made, and so older or younger than that window, at the moment it is made.
    /// </summary>
    private sealed class Backstop : IDisposable
    {
        private readonly Thread _thread;
        private readonly ManualResetEventSlim _finished = new();
        private nint _hwnd;

        private Backstop(Thread thread) => _thread = thread;

        public static Backstop Make()
        {
            using var made = new ManualResetEventSlim();
            Backstop? backstop = null;

            // Nothing on this thread may throw — an exception there would take the test host
            // down — so it only makes the window, and the test's own thread checks that it did.
            var thread = new Thread(() =>
            {
                backstop!._hwnd = MakeRaw(ToolWindow | NoActivate);
                SetWindowPos(backstop._hwnd, -1, OffScreen, OffScreen, Size, Size, 0x0010 | 0x0040);
                made.Set();
                backstop._finished.Wait();
                DestroyWindow(backstop._hwnd);
            });

            backstop = new Backstop(thread);
            thread.IsBackground = true;
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            made.Wait();

            if (backstop._hwnd == 0)
            {
                backstop.Dispose();
                Assert.Fail("the backstop window could not be made");
            }

            return backstop;
        }

        /// <summary>
        /// Shows the window over the backstop, both topmost, and asks — from a third thread —
        /// which of the two a click at its middle would reach.
        /// </summary>
        public bool IsTakenBy(nint hwnd)
        {
            Assert.True(SetWindowPos(hwnd, -1, OffScreen, OffScreen, Size, Size, 0x0010 | 0x0040));
            try
            {
                nint found = 0;
                var asker = new Thread(() =>
                    found = GetAncestor(WindowFromPoint(new NativePoint { X = OffScreen + (Size / 2), Y = OffScreen + (Size / 2) }), 2));
                asker.Start();
                asker.Join();

                Assert.True(found == hwnd || found == _hwnd, "something else answered for a point off every display");
                return found == hwnd;
            }
            finally
            {
                ShowWindow(hwnd, 0);
            }
        }

        public void Dispose()
        {
            _finished.Set();
            _thread.Join();
            _finished.Dispose();
        }
    }

    private static readonly WindowProc Procedure = DefWindowProc;

    private static readonly Lazy<ushort> Registered = new(() =>
    {
        var windowClass = new WindowClass
        {
            Size = (uint)Marshal.SizeOf<WindowClass>(),
            Proc = Marshal.GetFunctionPointerForDelegate(Procedure),
            Instance = GetModuleHandle(null),
            ClassName = "ArtDock.Tests.ClickThrough"
        };
        return RegisterClassEx(ref windowClass);
    });

    /// <summary>A bare popup of the test's own class, drawing nothing; 0 if it could not be made.</summary>
    private static nint MakeRaw(uint exStyle)
    {
        _ = Registered.Value;
        return CreateWindowEx(exStyle, "ArtDock.Tests.ClickThrough", "", Popup, -32000, -32000, 1, 1, 0, 0, GetModuleHandle(null), 0);
    }

    private delegate nint WindowProc(nint hwnd, uint msg, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        public uint Size;
        public uint Style;
        public nint Proc;
        public int ClassExtra;
        public int WindowExtra;
        public nint Instance;
        public nint Icon;
        public nint Cursor;
        public nint Background;
        public string? MenuName;
        public string ClassName;
        public nint SmallIcon;
    }

    [DllImport("user32.dll", EntryPoint = "RegisterClassExW", CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassEx(ref WindowClass windowClass);

    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowEx(
        uint exStyle, string className, string name, uint style, int x, int y, int width, int height,
        nint parent, nint menu, nint instance, nint param);

    [DllImport("user32.dll", EntryPoint = "DefWindowProcW")]
    private static extern nint DefWindowProc(nint hwnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(nint hwnd);

    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? name);

    [DllImport("user32.dll")]
    private static extern bool SetLayeredWindowAttributes(nint hwnd, uint key, byte alpha, uint flags);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hwnd, int command);

    [DllImport("user32.dll")]
    private static extern nint WindowFromPoint(NativePoint point);

    [DllImport("user32.dll")]
    private static extern nint GetAncestor(nint hwnd, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint hwnd, int index);
}
