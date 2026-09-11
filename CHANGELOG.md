# Changelog

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
