using System.IO;
using System.Xml.Linq;

namespace ArtDock.Tests;

/// <summary>
/// Holds the settings dialog's pages open to UI Automation, and so to a screen reader.
/// </summary>
/// <remarks>
/// <para>
/// The dialog draws its page list with a <c>TabControl</c> template of its own, and
/// <c>TabItemAutomationPeer</c> finds a page's contents through the one part of that template
/// it knows by name: the <c>ContentPresenter</c> called <c>PART_SelectedContentHost</c>. Left
/// unnamed, as it was until 2026-10-01, UI Automation saw the nine page names and not one
/// setting on any page. A probe that built the dialog without showing it counted 0 elements
/// inside the pages before the name and 251 after.
/// </para>
/// <para>
/// Read from the XAML rather than by building the dialog: the dialog cannot be built in the
/// test host, whose entry assembly has none of the dialog's resources — its window icon among
/// them. A template restyled again without the name is what this is here to catch.
/// </para>
/// </remarks>
public class SettingsAccessibilityTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void ThePagesTemplate_NamesItsContentHost_AsUIAutomationLooksForIt()
    {
        var template = Dialog().Descendants()
            .Single(element => element.Name.LocalName == "ControlTemplate"
                && (string?)element.Attribute("TargetType") == "TabControl");

        var host = template.Descendants()
            .Single(element => element.Name.LocalName == "ContentPresenter"
                && (string?)element.Attribute("ContentSource") == "SelectedContent");

        Assert.Equal("PART_SelectedContentHost", (string?)host.Attribute(Xaml + "Name"));
    }

    /// <summary>
    /// Every slider, drop-down, text box, checkbox and list says what it is. Each one's label is
    /// a text block beside it rather than its own content, which a screen reader does not
    /// connect to it: reachable, 39 of the dialog's 64 controls were read as nothing more than
    /// "slider" or "check box". Each is named from the same string as its label.
    /// </summary>
    [Fact]
    public void EveryControl_IsNamed_ForAScreenReader()
    {
        string[] kinds = ["Slider", "ComboBox", "TextBox", "CheckBox", "ListBox", "RadioButton"];
        var nameless = Dialog().Descendants()
            .Where(element => kinds.Contains(element.Name.LocalName))
            .Where(element => element.Attribute("AutomationProperties.Name") is null
                && element.Attribute("AutomationProperties.LabeledBy") is null
                && element.Attribute("Content") is null)
            .Select(element => (string?)element.Attribute(Xaml + "Name") ?? element.Name.LocalName)
            .ToList();

        Assert.Empty(nameless);
    }

    /// <summary>
    /// A list's rows hold objects, not strings, and a row with no name of its own is read as its
    /// object's type — every row of the Items page was "ArtDock.Services.PinnedAppSetting".
    /// </summary>
    [Fact]
    public void EveryListsRows_AreNamed_ForAScreenReader()
    {
        var unnamed = Dialog().Descendants()
            .Where(element => element.Name.LocalName == "ListBox")
            .Where(list => !list.Descendants()
                .Where(element => element.Name.LocalName == "ListBox.ItemContainerStyle")
                .Descendants()
                .Any(setter => setter.Name.LocalName == "Setter"
                    && (string?)setter.Attribute("Property") == "AutomationProperties.Name"))
            .Select(list => (string?)list.Attribute(Xaml + "Name") ?? "ListBox")
            .ToList();

        Assert.Empty(unnamed);
    }

    private static XDocument Dialog() =>
        XDocument.Load(Path.Combine(FindSourceRoot(), "Views", "SettingsWindow.xaml"));

    private static string FindSourceRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ArtDock.sln")))
            {
                return Path.Combine(dir.FullName, "src", "ArtDock");
            }
        }

        throw new DirectoryNotFoundException("The repository root, with ArtDock.sln in it, was not found above the tests.");
    }
}
