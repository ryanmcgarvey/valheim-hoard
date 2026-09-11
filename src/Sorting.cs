using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Hoard
{
    // Sort the inventory (visible grid only, favorites stay) or a container. Containers we
    // don't own are sorted by asking the owner over RPC, the same way the game hands any
    // other container edit to its owner.
    public static class Sorting
    {
        // ItemType -> coarse category so "by type" groups sensibly: weapons, armor, food, materials...
        private static readonly int[] TypeToCategory = { 0, 2, 4, 7, 7, 8, 10, 10, 0, 6, 0, 10, 10, 1, 7, 7, 0, 10, 9, 7, 7, 3, 7, 5, 9 };

        private static IComparable KeyOf(ItemDrop.ItemData item)
        {
            switch (HoardConfig.SortBy.Value)
            {
                case HoardConfig.SortCriteria.Name: return Localization.instance.Localize(item.m_shared.m_name);
                case HoardConfig.SortCriteria.Value: return item.m_shared.m_value;
                case HoardConfig.SortCriteria.Weight: return item.m_shared.m_weight;
                case HoardConfig.SortCriteria.Type:
                    int t = (int)item.m_shared.m_itemType;
                    return t >= 0 && t < TypeToCategory.Length ? TypeToCategory[t] : t;
                default: return item.m_shared.m_name;
            }
        }

        public static int Compare(ItemDrop.ItemData a, ItemDrop.ItemData b)
        {
            int c = KeyOf(a).CompareTo(KeyOf(b));
            if (!HoardConfig.SortAscending.Value) c = -c;
            if (c == 0) c = string.CompareOrdinal(a.m_shared.m_name, b.m_shared.m_name);
            if (c == 0) c = -a.m_quality.CompareTo(b.m_quality);
            if (c == 0) c = -a.m_stack.CompareTo(b.m_stack);
            return c;
        }

        public static void Run(Player player)
        {
            if (!HoardConfig.SortEnabled.Value || player == null) return;
            var open = InventoryGui.instance?.m_currentContainer;
            if (open)
            {
                SortContainer(open);
                if (HoardConfig.SortBothWhenContainerOpen.Value) SortPlayer(player);
            }
            else SortPlayer(player);
        }

        public static void SortPlayer(Player player) => SortInventory(player.m_inventory, Locks.For(player), HoardConfig.SortIncludesHotbar.Value);

        public static void SortContainer(Container c)
        {
            if (!Containers.IsValid(c)) return;
            if (c.m_nview.IsOwner()) SortInventory(c.GetInventory(), null, true);
            else c.m_nview.InvokeRPC(RpcSort);
        }

        private static bool IsPlain(ItemDrop.ItemData item)
            => item.m_customData == null || item.m_customData.All(kv => kv.Key.StartsWith("eaqs_") || kv.Key.StartsWith("hoard_"));

        // locks == null means "a container": everything moves.
        internal static void SortInventory(Inventory inv, Locks locks, bool includeHotbar)
        {
            bool isPlayer = locks != null;
            int visibleRows = isPlayer ? Mathf.Min(inv.GetHeight(), Slots.VisibleRows) : inv.GetHeight();
            var items = inv.m_inventory.Where(i =>
                (!isPlayer || (i.m_gridPos.y > 0 || includeHotbar))
                && !(isPlayer && Slots.IsSlotCell(i.m_gridPos))
                && !(isPlayer && locks.IsLocked(i))).ToList();
            if (HoardConfig.SortMergesStacks.Value) MergeStacks(items, inv);
            items.Sort(Compare);

            // Target cells: bottom row first (the game's own placement for materials),
            // skipping cells that keep an item that doesn't move; empty locked cells last.
            var reserved = new HashSet<Vector2i>(inv.m_inventory.Where(i => !items.Contains(i)).Select(i => i.m_gridPos));
            var cells = new List<Vector2i>();
            var skippedLockedSlots = new List<Vector2i>();
            int firstRow = (isPlayer && !includeHotbar) ? 1 : 0;
            for (int y = visibleRows - 1; y >= firstRow; y--)
                for (int x = 0; x < inv.GetWidth(); x++)
                {
                    var pos = new Vector2i(x, y);
                    if (reserved.Contains(pos)) continue;
                    if (isPlayer && locks.IsLocked(pos)) { skippedLockedSlots.Add(pos); continue; }
                    cells.Add(pos);
                }
            // Only if the grid is otherwise full do empty locked slots get used: an item
            // must never be left on a cell another item was just moved onto.
            cells.AddRange(skippedLockedSlots);
            int n = Math.Min(items.Count, cells.Count);
            for (int i = 0; i < n; i++) items[i].m_gridPos = cells[i];
            inv.Changed();
        }

        private static void MergeStacks(List<ItemDrop.ItemData> items, Inventory inv)
        {
            var groups = items.Where(i => i.m_stack < i.m_shared.m_maxStackSize && IsPlain(i))
                .GroupBy(i => (i.m_shared.m_name, i.m_quality, i.m_worldLevel)).Select(g => g.ToList()).ToList();
            foreach (var g in groups)
            {
                if (g.Count <= 1) continue;
                int total = g.Sum(i => i.m_stack);
                int max = g[0].m_shared.m_maxStackSize;
                foreach (var item in g)
                {
                    if (total <= 0)
                    {
                        item.m_stack = 0;
                        inv.RemoveItem(item);
                        items.Remove(item);
                    }
                    else
                    {
                        item.m_stack = Math.Min(max, total);
                        total -= item.m_stack;
                    }
                }
            }
        }

        // ---- remote sort

        private const string RpcSort = "Hoard_RequestSort";

        [HarmonyPatch(typeof(Container), nameof(Container.Awake))]
        private static class Container_Awake
        {
            private static void Postfix(Container __instance)
            {
                var nv = __instance.m_nview;
                if (!nv || nv.GetZDO() == null) return;
                nv.Unregister(RpcSort);
                nv.Register(RpcSort, (long sender) =>
                {
                    if (__instance && __instance.m_nview.IsOwner()) SortInventory(__instance.GetInventory(), null, true);
                });
            }
        }

        // ---- auto sort on open

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Show))]
        private static class InventoryGui_Show
        {
            private static void Postfix(InventoryGui __instance, Container container)
            {
                var mode = HoardConfig.SortOnOpen.Value;
                if (mode == HoardConfig.AutoSort.Never || !Player.m_localPlayer || !HoardConfig.SortEnabled.Value) return;
                if (mode == HoardConfig.AutoSort.Inventory || mode == HoardConfig.AutoSort.Both) SortPlayer(Player.m_localPlayer);
                if (container && (mode == HoardConfig.AutoSort.Container || mode == HoardConfig.AutoSort.Both)) SortContainer(container);
            }
        }
    }
}
