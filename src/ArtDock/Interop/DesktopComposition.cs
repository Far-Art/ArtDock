using System.Runtime.InteropServices;

namespace ArtDock.Interop;

/// <summary>
/// The Desktop Window Manager calls the dock's backdrop window needs: the system acrylic
/// material, the window shadow that comes with it, and the region that clips both to the
/// bar's own shape.
/// </summary>
/// <remarks>
/// Kept separate from <see cref="NativeMethods"/>, which is about the dock window's own
/// chrome. Everything here applies to a second, ordinary window sitting behind it — see
/// <c>Views/BackdropWindow.cs</c> for why the blur cannot live on the dock window itself.
/// </remarks>
internal static class DesktopComposition
{
    // ---- DwmSetWindowAttribute -----------------------------------------------

    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaSystemBackdropType = 38;

    /// <summary>No material at all.</summary>
    private const int DwmsbtNone = 1;

    /// <summary>Mica — the material the Settings app and File Explorer sit on.</summary>
    private const int DwmsbtMainWindow = 2;

    /// <summary>Acrylic — the transient-window material, which is the blurred one.</summary>
    private const int DwmsbtTransientWindow = 3;

    private const int DwmwcpDoNotRound = 1;
    private const int DwmwcpRound = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int Left;
        public int Right;
        public int Top;
        public int Bottom;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(nint hwnd, ref Margins margins);

    // ---- the older accent policy ---------------------------------------------
    //
    // Undocumented, and the only route to acrylic before Windows 11 22H2 gave
    // DWMWA_SYSTEMBACKDROP_TYPE a documented one. Kept as the fallback.

    private const int WcaAccentPolicy = 19;
    private const int AccentEnableBlurBehind = 3;

    /// <summary>
    /// Opts the window in to host-backdrop sampling.
    /// </summary>
    /// <remarks>
    /// Draws nothing by itself. It is what makes <c>Compositor.CreateHostBackdropBrush</c>
    /// return an actual blur of the desktop rather than solid black — that brush was built
    /// for windows that get this for free, and a plain Win32 window has to ask.
    /// </remarks>
    private const int AccentEnableHostBackdrop = 5;
    private const int AccentEnableAcrylicBlurBehind = 4;

    /// <summary>Applies the tint to every edge rather than leaving the borders bare.</summary>
    private const int AccentAllBorders = 0x20 | 0x40 | 0x80 | 0x100;

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public uint GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public nint Data;
        public int SizeOfData;
    }

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(
        nint hwnd, ref WindowCompositionAttributeData data);

    /// <summary>
    /// Turns a window into a sheet of acrylic.
    /// </summary>
    /// <remarks>
    /// Two things have to be true before DWM will show a backdrop: the frame has to be
    /// extended over the whole client area, and the client area has to be painted with
    /// nothing. The caller handles the second — see <c>BackdropWindow</c>.
    /// </remarks>
    /// <param name="tint">
    /// Colour the blur is washed with, as <c>#AABBGGRR</c>. The alpha is how much of the
    /// tint shows, so a low one lets whatever is behind the window come through in colour.
    /// </param>
    /// <returns>True when the window got a material of some kind.</returns>
    /// <remarks>
    /// The accent policy is tried before <c>DWMWA_SYSTEMBACKDROP_TYPE</c>, despite the
    /// latter being the documented one, because DWM's system materials fall back to a flat
    /// colour while their window is <b>inactive</b> — and this window is
    /// <c>WS_EX_NOACTIVATE</c>, so it is never anything else. What that produced was a grey
    /// slab with no sign of what was behind it. The accent policy is the route the taskbar
    /// itself is on, and it blurs whatever is behind the window regardless of focus.
    /// </remarks>
    public static bool EnableAcrylic(nint hwnd, uint tint)
    {
        var dark = 1;
        DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));

        if (EnableAccentBlur(hwnd, AccentEnableAcrylicBlurBehind, tint)
            || EnableAccentBlur(hwnd, AccentEnableBlurBehind, tint))
        {
            return true;
        }

        // Nothing else left to try. The frame has to be extended for this one, which is
        // why it is not done unconditionally above — it paints its own sheet over the
        // client area and would hide the accent blur.
        var sheet = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        DwmExtendFrameIntoClientArea(hwnd, ref sheet);

        var backdrop = DwmsbtTransientWindow;
        return DwmSetWindowAttribute(hwnd, DwmwaSystemBackdropType, ref backdrop, sizeof(int)) == 0;
    }

    /// <summary>
    /// Puts a window on Mica, the material Windows' own Settings app uses.
    /// </summary>
    /// <remarks>
    /// Unlike the dock's backdrop this is a window that activates normally, so DWM's system
    /// materials behave as designed — including going quieter when the window is not in
    /// front, which is the Settings app's behaviour too, not a fault.
    /// </remarks>
    /// <returns>True when DWM took it, which is the caller's cue to stop painting a background.</returns>
    public static bool EnableMica(nint hwnd, bool dark)
    {
        var immersive = dark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref immersive, sizeof(int));

        var sheet = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        DwmExtendFrameIntoClientArea(hwnd, ref sheet);

        var backdrop = DwmsbtMainWindow;
        return DwmSetWindowAttribute(hwnd, DwmwaSystemBackdropType, ref backdrop, sizeof(int)) == 0;
    }

    /// <summary>
    /// Takes a window back off Mica, undoing <see cref="EnableMica"/>: no material, and no
    /// frame extended into the client area for one to show through.
    /// </summary>
    /// <remarks>
    /// The title bar is still matched to the theme, which <see cref="EnableMica"/> otherwise
    /// does on every change of it.
    /// </remarks>
    public static void DisableMica(nint hwnd, bool dark)
    {
        var immersive = dark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref immersive, sizeof(int));

        var backdrop = DwmsbtNone;
        DwmSetWindowAttribute(hwnd, DwmwaSystemBackdropType, ref backdrop, sizeof(int));

        var none = default(Margins);
        DwmExtendFrameIntoClientArea(hwnd, ref none);
    }

    /// <summary>
    /// Lets a composition visual on this window sample the blurred desktop behind it.
    /// </summary>
    public static bool EnableHostBackdropSampling(nint hwnd) =>
        EnableAccentBlur(hwnd, AccentEnableHostBackdrop, tint: 0);

    private static bool EnableAccentBlur(nint hwnd, int state, uint tint)
    {
        var policy = new AccentPolicy
        {
            AccentState = state,
            AccentFlags = AccentAllBorders,
            GradientColor = tint
        };

        var size = Marshal.SizeOf<AccentPolicy>();
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(policy, buffer, fDeleteOld: false);

            var data = new WindowCompositionAttributeData
            {
                Attribute = WcaAccentPolicy,
                Data = buffer,
                SizeOfData = size
            };

            return SetWindowCompositionAttribute(hwnd, ref data) != 0;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>
    /// Asks DWM for its own rounded corners, roughly eight pixels. It will not take a
    /// radius, and there is no second way to shape the material.
    /// </summary>
    /// <remarks>
    /// <c>SetWindowRgn</c> is not it, which cost a while to establish: the region is
    /// accepted — <c>GetWindowRgnBox</c> reports the rounded shape back — but DWM composes
    /// the accent blur across the whole window rect regardless, in every combination of
    /// accent state and window frame. This whole path is therefore the fallback, and
    /// <see cref="CompositionBackdrop"/> — whose visual does take a clip — is preferred.
    /// </remarks>
    public static void SetRoundedCorners(nint hwnd, bool rounded)
    {
        var preference = rounded ? DwmwcpRound : DwmwcpDoNotRound;
        DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref preference, sizeof(int));
    }
}
