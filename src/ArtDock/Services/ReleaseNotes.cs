using System.Text;
using System.Text.RegularExpressions;
using Velopack;

namespace ArtDock.Services;

/// <summary>One version's release notes, as its release published them.</summary>
/// <param name="Version">The version they belong to.</param>
/// <param name="Markdown">The notes, in the Markdown <c>tools/release.ps1</c> was given.</param>
public sealed record ReleaseNote(SemanticVersion Version, string Markdown);

/// <summary>What a block of release notes is: a heading, a paragraph or one bullet of a list.</summary>
public enum NoteBlockKind
{
    Heading,
    Paragraph,
    Bullet
}

/// <summary>A run of text in a block of release notes, bold or italic or neither.</summary>
public sealed record NoteSpan(string Text, bool Bold = false, bool Italic = false);

/// <summary>A heading, a paragraph or a bullet of release notes, as runs of text.</summary>
public sealed record NoteBlock(NoteBlockKind Kind, IReadOnlyList<NoteSpan> Spans);

/// <summary>
/// The release notes the About page shows beside an update, so whoever is offered it can see
/// what it changes before taking it.
/// </summary>
/// <remarks>
/// <para>
/// Every version the update passes over, not only the one it lands on: a copy several releases
/// behind goes straight to the newest, and what changed in the releases between is as much a
/// part of what it is being offered. Each release carries its own notes in its feed, as
/// <c>tools/release.ps1</c> packs them.
/// </para>
/// <para>
/// Read as the small part of Markdown the notes are written in — headings, paragraphs, a
/// bulleted list, bold, italic, code and links — and nothing more. Anything else is shown as
/// the text it is, which is what Markdown is meant to read as anyway. Pure, so it is tested.
/// </para>
/// </remarks>
public static partial class ReleaseNotes
{
    /// <summary>
    /// The notes of every release after <paramref name="current"/> up to and including
    /// <paramref name="target"/>, newest first — one for each version, and none for a release
    /// that published none.
    /// </summary>
    /// <remarks>
    /// Taken from the full packages: a release has one full package and may have a delta as
    /// well, both carrying the same notes.
    /// </remarks>
    public static IReadOnlyList<ReleaseNote> Between(
        IEnumerable<VelopackAsset> assets, SemanticVersion current, SemanticVersion target) =>
        [.. assets
            .Where(asset => asset.Type == VelopackAssetType.Full
                && asset.Version > current
                && asset.Version <= target
                && !string.IsNullOrWhiteSpace(asset.NotesMarkdown))
            .GroupBy(asset => asset.Version)
            .Select(group => new ReleaseNote(group.Key, group.First().NotesMarkdown!))
            .OrderByDescending(note => note.Version)];

    /// <summary>Reads release notes into headings, paragraphs and bullets.</summary>
    /// <remarks>
    /// A bullet carries on over the lines indented under it, and a paragraph over the lines
    /// that follow it, as Markdown wraps them; a blank line, a heading or a new bullet ends
    /// either.
    /// </remarks>
    public static IReadOnlyList<NoteBlock> Parse(string? markdown)
    {
        var blocks = new List<NoteBlock>();
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return blocks;
        }

        NoteBlockKind? kind = null;
        var text = new StringBuilder();

        void Finish()
        {
            if (kind is { } finished && text.Length > 0)
            {
                blocks.Add(new NoteBlock(finished, Inline(text.ToString())));
            }

            kind = null;
            text.Clear();
        }

        foreach (var raw in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.TrimEnd();
            var trimmed = line.TrimStart();

            if (trimmed.Length == 0)
            {
                Finish();
                continue;
            }

            if (HeadingLine().Match(trimmed) is { Success: true } heading)
            {
                Finish();
                blocks.Add(new NoteBlock(NoteBlockKind.Heading, Inline(heading.Groups[1].Value)));
                continue;
            }

            if (BulletLine().Match(trimmed) is { Success: true } bullet)
            {
                Finish();
                kind = NoteBlockKind.Bullet;
                text.Append(bullet.Groups[1].Value);
                continue;
            }

            // A line under a bullet is the bullet's if it is indented, and starts a paragraph of
            // its own if it is not.
            if (kind == NoteBlockKind.Bullet && trimmed.Length == line.Length)
            {
                Finish();
            }

            kind ??= NoteBlockKind.Paragraph;
            if (text.Length > 0)
            {
                text.Append(' ');
            }

            text.Append(trimmed);
        }

        Finish();
        return blocks;
    }

    /// <summary>Reads a block's text into runs: bold, italic, code shown as text, a link as its words.</summary>
    private static IReadOnlyList<NoteSpan> Inline(string text, bool bold = false)
    {
        var spans = new List<NoteSpan>();
        var at = 0;

        foreach (Match match in InlineMark().Matches(text))
        {
            if (match.Index > at)
            {
                spans.Add(new NoteSpan(text[at..match.Index], bold));
            }

            if (match.Groups["bold"].Success)
            {
                spans.AddRange(Inline(match.Groups["bold"].Value, bold: true));
            }
            else if (match.Groups["italic"].Success)
            {
                spans.Add(new NoteSpan(match.Groups["italic"].Value, bold, Italic: true));
            }
            else if (match.Groups["code"].Success)
            {
                spans.Add(new NoteSpan(match.Groups["code"].Value, bold));
            }
            else
            {
                spans.Add(new NoteSpan(match.Groups["link"].Value, bold));
            }

            at = match.Index + match.Length;
        }

        if (at < text.Length)
        {
            spans.Add(new NoteSpan(text[at..], bold));
        }

        return spans;
    }

    [GeneratedRegex(@"^#{1,6}\s+(.+?)\s*#*$")]
    private static partial Regex HeadingLine();

    [GeneratedRegex(@"^[-*+]\s+(.*)$")]
    private static partial Regex BulletLine();

    /// <summary>
    /// Bold, then italic — by a star or an underscore, closed by the same, and not inside a
    /// word, so <c>snake_case</c> stays as it is — then code, then a link.
    /// </summary>
    [GeneratedRegex(@"\*\*(?<bold>.+?)\*\*|(?<![\w*])(?<mark>[*_])(?<italic>[^*_]+?)\k<mark>(?![\w*])|`(?<code>[^`]+)`|\[(?<link>[^\]]+)\]\([^)]*\)")]
    private static partial Regex InlineMark();
}
