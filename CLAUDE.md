# Healthy Inventory: working notes for Claude

SPT client mod: in a raid, the player's own inventory (Tab, no loot) shows the vanilla Health
panel in the right-hand column beside gear. Brief from Joel, 2026-11-10: native look, reuse game
components, never while looting or out of raid, responsive, no leaks, distinguish tested from not.
Layout reference screenshot was a mockup from another game; only its gear-left/health-right idea
was taken.

## State

0.1.0 built 2026-11-10 on the work machine against a mirror (below). **Never run in game.**
Verified outside the game: 0-warning build, 113 xunit tests (decision table, prefab geometry at 14
screen sizes, metadata of the patched assembly), and tools/PatchHarness in the game's own mono
runtime (all four patches apply with BepInEx's Harmony 2.9; decision glue on real controller types
for every screen kind). Next step: Joel runs docs/TEST-PLAN.md.

Repo https://github.com/JoelHauser/healthyinventory.git (public), `main`. 0.1.0 is a GitHub
**pre-release** (tag `v0.1.0`) with its zip. Commit as `Joel Hauser <jhauser@bostonlightsource.com>`;
write commit messages and release notes to a file (UTF-8 without BOM) and use `-F` / `--notes-file`.

## The design in one paragraph

`ItemsPanel` (InventoryScreen's `_itemsPanel`) holds `_equipmentPanel` and `_healthPanel`
(`InventoryScreenHealthPanel`) as siblings under "Left Panel"; `UpdateEquipment` closes both and
shows one by `_currentTab` (Gear/Health). The right column "Stash Panel" (parent of
`_simpleStashPanel`/`_complexStashPanel`) is empty unless `lootItem != null`. So on an eligible
Gear-tab show, the postfix reparents `_healthPanel` into a host under Stash Panel, calls the game's
own `Show(health, inventory, skills)`, and registers `End` with `ItemsPanel.AddDisposable` (public
on UIElement). ItemsPanel disposes its UIParent at the top of every `Show` and in `Close`, so the
panel goes back on Tab, Esc, tab switch, death and raid end with no polling.

Eligible (Rules.Decide): ItemsPanel is the current InventoryScreen's; controller is
`RaidInventoryScreenController`; `inRaid` argument true; controller `LootItem` and screen
`_lootItem` both null; tab Gear. A loot panel becoming active while combined hides the column
(LootPanelRelay on both loot panels → flag → plugin LateUpdate → Refresh; a relay must not
SetActive itself, its OnDisable runs inside a hierarchy deactivation).

## Facts read off the game (patched assembly + CommonUIScene = level44, 2026-11-10)

- InventoryScreen is a **fixed 1920-unit-wide column**, centre-anchored, vertically stretched. So
  horizontal geometry never changes with aspect ratio; only height (>= 1080) does.
- Scaler: `UICanvasScalerController` sets ConstantPixelSize, factor `min(w/1920, h/1080)`, re-applied
  live by a resolution observer. No user UI-scale setting exists in this build.
- At 1920x1080 units: LeftSide x 11.6..1224.9, y 130..1032; Stash Panel x 1228..1908, y 132..1005;
  quick-slot icons top at y 119. Column = Stash width minus gap, LeftSide's top/bottom: 675x902.
- `Health Panel` stretches over `Left Panel` (anchors 0..1); body parts are fraction anchors plus
  pixel offsets, so it is laid out at Left Panel's runtime size inside a `Fit` box and scaled
  uniformly (never up). Left Panel's width is set by LeftSide's layout group at runtime (not in
  the prefab data); expected <= 675, so scale 1. The log line reports it.
- In Left Panel, "Bottom Bar" is a *later* sibling than "Health Panel"; the copy keeps that order.
- `HealthBarButton.OnDrop` → `IHealthController.ApplyItem`; hover highlight via `CanApplyItem`.
- `EffectsPanel` subscribes 5 health events in Show, removes them in CleanUp via its UI.
- `BodyPartView.Update` runs every frame (vanilla); HealthParametersPanel updates once a second.
- Scavenger/BTR transfer/insurance screens have their own ItemsPanel; ScavengerInventoryScreen is
  not an InventoryScreen.
- Health controller event fields: ActiveHealthController's are field-like (`EffectStartedEvent`);
  OfflineHealthController's are `action_N`. Diagnostics finds them by name, then by the add
  accessor's ldfld/ldflda.
- UnityEngine.Object equality here is reference equality plus `m_CachedPtr != 0`; there is no
  `m_InstanceID`. The harness fakes `m_CachedPtr` to make shells "alive".
- UI Fixes (Tyfon.UIFixes.dll, decompiled): WindowPatches on InventoryScreen.ShowWithTab/Close
  clear windows in raid; OpenSortingTable touches `_simpleStashPanel` out of raid only. No overlap.

## Building on the work machine

`C:\HUH` (SPT 4.1.6, EFT 0.16.9.40743) is unpatched, never launched, and must stay untouched. The
plugin references Assembly-CSharp, so it needs a **patched** mirror: copy `C:\HUH\BepInEx\core`
and `C:\HUH\EscapeFromTarkov_Data\Managed` into `<mirror>\BepInEx\core` and
`<mirror>\EscapeFromTarkov_Data\Managed`, keep Assembly-CSharp.dll as `.spt-bak`, apply
`C:\HUH\SPT_Runtime\SPT_Data\Launcher\Patches\SPT-core\EscapeFromTarkov_Data\Managed\Assembly-CSharp.dll.delta`
with SharpHDiffPatch.Core (`new HDiffPatch(); p.Initialize(delta); p.Patch(input, output, false, default, false, false);`).
Patched is 16,233,472 bytes. 0.1.0 used a mirror copied from a FirstShotSmoothness session's scratchpad
whose `.spt-bak` hashed identical to C:\HUH's DLL (SHA-256 43A539F5...).

```
scripts\pack.ps1 -SPTPath <mirror>                      # build, 113 tests, reference check, zip
scripts\run-harness.ps1 -SPTPath <mirror> -Mono C:\HUH\MonoBleedingEdge -Work <empty dir>
```

PowerShell, not Bash. The harness's "mono install broken / out of sync" lines are noise from
running outside Unity. Tests need `HI_MANAGED` (pack.ps1 sets it). Prefab reading: UnityPy in a
venv; MonoBehaviour typetrees are stripped, so only names and RectTransforms read; the UI is in
`level44` + `sharedassets44.assets`, not resources.assets. InventoryScreen's ItemsPanel is
path_id 22872; 23290 is the Scavenger screen's.

## Rules

- Release zip holds only `BepInEx/plugins/HealthyInventory.dll` (forward-slash entry). Version in
  the csproj and `Plugin.PluginVersion`; pack.ps1 refuses a mismatch.
- No per-frame scene searches, allocation or rebuilding. The column's LateUpdate compares three
  rect sizes; everything else is event driven.
- Any exception in the postfix turns the feature off for the session and restores vanilla.
- GUID `com.mybutthasarash.healthyinventory`, as the sibling repos.
- Don't call it 1.0 before a live run.
