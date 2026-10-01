using System.Runtime.InteropServices;

namespace ArtDock.Interop;

/// <summary>
/// Hears about the Recycle Bin's icon changing between full and empty, wherever the change
/// was made.
/// </summary>
/// <remarks>
/// <para>
/// The bin is the one pin whose icon changes without its target changing, and it changes
/// from anywhere — a file deleted in Explorer, one restored, the bin emptied from the dock
/// or from the desktop. None of that passes through the dock, so the shell has to tell it,
/// which is what <c>SHChangeNotifyRegister</c> is for and what Explorer's own desktop
/// listens with.
/// </para>
/// <para>
/// <b>Only <c>SHCNE_UPDATEIMAGE</c>, and deliberately.</b> Measured by sending a file to
/// the bin and restoring it — from the watching process, from another process, and from a
/// background thread, which is where the dock empties the bin. The process that changes
/// the bin through the shell is the one that flips its icon: it rewrites the
/// <c>DefaultIcon</c> the shell reads the icon from, then announces the old image as stale.
/// And it does it in that order, <em>after</em> the contents have changed: a restore's
/// <c>SHCNE_DELETE</c> arrived with the bin already empty and its icon still full, about
/// 40ms before the flip. Re-reading on the contents changing therefore reads the old icon.
/// The image notification is the one sent after the flip, every time, by whoever made it.
/// </para>
/// <para>
/// No flip, no notification — and a flip is not guaranteed. A bin emptied without one
/// stays drawn full everywhere, Explorer included, until something asks the shell to look
/// again; see <see cref="RecycleBin.EmptyAsync"/>, which does, for the empty the dock makes.
/// </para>
/// <para>
/// The notification is not about the bin in particular. It names whatever image went
/// stale, and anything on the machine may raise one, so it only counts as a change when
/// the bin has gone from empty to not or back (<see cref="RecycleBin.TryIsEmpty"/>), or the
/// icon location the shell gives for it has moved — <c>imageres.dll,-54</c> full,
/// <c>-55</c> empty. Extracting the icon again and repainting it, for a change that was
/// someone else's, is COM work and a frame; counting the bin is tens of milliseconds, and the
/// notification is rare.
/// </para>
/// <para>
/// <b>The count, and not the location alone, since 2026-10-01.</b> On build 26300 the flip
/// described above stopped being reliable: an empty made in Explorer left the location
/// saying full, and a delete made there set it to empty with the file in the bin. The
/// notification itself still came, every time, with the count already right — measured
/// with a listener beside the dock while the user deleted a file and emptied the bin. A
/// watch that compared only the location heard it and threw it away. The location is still
/// compared, so a bin icon changed in *Desktop icon settings* is picked up too. See
/// <see cref="RecycleBin.IconLocation"/> for how the dock draws the bin now.
/// </para>
/// </remarks>
public sealed class RecycleBinWatch : IDisposable
{
    /// <summary><c>FOLDERID_RecycleBinFolder</c>.</summary>
    private static readonly Guid RecycleBinFolder = new("b7534046-3ecb-4c18-be4e-64cd4cb7d6ac");

    private const int ShcneUpdateImage = 0x0000_8000;

    /// <summary>Events raised through <c>SHChangeNotify</c>, which is how the flip is announced.</summary>
    private const int ShcnrfShellLevel = 0x0002;

    /// <summary>Delivered in shared memory, to be locked and unlocked by the recipient.</summary>
    private const int ShcnrfNewDelivery = 0x8000;

    private const uint ShgfiPidl = 0x0000_0008;
    private const uint ShgfiIconLocation = 0x0000_1000;

    /// <summary>The bin, kept for as long as the watch is, to ask for its icon location.</summary>
    private nint _pidl;

    private uint _registration;

    /// <summary>Where the bin's icon came from when last asked, or null if it could not be.</summary>
    private string? _iconLocation;

    /// <summary>Whether the bin was empty when last asked, or null if the shell would not say.</summary>
    private bool? _empty;

    private RecycleBinWatch(nint pidl, uint registration)
    {
        _pidl = pidl;
        _registration = registration;
        _iconLocation = IconLocation(pidl);
        _empty = RecycleBin.TryIsEmpty();
    }

    /// <summary>
    /// Asks the shell to post <paramref name="message"/> to <paramref name="hwnd"/> whenever
    /// an icon may have changed.
    /// </summary>
    /// <remarks>
    /// Start this before the bin's icon is first read. A flip that lands between the two is
    /// then either already in the icon, or followed by a notification that will find the
    /// location moved — never missed in the gap.
    /// </remarks>
    /// <returns>
    /// Null when the shell will not register the window. The dock then draws the bin as it
    /// was when its icon was last read — the same as before there was a watch at all.
    /// </returns>
    public static RecycleBinWatch? Start(nint hwnd, int message)
    {
        if (SHGetKnownFolderIDList(RecycleBinFolder, 0, 0, out var pidl) != 0 || pidl == 0)
        {
            return null;
        }

        var entry = new ChangeNotifyEntry { Pidl = pidl, Recursive = false };
        var registration = SHChangeNotifyRegister(
            hwnd, ShcnrfShellLevel | ShcnrfNewDelivery, ShcneUpdateImage, (uint)message, 1, ref entry);

        if (registration == 0)
        {
            Marshal.FreeCoTaskMem(pidl);
            return null;
        }

        return new RecycleBinWatch(pidl, registration);
    }

    /// <summary>
    /// Takes one of the watch's messages, and answers whether the bin's icon is now a
    /// different one from last time.
    /// </summary>
    public bool IconChanged(nint wParam, nint lParam)
    {
        // Locked and unlocked whatever it says. With new delivery the notification sits in
        // shared memory until the recipient has done both, and the one event asked for is
        // the only one that can arrive.
        var notification = SHChangeNotification_Lock(wParam, (uint)lParam, out _, out _);
        if (notification != 0)
        {
            SHChangeNotification_Unlock(notification);
        }

        if (_pidl == 0)
        {
            return false;
        }

        var location = IconLocation(_pidl);
        var empty = RecycleBin.TryIsEmpty();
        if (!HasChanged(_iconLocation, location, _empty, empty))
        {
            return false;
        }

        _iconLocation = location;
        _empty = empty;
        return true;
    }

    /// <summary>
    /// Whether the bin is to be drawn again: it has gone from empty to not or back, or the
    /// location the shell gives for its icon has moved.
    /// </summary>
    /// <remarks>
    /// Either one that cannot be read counts as a change: an icon read again for nothing is a
    /// smaller wrong than one left stale.
    /// </remarks>
    public static bool HasChanged(string? wasLocation, string? location, bool? wasEmpty, bool? empty) =>
        location is null || location != wasLocation || empty is null || empty != wasEmpty;

    public void Dispose()
    {
        if (_registration != 0)
        {
            SHChangeNotifyDeregister(_registration);
            _registration = 0;
        }

        if (_pidl != 0)
        {
            Marshal.FreeCoTaskMem(_pidl);
            _pidl = 0;
        }
    }

    /// <summary>Where the shell will take the bin's icon from now: a file and an index.</summary>
    private static string? IconLocation(nint pidl)
    {
        var info = new ShFileInfo();
        return SHGetFileInfo(
                pidl, 0, ref info, (uint)Marshal.SizeOf<ShFileInfo>(), ShgfiPidl | ShgfiIconLocation) != 0
            ? $"{info.szDisplayName},{info.iIcon}"
            : null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ChangeNotifyEntry
    {
        public nint Pidl;

        [MarshalAs(UnmanagedType.Bool)]
        public bool Recursive;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileInfo
    {
        public nint hIcon;
        public int iIcon;
        public uint dwAttributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderIDList(in Guid rfid, uint dwFlags, nint hToken, out nint ppidl);

    [DllImport("shell32.dll")]
    private static extern uint SHChangeNotifyRegister(
        nint hwnd, int fSources, int fEvents, uint wMsg, int cEntries, ref ChangeNotifyEntry pshcne);

    [DllImport("shell32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SHChangeNotifyDeregister(uint ulID);

    [DllImport("shell32.dll")]
    private static extern nint SHChangeNotification_Lock(
        nint hChange, uint dwProcId, out nint pppidl, out int plEvent);

    [DllImport("shell32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SHChangeNotification_Unlock(nint hLock);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHGetFileInfoW")]
    private static extern nint SHGetFileInfo(
        nint pidl, uint dwFileAttributes, ref ShFileInfo psfi, uint cbFileInfo, uint uFlags);
}
