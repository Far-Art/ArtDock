using System.Runtime.InteropServices;

namespace ArtDock.Interop;

/// <summary>
/// The Recycle Bin, for the pin that points at it.
/// </summary>
/// <remarks>
/// Only what the dock's menu needs: whether there is anything in there, and emptying it.
/// </remarks>
public static class RecycleBin
{
    /// <summary>
    /// Whether the Recycle Bin holds nothing, across every drive.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Asked so the menu entry can be greyed when there is nothing to empty, which is what
    /// Explorer's own menu does. Null root means every drive's bin rather than one — the pin
    /// is the Recycle Bin as a whole, so the count has to be too.
    /// </para>
    /// <para>
    /// It costs about 70ms cold on a machine with eight volumes, which is why nothing else
    /// asks: this runs once, on a right-click, on one item. Anything on the wave's path
    /// could not afford it.
    /// </para>
    /// <para>
    /// A failure answers false — offer the entry rather than grey it. An action that turns
    /// out to have nothing to do is a smaller wrong than an entry greyed out over a
    /// question we could not answer.
    /// </para>
    /// </remarks>
    public static bool IsEmpty()
    {
        var info = new RecycleBinInfo { cbSize = (uint)Marshal.SizeOf<RecycleBinInfo>() };

        // S_OK and zero items. The size field must be the struct's natural size — 24 bytes,
        // with the padding after cbSize that x64 alignment inserts. Packed to 20 it is
        // refused with E_INVALIDARG, which is a plausible-looking "the bin is empty" if the
        // result is not checked. Measured both ways against the shell's own item count.
        return SHQueryRecycleBin(null, ref info) == 0 && info.i64NumItems == 0;
    }

    /// <summary>
    /// Empties the Recycle Bin, asking first if that is what Windows would do.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>No <c>SHERB_NOCONFIRMATION</c>, deliberately.</b> That flag exists to override the
    /// shell's own judgement about whether to ask; without it the shell decides, which means
    /// it consults the same <em>Display delete confirmation dialog</em> setting Explorer's
    /// own <em>Empty Recycle Bin</em> consults. So the way to respect that checkbox is to
    /// pass nothing and let Windows answer. Reading the setting out of the registry and
    /// passing the flag ourselves would have been worse in three ways: the value
    /// (<c>ConfirmDelete</c> under <c>Explorer\BitBucket</c>) does not exist until the user
    /// changes it, so its default would have to be guessed; a guess wrong in one direction
    /// silently deletes without asking; and it would stop tracking Windows the moment
    /// Windows changed its mind.
    /// </para>
    /// <para>
    /// Progress and sound are left on for the same reason — the flags that turn them off are
    /// overrides too, and this should look like the shell doing it, because it is.
    /// </para>
    /// <para>
    /// Run on a thread of its own, because the call does not return until the confirmation
    /// has been answered and the progress dialog has finished. On the dock's thread that is
    /// the wave frozen for as long as the user takes to read the question. STA because it is
    /// shell UI, and background so it cannot hold the process open.
    /// </para>
    /// <para>
    /// The owner is the dock's own window rather than null. It is
    /// <c>WS_EX_NOACTIVATE</c>, which is not a problem for a dialog it merely owns — the
    /// shell's dialog is its own window — and it puts the question on screen next to the
    /// icon that asked it instead of wherever the desktop would have put it.
    /// </para>
    /// <para>
    /// <b>Followed by <c>SHUpdateRecycleBinIcon</c>, because nothing else will put the icon
    /// right.</b> Emptying the bin changes what is in it; the full-or-empty icon is separate
    /// state that the shell flips and announces, and a bin emptied without the flip stays
    /// drawn full. Measured by removing a file of the probe's own from the bin behind the
    /// shell's back: four seconds later the bin held nothing and every process on the
    /// machine, a fresh one included, still drew it full — Explorer does not notice. That
    /// fits what the dock saw after this call, where re-reading the icon the moment it
    /// returned found a full bin, and it is the fix another file manager landed on for the
    /// same call. Whether <c>SHEmptyRecycleBin</c> flips the icon itself was not measured —
    /// that means emptying a real bin — and does not need to be: the update does nothing,
    /// and announces nothing, when the icon is already right.
    /// </para>
    /// <para>
    /// The update returns before it has done anything; the flip lands about 40ms later on a
    /// thread of the shell's own, and was measured to land with this thread already gone and
    /// no thread in the process pumping messages. So there is nothing to wait for here, and
    /// no cost to the dock's thread. What the dock does about the new icon is
    /// <see cref="RecycleBinWatch"/>'s business, the same as after an empty made in Explorer.
    /// </para>
    /// </remarks>
    /// <param name="owner">Window to own the confirmation and progress dialogs.</param>
    public static void EmptyAsync(nint owner)
    {
        var thread = new Thread(() =>
        {
            try
            {
                SHEmptyRecycleBin(owner, null, 0);

                // Whatever the empty did, a declined confirmation and one cancelled part way
                // through included: this reads what is actually in the bin.
                SHUpdateRecycleBinIcon();
            }
            catch (Exception e) when (e is COMException or EntryPointNotFoundException)
            {
                // Nothing to say to the user that the shell has not already said.
            }
        })
        {
            IsBackground = true,
            Name = "ArtDock.EmptyRecycleBin"
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RecycleBinInfo
    {
        public uint cbSize;
        public long i64Size;
        public long i64NumItems;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHQueryRecycleBinW")]
    private static extern int SHQueryRecycleBin(string? pszRootPath, ref RecycleBinInfo pSHQueryRBInfo);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHEmptyRecycleBinW")]
    private static extern int SHEmptyRecycleBin(nint hwnd, string? pszRootPath, uint dwFlags);

    [DllImport("shell32.dll")]
    private static extern void SHUpdateRecycleBinIcon();
}
