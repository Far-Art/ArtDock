using ArtDock.Services;
using Velopack;

namespace ArtDock.Tests;

/// <summary>
/// Covers the release notes the About page shows beside an update: which releases' notes, and
/// how they are read.
/// </summary>
/// <remarks>
/// <para>
/// Which: every version the update brings, newest first, and nothing else — not the version
/// running, not one past what is on offer, one entry for a release that has a delta as well as
/// a full package, and none for a release that published no notes.
/// </para>
/// <para>
/// How: the small part of Markdown the notes are written in, with a release's own notes as
/// they were published (0.9.9's wrap their bullets over indented lines). Anything else reads as
/// the text it is.
/// </para>
/// </remarks>
public class ReleaseNotesTests
{
    private static VelopackAsset Asset(string version, VelopackAssetType type, string? notes) =>
        new()
        {
            PackageId = "ArtDock.App",
            Version = SemanticVersion.Parse(version),
            Type = type,
            FileName = $"ArtDock.App-{version}-{type}.nupkg",
            NotesMarkdown = notes
        };

    private static readonly VelopackAsset[] Feed =
    [
        Asset("0.9.9", VelopackAssetType.Full, "# ArtDock 0.9.9"),
        Asset("0.9.10", VelopackAssetType.Full, "# ArtDock 0.9.10"),
        Asset("0.9.10", VelopackAssetType.Delta, "# ArtDock 0.9.10"),
        Asset("0.9.11", VelopackAssetType.Full, "   "),
        Asset("0.9.12", VelopackAssetType.Full, "# ArtDock 0.9.12"),
        Asset("0.9.12", VelopackAssetType.Delta, "# ArtDock 0.9.12"),
        Asset("0.9.13", VelopackAssetType.Full, "# ArtDock 0.9.13")
    ];

    [Fact]
    public void EveryVersionTheUpdateBrings_NewestFirst_OncePerVersion()
    {
        var notes = ReleaseNotes.Between(Feed, SemanticVersion.Parse("0.9.9"), SemanticVersion.Parse("0.9.12"));

        Assert.Equal(["0.9.12", "0.9.10"], notes.Select(note => note.Version.ToString()));
    }

    [Fact]
    public void NotTheVersionRunning_NorOnePastWhatIsOffered()
    {
        var notes = ReleaseNotes.Between(Feed, SemanticVersion.Parse("0.9.12"), SemanticVersion.Parse("0.9.12"));

        Assert.Empty(notes);
    }

    [Fact]
    public void AReleaseWithoutNotes_IsLeftOut()
    {
        var notes = ReleaseNotes.Between(Feed, SemanticVersion.Parse("0.9.10"), SemanticVersion.Parse("0.9.11"));

        Assert.Empty(notes);
    }

    [Fact]
    public void Headings_Bullets_AndParagraphs()
    {
        var blocks = ReleaseNotes.Parse("# ArtDock 0.9.11\r\n\r\nA paragraph\r\nthat wraps.\r\n\r\n- One\r\n- Two\r\n");

        Assert.Equal(
            [NoteBlockKind.Heading, NoteBlockKind.Paragraph, NoteBlockKind.Bullet, NoteBlockKind.Bullet],
            blocks.Select(block => block.Kind));
        Assert.Equal("ArtDock 0.9.11", Text(blocks[0]));
        Assert.Equal("A paragraph that wraps.", Text(blocks[1]));
        Assert.Equal("Two", Text(blocks[3]));
    }

    [Fact]
    public void ABulletWrappedOverIndentedLines_IsOneBullet()
    {
        // As 0.9.9's notes were published.
        var blocks = ReleaseNotes.Parse(
            "- **Pin anything**: apps, files, folders, web addresses, and places such as This PC and the\n" +
            "  Recycle Bin. Search the apps.\n" +
            "- **Live window previews** above a running app's icon.\n" +
            "\n" +
            "Installs for the current user only.");

        Assert.Equal([NoteBlockKind.Bullet, NoteBlockKind.Bullet, NoteBlockKind.Paragraph], blocks.Select(block => block.Kind));
        Assert.Equal(
            "Pin anything: apps, files, folders, web addresses, and places such as This PC and the Recycle Bin. Search the apps.",
            Text(blocks[0]));
    }

    [Fact]
    public void AnUnindentedLineAfterABullet_StartsAParagraph()
    {
        var blocks = ReleaseNotes.Parse("- A bullet\nNot part of it.");

        Assert.Equal([NoteBlockKind.Bullet, NoteBlockKind.Paragraph], blocks.Select(block => block.Kind));
    }

    [Fact]
    public void BoldAndItalic_AreMarked_AndTheMarksGo()
    {
        var spans = ReleaseNotes.Parse("- **Fixed:** a window, or *New window* in its menu.")[0].Spans;

        Assert.Equal(
            [
                new NoteSpan("Fixed:", Bold: true),
                new NoteSpan(" a window, or "),
                new NoteSpan("New window", Italic: true),
                new NoteSpan(" in its menu.")
            ],
            spans);
    }

    [Fact]
    public void ItalicInsideBold_IsBoth()
    {
        var spans = ReleaseNotes.Parse("**Open *another* window**")[0].Spans;

        Assert.Contains(new NoteSpan("another", Bold: true, Italic: true), spans);
        Assert.Equal("Open another window", string.Concat(spans.Select(span => span.Text)));
    }

    [Fact]
    public void CodeAndLinks_ReadAsTheirText()
    {
        var blocks = ReleaseNotes.Parse("Run `ArtDock.exe --settings`, or see [the docs](https://example.com/docs).");

        Assert.Equal("Run ArtDock.exe --settings, or see the docs.", Text(blocks[0]));
    }

    [Fact]
    public void WhatIsNotMarkdownHere_StaysAsItIs()
    {
        // An underscore inside a word is not italic, and a lone star is a star.
        var blocks = ReleaseNotes.Parse("Set snake_case_names to 2 * 3, or **unclosed.");

        Assert.Equal("Set snake_case_names to 2 * 3, or **unclosed.", Text(blocks[0]));
        Assert.All(blocks[0].Spans, span => Assert.False(span.Bold || span.Italic));
    }

    [Fact]
    public void NothingToRead_IsNoBlocks()
    {
        Assert.Empty(ReleaseNotes.Parse(null));
        Assert.Empty(ReleaseNotes.Parse(" \n\n "));
    }

    private static string Text(NoteBlock block) => string.Concat(block.Spans.Select(span => span.Text));
}
