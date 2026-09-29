using ArtDock.Services;
using Velopack;

namespace ArtDock;

/// <summary>
/// The entry point, in place of the one WPF generates from <c>App.xaml</c>.
/// </summary>
/// <remarks>
/// Written out so that Velopack sees the process before anything else does. Its setup, updater
/// and uninstaller start this executable with arguments of their own, expect it to do what they
/// ask and exit within seconds, and must never meet a dock — nor the single-instance claim in
/// <see cref="App"/>, which would send them to the dock already running instead. A launch that
/// is none of those returns straight away and carries on as it always has, from a build run
/// out of the source tree too.
/// </remarks>
public static class Program
{
    [STAThread]
    public static void Main()
    {
        // Here rather than in AppUpdater, because vpk checks, when it packages a release, that
        // Main is where this call is — and warns about anywhere else.
        VelopackApp.Build()
            .OnAfterInstallFastCallback(AppUpdater.OnInstalled)
            .OnBeforeUninstallFastCallback(AppUpdater.OnUninstalling)
            .Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
