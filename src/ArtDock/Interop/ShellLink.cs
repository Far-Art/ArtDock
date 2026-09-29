using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace ArtDock.Interop;

/// <summary>
/// Reads what a Windows shortcut points at.
/// </summary>
/// <remarks>
/// <para>
/// A pinned <c>.lnk</c> is kept as the shortcut — that is what launches with the arguments,
/// the working directory and the icon its author chose. But a shortcut's own path never
/// matches the image path of the process it starts, so the running indicator stayed dark for
/// every pin made by dragging something out of the Start menu. This resolves the executable
/// behind the shortcut, which is the only thing that comparison needs.
/// </para>
/// <para>
/// <c>IShellLink::Resolve</c> is deliberately not called. It hunts for a target that has
/// moved, which can walk a volume and can put UI on screen, and this is a question the dock
/// can perfectly well have no answer to: an unresolvable shortcut simply gets no indicator.
/// </para>
/// </remarks>
public static class ShellLink
{
    /// <summary>Retrieved path length. <c>MAX_PATH</c> is what the shell stores; the rest is slack.</summary>
    private const int PathBufferLength = 1024;

    /// <summary>SLGP_UNCPRIORITY: the target as stored, preferring a UNC path where it has one.</summary>
    private const uint SlgpUncPriority = 0x0002;

    /// <summary>STGM_READ. The dock only ever reads a shortcut.</summary>
    private const int StgmRead = 0x0000_0000;

    /// <summary>
    /// The executable, folder or document a shortcut points at.
    /// </summary>
    /// <returns>
    /// The target's full path, or <see langword="null"/> when the file is not a readable
    /// shortcut, or points at something with no path of its own — a Store app or a control
    /// panel item, both of which are addressed by ID rather than by file.
    /// </returns>
    public static string? ResolveTarget(string linkPath)
    {
        if (string.IsNullOrWhiteSpace(linkPath))
        {
            return null;
        }

        object? link = null;
        try
        {
            link = new ShellLinkCoClass();
            ((IPersistFile)link).Load(linkPath, StgmRead);

            var target = new StringBuilder(PathBufferLength);
            var found = default(WindowsFindData);
            ((IShellLinkW)link).GetPath(target, target.Capacity, ref found, SlgpUncPriority);

            return target.Length > 0 ? target.ToString() : null;
        }
        catch (Exception e) when (e is COMException or IOException or UnauthorizedAccessException
            or InvalidCastException or ArgumentException)
        {
            // A missing, unreadable or malformed shortcut. None of it is worth reporting —
            // the pin still launches through the shell exactly as it did before, it simply
            // gets no running indicator. Note the breadth: the runtime translates the
            // shell's HRESULTs into specific exceptions rather than a COMException, so a
            // deleted target arrives here as FileNotFoundException and a pin whose shortcut
            // has since been removed would otherwise take the dock down with it.
            return null;
        }
        finally
        {
            if (link is not null)
            {
                Marshal.ReleaseComObject(link);
            }
        }
    }

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLinkCoClass
    {
    }

    /// <summary>
    /// <c>IShellLinkW</c>, of which only <c>GetPath</c> is declared.
    /// </summary>
    /// <remarks>
    /// It is the first entry in the vtable, so nothing above it has to be declared to reach
    /// it — but anything added <i>below</i> it would have to declare every method in between,
    /// in order, or it would call the wrong one.
    /// </remarks>
    [ComImport]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath(
            [MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile,
            int cch,
            ref WindowsFindData pfd,
            uint fFlags);
    }

    [ComImport]
    [Guid("0000010b-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPersistFile
    {
        // IPersist, which IPersistFile extends: its one method comes first in the vtable.
        void GetClassID(out Guid pClassID);

        [PreserveSig]
        int IsDirty();

        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, int dwMode);
    }

    /// <summary>
    /// <c>WIN32_FIND_DATAW</c>, filled in by <c>GetPath</c> and read by nothing here.
    /// </summary>
    /// <remarks>
    /// Passed rather than left null because the shell writes through the pointer, and a
    /// wrong layout here is memory corruption rather than a failed call. The two timestamps
    /// are pairs of 32-bit words rather than <c>long</c>s for exactly that reason: every
    /// field of the native structure is four bytes wide, so a <c>long</c> would align itself
    /// to eight and push everything after it out of place.
    /// </remarks>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private unsafe struct WindowsFindData
    {
        public uint FileAttributes;
        public uint CreationTimeLow;
        public uint CreationTimeHigh;
        public uint LastAccessTimeLow;
        public uint LastAccessTimeHigh;
        public uint LastWriteTimeLow;
        public uint LastWriteTimeHigh;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint Reserved0;
        public uint Reserved1;
        public fixed char FileName[260];
        public fixed char AlternateFileName[14];
    }
}
