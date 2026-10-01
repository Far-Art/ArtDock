using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace ArtDock.Services;

/// <summary>
/// Has WPF draw every window of the dock in software, for <see cref="DockSettings.NoGpu"/>.
/// </summary>
/// <remarks>
/// <para>
/// Two switches, because neither covers everything. <see cref="RenderOptions.ProcessRenderMode"/>
/// is the process's preference, and what every window made from then on is drawn by — the
/// menus, the tooltips, a dialog opened later. A window that is already up has a render target
/// of its own, and <see cref="HwndTarget.RenderMode"/> is the documented way to move one of
/// those: so the dock, the handle and the settings dialog whose checkbox this is are each told.
/// </para>
/// <para>
/// A machine with no graphics card draws in software whatever is asked — WPF falls back by
/// itself. Asking outright is still worth it there: no Direct3D device is made at all, which
/// was measured at about 15 MB of the dock's memory on a machine that has one, and nothing is
/// left to a display driver that is only standing in for one.
/// </para>
/// </remarks>
internal static class SoftwareRendering
{
    /// <summary>
    /// Turns software rendering on or off, for the windows that are up and the ones to come.
    /// </summary>
    /// <remarks>
    /// Only on a change: this is called wherever the settings are applied, and that is every
    /// tick of every slider in the settings dialog.
    /// </remarks>
    /// <returns>True when it changed anything.</returns>
    public static bool Apply(bool on)
    {
        var mode = on ? RenderMode.SoftwareOnly : RenderMode.Default;
        if (RenderOptions.ProcessRenderMode == mode)
        {
            return false;
        }

        RenderOptions.ProcessRenderMode = mode;

        foreach (PresentationSource source in PresentationSource.CurrentSources)
        {
            if (source is HwndSource { IsDisposed: false, CompositionTarget: { } target })
            {
                target.RenderMode = mode;
            }
        }

        return true;
    }

    /// <summary>Whether software rendering is on, as last asked for.</summary>
    public static bool IsOn => RenderOptions.ProcessRenderMode == RenderMode.SoftwareOnly;

    /// <summary>
    /// True when WPF reports that it has no hardware to draw with here, whatever it has been
    /// asked to use.
    /// </summary>
    /// <remarks>
    /// The render tier is the hardware's, not the process's: measured on 2026-10-01, it read 2
    /// before software rendering was forced, while it was, and after. So it can be asked at any
    /// time, with <see cref="DockSettings.NoGpu"/> on or off. The tier is in the high word.
    /// </remarks>
    public static bool HardwareMissing => RenderCapability.Tier >> 16 == 0;
}
