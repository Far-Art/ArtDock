using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ArtDock.Interop;

namespace ArtDock.Services;

/// <summary>
/// Pins that do something rather than open something.
/// </summary>
/// <remarks>
/// <para>
/// Every other pin names a target and hands it to the shell. A few of the things a dock
/// most wants are not targets at all: the Start menu has no path, no AUMID and no entry in
/// the shell namespace, so there is nothing for <c>ShellExecute</c> to open — it is asked
/// for by posting the taskbar the command its own button sends. Show Desktop, Lock and Task
/// View are the same shape of problem, and this is where each would go.
/// </para>
/// <para>
/// Carried in <see cref="PinnedAppSetting.TargetPath"/> under a scheme of this
/// application's own, rather than in a new field, and deliberately: the settings file grows
/// no property, so a file written by this build still reads on an older one — where the pin
/// degrades to a target the shell cannot resolve, which is a broken icon rather than a
/// refusal to load. It costs one prefix check on two paths that already switch on what the
/// target is.
/// </para>
/// <para>
/// A command carries no icon from the shell either — every candidate was tried, and
/// <c>shell:AppsFolder</c>, <c>imageres.dll</c> and the Start menu's own package all answer
/// with a generic folder, document or window. So the image ships with the application.
/// </para>
/// </remarks>
public static class DockCommands
{
    /// <summary>The scheme that marks a target as a command rather than something to open.</summary>
    /// <remarks>
    /// Named after the application so it cannot collide with a real one. <c>ShellExecute</c>
    /// would fail on it, which is why nothing must reach the shell without being asked about
    /// here first.
    /// </remarks>
    private const string Scheme = "artdock:";

    /// <summary>Target for the entry that opens the Start menu.</summary>
    public const string StartTarget = Scheme + "start";

    /// <summary>Whether a target is one of these rather than something to open.</summary>
    public static bool IsCommand(string? target) =>
        target is { Length: > 0 }
        && target.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Runs a command, or answers false for anything that is not one.
    /// </summary>
    /// <returns>
    /// True when the command was recognised and carried out. A target under this scheme that
    /// nothing here knows — a settings file from a later build, hand-edited, or simply
    /// mistyped — answers false and is left alone rather than guessed at.
    /// </returns>
    public static bool Run(string? target)
    {
        if (!string.Equals(target, StartTarget, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var taskbar = NativeMethods.FindWindow("Shell_TrayWnd", null);
        return taskbar != 0
            && NativeMethods.PostMessage(
                taskbar, NativeMethods.WM_SYSCOMMAND, NativeMethods.SC_TASKLIST, 0);
    }

    /// <summary>
    /// The image for a command, or null for anything that is not one.
    /// </summary>
    /// <remarks>
    /// Read once and frozen, like everything else the dock draws — and cached here rather
    /// than relying on the caller's cache, because that one is keyed by target and this is
    /// the same image whoever asks. The mark is deliberately larger than the 128px the shell
    /// is asked for elsewhere: an icon at the dock's largest size, on a 150% display, is
    /// close to 360px, and the one image that does not come from the shell may as well not
    /// be the one that softens.
    /// </remarks>
    public static ImageSource? IconFor(string? target) =>
        string.Equals(target, StartTarget, StringComparison.OrdinalIgnoreCase)
            ? StartMark.Value
            : null;

    private static readonly Lazy<ImageSource?> StartMark = new(() => Load("start.png"));

    private static ImageSource? Load(string file)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri($"pack://application:,,,/Assets/icons/{file}");
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception e) when (e is IOException or UriFormatException or NotSupportedException)
        {
            // A missing or unreadable resource leaves the dock drawing its fallback glyph,
            // which is what it does for any icon the shell will not give it either.
            return null;
        }
    }
}
