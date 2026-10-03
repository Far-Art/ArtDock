using System.IO;
using System.Runtime.InteropServices;

namespace ArtDock.Interop;

/// <summary>What the shell calls things.</summary>
public static class ShellNames
{
    /// <summary>SIGDN_NORMALDISPLAY: the name Explorer shows, rather than a path.</summary>
    private const uint SigdnNormalDisplay = 0;

    /// <summary>
    /// SIGDN_DESKTOPABSOLUTEPARSING: the name the shell parses back to the same item — a path
    /// for a folder on disk, <c>::{20D04FE0-3AEA-1069-A2D8-08002B30309D}</c> for This PC.
    /// </summary>
    private const uint SigdnDesktopAbsoluteParsing = 0x80028000;

    /// <summary>SFGAO_FOLDER, SFGAO_FILESYSTEM and SFGAO_STREAM.</summary>
    private const uint SfgaoFolder = 0x2000_0000;

    private const uint SfgaoFileSystem = 0x4000_0000;

    private const uint SfgaoStream = 0x0040_0000;

    /// <summary>
    /// Whether a path or a <c>shell:</c> name is a folder on disk — <c>shell:Downloads</c> as
    /// much as <c>C:\Work</c>.
    /// </summary>
    /// <remarks>
    /// Asked of the shell rather than of the file system, because the Add menu pins Downloads
    /// and the user folder by their <c>shell:</c> names, which no <c>Directory.Exists</c> will
    /// recognise. The shell counts more as folders than a folder on disk, though: This PC and
    /// the Recycle Bin are folders with nothing on disk behind them, and a <c>.zip</c> is a
    /// file it lets you browse — both ruled out by the other two attributes.
    /// </remarks>
    public static bool IsFileSystemFolder(string? parsingName) =>
        Attributes(parsingName, SfgaoFolder | SfgaoFileSystem | SfgaoStream)
            == (SfgaoFolder | SfgaoFileSystem);

    /// <summary>
    /// The attributes asked for that a path or a <c>shell:</c> name has, or
    /// <see langword="null"/> when the shell will not resolve it.
    /// </summary>
    private static uint? Attributes(string? parsingName, uint asked)
    {
        if (string.IsNullOrWhiteSpace(parsingName))
        {
            return null;
        }

        IShellItem? item = null;
        try
        {
            SHCreateItemFromParsingName(parsingName, 0, typeof(IShellItem).GUID, out item);
            item.GetAttributes(asked, out var attributes);
            return attributes & asked;
        }
        catch (Exception e) when (e is COMException or IOException or UnauthorizedAccessException
            or ArgumentException)
        {
            return null;
        }
        finally
        {
            if (item is not null)
            {
                Marshal.ReleaseComObject(item);
            }
        }
    }

    /// <summary>
    /// The name Explorer shows for a path or a <c>shell:</c> name, or <see langword="null"/>
    /// when the shell will not resolve it.
    /// </summary>
    /// <remarks>
    /// Not always the name on disk. The user folder, <c>shell:Profile</c>, is shown under the
    /// account's full name — "Artur Farmanov" for <c>C:\Users\artur</c> on the machine this was
    /// written on — which is the name a dock should give it too.
    /// </remarks>
    public static string? DisplayName(string parsingName) => Name(parsingName, SigdnNormalDisplay);

    /// <summary>
    /// The shell's own name for a folder File Explorer opens — on disk, as <c>D:\</c> and
    /// <c>shell:Downloads</c> are, or not, as This PC and the Recycle Bin are — or
    /// <see langword="null"/> for anything else, or when the shell will not resolve it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The name is the one the shell parses back to the same folder whichever way it was
    /// written — <c>C:\Users\name\Downloads</c> for <c>shell:Downloads</c>, This PC's CLSID for
    /// <c>shell:MyComputerFolder</c> — and the one File Explorer's windows give for the folders
    /// they show (<see cref="ExplorerWindows"/>), which is what a folder pin is lit by.
    /// </para>
    /// <para>
    /// A <c>.zip</c> is a folder to the shell as well, and a file, which opens in whatever owns
    /// its type: ruled out by <c>SFGAO_STREAM</c>, as <see cref="IsFileSystemFolder"/> rules it
    /// out.
    /// </para>
    /// </remarks>
    public static string? FolderName(string? parsingName) =>
        parsingName is null
            ? null
            : Name(parsingName, SigdnDesktopAbsoluteParsing, SfgaoFolder | SfgaoStream, SfgaoFolder);

    /// <summary>
    /// One of the names of a path or a <c>shell:</c> name, or null when the shell will not say —
    /// or, when <paramref name="asked"/> is given, when the item's attributes among those are
    /// not <paramref name="wanted"/>.
    /// </summary>
    private static string? Name(string parsingName, uint sigdn, uint asked = 0, uint wanted = 0)
    {
        if (string.IsNullOrWhiteSpace(parsingName))
        {
            return null;
        }

        IShellItem? item = null;
        nint name = 0;
        try
        {
            SHCreateItemFromParsingName(parsingName, 0, typeof(IShellItem).GUID, out item);
            if (asked != 0)
            {
                item.GetAttributes(asked, out var attributes);
                if ((attributes & asked) != wanted)
                {
                    return null;
                }
            }

            item.GetDisplayName(sigdn, out name);

            var text = Marshal.PtrToStringUni(name)?.Trim();
            return string.IsNullOrEmpty(text) ? null : text;
        }
        catch (Exception e) when (e is COMException or IOException or UnauthorizedAccessException
            or ArgumentException)
        {
            // The same breadth as ShellIcons.Load, for the same reason: the shell's HRESULTs
            // arrive as whichever exception the runtime maps them to.
            return null;
        }
        finally
        {
            if (name != 0)
            {
                Marshal.FreeCoTaskMem(name);
            }

            if (item is not null)
            {
                Marshal.ReleaseComObject(item);
            }
        }
    }

    [ComImport]
    [Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(nint pbc, in Guid bhid, in Guid riid, out nint ppv);

        void GetParent(out IShellItem ppsi);

        void GetDisplayName(uint sigdnName, out nint ppszName);

        void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);

        void Compare(IShellItem psi, uint hint, out int piOrder);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(
        [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
        nint pbc,
        in Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItem ppv);
}
