using System.Buffers.Binary;
using Microsoft.Win32;

namespace ArtDock.Interop;

/// <summary>
/// Reads what is pinned to Windows' taskbar, in the order the taskbar shows it.
/// </summary>
/// <remarks>
/// <para>
/// Not from the folder the taskbar keeps its shortcuts in,
/// <c>%APPDATA%\Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar</c>. That holds
/// only the pins that are shortcuts, in no order, and Store apps — Copilot, Media Player,
/// Spotify from the Store — pinned there have no shortcut at all. The taskbar's own list is
/// the <c>Favorites</c> value under <c>Explorer\Taskband</c>: every pin, as the shell's ID
/// list for it, in the taskbar's order.
/// </para>
/// <para>
/// The format is not documented. As read on Windows 11 (2026-10-03, <c>FavoritesVersion</c> 3):
/// a byte of <c>00</c>, then for each pin a 32-bit size and an absolute ID list of that many
/// bytes, its closing terminator counted, each followed by <c>00</c> — and the last by
/// <c>FF</c>. A shortcut's list runs through the <em>User Pinned</em> folder to the file; a
/// Store app's through <c>AppsFolder</c> to the app, whose parsing name is its app ID. Anything
/// that does not read that way is where the reading stops, with what was read so far.
/// </para>
/// </remarks>
public static class TaskbarFavorites
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Taskband";

    /// <summary>The taskbar's pins, left to right; empty when there are none or they cannot be read.</summary>
    public static IReadOnlyList<ShellIdList.Item> Read()
    {
        byte[]? blob;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
            blob = key?.GetValue("Favorites") as byte[];
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException)
        {
            return [];
        }

        return blob is null ? [] : [.. Split(blob).Select(ShellIdList.ReadOne).OfType<ShellIdList.Item>()];
    }

    /// <summary>
    /// The ID lists in a <c>Favorites</c> value, each walked against the value's length and
    /// against its own: one whose items run past its stated size ends the reading.
    /// </summary>
    public static IReadOnlyList<byte[]> Split(byte[] blob)
    {
        var lists = new List<byte[]>();
        var at = 0L;
        while (at < blob.Length && blob[at] == 0x00)
        {
            at++;
            if (at + sizeof(uint) > blob.Length)
            {
                break;
            }

            var size = BinaryPrimitives.ReadUInt32LittleEndian(blob.AsSpan((int)at));
            at += sizeof(uint);

            // At least a terminator, and no further than the value goes.
            if (size < sizeof(ushort) || at + size > blob.Length)
            {
                break;
            }

            var list = blob.AsSpan((int)at, (int)size).ToArray();
            if (!ShellIdList.IsIdList(list, 0))
            {
                break;
            }

            lists.Add(list);
            at += size;
        }

        return lists;
    }
}
