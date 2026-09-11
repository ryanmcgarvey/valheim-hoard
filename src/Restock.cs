using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Hoard
{
    // Restock: partial stacks in the inventory are topped up from nearby containers.
    public static class Restock
    {
        private class Want
        {
            public ItemDrop.ItemData item;
            public int have;
            public int target;
        }

        private static int TargetStack(ItemDrop.ItemData.SharedData shared)
        {
            int limit = HoardConfig.RestockStackLimit.Value;
            return limit > 0 ? Math.Min(shared.m_maxStackSize, limit) : shared.m_maxStackSize;
        }

        private static bool IsPlain(ItemDrop.ItemData item)
            => item.m_customData == null || item.m_customData.All(kv => kv.Key.StartsWith("eaqs_") || kv.Key.StartsWith("hoard_"));

        private static bool ShouldRestock(ItemDrop.ItemData item, bool includeHotbar)
        {
            int target = TargetStack(item.m_shared);
            if (target <= 1 || item.m_stack >= target) return false;
            if (!IsPlain(item)) return false;
            if (item.m_gridPos.y == 0 && !includeHotbar) return false;
            if (Slots.IsEquipmentCell(item.m_gridPos)) return false;
            var t = item.m_shared.m_itemType;
            if (HoardConfig.RestockOnlyAmmoAndConsumables.Value && t != ItemDrop.ItemData.ItemType.Ammo && t != ItemDrop.ItemData.ItemType.Consumable) return false;
            return true;
        }

        public static void Run(Player player, bool onlyOpenContainer = false)
        {
            if (!HoardConfig.RestockEnabled.Value || player == null || player.IsTeleporting()) return;
            var gui = InventoryGui.instance;
            gui?.SetupDragItem(null, null, 0);
            var wants = player.m_inventory.m_inventory
                .Where(i => ShouldRestock(i, HoardConfig.RestockIncludesHotbar.Value))
                .Select(i => new Want { item = i, have = i.m_stack, target = TargetStack(i.m_shared) })
                .ToList();
            int total = wants.Count;
            if (total == 0)
            {
                if (HoardConfig.RestockMessages.Value) Msg.Center("Nothing to restock");
                return;
            }
            wants.Sort((a, b) => -QuickStack.CompareSlotOrder(a.item.m_gridPos, b.item.m_gridPos));
            var touched = new HashSet<Vector2i>();
            int filled = 0;
            var open = gui?.m_currentContainer;
            if (open && Containers.Claim(open))
            {
                filled += From(open, player.m_inventory, wants, touched);
                Containers.Commit(open);
            }

            bool areaAllowed = HoardConfig.RestockRange.Value > 0f && !onlyOpenContainer
                               && (open == null || !HoardConfig.RestockOnlyFromOpenContainer.Value);
            if (areaAllowed)
            {
                foreach (var c in Containers.Nearby(player.transform.position, HoardConfig.RestockRange.Value))
                {
                    if (c == open) continue;
                    if (wants.Count == 0) break;
                    if (!Containers.Claim(c)) continue;
                    int before = touched.Count + filled;
                    filled += From(c, player.m_inventory, wants, touched);
                    if (touched.Count + filled != before) Containers.Commit(c);
                }
            }
            player.m_inventory.Changed();
            if (HoardConfig.RestockMessages.Value)
            {
                if (filled == 0 && touched.Count == 0) Msg.Center($"Restocked nothing ({total} stacks wanted)");
                else if (filled < total) Msg.Center($"Restocked {touched.Count}/{total} stacks (some partially)");
                else Msg.Center($"Restocked {total} stacks");
            }
        }

        private static int From(Container container, Inventory into, List<Want> wants, HashSet<Vector2i> touched)
        {
            var inv = container.GetInventory();
            int done = 0;
            for (int w = wants.Count - 1; w >= 0; w--)
            {
                var want = wants[w];
                for (int i = inv.m_inventory.Count - 1; i >= 0; i--)
                {
                    var src = inv.m_inventory[i];
                    if (!IsPlain(src) || src.m_shared.m_name != want.item.m_shared.m_name || src.m_quality != want.item.m_quality) continue;
                    int n = Math.Min(want.target - want.have, src.m_stack);
                    if (n <= 0) break;
                    if (!into.MoveItemToThis(inv, src, n, want.item.m_gridPos.x, want.item.m_gridPos.y)) continue;
                    want.have += n;
                    touched.Add(want.item.m_gridPos);
                    if (want.have >= want.target) break;
                }
                if (want.have >= want.target)
                {
                    wants.RemoveAt(w);
                    done++;
                }
            }
            return done;
        }
    }
}
