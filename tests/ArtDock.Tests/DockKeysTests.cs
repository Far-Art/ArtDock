using System.Globalization;
using System.Windows.Input;
using ArtDock.Dock;

namespace ArtDock.Tests;

/// <summary>
/// Covers the dock used from the keyboard: which item each key goes to, and what each key means.
/// </summary>
/// <remarks>
/// Separators are where it could go wrong: they are in the row, and the keys must neither stop
/// on one nor count it — the third place is the third icon, whatever divides the row.
/// </remarks>
public class DockKeysTests
{
    private static DockItem Item(string label) => new() { Id = label, Label = label };

    private static DockItem Separator(string id) => new() { Id = id, Label = string.Empty, IsSeparator = true };

    /// <summary>— Files Firefox — Edge excel —</summary>
    private static readonly DockItem[] Row =
    [
        Separator("s0"), Item("Files"), Item("Firefox"), Separator("s1"), Item("Edge"), Item("excel"), Separator("s2")
    ];

    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    [Fact]
    public void TheEnds_AreItems_NotTheSeparatorsBeyondThem()
    {
        Assert.Equal(1, DockKeys.First(Row));
        Assert.Equal(5, DockKeys.Last(Row));
    }

    [Fact]
    public void Moving_StepsOverASeparator()
    {
        Assert.Equal(4, DockKeys.Step(Row, 2, 1));
        Assert.Equal(2, DockKeys.Step(Row, 4, -1));
    }

    [Fact]
    public void Moving_StopsAtTheEnds_RatherThanGoingRound()
    {
        Assert.Equal(5, DockKeys.Step(Row, 5, 1));
        Assert.Equal(1, DockKeys.Step(Row, 1, -1));
    }

    [Fact]
    public void Moving_FromNoItem_StartsAtTheEndItIsGoingFrom()
    {
        Assert.Equal(1, DockKeys.Step(Row, -1, 1));
        Assert.Equal(5, DockKeys.Step(Row, -1, -1));
    }

    [Fact]
    public void ARowOfNothingButSeparators_HasNothingToHold()
    {
        DockItem[] row = [Separator("a"), Separator("b")];

        Assert.Equal(-1, DockKeys.First(row));
        Assert.Equal(-1, DockKeys.Last(row));
        Assert.Equal(-1, DockKeys.Step(row, -1, 1));
        Assert.Equal(-1, DockKeys.Place(row, 1));
        Assert.Equal(-1, DockKeys.StartingWith(row, -1, "a", English));
        Assert.Equal(-1, DockKeys.First([]));
    }

    [Fact]
    public void ThePlaces_AreCountedWithoutTheSeparators()
    {
        Assert.Equal(1, DockKeys.Place(Row, 1));
        Assert.Equal(2, DockKeys.Place(Row, 2));
        Assert.Equal(4, DockKeys.Place(Row, 3));
        Assert.Equal(5, DockKeys.Place(Row, 4));
        Assert.Equal(-1, DockKeys.Place(Row, 5));
        Assert.Equal(-1, DockKeys.Place(Row, 0));
    }

    [Fact]
    public void ALetter_GoesToTheNextNameStartingWithIt_AndRoundAgain()
    {
        Assert.Equal(2, DockKeys.StartingWith(Row, 1, "f", English));
        Assert.Equal(1, DockKeys.StartingWith(Row, 2, "F", English));

        // Case aside: excel is lower case.
        Assert.Equal(5, DockKeys.StartingWith(Row, 4, "E", English));
        Assert.Equal(4, DockKeys.StartingWith(Row, 5, "e", English));
    }

    [Fact]
    public void ALetter_OnTheOnlyNameStartingWithIt_StaysThere_AndOneThatNoNameStartsWith_GoesNowhere()
    {
        Assert.Equal(4, DockKeys.StartingWith(Row, 4, "ed", English));
        Assert.Equal(-1, DockKeys.StartingWith(Row, 1, "z", English));
    }

    [Fact]
    public void ALetter_FromNoItem_FindsTheFirstNameStartingWithIt()
    {
        Assert.Equal(1, DockKeys.StartingWith(Row, -1, "f", English));
    }

    [Fact]
    public void ALetter_IsMatchedInTheLanguagesOwnSenseOfCase()
    {
        DockItem[] row = [Item("Index")];

        Assert.Equal(0, DockKeys.StartingWith(row, -1, "i", English));

        // In Turkish the capital of i is İ, with its dot, and I is the capital of ı.
        Assert.Equal(-1, DockKeys.StartingWith(row, -1, "i", CultureInfo.GetCultureInfo("tr-TR")));
    }

    [Theory]
    [InlineData(Key.Left, ModifierKeys.None, DockKeyKind.Previous, 0)]
    [InlineData(Key.Right, ModifierKeys.None, DockKeyKind.Next, 0)]
    [InlineData(Key.Right, ModifierKeys.Control, DockKeyKind.Next, 0)]
    [InlineData(Key.Right, ModifierKeys.Windows, DockKeyKind.None, 0)]
    [InlineData(Key.Right, ModifierKeys.Alt, DockKeyKind.Next, 0)]
    [InlineData(Key.Left, ModifierKeys.Control | ModifierKeys.Alt, DockKeyKind.Previous, 0)]
    [InlineData(Key.Home, ModifierKeys.None, DockKeyKind.First, 0)]
    [InlineData(Key.End, ModifierKeys.Shift, DockKeyKind.Last, 0)]
    [InlineData(Key.Enter, ModifierKeys.None, DockKeyKind.Open, 0)]
    [InlineData(Key.Space, ModifierKeys.None, DockKeyKind.Open, 0)]
    [InlineData(Key.Enter, ModifierKeys.Control | ModifierKeys.Shift, DockKeyKind.OpenAsAdministrator, 0)]
    [InlineData(Key.Enter, ModifierKeys.Control, DockKeyKind.None, 0)]
    [InlineData(Key.Enter, ModifierKeys.Shift, DockKeyKind.None, 0)]
    [InlineData(Key.Space, ModifierKeys.Control | ModifierKeys.Shift, DockKeyKind.None, 0)]
    [InlineData(Key.D3, ModifierKeys.None, DockKeyKind.Place, 3)]
    [InlineData(Key.NumPad9, ModifierKeys.None, DockKeyKind.Place, 9)]
    [InlineData(Key.D3, ModifierKeys.Control, DockKeyKind.Place, 3)]
    [InlineData(Key.NumPad4, ModifierKeys.Control | ModifierKeys.Alt, DockKeyKind.Place, 4)]
    [InlineData(Key.D3, ModifierKeys.Windows, DockKeyKind.None, 0)]
    [InlineData(Key.D0, ModifierKeys.None, DockKeyKind.None, 0)]
    [InlineData(Key.Escape, ModifierKeys.None, DockKeyKind.Back, 0)]
    [InlineData(Key.F4, ModifierKeys.Alt, DockKeyKind.Back, 0)]
    [InlineData(Key.Apps, ModifierKeys.None, DockKeyKind.Menu, 0)]
    [InlineData(Key.F10, ModifierKeys.Shift, DockKeyKind.Menu, 0)]
    [InlineData(Key.F10, ModifierKeys.None, DockKeyKind.None, 0)]
    [InlineData(Key.Up, ModifierKeys.None, DockKeyKind.None, 0)]
    [InlineData(Key.Tab, ModifierKeys.None, DockKeyKind.None, 0)]
    [InlineData(Key.A, ModifierKeys.None, DockKeyKind.None, 0)]
    public void EachKey_MeansWhatItDoesAnywhereInWindows(Key key, ModifierKeys modifiers, DockKeyKind kind, int place)
    {
        Assert.Equal(new DockKeyCommand(kind, place), DockKeys.Command(key, modifiers));
    }
}
