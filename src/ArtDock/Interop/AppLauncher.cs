using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using ArtDock.Dock;

namespace ArtDock.Interop;

/// <summary>Starts pinned applications and brings already-running ones forward.</summary>
public static class AppLauncher
{
    /// <summary>
    /// Launches a pinned item.
    /// </summary>
    /// <remarks>
    /// Win32 targets go through the shell (<c>UseShellExecute</c>) rather than a direct
    /// process start, so shortcuts, file associations, the app's own working directory and
    /// any elevation prompt all behave as they would from Explorer. Store apps are activated
    /// by AUMID through the same <c>shell:AppsFolder</c> path Explorer uses.
    /// </remarks>
    public static bool Launch(DockItem item)
    {
        var target = item.ShellTarget;
        if (string.IsNullOrWhiteSpace(target))
        {
            return false;
        }

        // Before the shell sees it. A command is not a thing that can be opened, and
        // ShellExecute would only fail on the scheme.
        if (Services.DockCommands.IsCommand(target))
        {
            return Services.DockCommands.Run(target);
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = target,
                UseShellExecute = true
            };

            // Give the app its own directory so relative paths inside it resolve. Checked
            // rather than assumed: a target can be a web address now that the edit dialog
            // takes free text, and GetDirectoryName happily returns "https:/" for one.
            if (item.TargetPath is { Length: > 0 } path
                && Path.GetDirectoryName(path) is { Length: > 0 } dir
                && Directory.Exists(dir))
            {
                startInfo.WorkingDirectory = dir;
            }

            Process.Start(startInfo);
            return true;
        }
        catch (Win32Exception)
        {
            // Missing target, or the user dismissed the UAC prompt.
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>
    /// Brings <paramref name="hwnd"/> to the foreground.
    /// </summary>
    /// <remarks>
    /// Windows only lets the foreground process hand focus away, so a bare
    /// <c>SetForegroundWindow</c> from a background dock is silently downgraded to a taskbar
    /// flash. Attaching our input queue to the current foreground thread for the duration of
    /// the call is the long-standing way around that; the dock's own
    /// <c>WS_EX_NOACTIVATE</c> keeps it from becoming the foreground itself first.
    /// </remarks>
    public static bool Activate(nint hwnd)
    {
        if (hwnd == 0)
        {
            return false;
        }

        if (WindowsApi.IsIconic(hwnd))
        {
            WindowsApi.ShowWindow(hwnd, WindowsApi.SW_RESTORE);
        }

        var foreground = WindowsApi.GetForegroundWindow();
        if (foreground == hwnd)
        {
            return true;
        }

        var currentThread = WindowsApi.GetCurrentThreadId();
        var foregroundThread = foreground == 0
            ? 0
            : WindowsApi.GetWindowThreadProcessId(foreground, out _);

        var attached = foregroundThread != 0
            && foregroundThread != currentThread
            && WindowsApi.AttachThreadInput(currentThread, foregroundThread, true);

        try
        {
            return WindowsApi.SetForegroundWindow(hwnd);
        }
        finally
        {
            if (attached)
            {
                WindowsApi.AttachThreadInput(currentThread, foregroundThread, false);
            }
        }
    }
}
