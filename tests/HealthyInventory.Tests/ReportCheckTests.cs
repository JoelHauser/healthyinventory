using HealthyInventory;

namespace HealthyInventory.Tests;

/// <summary>The checks the in-game layout report runs on every visible graphic.</summary>
public class ReportCheckTests
{
    private const float T = Rules.ReportTolerance;
    private static readonly Box Column = new(1236, 130, 1908, 1032);

    [Fact]
    public void ABoxInsideIsInside() => Assert.True(Rules.Inside(new Box(1300, 200, 1800, 900), Column, T));

    [Fact]
    public void TheColumnIsInsideItself() => Assert.True(Rules.Inside(Column, Column, T));

    [Fact]
    public void RoundingOnAnEdgeIsNotClipping() => Assert.True(Rules.Inside(new Box(1235, 129, 1909, 1033), Column, T));

    [Theory]
    [InlineData(1230, 200, 1300, 300)]   // left
    [InlineData(1800, 200, 1912, 300)]   // right
    [InlineData(1300, 120, 1400, 300)]   // bottom, into the quick slots
    [InlineData(1300, 900, 1400, 1040)]  // top
    public void PastAnEdgeIsOutside(float x0, float y0, float x1, float y1) =>
        Assert.False(Rules.Inside(new Box(x0, y0, x1, y1), Column, T));

    [Fact]
    public void TouchingBoxesDoNotOverlap() =>
        Assert.False(Rules.Overlaps(new Box(0, 0, 100, 100), new Box(100, 0, 200, 100), T));

    [Fact]
    public void ARoundingSliverIsNotAnOverlap() =>
        Assert.False(Rules.Overlaps(new Box(0, 0, 101, 100), new Box(100, 0, 200, 100), T));

    [Fact]
    public void RealOverlapIsReported() =>
        Assert.True(Rules.Overlaps(new Box(0, 0, 110, 100), new Box(100, 0, 200, 100), T));

    [Fact]
    public void OverlapNeedsBothDirections() =>
        Assert.False(Rules.Overlaps(new Box(0, 0, 150, 100), new Box(100, 200, 200, 300), T));

    /// <summary>On the layout read off the prefab, the report's checks pass at every screen size:
    /// the column is inside the screen's items panel and clear of the gear column.</summary>
    [Theory]
    [MemberData(nameof(GeometryTests.Screens), MemberType = typeof(GeometryTests))]
    public void TheExpectedLayoutPassesTheReportChecks(int w, int h, string _)
    {
        var (_, items, leftSide, stash, _) = Prefab.Layout(w, h);
        Box column = Prefab.Column(stash, Rules.ColumnOffsets(stash, leftSide, true));
        Assert.True(Rules.Inside(column, items, T));
        Assert.False(Rules.Overlaps(column, leftSide, T));
        // Quick slots: their panel rect reaches above its icons, so GeometryTests checks the icons.
    }
}
