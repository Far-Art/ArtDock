using System.Windows;
using System.Windows.Controls;

namespace ArtDock.Localization;

/// <summary>
/// Formatted text in XAML: a string from the current language with a bound value put into it.
/// </summary>
/// <remarks>
/// <para>
/// For the read-outs beside the settings sliders, which used to be bindings with a
/// <c>StringFormat</c> — <c>{0:0} px</c>, <c>{0:0} icons</c>. A <c>StringFormat</c> is fixed
/// when the XAML is compiled and cannot be a resource, so it could not be translated, could
/// not say "1 icon", and formatted numbers in WPF's default of US English whatever the
/// language. Instead:
/// </para>
/// <code>
/// &lt;TextBlock loc:Localized.Key="Settings.Size.IconSize.Value"
///            loc:Localized.Value="{Binding ElementName=BaseSizeSlider, Path=Value}" /&gt;
/// </code>
/// <para>
/// The text is rebuilt when the value changes and when the language does. The second is
/// noticed without any subscription to hold the element alive: the element takes the current
/// culture as a <c>DynamicResource</c>, and a new language is a new culture.
/// </para>
/// </remarks>
public static class Localized
{
    /// <summary>The key of the string to show.</summary>
    public static readonly DependencyProperty KeyProperty = DependencyProperty.RegisterAttached(
        "Key", typeof(string), typeof(Localized), new PropertyMetadata(null, OnChanged));

    /// <summary>The value to put into it, as argument <c>{0}</c>.</summary>
    public static readonly DependencyProperty ValueProperty = DependencyProperty.RegisterAttached(
        "Value", typeof(object), typeof(Localized), new PropertyMetadata(null, OnChanged));

    /// <summary>The current culture, held as a resource reference so a language change reaches here.</summary>
    private static readonly DependencyProperty CultureProperty = DependencyProperty.RegisterAttached(
        "Culture", typeof(object), typeof(Localized), new PropertyMetadata(null, OnChanged));

    public static string? GetKey(DependencyObject element) => (string?)element.GetValue(KeyProperty);

    public static void SetKey(DependencyObject element, string? value) => element.SetValue(KeyProperty, value);

    public static object? GetValue(DependencyObject element) => element.GetValue(ValueProperty);

    public static void SetValue(DependencyObject element, object? value) => element.SetValue(ValueProperty, value);

    private static void OnChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not TextBlock text)
        {
            return;
        }

        // Once per element. Setting the reference resolves it straight away when the element
        // is already in a tree, which comes back through here — by then it is set.
        if (text.ReadLocalValue(CultureProperty) == DependencyProperty.UnsetValue)
        {
            text.SetResourceReference(CultureProperty, Localizer.CultureKey);
        }

        if (GetKey(text) is not { Length: > 0 } key)
        {
            return;
        }

        text.Text = GetValue(text) is { } value
            ? Localizer.Format(key, value)
            : Localizer.Get(key);
    }
}
