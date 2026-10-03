namespace ArtDock.Dock;

/// <summary>
/// Which of the windows on the desktop are a pin's: what lights its dot, what its previews show
/// and what a click on it brings forward. See <see cref="DockItem.RunningTarget"/>.
/// </summary>
/// <remarks>
/// <para>
/// A window is a program's, by the path of the process that owns it, and an app's, by Windows'
/// name for the app (<see cref="Interop.AppIds"/>) — the two are not the same, and a pin may know
/// either. A program pin is matched by both: by its path, failing that by its file name (Windows'
/// own launcher stubs, Notepad's, run from another folder under the same name), and by the app ID
/// its shortcut gives, if it gives one. A Store app's pin is its app ID and nothing else; its
/// window may belong to a host every such app shares.
/// </para>
/// <para>
/// A folder is shown by File Explorer's windows, every one of which is <c>explorer.exe</c>'s, and
/// a document by its app's windows, any of which may show some other document — so for those two
/// the program and the app say whose windows to look among, and the folder or the document which
/// of them are the pin's.
/// </para>
/// </remarks>
public sealed record RunningTarget
{
    /// <summary>The program, by its full path; for a document, the program that opens it, if the shell says.</summary>
    public string? Program { get; init; }

    /// <summary>Windows' name for the app (<see cref="Interop.AppIds"/>); for a document, its app's.</summary>
    public string? AppId { get; init; }

    /// <summary>For a folder, the shell's name for it: the File Explorer windows showing it are the pin's.</summary>
    public string? Folder { get; init; }

    /// <summary>
    /// For a document, its file name with its extension: the windows whose title names it are the
    /// pin's (<see cref="Services.DocumentWindows"/>).
    /// </summary>
    public string? Document { get; init; }
}
