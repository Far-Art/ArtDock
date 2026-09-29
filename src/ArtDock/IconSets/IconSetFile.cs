using ArtDock.Packs;

namespace ArtDock.IconSets;

/// <summary>
/// An icon set's <c>pack.json</c>: the common header, and which image to draw for what.
/// </summary>
/// <remarks>
/// <code>
/// {
///   "format": 1, "kind": "iconSet", "id": "flat", "name": "Flat",
///   "icons": [
///     { "match": { "exe": "notepad.exe" }, "file": "notepad.png" },
///     { "match": { "target": "shell:RecycleBinFolder", "state": "full" }, "file": "bin-full.png" }
///   ]
/// }
/// </code>
/// The full list of what can be matched is on <see cref="IconMatch"/>, and in
/// <c>docs/packs.md</c>.
/// </remarks>
public sealed class IconSetFile : PackManifest
{
    /// <summary>The set's icons, each with what it is drawn for.</summary>
    public List<IconRule>? Icons { get; set; }
}

/// <summary>One image in a set, and what it stands for.</summary>
public sealed class IconRule
{
    /// <summary>What a pin must be for this image to be drawn for it.</summary>
    public IconMatch? Match { get; set; }

    /// <summary>
    /// The image, as a path inside the set's folder. A PNG, square, and ideally 256 pixels
    /// or more: the dock magnifies, and on a 150% display its largest icon is well over 300.
    /// </summary>
    public string? File { get; set; }
}

/// <summary>
/// What a pin must be for an image to be drawn for it. Every field given must match; a field
/// left out matches anything.
/// </summary>
/// <remarks>
/// <para>
/// When several images match one pin, the most specific wins — a full path over a Store id or
/// a target, those over an executable's name, that over a file type, and a file type over
/// "any folder" — and among equals, the one listed first. So a set can say "every folder
/// looks like this" and still give one particular folder its own.
/// </para>
/// </remarks>
public sealed class IconMatch
{
    /// <summary>
    /// The executable's file name, <c>notepad.exe</c>. Matches a pin on the executable and
    /// a shortcut that points at it.
    /// </summary>
    public string? Exe { get; set; }

    /// <summary>
    /// A full path, which may use environment variables: <c>%WINDIR%\explorer.exe</c>.
    /// Matches a pin on that path, or a shortcut pointing at it.
    /// </summary>
    public string? Path { get; set; }

    /// <summary>A Store app's Application User Model ID.</summary>
    public string? Aumid { get; set; }

    /// <summary>
    /// A target exactly as the pin stores it — how the places and commands with no file behind
    /// them are matched: <c>shell:RecycleBinFolder</c>, <c>shell:MyComputerFolder</c>,
    /// <c>artdock:start</c>, or a web address.
    /// </summary>
    public string? Target { get; set; }

    /// <summary>A document's type, <c>.pdf</c> — with or without the dot.</summary>
    public string? Extension { get; set; }

    /// <summary>True to match any pinned folder.</summary>
    public bool? Folder { get; set; }

    /// <summary>
    /// <c>empty</c> or <c>full</c>, for a pin whose picture changes with what it holds — the
    /// Recycle Bin, and nothing else so far. A set that gives the bin one image without a
    /// state has it drawn the same whether it is full or not.
    /// </summary>
    public string? State { get; set; }
}
