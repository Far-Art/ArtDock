using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using ArtDock.Controls;
using ArtDock.Dock;
using ArtDock.Views;

namespace ArtDock.Tests;

/// <summary>
/// Covers the acrylic sheet following the bar (<see cref="BackdropWindow.Follow"/>): where its
/// window goes, and the shape it draws the bar in.
/// </summary>
/// <remarks>
/// The dock here is hosted in a window that is never shown, and the sheet is never shown either,
/// so nothing appears on the screen — both have real handles and real rectangles all the same.
/// </remarks>
public class SheetFollowTests
{
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

    /// <summary>Runs the dispatcher — and with it the render loop — until something has happened, or two seconds.</summary>
    private static void Pump(Func<bool> done)
    {
        var frame = new DispatcherFrame();
        var until = DateTime.UtcNow.AddSeconds(2);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(5) };
        timer.Tick += (_, _) =>
        {
            if (done() || DateTime.UtcNow > until)
            {
                timer.Stop();
                frame.Continue = false;
            }
        };

        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    /// <summary>
    /// Sizes that put the bar at fractions of a pixel, which is what shows whether the sheet
    /// keeps them or rounds them away.
    /// </summary>
    private static DockMetrics Fractional() => new() { BaseSize = 50.3, MaxSize = 70.4, Gap = 8.2, PaddingX = 14.3 };

    private static DockItem[] Pins(int count) =>
        [.. Enumerable.Range(0, count).Select(i => new DockItem { Id = $"p{i}", Label = $"P{i}" })];

    /// <summary>A dock in a window of its own, never shown, laid out, and followed by nothing yet.</summary>
    private static (HwndSource Host, DockBar Bar) Hosted(DockMetrics metrics)
    {
        var bar = new DockBar(metrics);

        // Something follows the bar, as the dock's window does, so its shape is announced once
        // drawn rather than taken as drawn at once.
        bar.BarRectChanged += (_, _) => { };
        bar.SetItems(Pins(5));

        var size = bar.PreferredSize();
        var host = new HwndSource(new HwndSourceParameters("ArtDock.Tests.Dock")
        {
            WindowStyle = unchecked((int)0x8000_0000),
            PositionX = 100,
            PositionY = 100,
            Width = (int)Math.Ceiling(size.Width),
            Height = (int)Math.Ceiling(size.Height)
        })
        {
            RootVisual = bar
        };

        bar.Measure(size);
        bar.Arrange(new Rect(size));
        bar.UpdateLayout();
        return (host, bar);
    }

    private static Rect Scaled(Rect rect, double scale) =>
        new(rect.X * scale, rect.Y * scale, rect.Width * scale, rect.Height * scale);

    private static double ScaleOf(HwndSource host) => host.CompositionTarget.TransformToDevice.M11;

    /// <summary>
    /// The compositor needs a dispatcher queue on the thread that makes it, and one used to be
    /// made for the first thread only: a sheet made on any other went to the accent-policy
    /// fallback without a word.
    /// </summary>
    [Fact]
    public void ASheetMadeOnAnyThread_DrawsTheBarItself()
    {
        for (var i = 0; i < 3; i++)
        {
            OnStaThread(() =>
            {
                using var sheet = new BackdropWindow();
                Assert.True(sheet.DrawsBar, "the sheet fell back to the accent policy on a thread of its own");
            });
        }
    }

    [Fact]
    public void BeforeTheDockHasDrawnItsBar_TheSheetDrawsNothing() => OnStaThread(() =>
    {
        var (host, bar) = Hosted(Fractional());
        using (host)
        using (var sheet = new BackdropWindow())
        {
            Assert.True(bar.RenderedBarRect.IsEmpty);
            Assert.False(sheet.Follow(bar, host.Handle));
            Assert.True(sheet.DrawnBar.IsEmpty);
        }
    });

    /// <summary>
    /// The sheet's window is the dock window's twin, and the bar is drawn in it where WPF draws it
    /// — to the fraction of a pixel. It used to be measured through <c>PointToScreen</c>, which
    /// rounds to whole pixels: harmless under a bar drawn by the dock, a visible step now that the
    /// sheet draws the bar itself.
    /// </summary>
    [Fact]
    public void TheSheetIsTheDockWindowsTwin_AndDrawsTheBarToTheFraction() => OnStaThread(() =>
    {
        var (host, bar) = Hosted(Fractional());
        using (host)
        using (var sheet = new BackdropWindow())
        {
            Pump(() => !bar.RenderedBarRect.IsEmpty);
            Assert.True(sheet.Follow(bar, host.Handle));

            Assert.True(GetWindowRect(host.Handle, out var dock));
            Assert.True(GetWindowRect(sheet.Hwnd, out var twin));
            Assert.Equal(dock, twin);

            var expected = Scaled(bar.RenderedBarRect, ScaleOf(host));
            Assert.True(
                Math.Abs(expected.X - Math.Round(expected.X)) > 0.01,
                $"test data: the bar must start at a fraction of a pixel, not at {expected.X}");

            var drawn = sheet.DrawnBar;
            Assert.Equal(expected.X, drawn.X, 6);
            Assert.Equal(expected.Y, drawn.Y, 6);
            Assert.Equal(expected.Width, drawn.Width, 6);
            Assert.Equal(expected.Height, drawn.Height, 6);
        }
    });

    /// <summary>
    /// The sheet reaches the screen a frame or two sooner than the dock's own window, so it draws
    /// the bar the dock has drawn, not the one being computed — or the bar runs ahead of its icons.
    /// </summary>
    [Fact]
    public void TheSheetDrawsTheBarTheDockHasDrawn_NotTheOneItIsComputing() => OnStaThread(() =>
    {
        var metrics = Fractional();
        var (host, bar) = Hosted(metrics);
        using (host)
        using (var sheet = new BackdropWindow())
        {
            Pump(() => !bar.RenderedBarRect.IsEmpty);
            Assert.True(sheet.Follow(bar, host.Handle));
            var before = sheet.DrawnBar;
            var scale = ScaleOf(host);

            bar.UpdateMetrics(metrics with { PaddingX = metrics.PaddingX + 6 });
            Assert.NotEqual(bar.RenderedBarRect, bar.BarRect);

            Assert.True(sheet.Follow(bar, host.Handle));
            Assert.Equal(before, sheet.DrawnBar);

            Pump(() => bar.RenderedBarRect == bar.BarRect);
            Assert.True(sheet.Follow(bar, host.Handle));

            var after = Scaled(bar.BarRect, scale);
            Assert.Equal(after.X, sheet.DrawnBar.X, 6);
            Assert.Equal(after.Width, sheet.DrawnBar.Width, 6);
        }
    });

    [StructLayout(LayoutKind.Sequential)]
    private record struct NativeRect(int Left, int Top, int Right, int Bottom);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint hwnd, out NativeRect rect);
}
