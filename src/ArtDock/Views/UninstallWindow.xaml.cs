using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using ArtDock.Interop;
using ArtDock.Localization;
using ArtDock.Services;

namespace ArtDock.Views;

/// <summary>
/// Asks, as ArtDock is uninstalled, whether its settings go too.
/// </summary>
/// <remarks>
/// <para>
/// Shown from inside Velopack's uninstaller — see <see cref="AppUpdater.OnUninstalling"/>. It
/// runs this executable once more with an argument of its own, before the dock or even the
/// application exists, and kills it if it has not finished within 30 seconds. So the question
/// answers itself as Keep a few seconds before that, and says so as it counts down; a kill in
/// the middle of it would keep them too, but without a word.
/// </para>
/// <para>
/// Keep is the answer anything short of a press of Delete comes to — Enter, Escape, the close
/// box, walking away, and anything at all going wrong on the way to asking. Deleting is the
/// one answer that cannot be taken back.
/// </para>
/// </remarks>
public sealed partial class UninstallWindow : Window
{
    /// <summary>
    /// When the question answers itself, as <see cref="Environment.TickCount64"/> — which a
    /// change of the clock cannot move, as it could move a wall-clock deadline.
    /// </summary>
    private readonly long _deadline;

    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };

    private UninstallWindow(TimeSpan timeLeft)
    {
        InitializeComponent();

        _deadline = Environment.TickCount64 + (long)timeLeft.TotalMilliseconds;
        ShowCountdown();

        _tick.Tick += (_, _) =>
        {
            if (SecondsLeft() <= 0)
            {
                Close();
                return;
            }

            ShowCountdown();
        };

        DeleteButton.Click += (_, _) => { DialogResult = true; };

        Loaded += (_, _) =>
        {
            // Started by Velopack's uninstaller rather than by the user, so Windows may not give
            // it the foreground of its own accord; see EditPinWindow.
            AppLauncher.Activate(new WindowInteropHelper(this).Handle);
            KeepButton.Focus();
            _tick.Start();
        };

        Closed += (_, _) => _tick.Stop();
    }

    /// <summary>
    /// Asks, in the user's language and appearance, and returns true only when the answer was
    /// to delete.
    /// </summary>
    /// <param name="timeLeft">
    /// How long there is before the question has to have answered itself.
    /// </param>
    /// <remarks>
    /// Makes the WPF application the dialog needs — the Fluent theme is the application's, and
    /// so are the strings XAML reads — which is safe only because the uninstaller's process
    /// never goes on to make the dock's own: Velopack exits it as soon as the hook returns.
    /// Every failure is caught and answered as Keep, whatever it is. A question that could not
    /// be asked has not been answered with Delete, and an uninstall is no place to crash.
    /// </remarks>
    public static bool AskToDeleteSettings(TimeSpan timeLeft)
    {
        // Too little left to read the question, let alone answer it.
        if (timeLeft < TimeSpan.FromSeconds(5))
        {
            return false;
        }

        try
        {
            var settings = new SettingsStore().Load();

            _ = new Application
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown,
                ThemeMode = AppTheme.Resolve(settings.Theme)
            };

            Localizer.Apply(settings.Language);

            return new UninstallWindow(timeLeft).ShowDialog() == true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private int SecondsLeft() =>
        (int)Math.Ceiling((_deadline - Environment.TickCount64) / 1000.0);

    private void ShowCountdown() =>
        CountdownText.Text = Localizer.Format("Uninstall.Countdown", (double)Math.Max(SecondsLeft(), 0));
}
