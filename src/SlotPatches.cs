using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Hoard
{
    // Everything that keeps the hidden slot rows consistent with the vanilla inventory:
    // height management, where new items land, drag & drop rules, and protection from
    // "place stacks".
    public static class SlotPatches
    {
        public static void ApplyInventoryHeight()
        {
            var p = Slots.CurrentPlayer;
            if (p == null) return;
            if (p.m_inventory.m_height != Slots.FullHeight)
            {
                Log.Debug($"inventory height {p.m_inventory.m_height} -> {Slots.FullHeight}");
                p.m_inventory.m_height = Slots.FullHeight;
                p.m_inventory.Changed();
            }
            SlotValidation.MarkItemsDirty();
        }

        [HarmonyPatch(typeof(Player), nameof(Player.Awake))]
        private static class Player_Awake
        {
            [HarmonyPriority(Priority.Last)]
            private static void Postfix(Player __instance)
            {
                // Remote players are Player instances too; their inventories are not ours to touch.
                if (__instance.m_nview && __instance.m_nview.GetZDO() != null && !__instance.m_nview.IsOwner()) return;
                Slots.CaptureBaseRows(__instance.m_inventory);
                Slots.LoadingPlayer = __instance;
                try { Slots.UpdatePositions(); }
                finally { Slots.LoadingPlayer = null; }
                __instance.m_inventory.m_height = Slots.FullHeight;
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.OnSpawned))]
        private static class Player_OnSpawned
        {
            private static void Postfix(Player __instance)
            {
                if (__instance == Player.m_localPlayer) ApplyInventoryHeight();
            }
        }

        // Haldor's inventory rows: vanilla shrinks the inventory to the bought row count and
        // drops everything outside it. Move the slot region first, restore the height after.
        [HarmonyPatch(typeof(Player), nameof(Player.SetInventorySize))]
        private static class Player_SetInventorySize
        {
            [HarmonyPriority(Priority.First)]
            private static void Prefix(Player __instance, int rows)
            {
                if (__instance == Slots.CurrentPlayer) Slots.SetBaseRows(Mathf.Clamp(rows, 0, 9));
            }

            [HarmonyPriority(Priority.Last)]
            private static void Postfix(Player __instance)
            {
                if (__instance == Slots.CurrentPlayer) __instance.m_inventory.m_height = Slots.FullHeight;
            }
        }

        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.DropInvalidItems))]
        private static class Humanoid_DropInvalidItems
        {
            [HarmonyPriority(Priority.First)]
            private static void Prefix(Humanoid __instance)
            {
                if (__instance != Slots.CurrentPlayer) return;
                __instance.m_inventory.m_height = Slots.FullHeight;
                SlotValidation.MarkItemsDirty();
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.Update))]
        private static class Player_Update
        {
            private static void Postfix(Player __instance)
            {
                if (__instance == Player.m_localPlayer) __instance.m_inventory.m_height = Slots.FullHeight;
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.Save))]
        private static class Player_Save
        {
            private static void Prefix(Player __instance)
            {
                if (__instance.m_inventory == Slots.PlayerInventory) Slots.RememberSlots();
            }
        }

        // ---- capacity: the visible grid plus free quick slots

        [HarmonyPatch(typeof(Player), nameof(Player.AutoPickup))]
        public static class Player_AutoPickup
        {
            public static bool Active;
            [HarmonyPriority(Priority.First)] private static void Prefix(Player __instance) => Active = HoardConfig.NoAutoPickupIntoQuickSlots.Value && __instance == Slots.CurrentPlayer;
            [HarmonyPriority(Priority.First)] private static void Finalizer() => Active = false;
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.SlotsUsedPercentage))]
        private static class Inventory_SlotsUsedPercentage
        {
            private static void Postfix(Inventory __instance, ref float __result)
            {
                if (__instance == Slots.PlayerInventory) __result = (float)__instance.m_inventory.Count / Slots.ActiveCells * 100f;
            }
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.GetEmptySlots))]
        private static class Inventory_GetEmptySlots
        {
            [HarmonyPriority(Priority.First)]
            private static void Postfix(Inventory __instance, ref int __result)
            {
                if (__instance != Slots.PlayerInventory) return;
                __result = Slots.VisibleRows * __instance.m_width
                           - __instance.m_inventory.Count(i => !Slots.IsInSlot(i))
                           + (Player_AutoPickup.Active ? 0 : Slots.EmptyQuickSlots());
            }
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.HaveEmptySlot))]
        private static class Inventory_HaveEmptySlot
        {
            [HarmonyPriority(Priority.First)]
            private static void Postfix(Inventory __instance, ref bool __result)
            {
                if (__instance == Slots.PlayerInventory) __result = __instance.GetEmptySlots() > 0;
            }
        }

        // FindEmptySlot scans the visible rows only, then falls back to a free quick slot.
        [HarmonyPatch(typeof(Inventory), nameof(Inventory.FindEmptySlot))]
        private static class Inventory_FindEmptySlot
        {
            [HarmonyPriority(Priority.First)]
            private static void Prefix(Inventory __instance)
            {
                if (__instance == Slots.PlayerInventory) __instance.m_height = Slots.VisibleRows;
            }

            [HarmonyPriority(Priority.First)]
            private static void Postfix(Inventory __instance, ref Vector2i __result)
            {
                if (__instance != Slots.PlayerInventory) return;
                __instance.m_height = Slots.FullHeight;
                if (__result == Slots.NoPosition && InventoryGui_DoCrafting.UpgradeSource is Slots.Slot src && src.IsFree)
                    __result = src.Position;
                if (__result == Slots.NoPosition && Inventory_AddItem_ByName.ItemToPlace != null && Slots.TryFindFreeSlot(Inventory_AddItem_ByName.ItemToPlace, out var byName))
                    __result = byName.Position;
                if (__result == Slots.NoPosition && !Slots.NoQuickSlotFallback)
                    __result = Slots.EmptyQuickSlot();
            }

            [HarmonyPriority(Priority.First)]
            private static void Finalizer(Inventory __instance)
            {
                if (__instance == Slots.PlayerInventory) __instance.m_height = Slots.FullHeight;
            }
        }

        // A finished upgrade re-adds the (possibly worn) item: hand the capacity probe the
        // cell it came from, and re-equip it so it stays in the cell.
        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.DoCrafting))]
        internal static class InventoryGui_DoCrafting
        {
            internal static Slots.Slot UpgradeSource;
            private static bool _wasEquipped;

            [HarmonyPriority(Priority.First)]
            private static void Prefix(InventoryGui __instance)
            {
                UpgradeSource = null;
                _wasEquipped = false;
                if (__instance.m_craftUpgradeItem is not ItemDrop.ItemData item) return;
                UpgradeSource = Slots.At(item.m_gridPos);
                _wasEquipped = Slots.CurrentPlayer != null && Slots.CurrentPlayer.IsItemEquiped(item);
            }

            private static void Postfix()
            {
                var slot = UpgradeSource;
                bool reequip = _wasEquipped;
                UpgradeSource = null;
                _wasEquipped = false;
                if (!reequip || slot == null || Slots.CurrentPlayer == null) return;
                var occupant = slot.Item;
                if (occupant != null && !Slots.CurrentPlayer.IsItemEquiped(occupant))
                    Slots.CurrentPlayer.EquipItem(occupant, false);
            }

            private static void Finalizer()
            {
                UpgradeSource = null;
                _wasEquipped = false;
            }
        }

        // ---- drag & drop rules

        private static bool PassDrop(InventoryGrid grid, Inventory from, ItemDrop.ItemData item, Vector2i pos)
        {
            if (item.m_gridPos == pos && grid.m_inventory == from) return true;
            var player = Player.m_localPlayer;
            var inv = Slots.PlayerInventory;
            // A worn item dragged out of its cell may go onto an empty cell only.
            if (from == inv && Slots.Of(item) is Slots.Slot itemSlot && itemSlot.IsEquipment && player.IsItemEquiped(item)
                && grid.m_inventory.GetItemAt(pos.x, pos.y) != null)
                return false;
            // Into a slot cell: must fit by type (and pass the drag-to-equip check for equipment cells).
            if (grid.m_inventory == inv && Slots.At(pos) is Slots.Slot target
                && (!target.Fits(item) || (target.IsEquipment && !Slots.WouldFitEquipment(target, item))))
                return false;
            // Swapping: the displaced item must fit the source slot.
            var displaced = grid.m_inventory.GetItemAt(pos.x, pos.y);
            if (displaced != null && displaced != item && from == inv && Slots.At(item.m_gridPos) is Slots.Slot source && !source.Fits(displaced))
                return false;
            return true;
        }

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.OnSelectedItem))]
        private static class InventoryGui_OnSelectedItem
        {
            private static Vector2i? _equipAt, _unequipAt;
            private static ItemDrop.ItemData.SharedData _droppedShared;

            private static bool Prefix(InventoryGui __instance, InventoryGrid grid, Vector2i pos)
            {
                _equipAt = _unequipAt = null;
                _droppedShared = null;
                var player = Player.m_localPlayer;
                if (player == null || player.IsTeleporting() || !__instance.m_dragGo || __instance.m_dragItem == null || __instance.m_dragInventory == null) return true;
                var inv = Slots.PlayerInventory;
                var drag = __instance.m_dragItem;
                bool toPlayer = grid.m_inventory == inv;
                bool fromPlayer = __instance.m_dragInventory == inv;
                var target = toPlayer ? Slots.At(pos) : null;
                var source = fromPlayer ? Slots.Of(drag) : null;
                bool dragEquipped = player.IsItemEquiped(drag);

                // Drag-to-equip: unequipped equippable onto its matching cell queues the equip.
                if (target != null && Slots.WouldFitEquipment(target, drag) && !dragEquipped)
                {
                    if (fromPlayer)
                    {
                        __instance.SetupDragItem(null, null, 1);
                        Slots.QueueEquip(player, drag);
                        return false;
                    }
                    _equipAt = pos;
                    _droppedShared = drag.m_shared;
                }

                if (source != null && source.IsEquipment && dragEquipped)
                {
                    var targetItem = grid.m_inventory.GetItemAt(pos.x, pos.y);
                    // Swap-equip: worn item dropped on an unworn item of the same type means "wear that one".
                    if (toPlayer && targetItem != null && targetItem != drag && Slots.WouldFitEquipment(source, targetItem) && !player.IsItemEquiped(targetItem))
                    {
                        __instance.SetupDragItem(null, null, 1);
                        Slots.QueueEquip(player, targetItem);
                        return false;
                    }
                    // Drag-to-unequip: worn item onto an empty regular/quick cell.
                    if (toPlayer && (target == null || !target.IsEquipment) && targetItem == null)
                    {
                        _unequipAt = pos;
                        _droppedShared = drag.m_shared;
                    }
                }
                return PassDrop(grid, __instance.m_dragInventory, drag, pos);
            }

            private static void Postfix()
            {
                var equipAt = _equipAt; var unequipAt = _unequipAt; var shared = _droppedShared;
                _equipAt = _unequipAt = null; _droppedShared = null;
                var player = Player.m_localPlayer;
                var inv = Slots.PlayerInventory;
                if (player == null || inv == null) return;
                if (equipAt is Vector2i e && Landed(inv, e, shared) is ItemDrop.ItemData toEquip) Slots.QueueEquip(player, toEquip);
                if (unequipAt is Vector2i u && Landed(inv, u, shared) is ItemDrop.ItemData toUnequip && (Slots.Of(toUnequip) is not Slots.Slot l || !l.IsEquipment))
                    Slots.QueueUnequip(player, toUnequip);
            }

            private static ItemDrop.ItemData Landed(Inventory inv, Vector2i pos, ItemDrop.ItemData.SharedData shared)
            {
                var item = inv.GetItemAt(pos.x, pos.y);
                return item != null && item.m_shared == shared ? item : null;
            }
        }

        [HarmonyPatch(typeof(InventoryGrid), nameof(InventoryGrid.DropItem))]
        private static class InventoryGrid_DropItem
        {
            private static bool Prefix(InventoryGrid __instance, Inventory fromInventory, ItemDrop.ItemData item, Vector2i pos) => PassDrop(__instance, fromInventory, item, pos);
        }

        // ---- where new items land

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.AddItem), typeof(ItemDrop.ItemData), typeof(int), typeof(int), typeof(int), typeof(bool))]
        private static class Inventory_AddItem_At
        {
            [HarmonyPriority(Priority.Last)]
            private static void Prefix(Inventory __instance, ItemDrop.ItemData item, ref int x, ref int y)
            {
                if (__instance != Slots.PlayerInventory || item == null) return;
                // A save being read places items at their saved absolute positions; validation sorts out strays afterwards.
                if (Slots.CurrentPlayer.m_isLoading) return;
                if (__instance.GetItemAt(x, y) != null) return;
                if (Slots.At(new Vector2i(x, y)) is not Slots.Slot slot || slot.Fits(item)) return;
                if (Slots.TryFindFreeSlot(item, out var free)) { x = free.Position.x; y = free.Position.y; return; }
                if (Slots.TryMakeRoomInGrid(out var pos)) { x = pos.x; y = pos.y; }
            }

            // Inventory.Load ignores a failed add and destroys the item: force it in instead.
            [HarmonyPriority(Priority.Last)]
            private static void Postfix(Inventory __instance, ItemDrop.ItemData item, int x, int y, int amount, ref bool __result)
            {
                if (__instance != Slots.PlayerInventory || !Inventory_AddItem_Load.InCall || __result) return;
                amount = Mathf.Min(amount, item.m_stack);
                var clone = item.Clone();
                clone.m_stack = amount;
                Log.Warn($"Item loss prevention on load: {item.m_shared.m_name} at {x},{y} x{amount}");
                if (Slots.TryFindFreeSlot(clone, out var slot)) clone.m_gridPos = slot.Position;
                else if (Slots.TryMakeRoomInGrid(out var pos)) clone.m_gridPos = pos;
                else clone.m_gridPos = new Vector2i(Slots.Width - 1, Slots.FullHeight - 1);
                __instance.m_inventory.Add(clone);
                item.m_stack -= amount;
                __result = true;
                __instance.Changed();
            }
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.AddItem), typeof(ItemDrop.ItemData))]
        private static class Inventory_AddItem_Simple
        {
            [HarmonyPriority(Priority.First)]
            private static void Postfix(Inventory __instance, ItemDrop.ItemData item, bool __runOriginal, ref bool __result)
            {
                if (__instance != Slots.PlayerInventory || __result || !__runOriginal || Slots.NoQuickSlotFallback) return;
                if (!Slots.TryFindFreeSlot(item, out var slot)) return;
                item.m_gridPos = slot.Position;
                __instance.m_inventory.Add(item);
                __instance.Changed();
                __result = true;
            }
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.AddItem), typeof(ItemDrop.ItemData), typeof(Vector2i))]
        private static class Inventory_AddItem_Pos
        {
            [HarmonyPriority(Priority.First)]
            private static void Prefix(Inventory __instance, ItemDrop.ItemData item, ref Vector2i pos)
            {
                if (__instance != Slots.PlayerInventory || item == null) return;
                if (__instance.GetItemAt(pos.x, pos.y) != null || Slots.At(pos) is not Slots.Slot slot || slot.Fits(item)) return;
                if (item.m_shared.m_maxStackSize > 1)
                {
                    int freeStack = __instance.GetAllItems()
                        .Where(i => i.m_shared.m_name == item.m_shared.m_name && i.m_quality == item.m_quality && i.m_worldLevel == item.m_worldLevel)
                        .Sum(i => i.m_shared.m_maxStackSize - i.m_stack);
                    if (freeStack > item.m_stack) return;
                }
                if (Slots.TryFindFreeSlot(item, out var free)) { pos = free.Position; return; }
                if (Slots.TryMakeRoomInGrid(out var p)) pos = p;
            }
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.AddItem), typeof(string), typeof(int), typeof(int), typeof(int), typeof(long), typeof(string), typeof(Vector2i), typeof(bool), typeof(bool), typeof(bool))]
        public static class Inventory_AddItem_ByName
        {
            public static ItemDrop.ItemData ItemToPlace;

            [HarmonyPriority(Priority.First)]
            private static void Prefix(Inventory __instance, string name)
            {
                if (__instance != Slots.PlayerInventory) return;
                var drop = ObjectDB.instance?.GetItemPrefab(name)?.GetComponent<ItemDrop>();
                if (drop == null || drop.m_itemData.m_shared.m_maxStackSize > 1) return;
                ItemToPlace = drop.m_itemData;
            }

            [HarmonyPriority(Priority.First)]
            private static void Finalizer() => ItemToPlace = null;
        }

        // Inventory.Load adds items through the prefab-hash overload; legacy saves through the name one.
        [HarmonyPatch(typeof(Inventory), nameof(Inventory.AddItem), typeof(int), typeof(int), typeof(float), typeof(Vector2i), typeof(bool), typeof(int), typeof(int), typeof(long), typeof(string), typeof(Dictionary<string, string>), typeof(int), typeof(bool), typeof(bool), typeof(bool))]
        public static class Inventory_AddItem_Load
        {
            public static bool InCall;
            [HarmonyPriority(Priority.First)] private static void Prefix() => InCall = true;
            [HarmonyPriority(Priority.First)] private static void Finalizer() => InCall = false;
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.AddItem), typeof(string), typeof(int), typeof(float), typeof(Vector2i), typeof(bool), typeof(int), typeof(int), typeof(long), typeof(string), typeof(Dictionary<string, string>), typeof(int), typeof(bool), typeof(bool), typeof(bool))]
        public static class Inventory_AddItem_LoadLegacy
        {
            [HarmonyPriority(Priority.First)] private static void Prefix() => Inventory_AddItem_Load.InCall = true;
            [HarmonyPriority(Priority.First)] private static void Finalizer() => Inventory_AddItem_Load.InCall = false;
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.CanAddItem), typeof(ItemDrop.ItemData), typeof(int))]
        private static class Inventory_CanAddItem
        {
            private static readonly List<ItemDrop.ItemData> _hidden = new List<ItemDrop.ItemData>();

            [HarmonyPriority(Priority.First)]
            private static void Prefix(Inventory __instance)
            {
                if (__instance != Slots.PlayerInventory) return;
                __instance.m_height = Slots.VisibleRows;
                _hidden.Clear();
                for (int i = __instance.m_inventory.Count - 1; i >= 0; i--)
                {
                    var item = __instance.m_inventory[i];
                    if (!Slots.IsInSlot(item)) continue;
                    _hidden.Add(item);
                    __instance.m_inventory.RemoveAt(i);
                }
                _hidden.Reverse();
            }

            [HarmonyPriority(Priority.First)]
            private static void Postfix(Inventory __instance, ItemDrop.ItemData item, int stack, ref bool __result)
            {
                if (__instance != Slots.PlayerInventory) return;
                Restore(__instance);
                if (__result) return;
                if (stack <= 0) stack = item.m_stack;
                long free = (long)__instance.FindFreeStackSpace(item.m_shared.m_name, item.m_worldLevel) + (long)__instance.GetEmptySlots() * item.m_shared.m_maxStackSize;
                if (free >= stack) __result = true;
                else if (stack <= item.m_shared.m_maxStackSize && !Slots.NoQuickSlotFallback) __result = Slots.TryFindFreeSlot(item, out _);
            }

            [HarmonyPriority(Priority.First)]
            private static void Finalizer(Inventory __instance)
            {
                if (__instance == Slots.PlayerInventory) Restore(__instance);
            }

            private static void Restore(Inventory inv)
            {
                inv.m_height = Slots.FullHeight;
                if (_hidden.Count == 0) return;
                inv.m_inventory.AddRange(_hidden);
                _hidden.Clear();
            }
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.MoveInventoryToGrave))]
        private static class Inventory_MoveInventoryToGrave
        {
            private static void Prefix(Inventory original)
            {
                if (original == Slots.PlayerInventory) original.m_height = Slots.FullHeight;
            }
        }

        // ---- "place stacks" must never pull from the slots

        private static class StackAllGuard
        {
            private static readonly List<ItemDrop.ItemData> _removed = new List<ItemDrop.ItemData>();
            public static bool InCall;

            public static void Hide()
            {
                var inv = Slots.PlayerInventory;
                for (int i = inv.m_inventory.Count - 1; i >= 0; i--)
                {
                    var item = inv.m_inventory[i];
                    if (!Slots.IsInSlot(item)) continue;
                    _removed.Add(item);
                    inv.m_inventory.RemoveAt(i);
                }
            }

            public static void Restore()
            {
                if (_removed.Count == 0) return;
                Slots.PlayerInventory.m_inventory.AddRange(_removed);
                _removed.Clear();
            }
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.StackAll))]
        private static class Inventory_StackAll
        {
            [HarmonyPriority(Priority.First)]
            private static void Prefix(Inventory fromInventory)
            {
                StackAllGuard.InCall = fromInventory == Slots.PlayerInventory && HoardConfig.ProtectSlotsFromStackAll.Value;
                if (StackAllGuard.InCall) StackAllGuard.Hide();
            }

            [HarmonyPriority(Priority.First)]
            private static void Finalizer()
            {
                if (StackAllGuard.InCall) StackAllGuard.Restore();
                StackAllGuard.InCall = false;
            }
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.Changed))]
        private static class Inventory_Changed
        {
            [HarmonyPriority(Priority.First)]
            private static void Prefix(Inventory __instance)
            {
                if (StackAllGuard.InCall && __instance == Slots.PlayerInventory) StackAllGuard.Restore();
            }
        }
    }
}
