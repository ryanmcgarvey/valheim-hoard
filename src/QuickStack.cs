using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Hoard
{
    // Quick stack: every stackable, non-favorited item in the inventory that a nearby
    // container already holds is moved into that container.
    public static class QuickStack
    {
        public static int CompareSlotOrder(Vector2i a, Vector2i b)
        {
            int c = -a.y.CompareTo(b.y);
            return c != 0 ? c : a.x.CompareTo(b.x);
        }

        private static bool ShouldStack(ItemDrop.ItemData item, Favorites fav, bool includeHotbar)
        {
            if (item.m_shared.m_maxStackSize <= 1) return false;
            if (item.m_equipped) return false;
            if (item.m_gridPos.y == 0 && !includeHotbar) return false;
            if (Slots.IsSlotCell(item.m_gridPos)) return false;
            if (fav.IsFavorite(item)) return false;
            return true;
        }

        public static void Run(Player player, bool onlyOpenContainer = false, Container containerOverride = null)
        {
            if (!HoardConfig.QuickStackEnabled.Value || player == null || player.IsTeleporting()) return;
            var gui = InventoryGui.instance;
            gui?.SetupDragItem(null, null, 0);
            var fav = Favorites.For(player);
            var items = player.m_inventory.m_inventory.Where(i => ShouldStack(i, fav, HoardConfig.QuickStackIncludesHotbar.Value)).ToList();
            if (items.Count == 0)
            {
                if (HoardConfig.QuickStackMessages.Value) Msg.Center("Nothing to quick stack");
                return;
            }
            items.Sort((a, b) => -CompareSlotOrder(a.m_gridPos, b.m_gridPos));
            List<ItemDrop.ItemData> trophies = null;
            if (HoardConfig.QuickStackTrophiesTogether.Value)
            {
                trophies = items.Where(i => i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Trophy).ToList();
                items.RemoveAll(i => i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Trophy);
            }

            int moved = 0;
            var open = containerOverride ? containerOverride : gui?.m_currentContainer;
            if (open && Containers.Claim(open))
            {
                moved += Into(open, player.m_inventory, items, trophies);
                Containers.Commit(open);
            }

            bool areaAllowed = HoardConfig.QuickStackRange.Value > 0f && !onlyOpenContainer
                               && (open == null || !HoardConfig.QuickStackOnlyToOpenContainer.Value);
            if (areaAllowed)
            {
                foreach (var c in Containers.Nearby(player.transform.position, HoardConfig.QuickStackRange.Value))
                {
                    if (c == open) continue;
                    if (items.Count == 0 && (trophies == null || trophies.Count == 0)) break;
                    if (!Containers.Claim(c)) continue;
                    int n = Into(c, player.m_inventory, items, trophies);
                    if (n > 0) Containers.Commit(c);
                    moved += n;
                }
            }
            player.m_inventory.Changed();
            if (HoardConfig.QuickStackMessages.Value)
                Msg.Center(moved == 0 ? "Quick stacked nothing" : moved == 1 ? "Quick stacked 1 stack" : $"Quick stacked {moved} stacks");
        }

        private static int Into(Container container, Inventory from, List<ItemDrop.ItemData> items, List<ItemDrop.ItemData> trophies)
        {
            var inv = container.GetInventory();
            int moved = 0;
            if (trophies != null && trophies.Count > 0 && inv.m_inventory.Any(i => i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Trophy))
            {
                for (int i = trophies.Count - 1; i >= 0; i--)
                {
                    var t = trophies[i];
                    if (!inv.AddItem(t)) continue;
                    from.RemoveItem(t);
                    trophies.RemoveAt(i);
                    moved++;
                }
            }
            // Names present in the container, snapshotted first: adding items would otherwise
            // make the container "contain" more names as we go.
            var names = new HashSet<string>(inv.m_inventory.Select(i => i.m_shared.m_name));
            for (int i = items.Count - 1; i >= 0; i--)
            {
                var item = items[i];
                if (!names.Contains(item.m_shared.m_name)) continue;
                if (!inv.AddItem(item)) continue;
                from.RemoveItem(item);
                items.RemoveAt(i);
                moved++;
            }
            return moved;
        }

        // The game's own hold-E "place stacks" on a chest goes through this response; route
        // it through our logic so favorites and slot items stay put.
        [HarmonyPatch(typeof(Container), nameof(Container.RPC_StackResponse))]
        private static class Container_RPC_StackResponse
        {
            private static bool Prefix(Container __instance, bool granted)
            {
                if (!HoardConfig.QuickStackEnabled.Value || !HoardConfig.QuickStackReplacesHoldToStack.Value) return true;
                if (!Player.m_localPlayer) return false;
                if (granted) Run(Player.m_localPlayer, onlyOpenContainer: true, containerOverride: __instance);
                else Player.m_localPlayer.Message(MessageHud.MessageType.Center, "$msg_inuse");
                return false;
            }
        }
    }
}
