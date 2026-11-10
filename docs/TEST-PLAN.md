# Healthy Inventory: in-game test plan

Version 0.1.0 has **never been run in game**. Everything below is still to be checked. Numbers in
brackets refer to the brief's eight validation points.

## Setup

1. Install `releases/HealthyInventory-0.1.0.zip` into the live SPT install (`H:\SPT4.1.X`), or
   `scripts\pack.ps1 -SPTPath H:\SPT4.1.X -InstallTo H:\SPT4.1.X` there.
2. Keep **Log layout decisions** on (F12, `3. Diagnostics`; it is on by default).
3. Open `BepInEx\LogOutput.log`. Expect `Healthy Inventory 0.1.0 loaded.` and no line containing
   `could not hook` or `failed`.

## A. Out of raid stays vanilla [6]

| Step | Expected log line | Look for |
| --- | --- | --- |
| Main menu → Character (stash) | `(LobbyInventoryScreenController, Gear tab): vanilla layout (not the in-raid inventory)` | Stash on the right, as before |
| Hideout → open stash | `(HideoutInventoryScreenController, Gear tab): vanilla layout (not the in-raid inventory)` | As before |
| Traders, flea, insurance, Scav transfer after a raid | none (different screens) | As before |
| Character → Health tab | `(LobbyInventoryScreenController, Health tab): vanilla layout (not the in-raid inventory)` | Vanilla Health tab |

## B. Own inventory in raid [1]

Press Tab in a raid with nothing looted.

- Log, first time only: `Health column built (background copied, bottom bar copied, border copied).`
  Any `missing` means that decoration is absent and needs a look.
- Log: `(RaidInventoryScreenController, Gear tab): combined layout.`, then
  `Combined layout on: health column 675x902, panel WxH at scale 1. Opens 1, closes 0. Effect handlers: started N, removed M.`
  **Write down N and M** for section G.
- Gear and weapons left, rig/pockets/backpack in the middle, health on the right, all at once, no click.
- Top of the health column level with the top of the gear column; bottom above the quick-slot icons.
- Body diagram undistorted; seven body parts with values; overall HP, hydration, energy,
  temperature and the rest along its bottom.
- The column background, bottom strip and frame look like the gear column's. If they look wrong,
  turn off **Column background and frame** (F12, `2. Look`) and note what was wrong.
- Nothing overlaps the containers column or the quick-slot bar.

## C. Live updates while open [2]

Easiest with Inventory Walker installed (walk with the inventory open); otherwise open it right
after each event.

1. Take a hit that bleeds; open the inventory: bleed icon on the part, HP ticking down live.
2. Drink water / eat: hydration/energy rise within about a second (the parameters panel updates once a second, as vanilla).
3. Take a painkiller: the effect icon appears and its timer counts down.
4. Fall damage or a fracture with the inventory open (Inventory Walker): values and icons change without reopening.

## D. Medical items and inventory interactions [3]

1. Drag a bandage from the rig onto the bleeding part: the part highlights on hover, the drop
   treats it, the icon clears. Same timing and sounds as the vanilla Health tab.
2. Drag ammo or a gun onto a body part: no highlight, nothing happens.
3. Splint on a fracture, surgical kit on a blacked-out part (if available), a medkit onto a part.
4. Right-click a medkit → Use: works as vanilla.
5. Drag items between pockets, rig and backpack; open a case (its window may float over the
   health column, as it does over the loot panel); inspect an item; context menus; equip and
   unequip a backpack and a rig; swap a weapon. All as vanilla.
6. Drop an item onto empty space in the health column: nothing happens, no error.

## E. Looting keeps the looting layout [4]

| Loot | Expected log line | Look for |
| --- | --- | --- |
| A body | `vanilla layout (looting)` | Body's gear on the right, as before, no health column |
| A container (crate, jacket) | `vanilla layout (looting)` | Vanilla loot grid |
| A backpack on the ground (Search) | `vanilla layout (looting)` | Vanilla loot grid |

No `Combined layout on` line while looting.

## F. Switching back and forth [5]

Ten times in a row: loot a body → close → Tab → close → open a container → close → Tab.

- Every Tab: `combined layout.` and the health column.
- Every loot: `vanilla layout (looting)` and no health column.
- Gear → Health tab → Gear in one open: Health tab shows the vanilla health panel on the left
  (log `Combined layout off; health panel returned to Left Panel.`), Gear shows the combined layout again.

## G. Repeated opening and closing [8]

Open and close the inventory twenty times, then look at the last `Combined layout on` line.

- `Opens` and `closes` go up by one per open/close; `closes` is one behind `opens` while open.
- **Effect handlers N and M are the same on every open** as in section B. A number that grows
  each open is a leak.
- No duplicate health panels (one silhouette), no stale values after taking damage between opens.
- No frame-time change with the inventory open versus before installing (the column does three
  rect reads a frame while open, and nothing while closed).

## H. Resolutions and window sizes [7]

For each of 1920x1080, 2560x1440, 1920x1200 (16:10), 2560x1080 or 3440x1440 (21:9),
1280x1024 (5:4) and 1280x720: set it in the game settings, start or continue a raid, press Tab.

- `Combined layout on: ... scale 1` (anything below 1 means Left Panel is wider than expected; note the numbers).
- Column height grows with taller aspect ratios (16:10, 5:4); width stays the same.
- On 21:9 and wider, the inventory stays a 1920-unit block in the middle (vanilla behaviour), with the column at its right.
- In windowed mode, resize the window with the inventory open: one `Layout changed; health column now ...`
  line and the column follows without reopening.

## I. Raid end and death

1. Die with the inventory open; next raid: Tab works and the log shows a fresh `Combined layout on`.
2. Extract with the inventory open; back in the stash: vanilla layout (section A).

## J. With UI Fixes installed

Repeat B, D5, E and F. Static reading says there is no overlap (its window save/restore is off in
raid, and its stash-panel hook is for the sorting table out of raid), but they have never run together.

## K. Settings while open

- Turn **Show health beside gear in raid** off with the inventory open: the column disappears at
  once and the gear column is vanilla. Back on: applies on the next open.
- Toggle **Column background and frame**: changes at once.
