using System.Text.Json;
using ArtDock.Services;

namespace ArtDock.Tests;

/// <summary>
/// An item's name is never longer than <see cref="PinnedAppSetting.MaxLabelLength"/>, however
/// it is given.
/// </summary>
public class LabelLengthTests
{
    [Fact]
    public void A_name_within_the_limit_is_kept()
    {
        var name = new string('a', PinnedAppSetting.MaxLabelLength);
        Assert.Equal(name, new PinnedAppSetting { Label = name }.Label);
    }

    [Fact]
    public void A_longer_name_is_cut_to_the_limit()
    {
        var pin = new PinnedAppSetting { Label = new string('a', 60) };
        Assert.Equal(PinnedAppSetting.MaxLabelLength, pin.Label.Length);
    }

    [Fact]
    public void The_cut_leaves_no_trailing_space()
    {
        var name = new string('a', PinnedAppSetting.MaxLabelLength - 1) + "  tail";
        Assert.Equal(new string('a', PinnedAppSetting.MaxLabelLength - 1), PinnedAppSetting.ClampLabel(name));
    }

    [Fact]
    public void The_cut_does_not_split_a_surrogate_pair()
    {
        var name = new string('a', PinnedAppSetting.MaxLabelLength - 1) + "😀😀";
        var cut = PinnedAppSetting.ClampLabel(name);
        Assert.Equal(PinnedAppSetting.MaxLabelLength - 1, cut.Length);
        Assert.False(char.IsHighSurrogate(cut[^1]));
    }

    [Fact]
    public void A_long_name_in_a_settings_file_is_cut_as_it_is_read()
    {
        var json = $$"""{"Label":"{{new string('b', 80)}}"}""";
        var pin = JsonSerializer.Deserialize<PinnedAppSetting>(json)!;
        Assert.Equal(PinnedAppSetting.MaxLabelLength, pin.Label.Length);
    }

    [Fact]
    public void A_missing_name_is_empty()
    {
        Assert.Equal(string.Empty, PinnedAppSetting.ClampLabel(null));
    }
}
