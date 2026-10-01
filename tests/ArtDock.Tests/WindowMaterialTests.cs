using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using ArtDock.Views;

namespace ArtDock.Tests;

/// <summary>
/// Holds a dialog taken off Mica, for <c>DockSettings.NoGpu</c>, to having a background.
/// </summary>
/// <remarks>
/// The Fluent theme gives a window none — a transparent one, for DWM's material to show through
/// — so a window taken off the material and left with the theme's own came up black, which is
/// how this was first built and how it was reported. The windows here are given a handle and
/// never shown.
/// </remarks>
public class WindowMaterialTests
{
    private static ResourceDictionary Fluent() => new()
    {
        Source = new Uri("pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.xaml")
    };

    private static Window Dialog(bool transparent = false)
    {
        var window = new Window
        {
            ShowInTaskbar = false,
            ShowActivated = false,
            Left = -20000,
            Top = -20000,
            Width = 200,
            Height = 100,
        };

        if (transparent)
        {
            window.WindowStyle = WindowStyle.None;
            window.AllowsTransparency = true;
            window.Background = Brushes.Transparent;
        }

        window.Resources.MergedDictionaries.Add(Fluent());
        new WindowInteropHelper(window).EnsureHandle();
        return window;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ADialogTakenOffTheMaterial_HasAnOpaqueBackground(bool dark) => OnStaThread(() =>
    {
        var window = Dialog();
        try
        {
            WindowMaterial.Apply(window, solid: true, dark);

            var brush = Assert.IsAssignableFrom<SolidColorBrush>(window.Background);
            Assert.Equal(255, brush.Color.A);
            Assert.Equal(1, brush.Opacity);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    /// Unticking the box puts the window back as the theme made it: no background of its own,
    /// which is what lets the material through.
    /// </summary>
    [Fact]
    public void ADialogPutBackOnTheMaterial_HasTheThemesBackgroundAgain() => OnStaThread(() =>
    {
        var window = Dialog();
        try
        {
            WindowMaterial.Apply(window, solid: true, dark: false);
            WindowMaterial.Apply(window, solid: false, dark: false);

            Assert.Equal(DependencyProperty.UnsetValue, window.ReadLocalValue(Control.BackgroundProperty));
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    /// The dock's own window and the menus' host are transparent on purpose. Given a background
    /// they would be a slab across the screen.
    /// </summary>
    [Fact]
    public void AWindowThatIsTransparentOnPurpose_IsLeftAlone() => OnStaThread(() =>
    {
        var window = Dialog(transparent: true);
        try
        {
            WindowMaterial.Apply(window, solid: true, dark: false);

            Assert.Same(Brushes.Transparent, window.Background);
        }
        finally
        {
            window.Close();
        }
    });

    private static void OnStaThread(Action body)
    {
        ExceptionDispatchInfo? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                body();
            }
            catch (Exception ex)
            {
                failure = ExceptionDispatchInfo.Capture(ex);
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        failure?.Throw();
    }
}
