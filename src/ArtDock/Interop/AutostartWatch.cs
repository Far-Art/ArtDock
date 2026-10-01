using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace ArtDock.Interop;

/// <summary>
/// Hears the dock's autostart entry change, wherever the change was made — Task Manager, the
/// Settings app's startup page, another copy of the dock.
/// </summary>
/// <remarks>
/// <para>
/// For the settings dialog, whose checkbox reads the registry when it opens and would
/// otherwise go on showing that while the user switches the dock off in Windows beside it.
/// Asked for on 2026-10-01.
/// </para>
/// <para>
/// <c>RegNotifyChangeKeyValue</c> on the two keys <see cref="Autostart"/> reads, rather than a
/// timer: nothing is read until something is written. A registration is good for one change,
/// so it is made again before anything is read — a change landing between the two is then
/// either in what is read or signalled again. <c>REG_NOTIFY_THREAD_AGNOSTIC</c>, because it is
/// made again on a pool thread, which may be gone by the time anything changes, and without it
/// a registration goes with the thread that made it.
/// </para>
/// <para>
/// The notice says only that a value under the key changed — any program's, under Run.
/// <see cref="Changed"/> is raised for all of them; the dialog reads the dock's entry, and
/// changes nothing when it reads as it did.
/// </para>
/// </remarks>
public sealed class AutostartWatch : IDisposable
{
    private const int RegNotifyChangeLastSet = 0x0000_0004;
    private const int RegNotifyThreadAgnostic = 0x1000_0000;

    private readonly KeyWatch[] _watches;

    /// <summary>Watches the dock's entry under <see cref="Registry.CurrentUser"/>.</summary>
    public AutostartWatch() : this(Registry.CurrentUser)
    {
    }

    /// <summary>
    /// Watches the two keys under <paramref name="root"/>, which is <c>HKCU</c> but for the
    /// tests. A key that cannot be watched is left out; the other is still heard.
    /// </summary>
    public AutostartWatch(RegistryKey root)
    {
        _watches =
        [
            .. new[] { Autostart.RunKeyPath, Autostart.ApprovedKeyPath }
                .Select(path => KeyWatch.Start(root, path, OnChanged))
                .OfType<KeyWatch>()
        ];
    }

    /// <summary>Raised on a pool thread when either key changes.</summary>
    public event EventHandler? Changed;

    public void Dispose()
    {
        foreach (var watch in _watches)
        {
            watch.Dispose();
        }
    }

    private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);

    /// <summary>One key, its event, and the pool's wait on it.</summary>
    private sealed class KeyWatch : IDisposable
    {
        private readonly RegistryKey _key;
        private readonly AutoResetEvent _signal = new(false);
        private readonly Action _changed;
        private readonly RegisteredWaitHandle _wait;
        private volatile bool _disposed;

        private KeyWatch(RegistryKey key, Action changed)
        {
            _key = key;
            _changed = changed;
            Arm();
            _wait = ThreadPool.RegisterWaitForSingleObject(
                _signal, (_, _) => OnSignalled(), null, Timeout.Infinite, executeOnlyOnce: false);
        }

        /// <summary>
        /// Null when the key can be neither opened nor made, or will not be watched. The
        /// approved key is made if it is missing, as Windows makes it at the first switch in
        /// Task Manager: an empty key, which changes nothing.
        /// </summary>
        public static KeyWatch? Start(RegistryKey root, string path, Action changed)
        {
            RegistryKey? key = null;
            try
            {
                key = root.OpenSubKey(path) ?? root.CreateSubKey(path, writable: false);
                return key is null ? null : new KeyWatch(key, changed);
            }
            catch (Exception e) when (e is Win32Exception or UnauthorizedAccessException
                or System.Security.SecurityException or IOException)
            {
                key?.Dispose();
                return null;
            }
        }

        public void Dispose()
        {
            _disposed = true;

            // The wait first, and until a callback under way has finished: closing the key
            // signals the event, and must find nobody left to answer it.
            using (var unregistered = new ManualResetEvent(false))
            {
                if (_wait.Unregister(unregistered))
                {
                    unregistered.WaitOne();
                }
            }

            _key.Dispose();
            _signal.Dispose();
        }

        private void Arm()
        {
            var error = RegNotifyChangeKeyValue(
                _key.Handle,
                watchSubtree: false,
                RegNotifyChangeLastSet | RegNotifyThreadAgnostic,
                _signal.SafeWaitHandle,
                asynchronous: true);

            if (error != 0)
            {
                throw new Win32Exception(error);
            }
        }

        private void OnSignalled()
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                Arm();
            }
            catch (Win32Exception)
            {
                // This change is still told; only the ones after it go unheard.
            }

            _changed();
        }

        [DllImport("advapi32.dll")]
        private static extern int RegNotifyChangeKeyValue(
            SafeRegistryHandle key,
            [MarshalAs(UnmanagedType.Bool)] bool watchSubtree,
            int notifyFilter,
            SafeWaitHandle eventHandle,
            [MarshalAs(UnmanagedType.Bool)] bool asynchronous);
    }
}
