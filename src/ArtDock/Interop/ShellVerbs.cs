using System.Runtime.InteropServices;
using System.Text;

namespace ArtDock.Interop;

/// <summary>What the shell can do with a type of file, as its associations say.</summary>
public static class ShellVerbs
{
    private const int AssocStrCommand = 1;
    private const int AssocStrDelegateExecute = 18;

    /// <summary>
    /// True when Windows can start a type of file as administrator: when its association has
    /// the <c>runas</c> verb, which is what Explorer's <em>Run as administrator</em> is.
    /// </summary>
    /// <remarks>
    /// Asked of the system rather than of a list, as <see cref="ShellIcons.IsPictureType"/> is.
    /// Read here on 2026-10-03: <c>.exe</c>, <c>.bat</c>, <c>.cmd</c> and <c>.msc</c> have it;
    /// <c>.lnk</c>, <c>.com</c>, <c>.msi</c>, <c>.ps1</c>, <c>.vbs</c>, documents and folders do
    /// not. A shortcut is judged by what it points at. A verb is either a command or a
    /// <c>DelegateExecute</c> handler, so either counts.
    /// </remarks>
    /// <param name="extension">The extension, with its dot: <c>.exe</c>.</param>
    public static bool HasRunAs(string extension) =>
        extension is { Length: > 1 }
        && (Has(extension, AssocStrCommand) || Has(extension, AssocStrDelegateExecute));

    private static bool Has(string extension, int what)
    {
        var length = 1024u;
        var buffer = new StringBuilder((int)length);
        return AssocQueryString(0, what, extension, "runas", buffer, ref length) == 0;
    }

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, EntryPoint = "AssocQueryStringW")]
    private static extern int AssocQueryString(
        int flags, int str, string assoc, string extra, StringBuilder output, ref uint length);
}
