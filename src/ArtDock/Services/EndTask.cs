using System.IO;
using System.Runtime.InteropServices;
using ArtDock.Interop;

namespace ArtDock.Services;

/// <summary>
/// <em>End task</em>, as the taskbar has it: the processes behind an app's open windows, ended
/// at once, without asking them.
/// </summary>
/// <remarks>
/// <para>
/// Only what the app's windows lead to, and never a process the desktop itself runs on. File
/// Explorer's windows belong to <c>explorer.exe</c>, which is also the taskbar and the desktop;
/// a Store app's frame belongs to <c>ApplicationFrameHost.exe</c>, which frames every such app
/// at once. So a frame is followed to the app inside it, and the shell's own processes are
/// spared whatever leads to them. The dock does not end itself either.
/// </para>
/// <para>
/// The dock asks first (<c>Views/ConfirmEndTaskWindow</c>), and looks the processes up again
/// after the answer: what was open when the menu opened may have closed since.
/// </para>
/// </remarks>
public static class EndTask
{
    /// <summary>A process to end, with one of its windows to judge it by.</summary>
    public readonly record struct Target(uint ProcessId, nint Window);

    /// <summary>The programs ending would take the desktop down with them, by file name.</summary>
    private static readonly HashSet<string> Spared = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer.exe",
        "ApplicationFrameHost.exe",
        "dwm.exe",
        "csrss.exe",
        "winlogon.exe",
        "sihost.exe",
        "ShellExperienceHost.exe",
        "StartMenuExperienceHost.exe",
        "SearchHost.exe",
        "TextInputHost.exe",
        "ShellHost.exe"
    };

    /// <summary>
    /// Whether a process is never to be ended from the dock: one of the shell's own, the dock
    /// itself, or one whose program cannot be read, and so cannot be told apart from those.
    /// </summary>
    /// <param name="programPath">The process's program, or null when it could not be read.</param>
    /// <param name="processId">The process.</param>
    /// <param name="ownProcessId">The dock's own process.</param>
    public static bool IsSpared(string? programPath, uint processId, uint ownProcessId) =>
        processId == 0
        || processId == ownProcessId
        || string.IsNullOrEmpty(programPath)
        || Spared.Contains(Path.GetFileName(programPath));

    /// <summary>The processes behind some windows that <em>End task</em> would end, each once.</summary>
    public static IReadOnlyList<Target> TargetsOf(IEnumerable<nint> windows)
    {
        var own = (uint)Environment.ProcessId;
        var targets = new List<Target>();
        foreach (var window in windows)
        {
            var judged = window;
            WindowsApi.GetWindowThreadProcessId(judged, out var processId);
            var program = WindowsApi.TryGetProcessPath(processId);

            // A Store app's frame: the app is the process of the window inside it. A frame with
            // none inside is an app Windows has suspended or not yet started, and there is
            // nothing of it to end.
            if (program is not null
                && Path.GetFileName(program).Equals("ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase))
            {
                judged = FindWindowEx(window, 0, "Windows.UI.Core.CoreWindow", null);
                if (judged == 0)
                {
                    continue;
                }

                WindowsApi.GetWindowThreadProcessId(judged, out processId);
                program = WindowsApi.TryGetProcessPath(processId);
            }

            if (!IsSpared(program, processId, own) && !targets.Any(target => target.ProcessId == processId))
            {
                targets.Add(new Target(processId, judged));
            }
        }

        return targets;
    }

    /// <summary>
    /// Whether the dock can end any of them: Windows does not let it end a program running as
    /// administrator, above its own level.
    /// </summary>
    public static bool CanEndAny(IReadOnlyList<Target> targets) =>
        targets.Any(target => !WindowsApi.IsElevated(target.Window));

    /// <summary>Ends each process it is allowed to.</summary>
    /// <returns>How many were ended.</returns>
    public static int End(IReadOnlyList<Target> targets)
    {
        var ended = 0;
        foreach (var target in targets)
        {
            if (WindowsApi.IsElevated(target.Window))
            {
                continue;
            }

            var process = WindowsApi.OpenProcess(ProcessTerminate, false, target.ProcessId);
            if (process == 0)
            {
                continue;
            }

            try
            {
                // 1, as Task Manager's End task gives.
                if (TerminateProcess(process, 1))
                {
                    ended++;
                }
            }
            finally
            {
                WindowsApi.CloseHandle(process);
            }
        }

        return ended;
    }

    private const uint ProcessTerminate = 0x0001;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateProcess(nint process, uint exitCode);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint FindWindowEx(nint parent, nint after, string? className, string? title);
}
