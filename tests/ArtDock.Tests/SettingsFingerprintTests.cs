using ArtDock.Services;

namespace ArtDock.Tests;

/// <summary>
/// Covers telling whether the settings dialog has anything unsaved, which an update asks about
/// before it closes the dialog.
/// </summary>
/// <remarks>
/// <para>
/// Asking when there is nothing to lose would be asked on every update, so what changes in the
/// dialog without the user changing anything must not count: which page is open, <em>Run at
/// login</em> (compared with Windows instead), and the pinned items while the list has not been
/// edited, since until then the dialog follows the dock's own changes to them. Anything the
/// user does change must count — a slider, and a pin once the list has been edited.
/// </para>
/// <para>
/// The fingerprint is taken of a copy: the dialog's settings are not changed by being looked at.
/// </para>
/// </remarks>
public class SettingsFingerprintTests
{
    private static DockSettings Settings() => new()
    {
        BaseSize = 48,
        SettingsPage = 2,
        RunAtLogin = true,
        PinnedApps = [new PinnedAppSetting { Id = "a", Label = "Notepad", TargetPath = @"C:\Windows\notepad.exe" }]
    };

    [Fact]
    public void TheSameSettings_HaveTheSameFingerprint()
    {
        Assert.Equal(SettingsFingerprint.Of(Settings(), withPins: true), SettingsFingerprint.Of(Settings(), withPins: true));
    }

    [Fact]
    public void ThePageAndRunAtLogin_DoNotCount()
    {
        var changed = Settings();
        changed.SettingsPage = 7;
        changed.RunAtLogin = false;

        Assert.Equal(SettingsFingerprint.Of(Settings(), withPins: false), SettingsFingerprint.Of(changed, withPins: false));
    }

    [Fact]
    public void APinTheDockChanged_DoesNotCount_UntilTheListIsEdited()
    {
        var changed = Settings();
        changed.PinnedApps[0].Label = "Editor";

        Assert.Equal(SettingsFingerprint.Of(Settings(), withPins: false), SettingsFingerprint.Of(changed, withPins: false));
        Assert.NotEqual(SettingsFingerprint.Of(Settings(), withPins: true), SettingsFingerprint.Of(changed, withPins: true));
    }

    [Fact]
    public void ASettingTheUserChanged_Counts()
    {
        var changed = Settings();
        changed.BaseSize = 56;

        Assert.NotEqual(SettingsFingerprint.Of(Settings(), withPins: false), SettingsFingerprint.Of(changed, withPins: false));
    }

    [Fact]
    public void TheSettingsLookedAt_AreLeftAsTheyWere()
    {
        var settings = Settings();
        SettingsFingerprint.Of(settings, withPins: false);

        Assert.Equal(2, settings.SettingsPage);
        Assert.True(settings.RunAtLogin);
        Assert.Single(settings.PinnedApps);
    }
}
