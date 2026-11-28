using System.Globalization;
using System.IO;
using System.Text;

namespace ArtDock.Services;

/// <summary>
/// A record of the wave, frame by frame, for looking at how it moves: switched on from the
/// environment (<c>ARTDOCK_WAVE_TRACE</c>, a file to append to), never from the settings.
/// </summary>
/// <remarks>
/// One line a frame worked on: the frame's time and the gap before it in ms, the wave's
/// amplitude and where it is heading, the bar's left edge and width in DIPs, and the time the
/// frame's own work and the dock's drawing since the frame before took, in ms. Written out
/// about once a second, so a dock killed while recording loses little.
/// </remarks>
internal static class WaveTrace
{
    public static string? Path { get; } = Environment.GetEnvironmentVariable("ARTDOCK_WAVE_TRACE");

    public static bool On => Path is not null;

    private static readonly StringBuilder Pending = new();

    private static double _flushedAt;

    public static void Note(
        double timeMs, double deltaMs, double amplitude, double target, double barLeft, double barWidth, double workMs, double renderMs)
    {
        if (Path is null)
        {
            return;
        }

        Pending.Append(string.Create(
            CultureInfo.InvariantCulture,
            $"{timeMs:F2}\t{deltaMs:F2}\t{amplitude:F5}\t{target:F0}\t{barLeft:F2}\t{barWidth:F2}\t{workMs:F3}\t{renderMs:F3}\n"));

        if (timeMs - _flushedAt > 1000)
        {
            _flushedAt = timeMs;
            Flush();
        }
    }

    public static void Flush()
    {
        if (Path is null || Pending.Length == 0)
        {
            return;
        }

        try
        {
            File.AppendAllText(Path, Pending.ToString());
        }
        catch (IOException)
        {
        }

        Pending.Clear();
    }
}
