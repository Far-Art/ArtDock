using System.Collections;
using System.Runtime.InteropServices;

namespace ArtDock.Interop;

/// <summary>
/// Gives the dock the environment the user signed in with, whoever started it — so that what it
/// launches gets that environment too, as from the taskbar.
/// </summary>
/// <remarks>
/// <para>
/// Every program the dock starts inherits the dock's environment: <c>ShellExecute</c>, which
/// <see cref="AppLauncher"/> goes through, takes no environment of its own. A dock started from
/// a terminal, an IDE or a tool's shell carries that shell's variables into every app it opens.
/// Found 2026-10-03: a dock restarted from a Claude Code session passed on its fifty-odd
/// variables — <c>GIT_EDITOR=true</c>, <c>GIT_TERMINAL_PROMPT=0</c>,
/// <c>NoDefaultCurrentDirectoryInExePath=1</c>, the account's email and a token among them —
/// to every app it opened. It was suspected of a failure of Rider's that turned out to have
/// another cause; it is fixed for what it is.
/// </para>
/// <para>
/// So the dock replaces its own environment, once, as it starts, with the one Windows builds
/// for the user — <c>CreateEnvironmentBlock</c>, from the system's and the user's variables,
/// not inheriting the caller's. Measured that day against Explorer's, read out of a dock it had
/// started: the same 49 variables at the same values, but for Explorer's
/// <c>__COMPAT_LAYER</c>, which it puts on each program it starts and which belongs to the dock
/// rather than to what the dock starts, and <c>SESSIONNAME</c>, which the block has. Read at
/// start, it also has any variable changed since sign-in, as Explorer would after the change
/// was announced. A change made while the dock runs is not heard: the announcement is a
/// broadcast, and broadcasts do not reach this app's windows.
/// </para>
/// <para>
/// The runtime has read what it needs by then (<c>DOTNET_*</c> are read as the process
/// starts), and nothing in the dock reads its environment afterwards for anything but paths
/// the block carries alike. If the block cannot be had, the environment is left as it was.
/// </para>
/// </remarks>
public static class SignInEnvironment
{
    /// <summary>Replaces this process's environment with the user's own, when it can be read.</summary>
    /// <returns>True when it was replaced.</returns>
    public static bool Adopt()
    {
        if (Read() is not { Count: > 0 } fresh)
        {
            return false;
        }

        var (remove, set) = Changes(Current(), fresh);
        foreach (var name in remove)
        {
            Environment.SetEnvironmentVariable(name, null);
        }

        foreach (var (name, value) in set)
        {
            Environment.SetEnvironmentVariable(name, value);
        }

        return true;
    }

    /// <summary>
    /// What turns one environment into another: the names to take out, and the values to set.
    /// </summary>
    /// <remarks>
    /// Names compare as Windows compares them, without case. A name starting with <c>=</c> is
    /// one of the hidden per-drive current directories (<c>=C:</c>), which is the process's own
    /// state rather than a variable, and is left alone either way.
    /// </remarks>
    public static (IReadOnlyList<string> Remove, IReadOnlyList<(string Name, string Value)> Set) Changes(
        IReadOnlyDictionary<string, string> current, IReadOnlyDictionary<string, string> wanted)
    {
        var wantedByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in wanted)
        {
            if (!IsHidden(name))
            {
                wantedByName[name] = value;
            }
        }

        var currentByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in current)
        {
            if (!IsHidden(name))
            {
                currentByName[name] = value;
            }
        }

        var remove = currentByName.Keys.Where(name => !wantedByName.ContainsKey(name)).ToList();
        var set = wantedByName
            .Where(pair => !currentByName.TryGetValue(pair.Key, out var had) || !string.Equals(had, pair.Value, StringComparison.Ordinal))
            .Select(pair => (pair.Key, pair.Value))
            .ToList();

        return (remove, set);
    }

    private static bool IsHidden(string name) => name.Length == 0 || name[0] == '=';

    private static Dictionary<string, string> Current()
    {
        var current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string name && entry.Value is string value)
            {
                current[name] = value;
            }
        }

        return current;
    }

    /// <summary>The environment Windows builds for this process's user, or null when it cannot.</summary>
    public static Dictionary<string, string>? Read()
    {
        if (!OpenProcessToken(GetCurrentProcess(), TokenQuery | TokenDuplicate | TokenImpersonate, out var token))
        {
            return null;
        }

        try
        {
            if (!CreateEnvironmentBlock(out var block, token, inherit: false))
            {
                return null;
            }

            try
            {
                var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                // NAME=value strings end to end, each closed by a null, the whole by another.
                var next = block;
                while (Marshal.PtrToStringUni(next) is { Length: > 0 } entry)
                {
                    var equals = entry.IndexOf('=', 1);
                    if (equals > 0)
                    {
                        variables[entry[..equals]] = entry[(equals + 1)..];
                    }

                    next += (entry.Length + 1) * sizeof(char);
                }

                return variables;
            }
            finally
            {
                DestroyEnvironmentBlock(block);
            }
        }
        finally
        {
            CloseHandle(token);
        }
    }

    private const int TokenDuplicate = 0x0002;
    private const int TokenImpersonate = 0x0004;
    private const int TokenQuery = 0x0008;

    [DllImport("kernel32.dll")]
    private static extern nint GetCurrentProcess();

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(nint process, int access, out nint token);

    [DllImport("userenv.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateEnvironmentBlock(out nint environment, nint token, [MarshalAs(UnmanagedType.Bool)] bool inherit);

    [DllImport("userenv.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyEnvironmentBlock(nint environment);
}
