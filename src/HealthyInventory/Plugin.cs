using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace HealthyInventory
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.mybutthasarash.healthyinventory";
        public const string PluginName = "Healthy Inventory";

        // Must match the csproj <Version>; scripts\pack.ps1 refuses to pack if they differ.
        public const string PluginVersion = "0.1.2";

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> ColumnFrame;
        internal static ConfigEntry<bool> VerboseLog;

        private Harmony _harmony;
        private bool _active;

        private void Awake()
        {
            Log = Logger;

            Enabled = Config.Bind("1. General", "Show health beside gear in raid", true,
                "When you open your own inventory in a raid (not while looting), show the Health panel in the right-hand column. " +
                "Turning it off restores the vanilla layout at once; turning it on applies the next time the inventory opens.");
            ColumnFrame = Config.Bind("2. Look", "Column background and frame", true,
                "Draw the gear column's own background, bottom strip and border behind the health column.");
            VerboseLog = Config.Bind("3. Diagnostics", "Log layout decisions", true,
                "Write one line to BepInEx\\LogOutput.log each time the inventory panel opens, saying which layout was used and why, " +
                "with open/close counts and effect handler counts for checking that nothing accumulates.");

            Enabled.SettingChanged += (_, __) => { if (!Enabled.Value) HealthColumn.EndCurrent(); };
            ColumnFrame.SettingChanged += (_, __) => HealthColumn.ApplyDecorationsToCurrent();

            _harmony = new Harmony(PluginGuid);
            try
            {
                InventoryPatches.Apply(_harmony);
                _active = true;
                Log.LogInfo($"{PluginName} {PluginVersion} loaded.");
            }
            catch (Exception e)
            {
                _harmony.UnpatchSelf();
                Log.LogError($"{PluginName} {PluginVersion} could not hook the inventory screen and is off; the game is unchanged. {e}");
            }
        }

        private void LateUpdate()
        {
            if (_active) HealthColumn.PumpRefresh();   // one bool check unless a loot panel just toggled
        }
    }
}
