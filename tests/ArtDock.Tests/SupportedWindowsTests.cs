using ArtDock.Services;

namespace ArtDock.Tests;

/// <summary>
/// Covers telling Windows 11 from what came before it, which the dock refuses to run on.
/// </summary>
/// <remarks>
/// Windows 11 reports itself as Windows 10.0, so it is the build that decides: 22000 and on.
/// The last Windows 10, 22H2, is build 19045, and must be turned away as surely as Windows 7.
/// </remarks>
public class SupportedWindowsTests
{
    [Theory]
    [InlineData("6.1.7601")]     // Windows 7 SP1
    [InlineData("6.2.9200")]     // Windows 8
    [InlineData("6.3.9600")]     // Windows 8.1
    [InlineData("10.0.10240")]   // Windows 10, the first
    [InlineData("10.0.19045")]   // Windows 10 22H2, the last
    [InlineData("10.0.21999")]
    public void OlderThanWindows11_IsNotSupported(string version)
    {
        Assert.False(SupportedWindows.Supports(Version.Parse(version)));
    }

    [Theory]
    [InlineData("10.0.22000")]       // Windows 11, the first
    [InlineData("10.0.22000.0")]
    [InlineData("10.0.26100.4652")]  // Windows 11 24H2
    [InlineData("10.0.26300")]
    [InlineData("11.0.0")]
    public void Windows11AndLater_IsSupported(string version)
    {
        Assert.True(SupportedWindows.Supports(Version.Parse(version)));
    }
}
