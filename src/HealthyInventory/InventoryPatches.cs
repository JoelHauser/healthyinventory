using System;
using System.Reflection;
using EFT;
using EFT.HealthSystem;
using EFT.InventoryLogic;
using EFT.UI;
using HarmonyLib;

namespace HealthyInventory
{
    /// <summary>
    /// Four hooks, all on BSG-named members that the SPT delta leaves alone:
    ///
    ///   InventoryScreen.Show(InventoryScreenController)  prefix   remember which screen and controller
    ///   InventoryScreen.Close()                           postfix  forget them (raid end, death, Tab)
    ///   ItemsPanel.UpdateEquipment(...)                   prefix   hand the health panel back first
    ///   ItemsPanel.UpdateEquipment(...)                   postfix  decide, and borrow it if eligible
    ///
    /// UpdateEquipment is only called from ItemsPanel.Show, which every open and every Gear/Health
    /// tab switch goes through, and which disposes ItemsPanel's UI first. It is async: Harmony's
    /// postfix runs when the method returns its Task, after the synchronous part that closes
    /// _healthPanel and shows the equipment panel, so the postfix always sees the panel closed.
    /// </summary>
    internal static class InventoryPatches
    {
        private static InventoryScreen _screen;
        private static InventoryScreen.InventoryScreenController _controller;
        private static bool _brokenThisSession;

        // Private in the game. Resolved once; a missing name fails Apply, not a frame.
        private static AccessTools.FieldRef<ItemsPanel, ItemsPanel.EItemsTab> _currentTab;
        private static AccessTools.FieldRef<ItemsPanel, IHealthController> _health;
        private static AccessTools.FieldRef<ItemsPanel, Inventory> _inventory;
        private static AccessTools.FieldRef<ItemsPanel, SkillManager> _skills;
        private static AccessTools.FieldRef<InventoryScreen, CompoundItem> _screenLoot;

        internal static void Apply(Harmony harmony)
        {
            _currentTab = AccessTools.FieldRefAccess<ItemsPanel, ItemsPanel.EItemsTab>("_currentTab");
            _health = AccessTools.FieldRefAccess<ItemsPanel, IHealthController>("_health");
            _inventory = AccessTools.FieldRefAccess<ItemsPanel, Inventory>("_inventory");
            _skills = AccessTools.FieldRefAccess<ItemsPanel, SkillManager>("_skills");
            _screenLoot = AccessTools.FieldRefAccess<InventoryScreen, CompoundItem>("_lootItem");

            MethodInfo show = Require(AccessTools.DeclaredMethod(typeof(InventoryScreen), nameof(InventoryScreen.Show),
                new[] { typeof(InventoryScreen.InventoryScreenController) }), "InventoryScreen.Show(controller)");
            MethodInfo close = Require(AccessTools.DeclaredMethod(typeof(InventoryScreen), nameof(InventoryScreen.Close), Type.EmptyTypes),
                "InventoryScreen.Close()");
            MethodInfo update = Require(AccessTools.DeclaredMethod(typeof(ItemsPanel), nameof(ItemsPanel.UpdateEquipment)),
                "ItemsPanel.UpdateEquipment");

            harmony.Patch(show, prefix: new HarmonyMethod(typeof(InventoryPatches), nameof(ScreenShowPrefix)));
            harmony.Patch(close, postfix: new HarmonyMethod(typeof(InventoryPatches), nameof(ScreenClosePostfix)));
            harmony.Patch(update,
                prefix: new HarmonyMethod(typeof(InventoryPatches), nameof(UpdateEquipmentPrefix)),
                postfix: new HarmonyMethod(typeof(InventoryPatches), nameof(UpdateEquipmentPostfix)));
        }

        private static MethodInfo Require(MethodInfo m, string what) =>
            m ?? throw new MissingMethodException("Not found in this game build: " + what);

        private static void ScreenShowPrefix(InventoryScreen __instance, InventoryScreen.InventoryScreenController controller)
        {
            _screen = __instance;
            _controller = controller;
        }

        private static void ScreenClosePostfix(InventoryScreen __instance)
        {
            if (__instance != _screen) return;
            // ItemsPanel.Close has already run through CloseAction and handed the panel back;
            // this is the backstop for any path that skips it.
            HealthColumn.EndFor(__instance._itemsPanel);
            _screen = null;
            _controller = null;
        }

        private static void UpdateEquipmentPrefix(ItemsPanel __instance)
        {
            try
            {
                HealthColumn.EndFor(__instance);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Restoring the health panel failed: {e}");
            }
        }

        private static void UpdateEquipmentPostfix(ItemsPanel __instance, bool inRaid)
        {
            if (_brokenThisSession) return;
            try
            {
                var facts = new ScreenFacts
                {
                    PanelBelongsToInventoryScreen = _screen != null && _screen._itemsPanel == __instance,
                    RaidController = _controller is InventoryScreen.RaidInventoryScreenController,
                    ControllerHasLoot = _controller != null && _controller.LootItem != null,
                    ScreenHasLoot = _screen != null && _screenLoot(_screen) != null,
                    GearTab = _currentTab(__instance) == ItemsPanel.EItemsTab.Gear,
                    InRaidArgument = inRaid,
                };
                Decision d = Rules.Decide(Plugin.Enabled.Value, facts);

                if (Plugin.VerboseLog.Value && facts.PanelBelongsToInventoryScreen)
                    Plugin.Log.LogInfo($"Inventory panel shown ({_controller?.GetType().Name ?? "no controller"}, " +
                                       $"{_currentTab(__instance)} tab): {Describe(d)}");

                if (d != Decision.Combine) return;

                HealthColumn column = HealthColumn.For(__instance);
                if (column == null) return;

                column.Begin(_health(__instance), _inventory(__instance), _skills(__instance));
                // Tied to ItemsPanel's own disposables: runs on Close (Tab, Esc, death, raid end,
                // tab switch) and at the top of the next Show.
                __instance.AddDisposable(column.End);
            }
            catch (Exception e)
            {
                _brokenThisSession = true;
                Plugin.Log.LogError($"Combined layout failed and is off until restart; the inventory stays vanilla. {e}");
                try { HealthColumn.EndFor(__instance); } catch { /* already logged */ }
            }
        }

        private static string Describe(Decision d)
        {
            switch (d)
            {
                case Decision.Combine: return "combined layout.";
                case Decision.Disabled: return "vanilla layout (mod switched off).";
                case Decision.NotRaid: return "vanilla layout (not the in-raid inventory).";
                case Decision.Looting: return "vanilla layout (looting).";
                case Decision.HealthTab: return "vanilla layout (Health tab).";
                default: return "vanilla layout (" + d + ").";
            }
        }
    }
}
