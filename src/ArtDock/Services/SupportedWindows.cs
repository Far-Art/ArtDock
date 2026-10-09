using System.Windows;
using ArtDock.Localization;

namespace ArtDock.Services;

/// <summary>
/// Windows 11 or later, which the dock is made for — and saying so, plainly, on anything older.
/// </summary>
/// <remarks>
/// <para>
/// Two places turn older Windows away. The setup does, before it installs anything, for
/// Windows 7, 8 and 8.1: <c>tools/release.ps1</c> packs the release for Windows 11, and
/// Velopack builds its setup to run on Windows 7 so that it can say so in Windows' own error
/// box. But Velopack compares versions without their build numbers, and Windows 11 is Windows
/// 10.0 by every number but its build — so on Windows 10 the setup installs, and it is the dock
/// that says so, here, each time it is started.
/// </para>
/// <para>
/// On Windows 7 and 8 the dock could not say anything at all: .NET 10 does not start there.
/// </para>
/// </remarks>
public static class SupportedWindows
{
    /// <summary>The first build of Windows 11, which reports itself as Windows 10.0 at this build or later.</summary>
    public const int FirstBuild = 22000;

    /// <summary>True on Windows 11 or later.</summary>
    /// <remarks>
    /// The version as Windows has it: since .NET 5, <see cref="Environment.OSVersion"/> is not
    /// told an older version by the compatibility shims, manifest or no manifest.
    /// </remarks>
    public static bool IsCurrent => Supports(Environment.OSVersion.Version);

    /// <summary>True when <paramref name="version"/> is Windows 11's or later.</summary>
    public static bool Supports(Version version) => version >= new Version(10, 0, FirstBuild);

    /// <summary>Says, in a plain message box, that the dock needs Windows 11.</summary>
    /// <remarks>
    /// Windows' own box rather than a window of the dock's: nothing else of the dock has started,
    /// and nothing else should, on a version of Windows it was not made for.
    /// </remarks>
    public static void SayUnsupported() =>
        MessageBox.Show(
            Localizer.Get("Requirements.Windows11"),
            "ArtDock",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
}
