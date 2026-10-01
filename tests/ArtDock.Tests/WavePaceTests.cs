using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ArtDock.Controls;
using ArtDock.Dock;

namespace ArtDock.Tests;

/// <summary>
/// Covers how often the wave is drawn under <c>DockSettings.NoGpu</c>: the rule
/// (<see cref="WavePace"/>), against a simulated display, and a real dock held to it.
/// </summary>
public class WavePaceTests
{
    /// <summary>
    /// The cap takes the frame that makes thirty a second at the paces displays run at, against
    /// a simulated display whose frames arrive on time or a little either side of it.
    /// </summary>
    /// <remarks>
    /// The second frame at 60 Hz and the fourth at 120 both come 33.3 ms after the last one
    /// drawn. A cap of exactly that would lose each one that arrived a hair early and draw the
    /// next, for twenty frames a second where thirty were meant — which is why it is thirty
    /// milliseconds, and what the jitter here is for.
    /// </remarks>
    [Theory]
    [InlineData(60, 30)]
    [InlineData(120, 30)]
    [InlineData(144, 28.8)]
    [InlineData(75, 25)]
    [InlineData(30, 30)]
    [InlineData(24, 24)]
    public void TheCap_DrawsAboutThirtyFramesASecond_AtAnyDisplaysPace(double hertz, double expected)
    {
        var frame = 1000 / hertz;
        var jitter = new[] { 0.0, -0.4, 0.3, -0.2, 0.4, 0.1, -0.3 };

        var last = 0.0;
        var drawn = 0;
        var ticks = (int)(hertz * 10);

        for (var tick = 1; tick <= ticks; tick++)
        {
            var now = (tick * frame) + jitter[tick % jitter.Length];
            if (WavePace.Draws(now - last, WavePace.CappedFrameMs))
            {
                last = now;
                drawn++;
            }
        }

        Assert.Equal(expected, drawn / 10.0, tolerance: 0.5);
    }

    [Fact]
    public void WithoutACap_EveryFrameIsDrawn()
    {
        Assert.True(WavePace.Draws(0, 0));
        Assert.True(WavePace.Draws(4.1, 0));
    }

    /// <summary>
    /// A real dock, sweeping its wave as the settings dialog has it do: capped, no two frames
    /// that move it are closer than the cap, and it still moves.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Hosted in a window that is never shown, off every display so the pointer cannot be on
    /// it, and watched from the render loop it runs on: a frame has moved the wave when the
    /// icons' magnification is not what it was a frame before.
    /// </para>
    /// <para>
    /// The uncapped run is the control, and says whether the cap had anything to do: where WPF
    /// offers no frames closer together than the cap — a test host at thirty a second — the
    /// capped run proves nothing either way, and the test says so rather than pass on it.
    /// </para>
    /// </remarks>
    [Fact]
    public void ADockUnderTheCap_MovesItsWaveNoOftenerThanTheCap() => OnStaThread(() =>
    {
        var bar = new DockBar(new DockMetrics { BaseSize = 50, MaxSize = 70, Gap = 8, PaddingX = 14 });
        bar.SetItems([.. Enumerable.Range(0, 5).Select(i => new DockItem { Id = $"p{i}", Label = $"P{i}" })]);

        var size = bar.PreferredSize();
        using var host = new HwndSource(new HwndSourceParameters("ArtDock.Tests.Wave")
        {
            WindowStyle = unchecked((int)0x8000_0000),
            PositionX = -32000,
            PositionY = -32000,
            Width = (int)Math.Ceiling(size.Width),
            Height = (int)Math.Ceiling(size.Height)
        })
        {
            RootVisual = bar
        };

        bar.Measure(size);
        bar.Arrange(new Rect(size));
        bar.UpdateLayout();
        bar.PreviewSweep(true);

        var uncapped = Gaps(MovedAt(bar, capMs: 0));
        var capped = Gaps(MovedAt(bar, WavePace.CappedFrameMs));

        Assert.True(uncapped.Count > 5, $"the wave did not move at all: {uncapped.Count} frames in the uncapped run");
        Assert.True(
            uncapped.Min() < WavePace.CappedFrameMs,
            $"test host: no two frames came closer than the cap ({uncapped.Min():F1} ms), so the cap had nothing to pass over");

        Assert.True(capped.Count > 5, $"the capped wave did not move: {capped.Count} frames");
        Assert.True(
            capped.Min() >= WavePace.CappedFrameMs,
            $"a capped frame came {capped.Min():F2} ms after the one before");

        bar.PreviewSweep(false);
    });

    /// <summary>
    /// Runs the dock's render loop for a while, and returns the times, in milliseconds, of the
    /// frames that moved the wave.
    /// </summary>
    private static List<double> MovedAt(DockBar bar, double capMs)
    {
        bar.FrameIntervalMs = capMs;

        var scales = bar.Children.OfType<DockItemVisual>()
            .Select(icon => ((TransformGroup)icon.RenderTransform).Children.OfType<ScaleTransform>().Single())
            .ToList();

        // Weighted by place. The plain sum would not do: the wave is built so that the icons'
        // sizes add up to the same total wherever it stands — that is what keeps the bar from
        // breathing — so a wave travelling along a dock leaves their sum where it was.
        double Magnification() => scales.Select((scale, place) => (place + 1) * scale.ScaleX).Sum();

        var last = Magnification();
        var moved = new List<double>();

        void OnRendering(object? sender, EventArgs e)
        {
            var now = ((RenderingEventArgs)e).RenderingTime.TotalMilliseconds;
            var magnification = Magnification();
            if (magnification == last)
            {
                return;
            }

            last = magnification;

            // WPF can raise the event twice for one frame; the wave moves in the first.
            if (moved.Count == 0 || now > moved[^1])
            {
                moved.Add(now);
            }
        }

        CompositionTarget.Rendering += OnRendering;
        Pump(TimeSpan.FromMilliseconds(700));
        CompositionTarget.Rendering -= OnRendering;

        return moved;
    }

    private static List<double> Gaps(List<double> times) =>
        [.. times.Zip(times.Skip(1), (earlier, later) => later - earlier)];

    private static void Pump(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = duration };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            frame.Continue = false;
        };

        timer.Start();
        Dispatcher.PushFrame(frame);
    }

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
}
