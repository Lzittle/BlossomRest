using TransitShelf.Platform;

namespace TransitShelf.Tests;

public class DisplayPlacementTests
{
    [Fact]
    public void 位置会限制在带负坐标的显示器工作区内()
    {
        var result = DisplayPlacement.ClampToWorkArea(
            desiredLeft: -2000,
            desiredTop: 2000,
            windowWidth: 300,
            windowHeight: 400,
            workLeft: -1920,
            workTop: 0,
            workRight: 0,
            workBottom: 1040);

        Assert.Equal((-1920, 640), result);
    }

    [Fact]
    public void 窗口大于工作区时仍贴在工作区左上角()
    {
        var result = DisplayPlacement.ClampToWorkArea(
            desiredLeft: 100,
            desiredTop: 100,
            windowWidth: 2000,
            windowHeight: 1200,
            workLeft: 0,
            workTop: 0,
            workRight: 1920,
            workBottom: 1080);

        Assert.Equal((0, 0), result);
    }
}
