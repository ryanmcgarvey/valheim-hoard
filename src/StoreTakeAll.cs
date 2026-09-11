using System.Collections.Generic;
using System.Linq;
using HarmonyLib;

namespace Hoard
{
    // Store all: dump the inventory (minus favorites, slots, hotbar, equipped) into the
    // open container. Take all: fill the inventory in grid order without vanilla's habit of
    // dropping container items onto their original grid positions (which lands them in
    // the equipment/quick slot region when a chest came from a bigger inventory).
    public static class StoreTakeAll
    {
        private static bool ShouldStore(ItemDrop.ItemData item, Favorites fav)
        {
            if (item.m_gridPos.y == 0 && !HoardConfig.StoreAllIncludesHotbar.Value) return false;
            if (item.m_equipped && !HoardConfig.StoreAllIncludesEquipped.Value) return false;
            if (Slots.IsSlotCell(item.m_gridPos)) return false;
            if (fav.IsFavorite(item)) return false;
            if (item.m_shared.m_questItem) return false;
            return true;
        }

        public static void StoreAll(Player player)
        {
            var gui = InventoryGui.instance;
            if (!gui || !gui.m_currentContainer || player.IsTeleporting()) return;
            if (!Containers.Claim(gui.m_currentContainer)) return;
            gui.SetupDragItem(null, null, 1);
            var fav = Favorites.For(player);
            var from = player.m_inventory;
            var to = gui.m_currentContainer.GetInventory();
            var items = from.m_inventory.Where(i => ShouldStore(i, fav)).ToList();
            items.Sort((a, b) => QuickStack.CompareSlotOrder(a.m_gridPos, b.m_gridPos));
            int n = 0;
            foreach (var item in items)
            {
                if (item.m_equipped)
                {
                    player.RemoveEquipAction(item);
                    player.UnequipItem(item, false);
                }
                if (!to.AddItem(item)) continue;
                from.RemoveItem(item);
                n++;
            }
            to.Changed();
            from.Changed();
            Containers.Commit(gui.m_currentContainer);
            Log.Debug($"stored {n} items");
        }

        public static void TakeAll(Player player)
        {
            var gui = InventoryGui.instance;
            if (!gui || !gui.m_currentContainer || player.IsTeleporting()) return;
            if (!Containers.Claim(gui.m_currentContainer)) return;
            gui.SetupDragItem(null, null, 1);
            var from = gui.m_currentContainer.GetInventory();
            var to = player.m_inventory;
            var items = new List<ItemDrop.ItemData>(from.m_inventory);
            items.Sort((a, b) => QuickStack.CompareSlotOrder(a.m_gridPos, b.m_gridPos));
            Slots.SuppressQuickSlotFallback = true; // grid cells only, never the quick slots
            try
            {
                foreach (var item in items)
                {
                    if (!to.AddItem(item)) continue;
                    from.RemoveItem(item);
                }
            }
            finally { Slots.SuppressQuickSlotFallback = false; }
            to.Changed();
            from.Changed();
            Containers.Commit(gui.m_currentContainer);
        }

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.OnTakeAll))]
        private static class InventoryGui_OnTakeAll
        {
            private static bool Prefix(InventoryGui __instance)
            {
                if (!HoardConfig.TakeAllInOrder.Value || !__instance.m_currentContainer) return true;
                if (__instance.m_currentContainer.GetComponent<TombStone>()) return true; // graves restore positions on purpose
                TakeAll(Player.m_localPlayer);
                return false;
            }
        }
    }
}
