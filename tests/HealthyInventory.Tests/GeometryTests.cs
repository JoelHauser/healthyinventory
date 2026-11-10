using HealthyInventory;

namespace HealthyInventory.Tests;

/// <summary>
/// The inventory screen's rects, as read from CommonUIScene (level44) of EFT 0.16.9.40743 with
/// UnityPy on 2026-11-10, and RectTransform's own maths to place them on a canvas. The canvas
/// size comes from Rules.CanvasUnits, which is UICanvasScalerController's formula.
/// </summary>
internal static class Prefab
{
    internal record struct RT(float AMinX, float AMinY, float AMaxX, float AMaxY, float PosX, float PosY, float SizeX, float SizeY, float PivX, float PivY);

    // InventoryScreen: a 1920 wide column, centred, stretched vertically.
    internal static readonly RT InventoryScreen = new(0.5f, 0f, 0.5f, 1f, 0f, 0f, 1920f, 0f, 0.5f, 0.5f);
    // Items Panel fills it.
    internal static readonly RT ItemsPanel = new(0f, 0f, 1f, 1f, 0f, 0f, 0f, 0f, 0.5f, 0.5f);
    internal static readonly RT LeftSide = new(0.47600001096725464f, 0f, 0.7929999828338623f, 1f, -902.34375f, -48f, 604.6866455078125f, -178f, 0f, 1f);
    internal static readonly RT StashPanel = new(1f, 0f, 1f, 1f, -12f, 28.5f, 680f, -207f, 1f, 0.5f);
    internal static readonly RT QuickUsePanel = new(0.5f, 0f, 0.5f, 0f, 25.600000381469727f, 30f, 854.2000122070312f, 115f, 0.5f, 0f);

    internal static Box Place(Box parent, RT r)
    {
        float offMinX = r.PosX - r.SizeX * r.PivX, offMinY = r.PosY - r.SizeY * r.PivY;
        float offMaxX = r.PosX + r.SizeX * (1 - r.PivX), offMaxY = r.PosY + r.SizeY * (1 - r.PivY);
        return new Box(
            parent.XMin + r.AMinX * parent.Width + offMinX,
            parent.YMin + r.AMinY * parent.Height + offMinY,
            parent.XMin + r.AMaxX * parent.Width + offMaxX,
            parent.YMin + r.AMaxY * parent.Height + offMaxY);
    }

    internal static (Box canvas, Box items, Box leftSide, Box stash, Box quick) Layout(int screenW, int screenH)
    {
        Rules.CanvasUnits(screenW, screenH, out float w, out float h);
        var canvas = new Box(0, 0, w, h);
        var screen = Place(canvas, InventoryScreen);
        var items = Place(screen, ItemsPanel);
        return (canvas, items, Place(items, LeftSide), Place(items, StashPanel), Place(items, QuickUsePanel));
    }

    /// <summary>Where the column ends up for given offsets, in the same space as the stash box.</summary>
    internal static Box Column(Box stash, Offsets o) =>
        new(stash.XMin + o.Left, stash.YMin + o.Bottom, stash.XMax + o.Right, stash.YMax + o.Top);
}

public class GeometryTests
{
    private const float Eps = 0.01f;

    public static TheoryData<int, int, string> Screens => new()
    {
        { 1280, 720, "16:9 small" },
        { 1366, 768, "16:9 laptop" },
        { 1920, 1080, "16:9 reference" },
        { 2560, 1440, "16:9 1440p" },
        { 3840, 2160, "16:9 4K" },
        { 1920, 1200, "16:10" },
        { 2560, 1600, "16:10 1600p" },
        { 2560, 1080, "21:9" },
        { 3440, 1440, "21:9 1440p" },
        { 5120, 1440, "32:9" },
        { 1024, 768, "4:3" },
        { 1600, 1200, "4:3 1200p" },
        { 1280, 1024, "5:4" },
        { 1080, 1920, "portrait" },
    };

    [Fact]
    public void TheReferenceNumbersAreTheOnesReadOffThePrefab()
    {
        // Pin the derived edges, so a wrong constant above fails loudly rather than quietly
        // moving every other assertion with it.
        var (_, _, left, stash, _) = Prefab.Layout(1920, 1080);
        Assert.Equal(11.6f, left.XMin, 1);
        Assert.Equal(1224.9f, left.XMax, 1);
        Assert.Equal(130f, left.YMin, 1);
        Assert.Equal(1032f, left.YMax, 1);
        Assert.Equal(1228f, stash.XMin, 1);
        Assert.Equal(1908f, stash.XMax, 1);
        Assert.Equal(132f, stash.YMin, 1);
        Assert.Equal(1005f, stash.YMax, 1);
    }

    [Theory]
    [MemberData(nameof(Screens))]
    public void TheCanvasIsNeverSmallerThanTheReference(int w, int h, string name)
    {
        Rules.CanvasUnits(w, h, out float cw, out float ch);
        Assert.True(cw >= 1920f - Eps && ch >= 1080f - Eps, $"{name}: canvas {cw}x{ch}");
    }

    [Theory]
    [MemberData(nameof(Screens))]
    public void TheColumnSharesTheGearColumnsTopAndBottom(int w, int h, string name)
    {
        var (_, _, left, stash, _) = Prefab.Layout(w, h);
        var col = Prefab.Column(stash, Rules.ColumnOffsets(stash, left, true));
        Assert.True(Math.Abs(col.YMax - left.YMax) < Eps, $"{name}: top {col.YMax} vs {left.YMax}");
        Assert.True(Math.Abs(col.YMin - left.YMin) < Eps, $"{name}: bottom {col.YMin} vs {left.YMin}");
    }

    [Theory]
    [MemberData(nameof(Screens))]
    public void TheColumnNeverReachesIntoTheContainersOrOffTheScreen(int w, int h, string name)
    {
        var (canvas, items, left, stash, _) = Prefab.Layout(w, h);
        var col = Prefab.Column(stash, Rules.ColumnOffsets(stash, left, true));
        Assert.True(col.XMin >= left.XMax + Rules.MinGap - Eps, $"{name}: column starts {col.XMin}, containers end {left.XMax}");
        Assert.True(col.XMax <= items.XMax + Eps, $"{name}: column ends past the inventory");
        Assert.True(col.XMin >= canvas.XMin - Eps && col.XMax <= canvas.XMax + Eps, $"{name}: column off canvas");
        Assert.True(col.YMin >= canvas.YMin - Eps && col.YMax <= canvas.YMax + Eps, $"{name}: column off canvas vertically");
        Assert.True(col.Width >= 670f, $"{name}: column only {col.Width} wide");
    }

    [Theory]
    [MemberData(nameof(Screens))]
    public void TheColumnClearsTheQuickSlotIcons(int w, int h, string name)
    {
        // The quick-use bar's 64 px icons sit in an 89-unit row starting 30 up; its header is at
        // its far left, under the gear column. The column's bottom edge must stay above the icons.
        var (_, _, left, stash, quick) = Prefab.Layout(w, h);
        var col = Prefab.Column(stash, Rules.ColumnOffsets(stash, left, true));
        float iconsTop = quick.YMin + 89f;
        Assert.True(col.YMin >= iconsTop - Eps, $"{name}: column bottom {col.YMin}, icons top {iconsTop}");
    }

    [Theory]
    [MemberData(nameof(Screens))]
    public void ThePanelIsShownAtNativeSizeForAnyPlausibleGearColumnWidth(int w, int h, string name)
    {
        // The vanilla Health Panel stretches over Left Panel, so its design size is Left Panel's.
        // Left Panel's width comes from LeftSide's layout group at runtime (not in the prefab
        // data); the gear slots alone are 494 wide and LeftSide is 1213, shared with containers.
        var (_, _, left, stash, _) = Prefab.Layout(w, h);
        var o = Rules.ColumnOffsets(stash, left, true);
        var col = Prefab.Column(stash, o);
        for (float designW = 494f; designW <= 670f; designW += 8f)
        {
            float k = Rules.FitScale(col.Width, col.Height, designW, left.Height);
            Assert.True(Math.Abs(k - 1f) < Eps, $"{name}: design {designW} scaled {k}");
        }
    }

    [Fact]
    public void AWiderPanelShrinksUniformlyAndStaysReadable()
    {
        var (_, _, left, stash, _) = Prefab.Layout(1920, 1080);
        var col = Prefab.Column(stash, Rules.ColumnOffsets(stash, left, true));
        // Even if Left Panel took the whole of LeftSide's 1213 units, which it cannot, since the
        // containers share it, the panel would only shrink, keep its proportions, and fit.
        float k = Rules.FitScale(col.Width, col.Height, 760f, left.Height);
        Assert.InRange(k, 0.85f, 0.9f);
        Assert.True(760f * k <= col.Width + Eps && left.Height * k <= col.Height + Eps);
    }

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(50f, 900f)]
    [InlineData(600f, 10f)]
    public void ADesignSizeReadBeforeLayoutRanFallsBackToTheColumn(float dw, float dh)
    {
        Assert.False(Rules.DesignUsable(dw, dh));
        Assert.Equal(1f, Rules.FitScale(675f, 902f, dw, dh));
    }

    [Fact]
    public void NeverEnlarged()
    {
        Assert.Equal(1f, Rules.FitScale(2000f, 2000f, 600f, 900f));
    }

    [Fact]
    public void AZeroSizedColumnDoesNotDivideByZero()
    {
        Assert.Equal(1f, Rules.FitScale(0f, 0f, 600f, 900f));
    }

    [Fact]
    public void WithoutTheLeftSideTheColumnIsExactlyStashPanel()
    {
        var (_, _, _, stash, _) = Prefab.Layout(1920, 1080);
        var o = Rules.ColumnOffsets(stash, default, false);
        Assert.Equal(default, o);
    }

    [Fact]
    public void AGearColumnMovedFarAwayIsNotFollowed()
    {
        // Another mod moving LeftSide 300 units up must not drag the column off the screen.
        var (_, _, left, stash, _) = Prefab.Layout(1920, 1080);
        var moved = new Box(left.XMin, left.YMin + 300, left.XMax, left.YMax + 300);
        var o = Rules.ColumnOffsets(stash, moved, true);
        Assert.Equal(Rules.MaxVerticalStretch, o.Top);
        Assert.Equal(Rules.MaxVerticalStretch, o.Bottom);
    }

    [Fact]
    public void AGearColumnWideningIntoTheStashKeepsAtLeastHalfTheColumn()
    {
        var (_, _, left, stash, _) = Prefab.Layout(1920, 1080);
        var wide = new Box(left.XMin, left.YMin, stash.XMax - 10, left.YMax);
        var o = Rules.ColumnOffsets(stash, wide, true);
        Assert.Equal(stash.Width * 0.5f, o.Left, 3);
    }

    [Fact]
    public void TheReferenceColumnIsAboutTheVanillaHealthTabsHeight()
    {
        var (_, _, left, stash, _) = Prefab.Layout(1920, 1080);
        var col = Prefab.Column(stash, Rules.ColumnOffsets(stash, left, true));
        Assert.Equal(902f, col.Height, 1);
        Assert.Equal(675.1f, col.Width, 1);
    }
}
