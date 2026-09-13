# Changelog

## 0.6.5 — 2026-09-12

- Fix: the row-planting preview threw an exception every frame (the extra ghosts were
  plain copies of the placement ghost, whose Plant script expects a live network view).
  Copies are now visual-only. Exceptions in the preview are caught and logged instead of
  escaping into the game's frame loop.
- build.sh installs atomically and refuses to install while Valheim is running.
  Overwriting Hoard.pdb in place under a live game made Mono abort on the next stack
  trace.

## 0.6.4 — 2026-09-12

- Fix: row planting placed only one or two extra plants. Paying for a plant changes the
  inventory, which makes the game rebuild the placement ghost mid-placement and wiped the
  list of positions still to plant. The list is now snapshotted first.
- Fix: the row direction still changed after planting. The same ghost rebuild re-rolls the
  rotation of random-rotation pieces; the rotation is now kept across rebuilds too.

## 0.6.3 — 2026-09-12

- The Sort/Stack/Restock/Trash buttons moved into Hoard's own panel, in a row under the
  quick slots (or under the equipment cells), on their own background strip. Full-size
  labels again; nothing overlaps the armor/weight readouts or the chest panel.

## 0.6.2 — 2026-09-12

- Fix: row planting kept changing direction. Plants take a random rotation after every
  placement, which spun the row axis; the rotation you set is now kept (*Keep rotation*
  in section 14).

## 0.6.1 — 2026-09-12

- Recycling moved from an inventory button to a **Recycle tab** on the crafting panel,
  available only while standing at a crafting station. Drop items onto the tab's panel.
- The inventory's Sort/Stack/Restock/Trash column now fits between the armor and weight
  readouts instead of covering the weight.

## 0.6.0 — 2026-09-12

- New: **Recycle** button in the inventory (section *15 - Recycling*). Drag an item onto
  it to get back the materials its recipe and upgrades cost. Anything the game won't let
  through a portal (metals, ores, other non-teleportable items) is forfeited, never
  returned, so recycling is not a way around hauling metal. Confirmation shows both lists.

## 0.5.0 — 2026-09-12

- Running on a made surface (any surface with a speed bonus) drains 50% stamina by
  default; *Run stamina on made surfaces %* in section 13.
- New: **row planting** with the cultivator (section *14 - Row planting*). Alt + scroll
  sets row length, Alt + Shift + scroll sets rows, N toggles fill mode. Extra ghosts
  preview the placement, invalid or unaffordable spots are red and skipped; each extra
  plant is paid for (chests count when craft-from-containers is on).

## 0.4.0 — 2026-09-12

- New: **surface run speed**. Running is faster on made surfaces: dirt paths +50%, paved
  paths +70%, wood floors +60%, stone floors +70% by default; cultivated soil and iron
  grates have their own (default 0% and 60%). Section *13 - Surface run speed*.

## 0.3.0 — 2026-09-11

- Favorites are gone; locked slots are the one mechanism. Ctrl + right-click a slot to
  lock it. Automatic actions (quick stack, sort, store all, trash) never touch a locked
  slot. Crafting, building and stations still can, but prefer unlocked stacks, then
  nearby chests, and use a locked slot only when nothing else covers the cost. The
  game's requirement counts include locked slots again, so a craft that only a locked
  stack can pay for shows as affordable.
- Trash-flagging an item is now Ctrl + click the Trash button while dragging it.
- Per-character data moved to `Hoard_locks_<id>.txt` (favorites files are no longer read).

## 0.2.2 — 2026-09-11

- Fix: with a chest open, the button rows collided. The inventory's Sort/Stack/Restock/
  Trash buttons now form a column in the inventory panel's right-hand strip between the
  armor and weight readouts; the chest's Store all sits beside Take All with Stack/
  Restock/Sort in a row directly below, above the chest grid. Take All keeps its size.

## 0.2.1 — 2026-09-11

- Fix: the Sort/Stack/Restock/Trash row was duplicated under the equipment panel and the
  quick slot panel (their backgrounds are clones of the inventory background, which now
  carried the buttons). Only the inventory panel has the row.

## 0.2.0 — 2026-09-11

- New: **locked slots**. Ctrl + right-click a slot to lock it (red border). Nothing is
  pulled out of a locked slot by crafting, building, station fueling, quick stack, sort,
  store all or trash, and the game doesn't count its contents as available. Items still
  stack into it, and an empty locked slot is filled only when every other cell is taken.
  Toggle key and color are configurable.
- Settings window scales with the game's UI scale (it was drawn in native pixels, tiny
  on high-DPI screens) and is larger by default; *Config window scale* adds a factor.
- Equipment slots are an aligned 2x3 grid instead of the staggered paperdoll layout.
- The Sort / Stack / Restock / Trash buttons sit in a row tucked under the bottom edge of
  the inventory panel.

## 0.1.1 — 2026-09-11

- Fix: the plugin did not load at all. The config key `Don't auto-pickup trash` contains
  an apostrophe, which BepInEx forbids in section/key names, so `ConfigFile.Bind` threw
  inside `Awake` before any patch was applied. Renamed to `Do not auto-pickup trash`.
  (Reported from the first install on another machine.)
- The build's static check now also rejects config section/key names with characters
  BepInEx forbids.

## 0.1.0 — 2026-09-11

First release. One client-side plugin replacing AzuCraftyBoxes, QuickStackStore and
EquipmentAndQuickSlots on Valheim 1.0:

- Craft, build, upgrade and fuel stations from chests within range.
- Quick stack, restock, sort, trash, favorites, store all, ordered take all.
- Equipment slots and up to six quick slots (same cell layout as EquipmentAndQuickSlots,
  so existing characters keep their slot items).
- In-game settings window on F7; everything applies live.
- Patches apply all-or-nothing: a game update that removes a patch target disables the
  mod cleanly instead of leaving it half-applied.
