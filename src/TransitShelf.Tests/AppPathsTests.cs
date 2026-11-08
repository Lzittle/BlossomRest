using TransitShelf.Platform;

namespace TransitShelf.Tests;

public class AppPathsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void DefaultDataDirectoryUsedWhenOverrideMissing(string? value)
    {
        var expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TransitShelf");
        Assert.Equal(expected, AppPaths.ResolveRoot(value));
    }

    [Theory]
    [InlineData("state")]
    [InlineData(@"C:state")]
    [InlineData(@"\state")]
    public void IncompleteDataDirectoryRejected(string value)
        => Assert.Throws<ArgumentException>(() => AppPaths.ResolveRoot(value));

    [Fact]
    public void AbsoluteOverrideIsNormalizedAndSeparateFromDefault()
    {
        var root = AppPaths.ResolveRoot(Path.Combine(Path.GetTempPath(), "shelf-verification", "..", "shelf-isolated"));
        Assert.Equal(Path.Combine(Path.GetTempPath(), "shelf-isolated"), root);
        Assert.NotEqual(AppPaths.ResolveRoot(null), root);
    }
}
