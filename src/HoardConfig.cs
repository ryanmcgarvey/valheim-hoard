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
        public static ConfigEntry<bool> RestockOnlyFavorites;
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
        public static ConfigEntry<bool> SortLeavesFavoriteSlotsEmpty;
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

        // ---- Favorites
        public static ConfigEntry<KeyboardShortcut> FavoriteModifier;
        public static ConfigEntry<bool> FavoriteTooltips;
        public static ConfigEntry<Color> FavoriteItemColor;
        public static ConfigEntry<Color> FavoriteSlotColor;
        public static ConfigEntry<Color> FavoriteBothColor;
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
            QuickStackEnabled = cfg.Bind(s, "Enabled", true, "Move stackable items from your inventory into nearby containers that already hold that item.");
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
            RestockOnlyFavorites = cfg.Bind(s, "Only favorites", false, "Only restock favorited items / items in favorited slots.");
            RestockStackLimit = cfg.Bind(s, "Stack limit", 0, new ConfigDescription("Restock stacks up to this size instead of the maximum. 0 = full stacks.", new AcceptableValueRange<int>(0, 100)));
            RestockMessages = cfg.Bind(s, "Result message", true, "Show how many stacks were restocked.");

            s = "6 - Sort";
            SortEnabled = cfg.Bind(s, "Enabled", true, "Sort the inventory and containers.");
            SortKey = cfg.Bind(s, "Key", new KeyboardShortcut(KeyCode.O), "Sort hotkey (inventory must be open).");
            SortBy = cfg.Bind(s, "Sort by", SortCriteria.Type, "Primary sort criterion. Ties break by name, then quality, then stack size.");
            SortAscending = cfg.Bind(s, "Ascending", true, "Sort direction.");
            SortIncludesHotbar = cfg.Bind(s, "Include hotbar", false, "Sorting may rearrange the hotbar row.");
            SortMergesStacks = cfg.Bind(s, "Merge stacks", true, "Combine partial stacks of the same item before sorting.");
            SortBothWhenContainerOpen = cfg.Bind(s, "Sort both with container open", false, "With a chest open the hotkey sorts both the chest and your inventory; off sorts only the chest.");
            SortLeavesFavoriteSlotsEmpty = cfg.Bind(s, "Leave favorite slots empty", true, "Sorting never places items into an empty favorited slot.");
            SortOnOpen = cfg.Bind(s, "Auto sort on open", AutoSort.Never, "Automatically sort when the inventory or a container opens.");

            s = "7 - Trash";
            TrashEnabled = cfg.Bind(s, "Enabled", true, "Trash can button and trash hotkeys.");
            TrashKey = cfg.Bind(s, "Trash key", new KeyboardShortcut(KeyCode.Delete), "Destroy the item you are dragging (inventory open).");
            QuickTrashKey = cfg.Bind(s, "Quick trash key", KeyboardShortcut.Empty, "Destroy every trash-flagged item in your inventory (inventory open).");
            TrashConfirm = cfg.Bind(s, "Confirm trash", true, "Ask before destroying an item that is not trash-flagged.");
            QuickTrashConfirm = cfg.Bind(s, "Confirm quick trash", true, "Ask before quick-trashing.");
            TrashCanAffectHotbar = cfg.Bind(s, "Trash can affect hotbar", true, "Allow trashing items that sit in the hotbar row.");
            TrophiesAreTrash = cfg.Bind(s, "Trophies count as trash", false, "Treat all non-favorited trophies as trash-flagged.");
            NoAutoPickupOfTrash = cfg.Bind(s, "Don't auto-pickup trash", false, "Trash-flagged items are not auto-picked-up.");

            s = "8 - Favorites";
            FavoriteModifier = cfg.Bind(s, "Favorite modifier", new KeyboardShortcut(KeyCode.LeftAlt), "Hold this and left-click an item to favorite/unfavorite it (by name), right-click a slot to favorite the slot. Favorites are never stacked, sorted, stored or trashed. Hold it and click the trash can with an item to trash-flag the item instead.");
            FavoriteTooltips = cfg.Bind(s, "Tooltip hints", true, "Item tooltips mention favorite / trash-flag status.");
            FavoriteItemColor = cfg.Bind(s, "Favorite item color", new Color(1f, 0.85f, 0f, 1f), "Border color of favorited items.");
            FavoriteSlotColor = cfg.Bind(s, "Favorite slot color", new Color(0f, 0.5f, 1f, 1f), "Border color of favorited slots.");
            FavoriteBothColor = cfg.Bind(s, "Favorite item in favorite slot color", new Color(0.5f, 0.67f, 0.5f, 1f), "Border color when a favorited item sits in a favorited slot.");
            TrashFlagColor = cfg.Bind(s, "Trash-flagged color", new Color(0.5f, 0f, 0f, 1f), "Border color of trash-flagged items.");

            s = "9 - Store and take all";
            StoreAllButton = cfg.Bind(s, "Store all button", true, "Show a 'Store all' button on open containers (moves everything except favorites, equipped items, hotbar and slots).");
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
