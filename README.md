# Healthy Inventory

An SPT client mod. When you open your own inventory in a raid, your health is already there: the
game's Health panel (body diagram, each body part's HP, bleeds, fractures and other effects,
overall HP, hydration, energy, temperature) sits in the right-hand column, which is empty for your
own inventory in raid. Gear, weapons, rig, pockets and backpack stay where they always are. You
can drag medical items onto body parts there, exactly as on the Health tab.

It only appears there. Looting a body, a container or a loose bag shows the normal looting layout
and its full loot space. The stash, hideout, main menu, traders and every out-of-raid screen are
untouched. The Health tab itself still works as before.

**Status: 0.1.5, being tested in game.** See
[docs/TEST-PLAN.md](docs/TEST-PLAN.md).

## Requirements

SPT 4.1.x (built against SPT 4.1.6, EFT client 0.16.9.40743), BepInEx 5 as shipped with SPT.

## Install

Extract the zip into the SPT folder, so that `BepInEx/plugins/HealthyInventory.dll` exists.

## Settings (F12)

| Setting | Default | |
| --- | --- | --- |
| Show health beside gear in raid | on | Off restores the vanilla layout at once. |
| Column background and frame | on | Draws the gear column's own background, bottom strip and border behind the health column. |
| Log layout decisions | on | One line per inventory open in `BepInEx/LogOutput.log`, plus a layout report (every rectangle, and a check that nothing clips or overlaps) on the first open of a session and after a resize. For testing. |

## How it works

In the column, overall HP, hydration and energy are left out, since the gear column's own
bottom strip shows them right beside it; toxicity and radiation move down into their places.

Nothing is drawn by the mod. The game already has the Health panel inside the inventory screen, in
the same place as the gear slots, and shows one or the other depending on the Gear/Health tab. On
the Gear tab of your own in-raid inventory, the mod moves that panel into the right-hand column,
shows it with the game's own code, and puts it back when the inventory closes or the tab changes.
So its numbers, icons, tooltips, drop highlights and treatment rules are the game's.

## Screen sizes

EFT scales its whole UI by `min(width/1920, height/1080)`, and the inventory is always a block
1920 units wide (wider screens get empty margins, as in vanilla). The health column is therefore
always the same width, 675 units, and at least 902 tall, exactly the height of the gear column
beside it. The panel is shown at its native size and fills the column's height; with an
ultrawide-inventory mod that widens the gear column, it still fits the column. Resolution and window-size changes take effect while the inventory is
open. There is no UI-scale setting in this EFT version to account for.

## Compatibility

- **UI Fixes:** no overlapping hooks found by reading its code; not yet run together.
- **CutContent / Re-Classic 1.3.0** (and its dependency Search Open Containers 1.5.0): no
  overlapping hooks found by reading their code; not yet run together. Its blood pressure,
  radiation and poison readouts and its extra effect icons appear in the health column, since they
  attach to the game's own panel wherever it is. Its body search, open-without-search containers
  and Search on open containers all still end in the normal looting layout.
- Mods that rebuild the inventory screen's layout: if the panels are not where the game puts them,
  the mod logs a warning and leaves the inventory vanilla.
- Container windows you open float over the health column, as they float over the loot panel.
- **Fika 2.4.6:** no overlapping hooks found by reading its code; not yet run together. Raids under
  Fika open the inventory through the same game code as single-player ones.
- **Scav raids** work the same as PMC raids (same game code path).

## Known limitations

- Not yet run in game; see the test plan.
