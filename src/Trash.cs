using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Hoard
{
    // Trash can: destroy the item you are dragging (with confirmation unless it is
    // trash-flagged), or with the lock modifier held, trash-flag it instead. Quick trash
    // destroys every trash-flagged item in the inventory at once.
    public static class Trash
    {
        private enum Pending { None, Trash, Flag, Quick }
        private static Pending _pending;

        public static bool Enabled => HoardConfig.TrashEnabled.Value;

        // Called by the trash button and by the trash hotkey. The actual work happens on the
        // next InventoryGui.UpdateItemDrag so it sees the same drag state the click saw.
        public static void OnTrashPressed(bool fromHotkey = false)
        {
            if (!Enabled || _pending != Pending.None || !InventoryGui.instance) return;
            if (InventoryGui.instance.m_dragGo)
                _pending = Locks.InLockMode() ? Pending.Flag : Pending.Trash;
            else if (!fromHotkey && !Locks.InLockMode())
                _pending = Pending.Quick;
        }

        public static void OnQuickTrashPressed()
        {
            if (!Enabled || _pending != Pending.None || !InventoryGui.instance || InventoryGui.instance.m_dragGo) return;
            if (Locks.InLockMode()) return;
            _pending = Pending.Quick;
        }

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.UpdateItemDrag))]
        private static class InventoryGui_UpdateItemDrag
        {
            private static void Postfix(InventoryGui __instance)
            {
                if (_pending == Pending.None) return;
                var pending = _pending;
                _pending = Pending.None;
                var player = Player.m_localPlayer;
                if (!player) return;
                var locks = Locks.For(player);

                if (pending == Pending.Quick)
                {
                    if (HoardConfig.QuickTrashConfirm.Value)
                        Confirm("Quick trash", "Destroy every trash-flagged item in your inventory?", () => QuickTrash(player, locks));
                    else QuickTrash(player, locks);
                    return;
                }

                var item = __instance.m_dragItem;
                var inv = __instance.m_dragInventory;
                int amount = __instance.m_dragAmount;
                if (item == null || inv == null || !inv.ContainsItem(item)) return;

                if (pending == Pending.Flag)
                {
                    locks.ToggleTrash(item.m_shared);
                    Msg.Center(locks.IsTrashFlagged(item.m_shared) ? "Trash-flagged" : "Trash flag removed");
                    return;
                }

                bool inPlayerInv = inv == player.m_inventory;
                if (inPlayerInv && item.m_gridPos.y == 0 && !HoardConfig.TrashCanAffectHotbar.Value)
                {
                    Msg.Center("Hotbar items are protected from trashing");
                    return;
                }
                if (inPlayerInv && locks.IsLocked(item))
                {
                    Msg.Center("That slot is locked");
                    return;
                }
                if (HoardConfig.TrashConfirm.Value && !locks.IsConsideredTrash(item.m_shared))
                    Confirm(Localization.instance.Localize(item.m_shared.m_name), $"Destroy {amount}/{item.m_shared.m_maxStackSize}?", () => TrashItem(__instance, inv, item, amount));
                else
                    TrashItem(__instance, inv, item, amount);
            }
        }

        private static void Confirm(string header, string text, System.Action yes)
        {
            if (!UnifiedPopup.IsAvailable())
            {
                yes();
                return;
            }
            UnifiedPopup.Push(new YesNoPopup(header, text, () => { UnifiedPopup.Pop(); yes(); }, () => UnifiedPopup.Pop(), localizeText: false));
        }

        private static void TrashItem(InventoryGui gui, Inventory inv, ItemDrop.ItemData item, int amount)
        {
            var player = Player.m_localPlayer;
            if (!player || !inv.ContainsItem(item)) return;
            if (amount >= item.m_stack)
            {
                player.RemoveEquipAction(item);
                player.UnequipItem(item, false);
                inv.RemoveItem(item);
            }
            else inv.RemoveItem(item, amount);
            gui.SetupDragItem(null, null, 1);
            gui.UpdateCraftingPanel(false);
            Log.Debug($"trashed {amount} x {item.m_shared.m_name}");
        }

        private static void QuickTrash(Player player, Locks locks)
        {
            int n = 0;
            var inv = player.m_inventory;
            for (int i = inv.m_inventory.Count - 1; i >= 0; i--)
            {
                var item = inv.m_inventory[i];
                if (item.m_gridPos.y == 0 && !HoardConfig.TrashCanAffectHotbar.Value) continue;
                if (Slots.IsSlotCell(item.m_gridPos)) continue;
                if (locks.IsLocked(item) || !locks.IsConsideredTrash(item.m_shared)) continue;
                player.RemoveEquipAction(item);
                player.UnequipItem(item, false);
                inv.RemoveItem(item);
                n++;
            }
            InventoryGui.instance?.SetupDragItem(null, null, 0);
            InventoryGui.instance?.UpdateCraftingPanel(false);
            inv.Changed();
            Msg.Center(n == 0 ? "No trash-flagged items" : $"Trashed {n} item{(n == 1 ? "" : "s")}");
        }
    }
}
