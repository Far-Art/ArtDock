using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using ArtDock.Views;

namespace ArtDock.Tests;

/// <summary>
/// Covers the acrylic sheet's z-order band (<see cref="BackdropWindow.SetTopmost"/>): that being
/// told the band it is already in leaves it where it is — directly under the dock.
/// </summary>
/// <remarks>
/// <para>
/// Every re-apply of the settings tells the sheet its band, every tick of every slider in the
/// settings dialog among them, and <c>HWND_TOPMOST</c> lifts a window already in the topmost band
/// to the top of it: over the dock, until the restack that follows put it back. The sheet draws
/// the bar, so the frames in between were the bar over the icons — the icons flickering while a
/// slider was dragged.
/// </para>
/// <para>
/// The windows are real and shown, as the sheet acts only while shown, but off every display —
/// the sheet where it is made, the stand-in for the dock twenty thousand pixels up and to the
/// left — so nothing appears on anyone's screen, and nothing takes the foreground.
/// </para>
/// </remarks>
public class SheetBandTests
{
    private const uint Transparent = 0x0000_0020;
    private const uint Layered = 0x0008_0000;
    private const uint ToolWindow = 0x0000_0080;
    private const uint NoActivate = 0x0800_0000;
    private const uint Popup = 0x8000_0000;
    private const uint Topmost = 0x0000_0008;

    private const nint HwndTopmost = -1;
    private const nint HwndNoTopmost = -2;
    private const uint NoSize = 0x0001;
    private const uint NoMove = 0x0002;
    private const uint NoActivateFlag = 0x0010;
    private const uint ShowWindowFlag = 0x0040;
    private const uint Above = 3;

    private const int OffScreen = -20000;

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
    [InlineData(true)]
    [InlineData(false)]
    public void TheBandItIsIn_LeavesTheSheetUnderTheDock(bool topmost) => OnStaThread(() =>
    {
        var dock = MakeDock(topmost);
        using var sheet = new BackdropWindow();
        try
        {
            ShowUnder(sheet, dock, topmost);

            for (var tick = 0; tick < 3; tick++)
            {
                sheet.SetTopmost(topmost);
                Assert.True(GetWindow(sheet.Hwnd, Above) == dock, $"tick {tick} lifted the sheet over the dock");
            }
        }
        finally
        {
            DestroyWindow(dock);
        }
    });

    /// <summary>
    /// A change of band is still made — and so is one undone behind the sheet's back, since the
    /// band is read off the window rather than remembered.
    /// </summary>
    [Fact]
    public void AnotherBand_IsStillTaken() => OnStaThread(() =>
    {
        var dock = MakeDock(topmost: true);
        using var sheet = new BackdropWindow();
        try
        {
            ShowUnder(sheet, dock, topmost: true);

            sheet.SetTopmost(false);
            Assert.False(IsTopmost(sheet.Hwnd));

            sheet.SetTopmost(true);
            Assert.True(IsTopmost(sheet.Hwnd));

            Assert.True(SetWindowPos(sheet.Hwnd, HwndNoTopmost, 0, 0, 0, 0, NoMove | NoSize | NoActivateFlag));
            Assert.False(IsTopmost(sheet.Hwnd));

            sheet.SetTopmost(true);
            Assert.True(IsTopmost(sheet.Hwnd), "a band changed behind the sheet's back was left");
        }
        finally
        {
            DestroyWindow(dock);
        }
    });

    /// <summary>Shows the sheet in the band given, directly under the dock, as the dock stacks it.</summary>
    private static void ShowUnder(BackdropWindow sheet, nint dock, bool topmost)
    {
        sheet.SetTopmost(topmost);
        sheet.Show();
        Assert.True(SetWindowPos(sheet.Hwnd, dock, 0, 0, 0, 0, NoMove | NoSize | NoActivateFlag));
        Assert.Equal(topmost, IsTopmost(sheet.Hwnd));
        Assert.True(GetWindow(sheet.Hwnd, Above) == dock, "the sheet could not be put under the dock");
    }

    /// <summary>A stand-in for the dock's window, drawing nothing, shown off every display in the band given.</summary>
    private static nint MakeDock(bool topmost)
    {
        _ = Registered.Value;
        var hwnd = CreateWindowEx(
            ToolWindow | NoActivate | Layered | Transparent, "ArtDock.Tests.SheetBand", "", Popup,
            -32000, -32000, 1, 1, 0, 0, GetModuleHandle(null), 0);
        Assert.NotEqual(0, hwnd);
        Assert.True(SetLayeredWindowAttributes(hwnd, 0, 0xFF, 0x2));
        Assert.True(SetWindowPos(
            hwnd, topmost ? HwndTopmost : HwndNoTopmost, OffScreen, OffScreen, 40, 40, NoActivateFlag | ShowWindowFlag));
        Assert.Equal(topmost, IsTopmost(hwnd));
        return hwnd;
    }

    private static bool IsTopmost(nint hwnd) => ((uint)GetWindowLongPtr(hwnd, -20) & Topmost) != 0;

    private static readonly WindowProc Procedure = DefWindowProc;

    private static readonly Lazy<ushort> Registered = new(() =>
    {
        var windowClass = new WindowClass
        {
            Size = (uint)Marshal.SizeOf<WindowClass>(),
            Proc = Marshal.GetFunctionPointerForDelegate(Procedure),
            Instance = GetModuleHandle(null),
            ClassName = "ArtDock.Tests.SheetBand"
        };
        return RegisterClassEx(ref windowClass);
    });

    private delegate nint WindowProc(nint hwnd, uint msg, nint wParam, nint lParam);

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
    private static extern nint GetWindow(nint hwnd, uint command);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint hwnd, int index);
}
