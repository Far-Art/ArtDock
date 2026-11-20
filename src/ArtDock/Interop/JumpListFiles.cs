using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace ArtDock.Interop;

/// <summary>
/// Where Windows keeps an app's jump list: two files under <c>Recent</c>, named by a hash of the
/// app's id.
/// </summary>
/// <remarks>
/// <c>CustomDestinations</c> holds what the app wrote itself — its tasks and its own categories,
/// with <c>ICustomDestinationList</c>; <c>AutomaticDestinations</c> holds the shell's record of
/// what it opened, the pinned entries among them.
/// </remarks>
public static class JumpListFiles
{
    /// <summary>
    /// The name the shell gives an app's jump-list files: a CRC-64 of its AppUserModelID in
    /// capitals, as UTF-16, in sixteen hex digits.
    /// </summary>
    /// <remarks>
    /// ECMA-182's polynomial, reflected, started from all ones and not inverted at the end —
    /// checked against File Explorer's, <c>Microsoft.Windows.Explorer</c>, whose file is
    /// <c>f01b4d95cf55d32a</c> on every machine.
    /// </remarks>
    public static string Id(string appUserModelId)
    {
        const ulong Polynomial = 0x92C64265D32139A4;
        var crc = ulong.MaxValue;

        foreach (var b in Encoding.Unicode.GetBytes(appUserModelId.ToUpperInvariant()))
        {
            crc ^= b;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ Polynomial : crc >> 1;
            }
        }

        return crc.ToString("x16");
    }

    /// <summary>Where a known folder is, or null where there is none.</summary>
    public static string? KnownFolderPath(Guid id) =>
        SHGetKnownFolderPath(id, KfFlagDontVerify, 0, out var path) == 0 ? path : null;

    private const uint KfFlagDontVerify = 0x4000;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetKnownFolderPath(
        in Guid id,
        uint flags,
        nint token,
        [MarshalAs(UnmanagedType.LPWStr)] out string path);

    /// <summary>The user's <c>Recent</c> folder, where both kinds of file are kept.</summary>
    public static string RecentFolder =>
        Environment.GetFolderPath(Environment.SpecialFolder.Recent, Environment.SpecialFolderOption.DoNotVerify);

    /// <summary>The file of the app's own entries: its tasks and categories.</summary>
    public static string CustomPath(string appUserModelId) =>
        Path.Combine(RecentFolder, "CustomDestinations", Id(appUserModelId) + ".customDestinations-ms");

    /// <summary>The shell's record of what the app opened, its pinned entries among them.</summary>
    public static string AutomaticPath(string appUserModelId) =>
        Path.Combine(RecentFolder, "AutomaticDestinations", Id(appUserModelId) + ".automaticDestinations-ms");
}
