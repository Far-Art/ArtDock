using System.Windows;

namespace ArtDock.Views;

/// <summary>What to do with the settings dialog's unsaved changes before an update.</summary>
public enum UnsavedChoice
{
    Save,
    Discard
}

/// <summary>
/// Asks, before an update closes the settings dialog, whether to save what has been changed in
/// it and not yet saved.
/// </summary>
/// <remarks>
/// <para>
/// The update closes the dock and starts the new version with the dialog open again, and a
/// dialog closed without Save is a Cancel: everything changed in it would go, unannounced, on
/// the way to a dialog that looks as if nothing had happened. Asked only when there is
/// something to lose (<see cref="ArtDock.Services.SettingsFingerprint"/>), and before anything is
/// downloaded, so that the answer is given while the user is still at the dialog rather than
/// some seconds later.
/// </para>
/// <para>
/// Only the update asks. The About page's Exit sends unsaved changes the way Cancel does, as it
/// always has, and says so on its card.
/// </para>
/// </remarks>
public sealed partial class UnsavedChangesWindow : Window
{
    private UnsavedChoice? _choice;

    private UnsavedChangesWindow()
    {
        InitializeComponent();

        SaveButton.Click += (_, _) => Choose(UnsavedChoice.Save);
        DiscardButton.Click += (_, _) => Choose(UnsavedChoice.Discard);
        Loaded += (_, _) => SaveButton.Focus();
    }

    /// <summary>Asks over <paramref name="owner"/>, and waits for the answer.</summary>
    /// <returns>Save or discard, or null when the user would rather not update after all.</returns>
    public static UnsavedChoice? Ask(Window owner)
    {
        var dialog = new UnsavedChangesWindow { Owner = owner };
        return dialog.ShowDialog() == true ? dialog._choice : null;
    }

    private void Choose(UnsavedChoice choice)
    {
        _choice = choice;
        DialogResult = true;
    }
}
