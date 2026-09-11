# Changelog

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
