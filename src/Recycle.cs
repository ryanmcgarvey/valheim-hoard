using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace Hoard
{
    // Recycle: drag an item onto the Recycle button to break it back into the materials
    // its recipe (and every upgrade level it has) cost. Only materials the game lets
    // through a portal are returned: metals, ores and anything else flagged
    // non-teleportable are silently forfeited. That closes the "craft axes, portal home,
    // recycle them" route around hauling metal by boat, while still letting you clean out
    // old gear.
    public static class Recycle
    {
        private static bool _pending;

        public static bool Enabled => HoardConfig.RecycleEnabled.Value;

        public static void OnRecyclePressed()
        {
            if (!Enabled || _pending || !InventoryGui.instance || !InventoryGui.instance.m_dragGo) return;
            _pending = true;
        }

        public class Plan
        {
            public Recipe recipe;
            public int count;                      // how many crafts' worth is being recycled
            public readonly List<(ItemDrop drop, int amount)> returned = new List<(ItemDrop, int)>();
            public readonly List<(string name, int amount)> forfeited = new List<(string, int)>();
        }

        // What recycling `amount` of `item` would give back; null when the item can't be recycled.
        public static Plan Compute(ItemDrop.ItemData item, int amount, out string reason)
        {
            reason = null;
            if (item == null || item.m_shared.m_questItem) { reason = "Can't recycle that"; return null; }
            if (item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Consumable && !HoardConfig.RecycleConsumables.Value) { reason = "Food and mead can't be recycled"; return null; }
            var recipe = ObjectDB.instance?.GetRecipe(item);
            if (recipe == null || recipe.m_resources == null || recipe.m_resources.Length == 0 || recipe.m_amount <= 0) { reason = "No recipe to recycle from"; return null; }
            if (item.m_shared.m_maxStackSize > 1 && amount % recipe.m_amount != 0 && amount < recipe.m_amount) { reason = $"Recycle at least {recipe.m_amount} at a time"; return null; }

            var plan = new Plan { recipe = recipe, count = amount };
            float rate = Mathf.Clamp(HoardConfig.RecycleRate.Value, 0f, 100f) / 100f;
            // Stackables: the recipe makes m_amount per craft, so a stack is amount/m_amount crafts.
            float crafts = item.m_shared.m_maxStackSize > 1 ? amount / (float)recipe.m_amount : amount;
            foreach (var req in recipe.m_resources)
            {
                if (!req.m_resItem || req.m_upgraderResource) continue;
                int invested = req.GetAmount(1);
                for (int q = 2; q <= item.m_quality; q++) invested += req.GetAmount(q);
                int give = Mathf.FloorToInt(invested * crafts * rate);
                if (give <= 0) continue;
                var shared = req.m_resItem.m_itemData.m_shared;
                if (!shared.m_teleportable || IsBanned(req.m_resItem.name, shared.m_name))
                    plan.forfeited.Add((shared.m_name, give));
                else
                    plan.returned.Add((req.m_resItem, give));
            }
            if (plan.returned.Count == 0 && plan.forfeited.Count == 0) { reason = "Nothing would come back"; return null; }
            return plan;
        }

        private static bool IsBanned(string prefabName, string sharedName)
        {
            var list = HoardConfig.RecycleNeverReturn.Value;
            if (string.IsNullOrWhiteSpace(list)) return false;
            foreach (var raw in list.Split(','))
            {
                var s = raw.Trim();
                if (s.Length == 0) continue;
                if (string.Equals(s, prefabName, System.StringComparison.OrdinalIgnoreCase) || string.Equals(s, sharedName, System.StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.UpdateItemDrag))]
        private static class InventoryGui_UpdateItemDrag
        {
            private static void Postfix(InventoryGui __instance)
            {
                if (!_pending) return;
                _pending = false;
                var player = Player.m_localPlayer;
                var item = __instance.m_dragItem;
                var inv = __instance.m_dragInventory;
                int amount = __instance.m_dragAmount;
                if (!player || item == null || inv == null || !inv.ContainsItem(item)) return;
                if (inv != player.m_inventory) { Msg.Center("Move it to your inventory first"); return; }

                var plan = Compute(item, amount, out string reason);
                if (plan == null) { Msg.Center(reason); return; }

                var sb = new StringBuilder();
                foreach (var (drop, n) in plan.returned) sb.Append($"{n} {Localization.instance.Localize(drop.m_itemData.m_shared.m_name)}\n");
                if (plan.returned.Count == 0) sb.Append("Nothing comes back.\n");
                if (plan.forfeited.Count > 0)
                {
                    sb.Append("\nNot returned (can't go through a portal):\n");
                    foreach (var (name, n) in plan.forfeited) sb.Append($"{n} {Localization.instance.Localize(name)}\n");
                }
                string header = $"Recycle {amount} x {Localization.instance.Localize(item.m_shared.m_name)}?";
                if (HoardConfig.RecycleConfirm.Value) Trash.Confirm(header, sb.ToString().TrimEnd(), () => Execute(__instance, player, item, amount, plan));
                else Execute(__instance, player, item, amount, plan);
            }
        }

        private static void Execute(InventoryGui gui, Player player, ItemDrop.ItemData item, int amount, Plan plan)
        {
            var inv = player.m_inventory;
            if (!inv.ContainsItem(item)) return;
            if (amount >= item.m_stack)
            {
                player.RemoveEquipAction(item);
                player.UnequipItem(item, false);
                inv.RemoveItem(item);
            }
            else inv.RemoveItem(item, amount);

            foreach (var (drop, n) in plan.returned)
            {
                int left = n;
                var data = drop.m_itemData;
                // Fill stacks in the inventory; whatever doesn't fit lands at your feet.
                while (left > 0)
                {
                    int chunk = Mathf.Min(left, Mathf.Max(1, data.m_shared.m_maxStackSize));
                    var added = inv.AddItem(drop.name, chunk, 1, 0, 0L, "", false);
                    if (added == null)
                    {
                        var clone = data.Clone();
                        clone.m_dropPrefab = drop.gameObject;
                        clone.m_stack = chunk;
                        ItemDrop.DropItem(clone, chunk, player.transform.position + player.transform.forward + Vector3.up, Quaternion.identity);
                    }
                    left -= chunk;
                }
            }
            gui.SetupDragItem(null, null, 1);
            gui.UpdateCraftingPanel(false);
            inv.Changed();
            Msg.Center(plan.returned.Count > 0 ? "Recycled" : "Recycled (nothing portable to return)");
            Log.Debug($"recycled {amount} x {item.m_shared.m_name}: {plan.returned.Count} materials returned, {plan.forfeited.Count} forfeited");
        }
    }
}
