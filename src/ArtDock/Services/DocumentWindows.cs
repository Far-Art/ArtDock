using System.IO;

namespace ArtDock.Services;

/// <summary>
/// Whether a window shows a document, by its title: what lights a document's pin, as a folder's
/// is lit by the windows showing the folder.
/// </summary>
/// <remarks>
/// <para>
/// A title is all there is to go by. A program says nothing to anyone outside it about which
/// file a window has open, and the one other thing the dock could know — that the window came up
/// when the pin was clicked — is lost the moment the dock restarts, and never was for a document
/// opened anywhere else. The title is what Alt+Tab and the taskbar show, and what the user tells
/// the windows apart by too. Measured 2026-10-03: Word titles a window
/// <c>name.docx - Word</c>, Photos plain <c>name.jpg</c>.
/// </para>
/// <para>
/// So a window shows a document when its title names the file, extension and all, as a word of
/// its own — not inside a longer name, or a pin for <c>1.jpg</c> would be lit by <c>21.jpg</c>. A
/// window of the app that opens the file (<see cref="Interop.ShellVerbs.HandlerAppId"/>) may also
/// leave the extension out, as an app does when Explorer is set to hide them, but only at the
/// start of the title and followed by the app's own name or nothing: <c>1 (2).jpg</c> does not
/// show <c>1.jpg</c>.
/// </para>
/// </remarks>
public static class DocumentWindows
{
    /// <summary>Whether a window titled <paramref name="title"/> shows the file <paramref name="fileName"/>.</summary>
    /// <param name="title">The window's title.</param>
    /// <param name="fileName">The document's file name, with its extension.</param>
    /// <param name="byItsApp">Whether the window belongs to the app that opens the document.</param>
    public static bool Shows(string? title, string? fileName, bool byItsApp)
    {
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        if (NamesAsAWord(title, fileName))
        {
            return true;
        }

        var stem = Path.GetFileNameWithoutExtension(fileName);
        if (!byItsApp || stem.Length == 0 || stem.Length == fileName.Length)
        {
            return false;
        }

        // An unsaved change is marked at the front, by some: *name - Notepad.
        var shown = title.TrimStart(' ', '*', '\u2022', '\u200E', '\u200F');
        if (!shown.StartsWith(stem, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var rest = shown[stem.Length..].TrimStart(' ', '\u200E', '\u200F', '\u00A0');
        return rest.Length == 0 || rest[0] is '-' or '\u2013' or '\u2014' or '|' or '@' or '*' or '\u2022';
    }

    /// <summary>Whether <paramref name="name"/> is in <paramref name="title"/> with no letter or digit against either end.</summary>
    private static bool NamesAsAWord(string title, string name)
    {
        for (var at = title.IndexOf(name, StringComparison.OrdinalIgnoreCase);
             at >= 0;
             at = title.IndexOf(name, at + 1, StringComparison.OrdinalIgnoreCase))
        {
            // Nor is the next a further extension: Letter.docx.bak is not Letter.docx.
            var end = at + name.Length;
            if ((at == 0 || !char.IsLetterOrDigit(title[at - 1]))
                && (end >= title.Length || !char.IsLetterOrDigit(title[end]))
                && !(end + 1 < title.Length && title[end] == '.' && char.IsLetterOrDigit(title[end + 1])))
            {
                return true;
            }

            if (at + 1 >= title.Length)
            {
                break;
            }
        }

        return false;
    }
}
