using System;
using BepInEx.Configuration;
using UnityEngine;

namespace Hoard
{
    // Every setting the mod has. All of them are read live (nothing is cached at startup
    // unless noted), so a change from the in-game window, the config file on disk, or
    // another config editor applies immediately.
    public static class HoardConfig
    {
        public static ConfigFile File;

        // ---- General
        public static ConfigEntry<KeyboardShortcut> ConfigWindowKey;
        public static ConfigEntry<float> ConfigWindowScale;
        public static ConfigEntry<bool> DebugLogging;

        // ---- Craft from containers
        public static ConfigEntry<bool> CraftFromContainers;
        public static ConfigEntry<float> CraftRange;
        public static ConfigEntry<bool> CraftLeaveOne;
        public static ConfigEntry<bool> CraftFuelStations;
        public static ConfigEntry<KeyboardShortcut> FillAllKey;
        public static ConfigEntry<bool> ShowContainerCounts;
        public static ConfigEntry<bool> ShowBuildCount;

        // ---- Containers (which chests area operations may touch)
        public static ConfigEntry<bool> ContainersIncludeCarts;
        public static ConfigEntry<bool> ContainersIncludeShips;
        public static ConfigEntry<bool> ContainersIncludeNonPlayerBuilt;

        // ---- Quick stack
        public static ConfigEntry<bool> QuickStackEnabled;
        public static ConfigEntry<KeyboardShortcut> QuickStackKey;
        public static ConfigEntry<float> QuickStackRange;
        public static ConfigEntry<bool> QuickStackIncludesHotbar;
        public static ConfigEntry<bool> QuickStackOnlyToOpenContainer;
        public static ConfigEntry<bool> QuickStackTrophiesTogether;
        public static ConfigEntry<bool> QuickStackReplacesHoldToStack;
        public static ConfigEntry<bool> QuickStackMessages;

        // ---- Restock
        public static ConfigEntry<bool> RestockEnabled;
        public static ConfigEntry<KeyboardShortcut> RestockKey;
        public static ConfigEntry<float> RestockRange;
        public static ConfigEntry<bool> RestockIncludesHotbar;
        public static ConfigEntry<bool> RestockOnlyFromOpenContainer;
        public static ConfigEntry<bool> RestockOnlyAmmoAndConsumables;
        public static ConfigEntry<int> RestockStackLimit;
        public static ConfigEntry<bool> RestockMessages;

        // ---- Sort
        public static ConfigEntry<bool> SortEnabled;
        public static ConfigEntry<KeyboardShortcut> SortKey;
        public static ConfigEntry<SortCriteria> SortBy;
        public static ConfigEntry<bool> SortAscending;
        public static ConfigEntry<bool> SortIncludesHotbar;
        public static ConfigEntry<bool> SortMergesStacks;
        public static ConfigEntry<bool> SortBothWhenContainerOpen;
        public static ConfigEntry<AutoSort> SortOnOpen;

        // ---- Trash
        public static ConfigEntry<bool> TrashEnabled;
        public static ConfigEntry<KeyboardShortcut> TrashKey;
        public static ConfigEntry<KeyboardShortcut> QuickTrashKey;
        public static ConfigEntry<bool> TrashConfirm;
        public static ConfigEntry<bool> QuickTrashConfirm;
        public static ConfigEntry<bool> TrashCanAffectHotbar;
        public static ConfigEntry<bool> TrophiesAreTrash;
        public static ConfigEntry<bool> NoAutoPickupOfTrash;

        // ---- Locked slots
        public static ConfigEntry<KeyboardShortcut> LockModifier;
        public static ConfigEntry<Color> LockedSlotColor;
        public static ConfigEntry<bool> LockTooltips;
        public static ConfigEntry<Color> TrashFlagColor;

        // ---- Store / take all
        public static ConfigEntry<bool> StoreAllButton;
        public static ConfigEntry<KeyboardShortcut> StoreAllKey;
        public static ConfigEntry<bool> StoreAllIncludesHotbar;
        public static ConfigEntry<bool> StoreAllIncludesEquipped;
        public static ConfigEntry<bool> TakeAllInOrder;

        // ---- Buttons
        public static ConfigEntry<bool> ShowButtons;
        public static ConfigEntry<bool> HideVanillaStackAllButton;

        // ---- Slots
        public static ConfigEntry<bool> EquipmentSlots;
        public static ConfigEntry<bool> QuickSlots;
        public static ConfigEntry<int> QuickSlotCount;
        public static ConfigEntry<int> ExtraInventoryRows;
        public static readonly ConfigEntry<KeyboardShortcut>[] QuickSlotKeys = new ConfigEntry<KeyboardShortcut>[Slots.MaxQuickSlots];
        public static readonly ConfigEntry<string>[] QuickSlotLabels = new ConfigEntry<string>[Slots.MaxQuickSlots];
        public static ConfigEntry<Vector2> PanelPosition;
        public static ConfigEntry<bool> PanelDraggable;
        public static ConfigEntry<KeyboardShortcut> PanelDragKey;
        public static ConfigEntry<TextAnchor> QuickBarAnchor;
        public static ConfigEntry<Vector2> QuickBarPosition;
        public static ConfigEntry<bool> ProtectSlotsFromStackAll;
        public static ConfigEntry<bool> NoAutoPickupIntoQuickSlots;
        public static ConfigEntry<bool> SlotBackup;

        // ---- Death
        public static ConfigEntry<bool> KeepEquipmentOnDeath;
        public static ConfigEntry<bool> KeepQuickSlotsOnDeath;
        public static ConfigEntry<bool> ReequipArmorOnPickup;
        public static ConfigEntry<bool> ReequipBeltOnPickup;
        public static ConfigEntry<bool> ReequipWeaponsOnPickup;

        // ---- Surface run speed
        public static ConfigEntry<bool> SurfaceSpeedEnabled;
        public static ConfigEntry<float> SpeedDirtPath, SpeedPavedPath, SpeedCultivated, SpeedWood, SpeedStone, SpeedMetal;
        public static ConfigEntry<float> SpeedStaminaPercent;

        // ---- Row planting
        public static ConfigEntry<bool> RowPlantingEnabled;
        public static ConfigEntry<KeyboardShortcut> RowModifier, RowsModifier, RowFillKey;
        public static ConfigEntry<int> RowMaxLength, RowMaxRows;
        public static ConfigEntry<float> RowSpacing;
        public static ConfigEntry<bool> RowKeepRotation;

        // ---- Recycling
        public static ConfigEntry<bool> RecycleEnabled, RecycleConfirm, RecycleConsumables;
        public static ConfigEntry<float> RecycleRate;
        public static ConfigEntry<string> RecycleNeverReturn;
        public static ConfigEntry<KeyboardShortcut> RecycleKey;

        public enum SortCriteria { Type, Name, Weight, Value, InternalName }
        public enum AutoSort { Never, Inventory, Container, Both }

        private static readonly KeyCode[] DefaultQuickKeys = { KeyCode.Z, KeyCode.V, KeyCode.B, KeyCode.None, KeyCode.None, KeyCode.None };

        public static void Bind(ConfigFile cfg)
        {
            File = cfg;
            cfg.SaveOnConfigSet = true;

            string s;

            s = "1 - General";
            ConfigWindowKey = cfg.Bind(s, "Config window key", new KeyboardShortcut(KeyCode.F7), "Opens the in-game settings window for this mod. Everything applies live.");
            ConfigWindowScale = cfg.Bind(s, "Config window scale", 1f, new ConfigDescription("Extra scale for the settings window on top of the game's own UI scale.", new AcceptableValueRange<float>(0.5f, 2.5f)));
            DebugLogging = cfg.Bind(s, "Debug logging", false, "Verbose log output for troubleshooting.");

            s = "2 - Craft from containers";
            CraftFromContainers = cfg.Bind(s, "Enabled", true, "Crafting, building, upgrading and station fueling may take materials from nearby containers.");
            CraftRange = cfg.Bind(s, "Range", 20f, new ConfigDescription("How far (metres) from the player a container may be to be used for crafting.", new AcceptableValueRange<float>(1f, 100f)));
            CraftLeaveOne = cfg.Bind(s, "Leave one", false, "Never take the last item of a stack out of a container (keeps chests 'labelled').");
            CraftFuelStations = cfg.Bind(s, "Feed stations", true, "Smelters, kilns, blast furnaces, fireplaces, cooking stations and fermenters may also pull ore, fuel and food from nearby containers when your inventory has none.");
            FillAllKey = cfg.Bind(s, "Fill all modifier", new KeyboardShortcut(KeyCode.LeftShift), "Hold this while using a smelter/kiln/fire/cooking station to add as much as fits, from inventory and containers.");
            ShowContainerCounts = cfg.Bind(s, "Show combined counts", true, "Requirement lists show 'have/need' where 'have' includes nearby containers.");
            ShowBuildCount = cfg.Bind(s, "Show buildable count", true, "The build menu shows how many of the selected piece you can afford.");

            s = "3 - Containers";
            ContainersIncludeCarts = cfg.Bind(s, "Include carts", true, "Area operations (crafting, quick stack, restock) may use carts that nobody is pulling.");
            ContainersIncludeShips = cfg.Bind(s, "Include ships", false, "Area operations may use ship cargo holds. Off by default: a ship hold is usually the one chest you don't want your junk stacked into.");
            ContainersIncludeNonPlayerBuilt = cfg.Bind(s, "Include world containers", false, "Area operations may use containers that were not built by a player (dungeon chests, wrecks).");

            s = "4 - Quick stack";
            QuickStackEnabled = cfg.Bind(s, "Enabled", true, "Move stackable items from your inventory into nearby containers that already hold that item. Locked slots are never touched.");
            QuickStackKey = cfg.Bind(s, "Key", new KeyboardShortcut(KeyCode.P), "Quick stack hotkey. Works with the inventory open or closed.");
            QuickStackRange = cfg.Bind(s, "Range", 10f, new ConfigDescription("Containers within this many metres are stacked into. 0 = only the open container.", new AcceptableValueRange<float>(0f, 50f)));
            QuickStackIncludesHotbar = cfg.Bind(s, "Include hotbar", false, "Also stack items out of the hotbar row.");
            QuickStackOnlyToOpenContainer = cfg.Bind(s, "Only open container when one is open", true, "With a chest open, the hotkey stacks into that chest only; with none open it uses every chest in range.");
            QuickStackTrophiesTogether = cfg.Bind(s, "Trophies into trophy chests", true, "Trophies go into any container that already has a trophy, not only one with that exact trophy.");
            QuickStackReplacesHoldToStack = cfg.Bind(s, "Replace hold-to-stack", true, "The game's hold-E-on-a-chest stacking uses this mod's logic (respects favorites).");
            QuickStackMessages = cfg.Bind(s, "Result message", true, "Show how many stacks were moved.");

            s = "5 - Restock";
            RestockEnabled = cfg.Bind(s, "Enabled", true, "Top up partial stacks in your inventory from nearby containers.");
            RestockKey = cfg.Bind(s, "Key", new KeyboardShortcut(KeyCode.L), "Restock hotkey.");
            RestockRange = cfg.Bind(s, "Range", 10f, new ConfigDescription("Containers within this many metres are restocked from. 0 = only the open container.", new AcceptableValueRange<float>(0f, 50f)));
            RestockIncludesHotbar = cfg.Bind(s, "Include hotbar", true, "Also restock items in the hotbar row.");
            RestockOnlyFromOpenContainer = cfg.Bind(s, "Only open container when one is open", true, "With a chest open, the hotkey restocks from that chest only.");
            RestockOnlyAmmoAndConsumables = cfg.Bind(s, "Only ammo and consumables", true, "Only restock arrows/bolts and food/mead; off restocks every partial stack.");
            RestockStackLimit = cfg.Bind(s, "Stack limit", 0, new ConfigDescription("Restock stacks up to this size instead of the maximum. 0 = full stacks.", new AcceptableValueRange<int>(0, 100)));
            RestockMessages = cfg.Bind(s, "Result message", true, "Show how many stacks were restocked.");

            s = "6 - Sort";
            SortEnabled = cfg.Bind(s, "Enabled", true, "Sort the inventory and containers. Locked slots stay where they are and empty ones are filled last.");
            SortKey = cfg.Bind(s, "Key", new KeyboardShortcut(KeyCode.O), "Sort hotkey (inventory must be open).");
            SortBy = cfg.Bind(s, "Sort by", SortCriteria.Type, "Primary sort criterion. Ties break by name, then quality, then stack size.");
            SortAscending = cfg.Bind(s, "Ascending", true, "Sort direction.");
            SortIncludesHotbar = cfg.Bind(s, "Include hotbar", false, "Sorting may rearrange the hotbar row.");
            SortMergesStacks = cfg.Bind(s, "Merge stacks", true, "Combine partial stacks of the same item before sorting.");
            SortBothWhenContainerOpen = cfg.Bind(s, "Sort both with container open", false, "With a chest open the hotkey sorts both the chest and your inventory; off sorts only the chest.");
            SortOnOpen = cfg.Bind(s, "Auto sort on open", AutoSort.Never, "Automatically sort when the inventory or a container opens.");

            s = "7 - Trash";
            TrashEnabled = cfg.Bind(s, "Enabled", true, "Trash can button and trash hotkeys.");
            TrashKey = cfg.Bind(s, "Trash key", new KeyboardShortcut(KeyCode.Delete), "Destroy the item you are dragging (inventory open).");
            QuickTrashKey = cfg.Bind(s, "Quick trash key", KeyboardShortcut.Empty, "Destroy every trash-flagged item in your inventory (inventory open).");
            TrashConfirm = cfg.Bind(s, "Confirm trash", true, "Ask before destroying an item that is not trash-flagged.");
            QuickTrashConfirm = cfg.Bind(s, "Confirm quick trash", true, "Ask before quick-trashing.");
            TrashCanAffectHotbar = cfg.Bind(s, "Trash can affect hotbar", true, "Allow trashing items that sit in the hotbar row.");
            TrophiesAreTrash = cfg.Bind(s, "Trophies count as trash", false, "Treat all non-favorited trophies as trash-flagged.");
            NoAutoPickupOfTrash = cfg.Bind(s, "Do not auto-pickup trash", false, "Trash-flagged items are not auto-picked-up.");

            s = "8 - Locked slots";
            LockModifier = cfg.Bind(s, "Lock modifier", new KeyboardShortcut(KeyCode.LeftControl), "Hold this and right-click a slot to lock/unlock it. Automatic actions (quick stack, sort, store all, trash) never touch a locked slot. Crafting, building and stations may still use its contents, but only after every unlocked stack and every nearby chest is used up. Items still stack into it, and it is filled only when every other cell is taken. Hold it and click the Trash button with an item to trash-flag that item instead of destroying it.");
            LockedSlotColor = cfg.Bind(s, "Locked slot color", new Color(0.9f, 0.25f, 0.2f, 1f), "Border color of locked slots.");
            TrashFlagColor = cfg.Bind(s, "Trash-flagged color", new Color(0.5f, 0f, 0f, 1f), "Border color of trash-flagged items.");
            LockTooltips = cfg.Bind(s, "Tooltip hints", true, "Item tooltips mention locked-slot / trash-flag status.");

            s = "9 - Store and take all";
            StoreAllButton = cfg.Bind(s, "Store all button", true, "Show a 'Store all' button on open containers (moves everything except locked slots, equipped items, hotbar and slots).");
            StoreAllKey = cfg.Bind(s, "Store all key", KeyboardShortcut.Empty, "Store all hotkey (container open).");
            StoreAllIncludesHotbar = cfg.Bind(s, "Store all includes hotbar", false, "Store all also empties the hotbar row.");
            StoreAllIncludesEquipped = cfg.Bind(s, "Store all includes equipped", false, "Store all also unequips and stores worn items.");
            TakeAllInOrder = cfg.Bind(s, "Take all keeps order", true, "'Take all' fills your inventory in container order and never touches equipment/quick slot cells.");

            s = "10 - Buttons";
            ShowButtons = cfg.Bind(s, "Show buttons", true, "Add quick stack / restock / sort / store all / trash buttons to the inventory and container panels.");
            HideVanillaStackAllButton = cfg.Bind(s, "Hide vanilla stack button", true, "Hide the game's own 'Place stacks' button (this mod's quick stack replaces it).");

            s = "11 - Equipment and quick slots";
            EquipmentSlots = cfg.Bind(s, "Equipment slots", true, "Dedicated cells for worn helmet, chest, legs, cape, utility item and trinket. Turning this off moves worn gear back into the grid without unequipping it.");
            QuickSlots = cfg.Bind(s, "Quick slots", true, "Extra hotkey slots shown next to the hotbar.");
            QuickSlotCount = cfg.Bind(s, "Quick slot count", 3, new ConfigDescription("How many quick slots (1-6).", new AcceptableValueRange<int>(0, Slots.MaxQuickSlots)));
            ExtraInventoryRows = cfg.Bind(s, "Extra inventory rows", 0, new ConfigDescription("Additional visible inventory rows. Balance change; 0 keeps the vanilla grid.", new AcceptableValueRange<int>(0, Slots.MaxExtraRows)));
            for (int i = 0; i < Slots.MaxQuickSlots; i++)
            {
                QuickSlotKeys[i] = cfg.Bind(s, $"Quick slot {i + 1} key", new KeyboardShortcut(DefaultQuickKeys[i]), $"Hotkey that uses the item in quick slot {i + 1}.");
                QuickSlotLabels[i] = cfg.Bind(s, $"Quick slot {i + 1} label", "", "Label shown on the slot. Blank shows the key.");
            }
            PanelPosition = cfg.Bind(s, "Panel position", new Vector2(615f, 28f), "Position of the slot panel relative to the inventory grid. Drag the panel background in-game while holding the drag key.");
            PanelDraggable = cfg.Bind(s, "Panel always draggable", false, "Drag the slot panel without holding the drag key.");
            PanelDragKey = cfg.Bind(s, "Panel drag key", new KeyboardShortcut(KeyCode.LeftAlt), "Hold while dragging the panel background to move it.");
            QuickBarAnchor = cfg.Bind(s, "Quick bar anchor", TextAnchor.LowerLeft, "Screen corner the on-screen quick slot bar is anchored to.");
            QuickBarPosition = cfg.Bind(s, "Quick bar position", new Vector2(216f, 150f), "Offset of the quick slot bar from its anchor.");
            ProtectSlotsFromStackAll = cfg.Bind(s, "Protect slots from stack all", true, "The game's 'place stacks' never pulls from equipment or quick slots.");
            NoAutoPickupIntoQuickSlots = cfg.Bind(s, "No auto-pickup into quick slots", false, "Picked-up items never land in a quick slot.");
            SlotBackup = cfg.Bind(s, "Slot backup", true, "Back up slot contents into the character save so they can be restored if the mod is removed and re-added.");

            s = "12 - Death";
            KeepEquipmentOnDeath = cfg.Bind(s, "Keep equipment on death", false, "Worn gear in the equipment slots stays with you instead of going into the tombstone.");
            KeepQuickSlotsOnDeath = cfg.Bind(s, "Keep quick slots on death", false, "Quick slot items stay with you instead of going into the tombstone.");
            ReequipArmorOnPickup = cfg.Bind(s, "Re-equip armor on pickup", true, "Picking up your tombstone re-equips the armor that was in your equipment slots.");
            ReequipBeltOnPickup = cfg.Bind(s, "Re-equip carry-weight items on pickup", true, "Belts (Megingjord) from the tombstone are equipped first so the loot stays carryable.");
            ReequipWeaponsOnPickup = cfg.Bind(s, "Re-equip weapon and shield on pickup", true, "The weapon and shield you died holding are re-equipped on pickup.");

            s = "13 - Surface run speed";
            SurfaceSpeedEnabled = cfg.Bind(s, "Enabled", true, "Run faster on made surfaces. The bonus multiplies your run speed (after skill and equipment) while running; walking, sneaking and swimming are unaffected.");
            SpeedDirtPath = cfg.Bind(s, "Dirt path %", 50f, new ConfigDescription("Extra run speed on terrain flattened/pathed with the hoe.", new AcceptableValueRange<float>(0f, 300f)));
            SpeedPavedPath = cfg.Bind(s, "Paved path %", 70f, new ConfigDescription("Extra run speed on paved (stone) paths made with the hoe.", new AcceptableValueRange<float>(0f, 300f)));
            SpeedCultivated = cfg.Bind(s, "Cultivated soil %", 0f, new ConfigDescription("Extra run speed on cultivated ground.", new AcceptableValueRange<float>(0f, 300f)));
            SpeedWood = cfg.Bind(s, "Wood floor %", 60f, new ConfigDescription("Extra run speed on wood and core-wood pieces (floors, bridges, docks).", new AcceptableValueRange<float>(0f, 300f)));
            SpeedStone = cfg.Bind(s, "Stone floor %", 70f, new ConfigDescription("Extra run speed on stone, marble and black-marble pieces.", new AcceptableValueRange<float>(0f, 300f)));
            SpeedMetal = cfg.Bind(s, "Metal floor %", 60f, new ConfigDescription("Extra run speed on iron pieces (iron grates).", new AcceptableValueRange<float>(0f, 300f)));
            SpeedStaminaPercent = cfg.Bind(s, "Run stamina on made surfaces %", 50f, new ConfigDescription("Running stamina drain per second on any surface that has a speed bonus, as a percentage of normal. 100 = unchanged.", new AcceptableValueRange<float>(0f, 200f)));

            s = "14 - Row planting";
            RowPlantingEnabled = cfg.Bind(s, "Enabled", true, "With the cultivator, plant a row or a block of rows in one click. Extra ghosts preview the placement; red ones are skipped.");
            RowModifier = cfg.Bind(s, "Row length modifier", new KeyboardShortcut(KeyCode.LeftAlt), "Hold and scroll while placing a plant to change how many plants long the row is (the scroll doesn't rotate the piece while held). Alt is unused by the game in build mode; Shift toggles snapping and Ctrl crouches, which is why they aren't the defaults.");
            RowsModifier = cfg.Bind(s, "Row count modifier", new KeyboardShortcut(KeyCode.LeftAlt, KeyCode.LeftShift), "Hold and scroll while placing a plant to change how many rows deep the block is.");
            RowFillKey = cfg.Bind(s, "Fill key", new KeyboardShortcut(KeyCode.N), "Toggle fill mode: the row runs as far as it can - until the ground stops being plantable, something is in the way, or you run out of seeds.");
            RowMaxLength = cfg.Bind(s, "Max row length", 30, new ConfigDescription("Upper limit for a row.", new AcceptableValueRange<int>(2, 100)));
            RowMaxRows = cfg.Bind(s, "Max rows", 10, new ConfigDescription("Upper limit for rows deep.", new AcceptableValueRange<int>(1, 30)));
            RowKeepRotation = cfg.Bind(s, "Keep rotation", true, "Plants normally get a random rotation after each placement, which also spins the row direction. Keep the rotation you set instead.");
            RowSpacing = cfg.Bind(s, "Spacing", 1f, new ConfigDescription("Distance between plants as a multiple of the minimum the plant needs to grow. 1 = as tight as they'll grow.", new AcceptableValueRange<float>(0.8f, 3f)));

            s = "15 - Recycling";
            RecycleEnabled = cfg.Bind(s, "Enabled", true, "A Recycle tab on the crafting panel while at a crafting station: drag an item onto it to get its crafting materials back. Materials that can't go through a portal (metals, ores) are never returned, so recycling can't be used to move metal past a portal.");
            RecycleRate = cfg.Bind(s, "Return rate %", 100f, new ConfigDescription("Share of the invested materials (base recipe plus every upgrade level) that comes back, rounded down.", new AcceptableValueRange<float>(0f, 100f)));
            RecycleConsumables = cfg.Bind(s, "Recycle food and mead", false, "Allow recycling consumables back into their ingredients.");
            RecycleConfirm = cfg.Bind(s, "Confirm", true, "Show what you'll get back (and what is forfeited) before recycling.");
            RecycleNeverReturn = cfg.Bind(s, "Never return", "", "Comma-separated item names (prefab or $item_ names) that are forfeited on top of the non-portable ones.");
            RecycleKey = cfg.Bind(s, "Key", KeyboardShortcut.Empty, "Recycle the item being dragged (at a crafting station).");

            // Live hooks: the slot region depends on these.
            EquipmentSlots.SettingChanged += (_, __) => Slots.OnSlotActivationChanged();
            QuickSlots.SettingChanged += (_, __) => Slots.OnSlotActivationChanged();
            QuickSlotCount.SettingChanged += (_, __) => Slots.OnSlotActivationChanged();
            ExtraInventoryRows.SettingChanged += (_, __) => Slots.OnVisibleRowsChanged();
            foreach (var k in QuickSlotKeys)
                k.SettingChanged += (_, __) => PreventSimilarHotkeys.Refresh();
        }
    }
}
