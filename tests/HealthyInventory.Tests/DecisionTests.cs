using HealthyInventory;

namespace HealthyInventory.Tests;

public class DecisionTests
{
    private static ScreenFacts PersonalInRaid() => new()
    {
        PanelBelongsToInventoryScreen = true,
        RaidController = true,
        ControllerHasLoot = false,
        ScreenHasLoot = false,
        GearTab = true,
        InRaidArgument = true,
    };

    [Fact]
    public void OwnInventoryInRaidOnTheGearTabCombines()
    {
        Assert.Equal(Decision.Combine, Rules.Decide(true, PersonalInRaid()));
    }

    [Fact]
    public void SwitchedOffNeverCombines()
    {
        Assert.Equal(Decision.Disabled, Rules.Decide(false, PersonalInRaid()));
    }

    [Theory]
    [InlineData(true, false)]   // body, container, loose item: the controller carries the loot
    [InlineData(false, true)]   // the screen's copy (after grid-content wrapping) carries it
    [InlineData(true, true)]
    public void AnyLootSourceKeepsTheLootingLayout(bool controllerLoot, bool screenLoot)
    {
        var f = PersonalInRaid();
        f.ControllerHasLoot = controllerLoot;
        f.ScreenHasLoot = screenLoot;
        Assert.Equal(Decision.Looting, Rules.Decide(true, f));
    }

    [Fact]
    public void StashHideoutMenuAndMatchmakingControllersStayVanilla()
    {
        // Lobby, Hideout and Matchmaking controllers are not RaidInventoryScreenController.
        var f = PersonalInRaid();
        f.RaidController = false;
        Assert.Equal(Decision.NotRaid, Rules.Decide(true, f));
    }

    [Fact]
    public void TheInRaidArgumentMustAgree()
    {
        var f = PersonalInRaid();
        f.InRaidArgument = false;
        Assert.Equal(Decision.NotRaid, Rules.Decide(true, f));
    }

    [Fact]
    public void OtherScreensItemsPanelsStayVanilla()
    {
        // Scav transfer, insurance and BTR screens each own a different ItemsPanel.
        var f = PersonalInRaid();
        f.PanelBelongsToInventoryScreen = false;
        Assert.Equal(Decision.NotInventoryScreen, Rules.Decide(true, f));
    }

    [Fact]
    public void TheVanillaHealthTabIsLeftAlone()
    {
        var f = PersonalInRaid();
        f.GearTab = false;
        Assert.Equal(Decision.HealthTab, Rules.Decide(true, f));
    }

    [Fact]
    public void LootingWinsOverEveryOtherReasonButRaidAndScreen()
    {
        // A loot item on the Health tab is still looting; the order only changes the log line,
        // but the log line is what the test plan reads.
        var f = PersonalInRaid();
        f.GearTab = false;
        f.ControllerHasLoot = true;
        Assert.Equal(Decision.Looting, Rules.Decide(true, f));
    }

    [Fact]
    public void ExactlyOneCombinationOfTheSixFactsCombines()
    {
        int combines = 0;
        for (int bits = 0; bits < 64; bits++)
        {
            var f = new ScreenFacts
            {
                PanelBelongsToInventoryScreen = (bits & 1) != 0,
                RaidController = (bits & 2) != 0,
                ControllerHasLoot = (bits & 4) != 0,
                ScreenHasLoot = (bits & 8) != 0,
                GearTab = (bits & 16) != 0,
                InRaidArgument = (bits & 32) != 0,
            };
            if (Rules.Decide(true, f) == Decision.Combine) combines++;
            Assert.NotEqual(Decision.Combine, Rules.Decide(false, f));
        }
        Assert.Equal(1, combines);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]   // a loot panel appearing under the combined layout hides the column
    public void ColumnVisibility(bool combined, bool lootPanelActive, bool visible)
    {
        Assert.Equal(visible, Rules.ColumnVisible(combined, lootPanelActive));
    }
}
