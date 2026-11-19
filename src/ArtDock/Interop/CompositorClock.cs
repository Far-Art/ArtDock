using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace ArtDock.Interop;

/// <summary>
/// Calls back on the UI thread once for each frame Windows composes, with the time it was
/// told of the frame — a frame clock for moving a window, which WPF's own is not.
/// </summary>
/// <remarks>
/// <para>
/// Found 2026-11-20, measuring auto-hide's slide: driven from <c>CompositionTarget.Rendering</c>,
/// every move of the window had WPF draw again at once and raise <c>Rendering</c> again a
/// millisecond later, so each real frame became a burst of one-pixel moves a millisecond apart
/// and then a wait of 15 to 30 ms for the next — about 35 places on the screen a second, on a
/// display showing 120. Nothing moved from here is moved by a frame WPF draws, so nothing feeds
/// back.
/// </para>
/// <para>
/// A thread of its own waits on DWM's compositor clock (<c>DCompositionWaitForCompositorClock</c>,
/// Windows 11), asked to run at the display's full rate while it is in use, and posts each tick at
/// <see cref="DispatcherPriority.Send"/>; a tick the UI thread has not taken yet is not posted
/// twice. Where the clock is not to be had, <c>DwmFlush</c> waits for the next composition instead.
/// Whatever is driven by it should go by the time it is given, not count ticks: one can be late,
/// or skipped.
/// </para>
/// </remarks>
internal sealed class CompositorClock : IDisposable
{
    private const uint WaitFailed = 0xFFFFFFFF;

    private static bool _noCompositorClock;

    private readonly Dispatcher _dispatcher;
    private readonly Action<long> _tick;
    private readonly ManualResetEvent _stop = new(false);
    private volatile bool _stopped;
    private int _pending;

    /// <summary>Starts ticking: <paramref name="tick"/> is given each frame's <see cref="Stopwatch"/> timestamp.</summary>
    public CompositorClock(Dispatcher dispatcher, Action<long> tick)
    {
        _dispatcher = dispatcher;
        _tick = tick;
        new Thread(Run)
        {
            IsBackground = true,
            Name = "ArtDock compositor clock",
            Priority = ThreadPriority.AboveNormal
        }.Start();
    }

    public void Dispose()
    {
        _stopped = true;
        _stop.Set();
    }

    private void Run()
    {
        var boosted = Boost(true);
        try
        {
            // The UI thread gone — a test's, say — leaves nobody to tick for.
            while (!_stopped && _dispatcher.Thread.IsAlive)
            {
                if (!WaitForFrame())
                {
                    break;
                }

                var now = Stopwatch.GetTimestamp();
                if (Interlocked.Exchange(ref _pending, 1) == 0)
                {
                    _dispatcher.BeginInvoke(DispatcherPriority.Send, () =>
                    {
                        Volatile.Write(ref _pending, 0);
                        if (!_stopped)
                        {
                            _tick(now);
                        }
                    });
                }
            }
        }
        finally
        {
            if (boosted)
            {
                Boost(false);
            }

            _stop.Dispose();
        }
    }

    /// <summary>Waits for the next frame; false once stopped.</summary>
    private bool WaitForFrame()
    {
        if (!_noCompositorClock)
        {
            try
            {
                nint[] handles = [_stop.SafeWaitHandle.DangerousGetHandle()];
                var result = DCompositionWaitForCompositorClock(1, handles, 100);
                if (result == 0)
                {
                    return false;
                }

                // A tick, or a timeout — the clock idle with nothing on the screen changing,
                // which a step taken by the time it is given puts right.
                if (result != WaitFailed)
                {
                    return !_stopped;
                }
            }
            catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
            {
                _noCompositorClock = true;
            }
        }

        if (DwmFlush() < 0)
        {
            Thread.Sleep(8);
        }

        return !_stopped;
    }

    private static bool Boost(bool enable)
    {
        try
        {
            return DCompositionBoostCompositorClock(enable) >= 0;
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
            return false;
        }
    }

    [DllImport("dcomp.dll")]
    private static extern uint DCompositionWaitForCompositorClock(uint count, nint[] handles, uint timeoutInMs);

    [DllImport("dcomp.dll")]
    private static extern int DCompositionBoostCompositorClock([MarshalAs(UnmanagedType.Bool)] bool enable);

    [DllImport("dwmapi.dll")]
    private static extern int DwmFlush();
}
