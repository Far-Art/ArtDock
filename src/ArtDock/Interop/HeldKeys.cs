namespace ArtDock.Interop;

/// <summary>
/// Which of the keys the dock cares about are held down this moment, whichever window has the
/// keyboard: the Windows key and Ctrl, held for the items' numbers, and the few keys that end
/// such a hold.
/// </summary>
/// <remarks>
/// <para>
/// Asked of Windows' own record of the keys (<c>GetAsyncKeyState</c>), some thirty times a second,
/// on the look the dock already takes at the pointer — not heard, as a keyboard hook would hear
/// them. The dock has none, for the reason it has no mouse hook: a low-level hook sits in every
/// program's input. Ctrl and the two Windows keys are asked about, and six more only while those
/// are down; no other key is asked about, and nothing is recorded.
/// </para>
/// <para>
/// Nothing is read on the secure desktop — the lock screen, an elevation prompt — where Windows
/// answers that no key is down.
/// </para>
/// </remarks>
internal static class HeldKeys
{
    private const int VK_SHIFT = 0x10;
    private const int VK_CONTROL = 0x11;
    private const int VK_MENU = 0x12;
    private const int VK_LEFT = 0x25;
    private const int VK_UP = 0x26;
    private const int VK_RIGHT = 0x27;
    private const int VK_DOWN = 0x28;
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;

    /// <summary>True while either Windows key and either Ctrl are both held down.</summary>
    /// <remarks>Ctrl first, which is up nearly always, so the usual look asks one key.</remarks>
    public static bool WinAndCtrl() => IsDown(VK_CONTROL) && (IsDown(VK_LWIN) || IsDown(VK_RWIN));

    /// <summary>
    /// True while a key is held that makes the Windows key and Ctrl the start of something else:
    /// Shift or Alt, another family's, or an arrow — Win+Ctrl and Left or Right is Windows'
    /// switch between virtual desktops.
    /// </summary>
    public static bool AnythingElse() =>
        IsDown(VK_SHIFT) || IsDown(VK_MENU)
        || IsDown(VK_LEFT) || IsDown(VK_RIGHT) || IsDown(VK_UP) || IsDown(VK_DOWN);

    private static bool IsDown(int key) => WindowsApi.GetAsyncKeyState(key) < 0;
}
