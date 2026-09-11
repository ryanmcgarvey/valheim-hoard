using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace Hoard
{
    // Two dirty flags drained once per frame from the plugin's LateUpdate, outside any
    // vanilla call stack.
    //   Slots: an item sitting in a cell it no longer belongs in (unequipped armor in an
    //          equipment cell, item in a deactivated quick slot) is moved out.
    //   Items: an equipped paperdoll-type item outside its cell is moved in; overlapping
    //          and out-of-grid items are rescued.
    public static class SlotValidation
    {
        private static bool _slotsDirty, _itemsDirty;
        public static void MarkSlotsDirty() => _slotsDirty = true;
        public static void MarkItemsDirty() => _itemsDirty = true;

        public static void Run()
        {
            ValidateItems();
            ValidateSlots();
        }

        private static bool PlaceSomewhere(ItemDrop.ItemData item)
        {
            var inv = Slots.PlayerInventory;
            // Several items are placed in one pass; the slot cache must not keep reporting a
            // cell as free after an item was just put there.
            if (Slots.TryRememberedSlot(item, out var prev) && prev.IsActive && prev.Belongs(item) && (prev.IsFree || item == prev.Item))
            {
                item.m_gridPos = prev.Position;
                Slots.ClearCache();
                return true;
            }
            var pos = inv.FindEmptySlot(true);
            if (pos.x >= 0) { item.m_gridPos = pos; Slots.ClearCache(); return true; }
            if (Slots.TryFindFreeSlot(item, out var slot)) { item.m_gridPos = slot.Position; Slots.ClearCache(); return true; }
            if (Slots.TryMakeRoomInGrid(out var freed)) { item.m_gridPos = freed; Slots.ClearCache(); return true; }
            return false;
        }

        private static void ValidateSlots()
        {
            if (!_slotsDirty || !Player.m_localPlayer || Player.m_localPlayer.m_isLoading) return;
            _slotsDirty = false;
            var inv = Slots.PlayerInventory;
            bool moved = false;
            foreach (var slot in Slots.All)
            {
                var item = slot.Item;
                if (item == null || slot.Belongs(item)) continue;
                Log.Debug($"slot validation: {item.m_shared.m_name} no longer belongs in {slot}");
                if (slot.IsEquipment && Slots.IsEquipped(item))
                {
                    slot.ClearCache();
                    if (Slots.TryFindFreeEquipmentSlot(item, out var free))
                    {
                        item.m_gridPos = free.Position;
                        free.ClearCache();
                        moved = true;
                        continue;
                    }
                    if (Slots.TryFindUnequippedOccupiedSlot(item, out var swap))
                    {
                        var other = swap.Item;
                        other.m_gridPos = item.m_gridPos;
                        item.m_gridPos = swap.Position;
                        slot.ClearCache();
                        swap.ClearCache();
                        moved = true;
                        item = slot.Item;
                        if (item == null || slot.Belongs(item)) continue;
                    }
                }
                if (PlaceSomewhere(item)) moved = true;
            }
            if (moved) inv.Changed();
        }

        private static readonly HashSet<Vector2i> _occupied = new HashSet<Vector2i>();
        private static readonly List<ItemDrop.ItemData> _misplaced = new List<ItemDrop.ItemData>();

        private static void ValidateItems()
        {
            if (!_itemsDirty || !Player.m_localPlayer || Player.m_localPlayer.m_isLoading) return;
            _itemsDirty = false;
            var inv = Slots.PlayerInventory;
            if (inv?.m_inventory == null) return;
            var player = Player.m_localPlayer;
            _occupied.Clear();
            _misplaced.Clear();
            for (int i = 0; i < inv.m_inventory.Count; i++)
            {
                var item = inv.m_inventory[i];
                if (item == null) continue;
                // Equipped paperdoll-type item outside its cell moves in (unless it is being unequipped).
                if (player.IsItemEquiped(item) && !Slots.IsUnequipQueued(item) && Slots.IsEquipmentSlotItem(item)
                    && (Slots.Of(item) is not Slots.Slot cur || !cur.IsEquipment))
                {
                    if (Slots.TryFindFreeEquipmentSlot(item, out var slot))
                    {
                        item.m_gridPos = slot.Position;
                        inv.Changed();
                    }
                    else if (Slots.TryFindUnequippedOccupiedSlot(item, out var swap))
                    {
                        var other = swap.Item;
                        other.m_gridPos = item.m_gridPos;
                        item.m_gridPos = swap.Position;
                        inv.Changed();
                    }
                }
                bool outOfGrid = item.m_gridPos.x < 0 || item.m_gridPos.x >= Slots.Width || item.m_gridPos.y < 0 || item.m_gridPos.y >= Slots.FullHeight;
                if (_occupied.Contains(item.m_gridPos) && inv.GetOtherItemAt(item.m_gridPos.x, item.m_gridPos.y, item) != null)
                {
                    Log.Warn($"item validation: {item.m_shared.m_name} at {item.m_gridPos} overlaps another item");
                    _misplaced.Add(item);
                }
                else if (outOfGrid)
                {
                    Log.Warn($"item validation: {item.m_shared.m_name} at {item.m_gridPos} is outside the grid");
                    _misplaced.Add(item);
                }
                _occupied.Add(item.m_gridPos);
            }
            if (_misplaced.Count(PlaceSomewhere) > 0) inv.Changed();

            // Settled slot items don't need a return address any more.
            foreach (var slot in Slots.All)
                if (slot.Item is ItemDrop.ItemData si && slot.Belongs(si)) Slots.ForgetSlot(si);
            foreach (var item in inv.m_inventory)
            {
                if (item == null || !item.m_customData.TryGetValue(Slots.KeyParked, out var parked)) continue;
                if (player.IsItemEquiped(item) || Slots.Of(item)?.Id != parked) item.m_customData.Remove(Slots.KeyParked);
            }
        }

        // ---- triggers

        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.SetupEquipment))]
        private static class Humanoid_SetupEquipment
        {
            private static void Postfix(Humanoid __instance)
            {
                if (__instance is Player p && Slots.IsLocal(p) && !p.m_isLoading) MarkSlotsDirty();
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.OnInventoryChanged))]
        private static class Player_OnInventoryChanged
        {
            private static void Postfix(Player __instance)
            {
                Slots.ClearCache();
                if (Slots.IsLocal(__instance) && !__instance.m_isLoading) MarkSlotsDirty();
            }
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.MoveAll))]
        private static class Inventory_MoveAll
        {
            private static void Postfix(Inventory __instance, Inventory fromInventory)
            {
                if (__instance == Slots.PlayerInventory || fromInventory == Slots.PlayerInventory) MarkItemsDirty();
            }
        }

        [HarmonyPatch]
        private static class Humanoid_EquipUnequip
        {
            private static IEnumerable<MethodBase> TargetMethods()
            {
                yield return AccessTools.Method(typeof(Humanoid), nameof(Humanoid.EquipItem));
                yield return AccessTools.Method(typeof(Humanoid), nameof(Humanoid.UnequipItem));
            }

            private static void Prefix(Humanoid __instance)
            {
                if (Slots.IsLocal(__instance)) MarkItemsDirty();
            }
        }

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Show))]
        private static class InventoryGui_Show
        {
            private static void Postfix()
            {
                if (!Player.m_localPlayer) return;
                MarkSlotsDirty();
                MarkItemsDirty();
            }
        }
    }
}
