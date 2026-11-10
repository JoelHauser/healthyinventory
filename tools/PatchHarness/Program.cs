using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using BepInEx.Configuration;
using BepInEx.Logging;
using EFT.HealthSystem;
using EFT.InventoryLogic;
using EFT.UI;
using HarmonyLib;
using HealthyInventory;

// Runs inside the game's mono runtime (tools/MonoHost), against the SPT-patched Assembly-CSharp and
// BepInEx's own Harmony. Compiles the plugin's real sources. What it proves, and what it cannot:
//
//   proves:  the four patches apply to the real methods with the real Harmony; the private fields
//            resolve; the decision glue reads the real controller, loot and tab fields and reaches
//            the right decision for every screen kind; a Gear-tab raid open reaches the column.
//   cannot:  anything that needs Unity's native side (no GameObjects, no layout, no rendering).
//            The column itself is only exercised in game.
internal static class Program
{
    private static int _failures;
    private static readonly List<string> Lines = new List<string>();
    private static int _nextInstanceId = 1000;

    private sealed class Capture : ILogListener
    {
        public void LogEvent(object sender, LogEventArgs e)
        {
            string s = e.Data?.ToString() ?? "";
            Lines.Add(s);
            Console.WriteLine("    log[" + e.Level + "]: " + s);
        }

        public void Dispose() { }
    }

    private static int Main(string[] args)
    {
        string work = args.Length > 0 ? args[0] : Path.GetTempPath();
        Console.WriteLine("Patch harness on " + Environment.Version + ", Harmony " + typeof(Harmony).Assembly.GetName().Version);

        // ConfigFile's static constructor reads BepInEx's paths; point them at the work folder.
        MethodInfo setPath = AccessTools.Method(typeof(BepInEx.Paths), "SetExecutablePath");
        object[] pathArgs = setPath.GetParameters().Select(p => p.ParameterType == typeof(string) ? (object)null : null).ToArray();
        pathArgs[0] = Path.Combine(work, "EscapeFromTarkov.exe");
        if (pathArgs.Length > 1) pathArgs[1] = Path.Combine(work, "BepInEx");
        setPath.Invoke(null, pathArgs);

        BepInEx.Logging.Logger.Listeners.Add(new Capture());
        Plugin.Log = BepInEx.Logging.Logger.CreateLogSource("HealthyInventory");
        var cfg = new ConfigFile(Path.Combine(work, "harness.cfg"), true);
        Plugin.Enabled = cfg.Bind("1", "Enabled", true);
        Plugin.ColumnFrame = cfg.Bind("2", "Frame", true);
        Plugin.VerboseLog = cfg.Bind("3", "Verbose", true);

        // ------------------------------------------------------------ patches apply
        var harmony = new Harmony("harness.healthyinventory");
        if (!Check("all four patches apply", () => InventoryPatches.Apply(harmony))) return Done();

        CheckPatched(AccessTools.DeclaredMethod(typeof(InventoryScreen), "Show", new[] { typeof(InventoryScreen.InventoryScreenController) }), prefixes: 1, postfixes: 0);
        CheckPatched(AccessTools.DeclaredMethod(typeof(InventoryScreen), "Close", Type.EmptyTypes), prefixes: 0, postfixes: 1);
        CheckPatched(AccessTools.DeclaredMethod(typeof(ItemsPanel), "UpdateEquipment"), prefixes: 1, postfixes: 1);

        // ------------------------------------------------------------ shells of the real types
        var items = Shell<ItemsPanel>();
        var otherItems = Shell<ItemsPanel>();   // a different screen's ItemsPanel (Scav, insurance...)
        var screen = Shell<InventoryScreen>();
        screen._itemsPanel = items;
        var loot = (CompoundItem)Shell(ConcreteSubclass(typeof(CompoundItem)));

        var tab = AccessTools.FieldRefAccess<ItemsPanel, ItemsPanel.EItemsTab>("_currentTab");
        var screenLoot = AccessTools.FieldRefAccess<InventoryScreen, CompoundItem>("_lootItem");
        FieldInfo controllerLoot = AccessTools.Field(typeof(InventoryScreen.InventoryScreenController), "LootItem");

        MethodInfo showPrefix = AccessTools.Method(typeof(InventoryPatches), "ScreenShowPrefix");
        MethodInfo closePostfix = AccessTools.Method(typeof(InventoryPatches), "ScreenClosePostfix");
        MethodInfo postfix = AccessTools.Method(typeof(InventoryPatches), "UpdateEquipmentPostfix");
        FieldInfo broken = AccessTools.Field(typeof(InventoryPatches), "_brokenThisSession");

        Func<Type, InventoryScreen.InventoryScreenController> controllerOf = t => (InventoryScreen.InventoryScreenController)Shell(t);
        Type raid = typeof(InventoryScreen.RaidInventoryScreenController);

        void Scenario(string name, Type controllerType, ItemsPanel.EItemsTab t, bool lootOnController, bool lootOnScreen,
                      bool inRaid, ItemsPanel panel, string expect, bool expectNoLine = false)
        {
            var c = controllerOf(controllerType);
            controllerLoot.SetValue(c, lootOnController ? loot : null);
            screenLoot(screen) = lootOnScreen ? loot : null;
            tab(panel) = t;
            showPrefix.Invoke(null, new object[] { screen, c });
            Lines.Clear();
            broken.SetValue(null, false);
            Console.WriteLine("  " + name);
            postfix.Invoke(null, new object[] { panel, inRaid });
            if (expectNoLine)
                Report(name + ": no decision logged", Lines.Count == 0, string.Join(" | ", Lines));
            else
                Report(name + ": " + expect, Lines.Any(l => l.Contains(expect)), string.Join(" | ", Lines));
        }

        var gear = ItemsPanel.EItemsTab.Gear;
        var health = ItemsPanel.EItemsTab.Health;
        Type lobby = typeof(InventoryScreen.LobbyInventoryScreenController);
        Type hideout = typeof(InventoryScreen.HideoutInventoryScreenController);
        Type matching = typeof(InventoryScreen.MatchmakingInventoryScreenController);

        Scenario("stash (main menu)", lobby, gear, false, false, false, items, "not the in-raid inventory");
        Scenario("hideout", hideout, gear, false, false, false, items, "not the in-raid inventory");
        Scenario("matchmaking", matching, gear, false, false, false, items, "not the in-raid inventory");
        Scenario("raid, looting a body/container", raid, gear, true, true, true, items, "vanilla layout (looting)");
        Scenario("raid, loot only on the screen (grid content)", raid, gear, false, true, true, items, "vanilla layout (looting)");
        Scenario("raid, Health tab", raid, health, false, false, true, items, "vanilla layout (Health tab)");
        Scenario("raid, another screen's ItemsPanel", raid, gear, false, false, true, otherItems, null, expectNoLine: true);

        Plugin.Enabled.Value = false;
        Scenario("raid, mod switched off", raid, gear, false, false, true, items, "mod switched off");
        Plugin.Enabled.Value = true;

        // Eligible: decides Combine, then reaches HealthColumn.For, whose first step reads
        // ItemsPanel's serialized panels. These shells have none, so For must refuse cleanly with
        // its warning and leave the inventory vanilla (no exception, feature not disabled).
        Scenario("raid, own inventory, Gear tab", raid, gear, false, false, true, items, "combined layout.");
        Report("eligible open reached the column and it refused the bare shell cleanly",
            Lines.Any(l => l.Contains("not the expected one")), string.Join(" | ", Lines));
        Report("no exception path taken", !(bool)broken.GetValue(null), "broken flag set");

        // Close forgets the screen: the next postfix must not treat the panel as the screen's.
        closePostfix.Invoke(null, new object[] { screen });
        Lines.Clear();
        tab(items) = gear;
        postfix.Invoke(null, new object[] { items, true });
        Report("after InventoryScreen.Close nothing is decided for its panel", Lines.Count == 0, string.Join(" | ", Lines));

        // ------------------------------------------------------------ diagnostics reflection
        foreach (Type t in typeof(IHealthController).Assembly.GetTypes()
                     .Where(t => !t.IsAbstract && !t.IsInterface && typeof(IHealthController).IsAssignableFrom(t)))
        {
            var hc = (IHealthController)FormatterServices.GetUninitializedObject(t);
            Console.WriteLine("  diagnostics on " + t.Name + ": " + Diagnostics.HandlerSummary(hc));
        }

        harmony.UnpatchSelf();
        return Done();
    }

    private static int Done()
    {
        Console.WriteLine(_failures == 0 ? "ALL PASSED" : _failures + " FAILED");
        return _failures == 0 ? 0 : 1;
    }

    private static bool Check(string name, Action a)
    {
        try
        {
            a();
            Report(name, true, null);
            return true;
        }
        catch (Exception e)
        {
            Report(name, false, e.ToString());
            return false;
        }
    }

    private static void Report(string name, bool ok, string detail)
    {
        Console.WriteLine((ok ? "  PASS " : "  FAIL ") + name + (ok || detail == null ? "" : "\n       " + detail));
        if (!ok) _failures++;
    }

    private static void CheckPatched(MethodBase m, int prefixes, int postfixes)
    {
        var info = Harmony.GetPatchInfo(m);
        int pre = info?.Prefixes.Count(p => p.owner == "harness.healthyinventory") ?? 0;
        int post = info?.Postfixes.Count(p => p.owner == "harness.healthyinventory") ?? 0;
        Report($"{m.DeclaringType?.Name}.{m.Name}: {pre} prefix, {post} postfix", pre == prefixes && post == postfixes, $"got {pre}/{post}");
    }

    private static T Shell<T>() => (T)Shell(typeof(T));

    /// <summary>A managed instance with no constructor run. Unity objects get a fake native
    /// pointer, so Unity's == (reference equality plus a non-zero native pointer) sees them as live.</summary>
    private static object Shell(Type t)
    {
        object o = FormatterServices.GetUninitializedObject(t);
        if (o is UnityEngine.Object)
        {
            AccessTools.Field(typeof(UnityEngine.Object), "m_CachedPtr").SetValue(o, new IntPtr(0x1000 + _nextInstanceId++));
        }
        return o;
    }

    private static Type ConcreteSubclass(Type b) =>
        b.Assembly.GetTypes().First(t => !t.IsAbstract && !t.IsGenericTypeDefinition && b.IsAssignableFrom(t));
}
