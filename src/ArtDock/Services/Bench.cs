using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace ArtDock.Services;

/// <summary>
/// A measuring mode, for finding out what the dock's features cost: switched on from the
/// environment, never from the settings, and never written to them.
/// </summary>
/// <remarks>
/// <para>
/// With <c>ARTDOCK_BENCH=1</c> the dock starts as usual, stays revealed, sweeps its own wave
/// (no pointer needed) unless <c>ARTDOCK_BENCH_SWEEP=0</c>, and quits after
/// <c>ARTDOCK_BENCH_WARMUP</c> + <c>ARTDOCK_BENCH_SECONDS</c>, appending one line to
/// <c>ARTDOCK_BENCH_LOG</c>: its own CPU time over the measured stretch, and the frames run
/// and painted.
/// </para>
/// <para>
/// One override at a time, each a candidate for a step of a performance setting:
/// <c>ARTDOCK_BENCH_FPS</c> caps the frame loop, <c>ARTDOCK_BENCH_BLUR=0</c> makes the bar
/// solid with no sheet behind it, <c>ARTDOCK_BENCH_SHADOWS=0</c> drops the icons' shadows.
/// The settings file is not saved while this is on.
/// </para>
/// </remarks>
internal static class Bench
{
    public static bool On { get; } = Read("ARTDOCK_BENCH") == "1";

    public static bool Sweep { get; } = On && Read("ARTDOCK_BENCH_SWEEP") != "0";

    /// <summary>The frame loop's cap in frames a second, or 0 for the display's own rate.</summary>
    public static int Fps { get; } = On ? ReadInt("ARTDOCK_BENCH_FPS", 0) : 0;

    public static bool SolidBar { get; } = On && Read("ARTDOCK_BENCH_BLUR") == "0";

    public static bool NoIconShadows { get; } = On && Read("ARTDOCK_BENCH_SHADOWS") == "0";

    /// <summary>
    /// Drawn without the graphics card (WPF's software renderer), as on a machine with none, but
    /// keeping the blur and shadows that No GPU would also drop, so each can still be measured.
    /// </summary>
    public static bool Software { get; } = On && Read("ARTDOCK_BENCH_SOFTWARE") == "1";

    public static int WarmupSeconds { get; } = ReadInt("ARTDOCK_BENCH_WARMUP", 5);

    public static int Seconds { get; } = ReadInt("ARTDOCK_BENCH_SECONDS", 20);

    public static string? Log { get; } = Read("ARTDOCK_BENCH_LOG");

    public static string Label { get; } = Read("ARTDOCK_BENCH_LABEL") ?? "";

    /// <summary>Frames the dock's loop ran its work on, rather than skipped under the cap.</summary>
    public static long FramesRun;

    /// <summary>Frames the dock repainted on.</summary>
    public static long FramesPainted;

    /// <summary>Time between the frames worked on, in ms, over the measured stretch only.</summary>
    private static readonly List<double> FrameGaps = new(8192);

    private static bool _measuring;

    /// <summary>Notes the gap before a frame worked on, while measuring.</summary>
    public static void NoteFrame(double deltaMs)
    {
        if (_measuring && deltaMs > 0)
        {
            FrameGaps.Add(deltaMs);
        }
    }

    private static TimeSpan _cpuAtStart;
    private static long _runAtStart;
    private static long _paintedAtStart;
    private static long _startedAt;

    public static void Begin()
    {
        _cpuAtStart = Process.GetCurrentProcess().TotalProcessorTime;
        _runAtStart = FramesRun;
        _paintedAtStart = FramesPainted;
        _startedAt = Stopwatch.GetTimestamp();
        FrameGaps.Clear();
        _measuring = true;
    }

    private static double Percentile(List<double> sorted, double p) =>
        sorted.Count == 0 ? 0 : sorted[Math.Min(sorted.Count - 1, (int)(p * sorted.Count))];

    public static void End(bool dockShown)
    {
        _measuring = false;
        var gaps = FrameGaps.OrderBy(gap => gap).ToList();

        // Hitches: a gap more than half again the usual one, which is a frame the eye sees missed.
        var usual = Percentile(gaps, 0.5);
        var hitches = gaps.Count(gap => gap > usual * 1.5);
        var seconds = Stopwatch.GetElapsedTime(_startedAt).TotalSeconds;
        var cpu = (Process.GetCurrentProcess().TotalProcessorTime - _cpuAtStart).TotalSeconds;
        var line = string.Create(
            CultureInfo.InvariantCulture,
            $"{{\"label\":\"{Label}\",\"pid\":{Environment.ProcessId},\"seconds\":{seconds:F2},"
            + $"\"cpuPercentOfCore\":{cpu / seconds * 100:F2},"
            + $"\"framesRunPerSec\":{(FramesRun - _runAtStart) / seconds:F1},"
            + $"\"framesPaintedPerSec\":{(FramesPainted - _paintedAtStart) / seconds:F1},"
            + $"\"gapP50Ms\":{usual:F2},\"gapP95Ms\":{Percentile(gaps, 0.95):F2},\"gapP99Ms\":{Percentile(gaps, 0.99):F2},"
            + $"\"gapMaxMs\":{(gaps.Count == 0 ? 0 : gaps[^1]):F1},\"hitchesPerSec\":{hitches / seconds:F2},"
            + $"\"software\":{(SoftwareRendering.IsOn ? "true" : "false")},"
            + $"\"affinity\":{(long)Process.GetCurrentProcess().ProcessorAffinity},"
            + $"\"workingSetMB\":{Environment.WorkingSet / 1048576.0:F1},\"dockShown\":{(dockShown ? "true" : "false")}}}");

        if (Log is { } path)
        {
            File.AppendAllText(path, line + Environment.NewLine);
        }
    }

    private static string? Read(string name) => Environment.GetEnvironmentVariable(name);

    private static int ReadInt(string name, int fallback) =>
        int.TryParse(Read(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;
}
