using System.Runtime.InteropServices;

namespace ArtDock.Interop;

/// <summary>
/// Tells which of WPF's <c>CompositionTarget.Rendering</c> calls are a new frame on the screen,
/// for anything that waits a number of frames for WPF's drawing to get there.
/// </summary>
/// <remarks>
/// <para>
/// <c>Rendering</c> is not raised once a frame. Whatever has WPF draw again — a window moved,
/// shown or resized — raises it again about a millisecond later, and so on while that goes on:
/// measured 2026-11-20 on auto-hide's slide, five to ten calls inside one frame of the display
/// (see <see cref="CompositorClock"/>). A wait that counted calls could be over in one frame,
/// or less — a preview panel was caught going opaque 8 ms after it was shown, after a wait of two.
/// </para>
/// <para>
/// So a call counts only once the display has refreshed since the last one counted, by DWM's
/// own record of its last vblank. Where that cannot be read, every call counts, as before.
/// </para>
/// </remarks>
internal sealed class DisplayFrames
{
    private ulong _lastVBlank;
    private bool _counted;

    /// <summary>
    /// True when this call of <c>Rendering</c> is in a frame of the display that no earlier
    /// call counted — the first call always is.
    /// </summary>
    public bool IsNewFrame()
    {
        var info = new DWM_TIMING_INFO { cbSize = (uint)Marshal.SizeOf<DWM_TIMING_INFO>() };
        if (DwmGetCompositionTimingInfo(0, ref info) < 0)
        {
            return true;
        }

        if (_counted && info.qpcVBlank == _lastVBlank)
        {
            return false;
        }

        _counted = true;
        _lastVBlank = info.qpcVBlank;
        return true;
    }

    /// <summary>
    /// How long one of the display's frames lasts, in ms, by DWM's own clock; a 60 Hz frame if
    /// that cannot be read.
    /// </summary>
    public static double RefreshPeriodMs()
    {
        var info = new DWM_TIMING_INFO { cbSize = (uint)Marshal.SizeOf<DWM_TIMING_INFO>() };
        if (DwmGetCompositionTimingInfo(0, ref info) < 0 || info.qpcRefreshPeriod == 0)
        {
            return 1000.0 / 60;
        }

        var ms = info.qpcRefreshPeriod * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        return ms is > 1 and < 100 ? ms : 1000.0 / 60;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetCompositionTimingInfo(nint hwnd, ref DWM_TIMING_INFO info);

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct UNSIGNED_RATIO
    {
        public uint uiNumerator;
        public uint uiDenominator;
    }

    /// <summary>All of it, as dwmapi.h has it: the call refuses a shorter <c>cbSize</c>.</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct DWM_TIMING_INFO
    {
        public uint cbSize;
        public UNSIGNED_RATIO rateRefresh;
        public ulong qpcRefreshPeriod;
        public UNSIGNED_RATIO rateCompose;
        public ulong qpcVBlank;
        public ulong cRefresh;
        public uint cDXRefresh;
        public ulong qpcCompose;
        public ulong cFrame;
        public uint cDXPresent;
        public ulong cRefreshFrame;
        public ulong cFrameSubmitted;
        public uint cDXPresentSubmitted;
        public ulong cFrameConfirmed;
        public uint cDXPresentConfirmed;
        public ulong cRefreshConfirmed;
        public uint cDXRefreshConfirmed;
        public ulong cFramesLate;
        public uint cFramesOutstanding;
        public ulong cFrameDisplayed;
        public ulong qpcFrameDisplayed;
        public ulong cRefreshFrameDisplayed;
        public ulong cFrameComplete;
        public ulong qpcFrameComplete;
        public ulong cFramePending;
        public ulong qpcFramePending;
        public ulong cFramesDisplayed;
        public ulong cFramesComplete;
        public ulong cFramesPending;
        public ulong cFramesAvailable;
        public ulong cFramesDropped;
        public ulong cFramesMissed;
        public ulong cRefreshNextDisplayed;
        public ulong cRefreshNextPresented;
        public ulong cRefreshesDisplayed;
        public ulong cRefreshesPresented;
        public ulong cRefreshStarted;
        public ulong cPixelsReceived;
        public ulong cPixelsDrawn;
        public ulong cBuffersEmpty;
    }
}
