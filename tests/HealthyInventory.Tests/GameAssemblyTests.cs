using System.Reflection;

namespace HealthyInventory.Tests;

/// <summary>
/// The plugin compiles against the game's types, so typed members are checked by the build. These
/// tests cover what the compiler cannot: members reached by string (private fields through
/// AccessTools), the parameter names Harmony binds patch arguments by, and the shape of the
/// lifecycle the design depends on. They read the SPT-patched Assembly-CSharp as metadata only.
///
/// Set HI_MANAGED to its EscapeFromTarkov_Data\Managed folder; scripts\pack.ps1 does.
/// </summary>
public sealed class GameAssemblyTests : IDisposable
{
    private readonly MetadataLoadContext _mlc;
    private readonly Assembly _game;

    private const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    public GameAssemblyTests()
    {
        string? managed = Environment.GetEnvironmentVariable("HI_MANAGED");
        if (string.IsNullOrEmpty(managed) || !File.Exists(Path.Combine(managed, "Assembly-CSharp.dll")))
            throw new InvalidOperationException(
                "Set HI_MANAGED to the SPT-patched EscapeFromTarkov_Data\\Managed folder (scripts\\pack.ps1 does this).");

        var paths = Directory.GetFiles(managed, "*.dll");
        _mlc = new MetadataLoadContext(new PathAssemblyResolver(paths), "mscorlib");
        _game = _mlc.LoadFromAssemblyPath(Path.Combine(managed, "Assembly-CSharp.dll"));
    }

    public void Dispose() => _mlc.Dispose();

    private Type T(string name) => _game.GetType(name, throwOnError: true)!;

    private FieldInfo Field(string type, string name) =>
        T(type).GetField(name, All) ?? throw new Xunit.Sdk.XunitException($"{type}.{name} not found");

    [Fact]
    public void TheAssemblyIsThePatchedOne()
    {
        // The unpatched assembly is 15,994,432 bytes, the patched one 16,233,472 (EFT 0.16.9.40743).
        // The patched one has these BSG names readable; a wrong file fails here first.
        Assert.NotNull(_game.GetType("EFT.UI.ItemsPanel"));
        Assert.NotNull(_game.GetType("EFT.UI.Health.InventoryScreenHealthPanel"));
    }

    [Theory]
    [InlineData("EFT.UI.ItemsPanel", "_currentTab", "EFT.UI.ItemsPanel+EItemsTab")]
    [InlineData("EFT.UI.ItemsPanel", "_health", "EFT.HealthSystem.IHealthController")]
    [InlineData("EFT.UI.ItemsPanel", "_inventory", "EFT.InventoryLogic.Inventory")]
    [InlineData("EFT.UI.ItemsPanel", "_skills", "EFT.SkillManager")]
    [InlineData("EFT.UI.InventoryScreen", "_lootItem", "EFT.InventoryLogic.CompoundItem")]
    [InlineData("EFT.UI.Health.HealthParameterPanel", "_defaultColor", "UnityEngine.Color")]
    [InlineData("EFT.UI.Health.HealthParameterPanel", "_currentValue", "TMPro.TMP_Text")]
    [InlineData("EFT.UI.Health.InventoryScreenHealthPanel", "_parametersPanel", "EFT.UI.Health.HealthParametersPanel")]
    [InlineData("EFT.UI.Health.HealthParametersPanel", "_overallHealth", "EFT.UI.Health.BuffableHealthParameterPanel")]
    [InlineData("EFT.UI.Health.HealthParametersPanel", "_hydration", "EFT.UI.Health.BuffableHealthParameterPanel")]
    [InlineData("EFT.UI.Health.HealthParametersPanel", "_energy", "EFT.UI.Health.BuffableHealthParameterPanel")]
    [InlineData("EFT.UI.Health.HealthParametersPanel", "_temperature", "EFT.UI.Health.BuffableHealthParameterPanel")]
    [InlineData("EFT.UI.Health.HealthParametersPanel", "_poisoning", "EFT.UI.Health.HealthParameterPanel")]
    [InlineData("EFT.UI.Health.HealthParametersPanel", "_radiation", "EFT.UI.Health.HealthParameterPanel")]
    [InlineData("EFT.UI.Health.HealthParametersPanel", "_bloodPressure", "EFT.UI.Health.HealthParameterPanel")]
    public void PrivateFieldsReachedByNameExistWithTheirTypes(string type, string field, string fieldType)
    {
        Assert.Equal(fieldType, Field(type, field).FieldType.FullName);
    }

    [Fact]
    public void ScreenShowTakesTheControllerByTheNameThePrefixUses()
    {
        var show = T("EFT.UI.InventoryScreen").GetMethods(All)
            .Single(m => m.Name == "Show" && m.GetParameters().Length == 1);
        var p = show.GetParameters()[0];
        Assert.Equal("controller", p.Name);
        Assert.Equal("EFT.UI.InventoryScreen+InventoryScreenController", p.ParameterType.FullName);
    }

    [Fact]
    public void ScreenCloseIsDeclaredOnInventoryScreen()
    {
        Assert.Contains(T("EFT.UI.InventoryScreen").GetMethods(All), m => m.Name == "Close" && m.GetParameters().Length == 0);
    }

    [Fact]
    public void UpdateEquipmentIsUniqueAndHasAnInRaidParameter()
    {
        var m = T("EFT.UI.ItemsPanel").GetMethods(All).Where(x => x.Name == "UpdateEquipment").ToArray();
        Assert.Single(m);
        var p = m[0].GetParameters();
        Assert.Contains(p, x => x.Name == "inRaid" && x.ParameterType.FullName == "System.Boolean");
        Assert.Equal("System.Threading.Tasks.Task", m[0].ReturnType.FullName);
    }

    [Fact]
    public void TheRaidControllerIsItsOwnTypeAndTheOthersAreNotIt()
    {
        var raid = T("EFT.UI.InventoryScreen+RaidInventoryScreenController");
        foreach (var other in new[] { "LobbyInventoryScreenController", "HideoutInventoryScreenController", "MatchmakingInventoryScreenController" })
            Assert.False(raid.IsAssignableFrom(T("EFT.UI.InventoryScreen+" + other)), other);
    }

    [Fact]
    public void ScavengerScreenIsNotAnInventoryScreen()
    {
        // Its ItemsPanel is a different object; the screen check keeps it vanilla.
        Assert.False(T("EFT.UI.InventoryScreen").IsAssignableFrom(T("EFT.UI.ScavengerInventoryScreen")));
    }

    [Theory]
    [InlineData("_healthPanel", "EFT.UI.Health.InventoryScreenHealthPanel")]
    [InlineData("_simpleStashPanel", "EFT.UI.SimpleStashPanel")]
    [InlineData("_complexStashPanel", "EFT.UI.DragAndDrop.ComplexStashPanel")]
    [InlineData("_equipmentPanel", "EFT.UI.EquipmentTab")]
    public void ItemsPanelHoldsBothColumnsContents(string field, string type)
    {
        Assert.Equal(type, Field("EFT.UI.ItemsPanel", field).FieldType.FullName);
    }

    [Fact]
    public void TreatmentGoesThroughTheHealthControllersOwnChecks()
    {
        // HealthBarButton is the drop target on each body part; it must still be the game's
        // drop handler for the reused panel to keep medical drag-and-drop.
        var b = T("EFT.UI.Health.HealthBarButton");
        Assert.Contains(b.GetInterfaces(), i => i.FullName == "UnityEngine.EventSystems.IDropHandler");
        var ihc = T("EFT.HealthSystem.IHealthController");
        var all = ihc.GetMethods().Concat(ihc.GetInterfaces().SelectMany(i => i.GetMethods())).Select(m => m.Name).ToHashSet();
        Assert.Contains("CanApplyItem", all);
        Assert.Contains("ApplyItem", all);
    }

    [Fact]
    public void UIElementLetsAnyoneAddADisposable()
    {
        var m = T("EFT.UI.UIElement").GetMethod("AddDisposable", BindingFlags.Instance | BindingFlags.Public, new[] { _mlc.CoreAssembly!.GetType("System.Action")! });
        Assert.NotNull(m);
    }
}
