using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using ArtDock.Localization;

namespace ArtDock.Packs;

/// <summary>
/// What every <c>pack.json</c> says about itself, whatever kind of pack it is.
/// </summary>
/// <remarks>
/// <para>
/// The kinds add their own content below these fields — a language its strings, an icon set
/// its icons — but the header is common, so that anything that has to reason about packs in
/// general (listing them, checking whether this build can read one, and later comparing an
/// installed pack with what a catalog offers) reads one shape.
/// </para>
/// <para>
/// Hand-written JSON, so it is read forgivingly: camel case or not, comments allowed, a
/// trailing comma allowed. What is <em>not</em> forgiven is anything that would make the
/// pack mean something other than what its author meant — see <see cref="Check"/>.
/// </para>
/// </remarks>
public class PackManifest
{
    /// <summary>The <see cref="Kind"/> a language pack declares.</summary>
    public const string LanguageKind = "language";

    /// <summary>The <see cref="Kind"/> an icon set declares.</summary>
    public const string IconSetKind = "iconSet";

    /// <summary>
    /// The layout of the file, for the day a kind's content has to change shape.
    /// </summary>
    /// <remarks>
    /// The same rule as <c>DockSettings.Version</c>: adding an optional field does not bump
    /// it, because an unknown field is ignored and a missing one takes its default. Changing
    /// what an existing field means does. A pack in a format newer than this build reads is
    /// skipped rather than half-understood.
    /// </remarks>
    public int Format { get; set; } = 1;

    /// <summary><see cref="LanguageKind"/> or <see cref="IconSetKind"/>.</summary>
    /// <remarks>
    /// Redundant with the folder the pack sits in, deliberately: a pack copied into the wrong
    /// folder says so, rather than being read as something it is not.
    /// </remarks>
    public string? Kind { get; set; }

    /// <summary>
    /// What the pack is known by. A language's is its language tag (<c>ru</c>, <c>pt-BR</c>);
    /// an icon set's is a short name of its author's choosing. This is what the settings file
    /// stores, so it must not change between versions of the same pack.
    /// </summary>
    public string? Id { get; set; }

    /// <summary>What the pack is called where the user picks it.</summary>
    public string? Name { get; set; }

    /// <summary>The pack's own version, such as <c>1.2.0</c>.</summary>
    public string? Version { get; set; }

    /// <summary>The oldest ArtDock the pack works with, if it needs a recent one.</summary>
    public string? MinAppVersion { get; set; }

    /// <summary>Who made it.</summary>
    public string? Author { get; set; }

    /// <summary>
    /// What it may be used and passed on under, as an SPDX identifier where there is one
    /// (<c>CC-BY-4.0</c>, <c>MIT</c>). Carried so it can be shown; nothing enforces it.
    /// </summary>
    public string? License { get; set; }

    /// <summary>A sentence about the pack, for wherever it is offered.</summary>
    public string? Description { get; set; }

    /// <summary><see cref="Version"/> parsed, or null when it is absent or not a version.</summary>
    [JsonIgnore]
    public Version? ParsedVersion => System.Version.TryParse(Version, out var version) ? version : null;

    /// <summary>The <see cref="Kind"/> string a kind of pack declares.</summary>
    public static string KindName(PackKind kind) => kind switch
    {
        PackKind.Language => LanguageKind,
        PackKind.IconSet => IconSetKind,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };

    /// <summary>
    /// This build's version, as a pack's <see cref="MinAppVersion"/> is compared against.
    /// </summary>
    /// <remarks>
    /// The informational version, which is the one the project's <c>&lt;Version&gt;</c>
    /// sets, without the <c>+commit</c> suffix some builds append.
    /// </remarks>
    public static Version AppVersion { get; } = ReadAppVersion();

    private static Version ReadAppVersion()
    {
        var informational = typeof(PackManifest).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        return System.Version.TryParse(informational?.Split('+', '-')[0], out var version)
            ? version
            : new Version(0, 0);
    }

    /// <summary>
    /// Why this build cannot use the pack, or null when it can.
    /// </summary>
    /// <param name="expected">The kind of folder the pack was found in.</param>
    /// <param name="highestFormat">The newest <see cref="Format"/> this build reads for that kind.</param>
    /// <param name="appVersion">The version to hold <see cref="MinAppVersion"/> against.</param>
    /// <returns>A sentence for the user, in the dock's language.</returns>
    public string? Check(PackKind expected, int highestFormat, Version appVersion)
    {
        var kind = KindName(expected);
        if (!string.Equals(Kind, kind, StringComparison.OrdinalIgnoreCase))
        {
            return Localizer.Format("Packs.Problem.WrongKind", Kind ?? "?", kind);
        }

        if (Format > highestFormat)
        {
            return Localizer.Format("Packs.Problem.NewerFormat", Format, highestFormat);
        }

        if (string.IsNullOrWhiteSpace(Id))
        {
            return Localizer.Get("Packs.Problem.NoId");
        }

        if (string.IsNullOrWhiteSpace(Name))
        {
            return Localizer.Get("Packs.Problem.NoName");
        }

        if (System.Version.TryParse(MinAppVersion, out var needed) && needed > appVersion)
        {
            return Localizer.Format("Packs.Problem.NeedsNewerApp", needed);
        }

        return null;
    }

    /// <summary>
    /// Reads a pack's manifest as one kind or another.
    /// </summary>
    /// <returns>The manifest, or a sentence saying why there is none.</returns>
    internal static (T? Manifest, string? Problem) Read<T>(
        string folder,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type)
        where T : PackManifest
    {
        var path = Path.Combine(folder, PackLocations.ManifestFileName);
        if (!File.Exists(path))
        {
            return (null, Localizer.Get("Packs.Problem.NoManifest"));
        }

        try
        {
            using var stream = File.OpenRead(path);
            return Parse(stream, type) is { } manifest
                ? (manifest, null)
                : (null, Localizer.Get("Packs.Problem.Empty"));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return (null, Localizer.Format("Packs.Problem.Unreadable", e.Message));
        }
    }

    /// <summary>
    /// Parses a manifest from a stream: null for a file that is JSON's <c>null</c>, and a
    /// throw for one that is not JSON of this shape at all.
    /// </summary>
    /// <exception cref="JsonException">The stream is not a JSON object of this shape.</exception>
    internal static T? Parse<T>(Stream stream, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type)
        where T : PackManifest =>
        JsonSerializer.Deserialize(stream, type);
}

/// <summary>
/// A pack that was found and could not be used, and why — so the settings dialog can say so
/// rather than leave a pack that was copied into place to not appear, unexplained.
/// </summary>
/// <param name="Folder">The pack's folder name, which is what the user will recognise.</param>
/// <param name="Reason">What is wrong with it, in the dock's language.</param>
public sealed record PackProblem(string Folder, string Reason)
{
    /// <summary>
    /// The pack is in use, and this is about a part of it that was left out — an image an
    /// icon set names and does not have — rather than about the pack as a whole.
    /// </summary>
    public bool IsPartial { get; init; }

    /// <summary>The sentence the settings dialog shows for it.</summary>
    public string Describe() => Localizer.Format(
        IsPartial ? "Settings.Packs.PartlySkipped" : "Settings.Packs.Problem", Folder, Reason);
}

/// <summary>
/// Source-generated reading for every kind of <c>pack.json</c>.
/// </summary>
/// <remarks>
/// Forgiving about the things a person writing JSON by hand gets wrong without changing the
/// meaning — capitalisation, comments, a trailing comma — for the same reason the settings
/// file is read through a generated context: nothing here depends on reflection surviving.
/// </remarks>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(PackManifest))]
[JsonSerializable(typeof(LanguagePackFile))]
[JsonSerializable(typeof(IconSets.IconSetFile))]
internal partial class PackJsonContext : JsonSerializerContext;
