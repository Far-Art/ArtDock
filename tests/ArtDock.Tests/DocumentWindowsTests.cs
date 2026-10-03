using ArtDock.Services;

namespace ArtDock.Tests;

/// <summary>
/// Which windows show a document, by their titles (<see cref="DocumentWindows"/>) — what lights a
/// document's pin.
/// </summary>
public class DocumentWindowsTests
{
    [Theory]
    // As measured on 2026-10-03: Word, and Photos, which puts nothing after the name.
    [InlineData("Letter.docx - Word", "Letter.docx")]
    [InlineData("popeye_large.jpg", "popeye_large.jpg")]
    [InlineData("kapshitar-photo.psd @ 66.7% (Layer 1, RGB/8#)", "kapshitar-photo.psd")]
    [InlineData("*notes.txt - Notepad", "notes.txt")]
    [InlineData("LETTER.DOCX - Word", "Letter.docx")]
    public void AWindowNamingTheFile_ShowsIt(string title, string file) =>
        Assert.True(DocumentWindows.Shows(title, file, byItsApp: false));

    [Fact]
    public void AWindowInHebrew_ShowsItsFile() =>
        Assert.True(DocumentWindows.Shows("מכתב לדר אורי.docx - Word", "מכתב לדר אורי.docx", byItsApp: false));

    [Theory]
    // Inside a longer name, at either end: the pin for 1.jpg is not lit by 21.jpg.
    [InlineData("21.jpg", "1.jpg")]
    [InlineData("1.jpg2", "1.jpg")]
    [InlineData("Letter.docx.bak - Notepad", "Letter.docx")]
    public void APartOfALongerName_DoesNotShowIt(string title, string file) =>
        Assert.False(DocumentWindows.Shows(title, file, byItsApp: false));

    [Fact]
    public void AnotherOccurrence_StillCounts_WhenTheFirstIsInsideALongerName() =>
        Assert.True(DocumentWindows.Shows("21.jpg and 1.jpg", "1.jpg", byItsApp: false));

    [Theory]
    [InlineData("Letter - Word")]
    [InlineData("Letter")]
    [InlineData("*Letter - Notepad")]
    public void ItsAppsWindow_MayLeaveTheExtensionOut(string title) =>
        Assert.True(DocumentWindows.Shows(title, "Letter.docx", byItsApp: true));

    [Fact]
    public void AnotherAppsWindow_MayNot() =>
        Assert.False(DocumentWindows.Shows("Letter - Google Chrome", "Letter.docx", byItsApp: false));

    [Theory]
    // Without its extension, the name must start the title and end with it or the app's name.
    [InlineData("1 (2).jpg", "1.jpg")]
    [InlineData("1984.jpg", "1.jpg")]
    [InlineData("Cover Letter - Word", "Letter.docx")]
    [InlineData("Letters - Word", "Letter.docx")]
    public void ItsAppsWindow_ShowingAnotherFile_DoesNotShowIt(string title, string file) =>
        Assert.False(DocumentWindows.Shows(title, file, byItsApp: true));

    [Theory]
    [InlineData(null, "Letter.docx")]
    [InlineData("", "Letter.docx")]
    [InlineData("Letter - Word", null)]
    [InlineData("Letter - Word", "")]
    public void NothingToGoBy_ShowsNothing(string? title, string? file) =>
        Assert.False(DocumentWindows.Shows(title, file, byItsApp: true));

    [Fact]
    public void AFileWithNoExtension_IsNamedInFull() =>
        Assert.False(DocumentWindows.Shows("README - Notepad", "READMEFILE", byItsApp: true));
}
