using System.Runtime.InteropServices;

namespace ArtDock.Interop;

/// <summary>
/// Windows' own peek, as the taskbar's previews do it: one window shown on the desktop as it is,
/// every other faded to a sheet of glass — and back.
/// </summary>
/// <remarks>
/// <para>
/// <b>Not public.</b> DWM does it for the taskbar through <c>DwmpActivateLivePreview</c>, which
/// <c>dwmapi.dll</c> exports by number only (113), and which Microsoft has never documented. Left
/// out on purpose until 2026-11-20 for that reason (TODO.md item 25), and asked for then, with
/// one condition: <b>if it fails, it does nothing.</b> So it is looked up by number once, and the
/// first time it is missing or returns a failure it is never called again for the life of the
/// process — the previews go on without a peek. The signature is the one Windows has used since
/// 8.1 — on, the window to show, a window to keep over the glass, the kind of peek, and a value
/// Windows 10 added — and the kind is the taskbar thumbnail's (3).
/// </para>
/// <para>
/// What else stays out of the peek is the documented half: <c>DWMWA_EXCLUDED_FROM_PEEK</c>, set on
/// the dock's own windows for as long as a peek lasts (<see cref="Exclude"/>), so the dock stays
/// on the screen as the taskbar does. Only while it lasts: set for good, the dock would also stay
/// up through the taskbar's own peeks and its *show desktop*.
/// </para>
/// </remarks>
internal static class LivePreview
{
    private const int Ordinal = 113;

    /// <summary>The kind of peek a taskbar thumbnail makes, as against the desktop's (1) or Alt+Tab's (2).</summary>
    private const uint TaskbarThumbnail = 3;

    private const int DwmwaExcludedFromPeek = 12;

    private static nint _function;
    private static bool _looked;
    private static bool _broken;

    /// <summary>Whether a peek is showing.</summary>
    public static bool IsActive { get; private set; }

    /// <summary>
    /// Whether Windows' own peek is on — <c>EnableAeroPeek</c> under the current user's DWM key,
    /// which the taskbar's previews follow and which is on unless someone has turned it off — and
    /// this process has not found the peek failing.
    /// </summary>
    public static bool IsAllowed()
    {
        if (_broken)
        {
            return false;
        }

        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
            return key?.GetValue("EnableAeroPeek") is not int value || value != 0;
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException)
        {
            return true;
        }
    }

    /// <summary>
    /// Shows <paramref name="window"/> alone, everything else faded, with <paramref name="above"/>
    /// kept over it; false — and no peek, now or later — when Windows will not.
    /// </summary>
    public static unsafe bool Show(nint window, nint above)
    {
        if (window == 0 || Function() is not { } call)
        {
            return false;
        }

        if (!Call(call, true, window, above))
        {
            return false;
        }

        IsActive = true;
        return true;
    }

    /// <summary>Ends a peek, if one is showing: every window back as it was.</summary>
    public static unsafe void End()
    {
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        if (Function() is { } call)
        {
            Call(call, false, 0, 0);
        }
    }

    /// <summary>
    /// Keeps windows out of a peek, or lets them back in: shown as they are, over the glass, as
    /// the taskbar is. Documented; a window it fails on simply fades with the rest.
    /// </summary>
    public static void Exclude(ReadOnlySpan<nint> windows, bool excluded)
    {
        var value = excluded ? 1 : 0;
        foreach (var window in windows)
        {
            if (window != 0)
            {
                DwmSetWindowAttribute(window, DwmwaExcludedFromPeek, ref value, sizeof(int));
            }
        }
    }

    private static unsafe bool Call(nint function, bool on, nint window, nint above)
    {
        try
        {
            var peek = (delegate* unmanaged[Stdcall]<int, nint, nint, uint, nuint, int>)function;
            if (peek(on ? 1 : 0, window, above, TaskbarThumbnail, 0) >= 0)
            {
                return true;
            }
        }
        catch (Exception e) when (e is SEHException or AccessViolationException or InvalidOperationException)
        {
            // Not expected to be caught — but where it is, the same as a failure.
        }

        _broken = true;
        IsActive = false;
        return false;
    }

    /// <summary>The function, looked up once; null when it is not there or has failed before.</summary>
    private static nint? Function()
    {
        if (_broken)
        {
            return null;
        }

        if (!_looked)
        {
            _looked = true;
            try
            {
                var module = NativeLibrary.Load("dwmapi.dll");
                _function = GetProcAddress(module, Ordinal);
            }
            catch (Exception e) when (e is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
            {
                _function = 0;
            }

            if (_function == 0)
            {
                _broken = true;
                return null;
            }
        }

        return _function;
    }

    [DllImport("kernel32.dll", EntryPoint = "GetProcAddress")]
    private static extern nint GetProcAddress(nint module, nint ordinal);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
}
