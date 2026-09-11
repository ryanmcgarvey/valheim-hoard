# Hoard

One client-side Valheim mod for the inventory chores: **craft from chests**, **quick
stack / restock / sort / trash / favorites**, **equipment and quick slots**, and a live
in-game settings window on **F7**. No dependencies beyond BepInEx. Built for Valheim 1.0.

It replaces three mods that used to do this separately (AzuCraftyBoxes, Quick Stack
Store Sort Trash Restock, EquipmentAndQuickSlots) and was written when two of them
broke on the 1.0 update. Their ideas and, for the slot system, their design are the
prior art; see [Credits](#credits).

## Install

You need [BepInExPack for Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/)
(denikson's pack — the stock BepInEx download does not work with Valheim).

**With r2modman / Gale / Thunderstore Mod Manager:** download the release zip from
[Releases](../../releases) and use *Import local mod*.

**By hand:** unzip the release and copy `plugins/Hoard.dll` to
`<Valheim>/BepInEx/plugins/Hoard/Hoard.dll`. On Windows the game folder is usually
`C:\Program Files (x86)\Steam\steamapps\common\Valheim`. Start the game once; the
config file appears at `BepInEx/config/com.ryan.hoard.cfg`.

**macOS with Macheim:** Macheim has no local-mod import; install by hand as above and it
adopts the DLL as an "unmanaged" mod. It shows such mods as version 0.0.0 forever, so
check `BepInEx/LogOutput.log` for `Loading [Hoard x.y.z]` to confirm which version is
actually running. Clear the quarantine flag after copying:
`xattr -d com.apple.quarantine .../BepInEx/plugins/Hoard/Hoard.dll`.

Client-side only: the server needs nothing, and other players don't need the mod.

## What it does

| | |
|---|---|
| **Craft from chests** | Crafting, building, upgrading and single-ingredient recipes count and consume materials from containers within 20 m. Requirement rows show `have/need` including chests; the build menu shows how many pieces you can afford. Smelters, kilns, blast furnaces, fires, cooking stations and fermenters take ore/fuel/food from chests when your inventory has none. Hold **Shift** while using a station to fill it from inventory and chests. |
| **Quick stack** (`P`) | Moves stackable items into the open chest, or every chest within 10 m that already holds that item. Also replaces the game's hold-E "place stacks". |
| **Restock** (`L`) | Tops up partial stacks of ammo and food from the open chest or chests in range. |
| **Sort** (`O`) | Sorts the inventory, or the open chest, by type/name/weight/value. Favorites stay put. |
| **Trash** (`Delete` / button) | Destroys the item you're dragging, with confirmation. Quick trash destroys everything trash-flagged. |
| **Favorites** | **Alt + left-click** an item to favorite it (by name), **Alt + right-click** a slot to favorite the slot. Favorites are never stacked, sorted, stored or trashed. **Alt + Trash button** trash-flags the dragged item instead. |
| **Locked slots** | **Ctrl + right-click** a slot to lock it (red border). Nothing is ever pulled out of a locked slot: not by crafting, building, station fueling, quick stack, sort, store all or trash, and the game doesn't count its contents as available. Items still stack into it, you can still use or move them by hand, and an empty locked slot is only filled when every other cell is taken. |
| **Store all / Take all** | *Store all* button on chests (skips favorites, equipped items, hotbar, slots). *Take all* fills your inventory in order and never uses the quick slots. |
| **Equipment slots** | Dedicated cells for helmet, chest, legs, cape, utility item and trinket, next to the inventory. Drag an unworn helmet onto the Head cell to equip it; drag worn gear onto the grid to unequip. |
| **Quick slots** | Up to six hotkey cells (`Z`, `V`, `B` by default; slots 4–6 unbound) shown as a second bar next to the hotbar. |
| **Death** | The tombstone is enlarged to fit everything. On pickup, belts, your armor and the weapon/shield you held are re-equipped. Keeping slot items through death is available but off by default. |
| **Settings** | **F7** opens the settings window (scales with the game's UI scale; extra factor in *Config window scale*). Every change applies immediately and is saved. Editing the `.cfg` file while playing also applies live. |

## Multiplayer

Safe on a vanilla server. A chest is only edited after claiming ownership of it, which
is what the game itself does when you open a chest or take-all from it, and a chest
another player currently has open is never touched. Ships are excluded from area
operations by default, carts are included when nobody is pulling them, tombstones and
dungeon chests are never used.

## Coming from EquipmentAndQuickSlots

Hoard uses the same hidden-row slot layout, so a character's equipment and quick slot
items are already in the right cells. Remove EquipmentAndQuickSlots (Hoard refuses to
load next to it, or to QuickStackStore / AzuCraftyBoxes). Going back is just as safe.

## Building

The project compiles against the DLLs of your installed game and BepInEx, and reaches
private game members through `BepInEx.AssemblyPublicizer.MSBuild` at build time. No
game code is in this repository.

```
./build.sh                # build, static patch check, copy into the game's BepInEx/plugins/Hoard/
./build.sh --no-install   # build and check only
./release.sh 0.2.0        # bump version, build, package a Thunderstore-layout zip into dist/, tag, GitHub release
```

Requirements: a .NET SDK (the script looks for `~/.dotnet/dotnet`, installable without
root via `curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 10.0
--install-dir ~/.dotnet --no-path`), Valheim with BepInEx installed at
`~/.local/share/Steam/steamapps/common/Valheim` (override with `GAME=...`).

`tools/PatchCheck` inspects the built DLL with Mono.Cecil and verifies that every
Harmony patch target still exists in the game assemblies with the expected signature
and parameter names — run it after every Valheim update before launching the game. At
runtime the plugin applies its patches all-or-nothing and unpatches itself if any fail.

## Credits

- [EquipmentAndQuickSlots](https://github.com/RandyKnapp/ValheimMods) (RandyKnapp and
  contributors, MIT) — the hidden-row slot design and much of its edge-case handling.
- [Quick Stack Store Sort Trash Restock](https://www.nexusmods.com/valheim/mods/2094)
  (Goldenrevolver, MIT) — feature set and semantics of the stacking/sorting tools.
- [AzuCraftyBoxes](https://github.com/AzumattDev/AzuCraftyBoxes) (Azumatt, MIT) — the
  craft-from-containers approach.

MIT licensed.
