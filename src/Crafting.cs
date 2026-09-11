using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace Hoard
{
    // Craft, build and upgrade with materials that are still in nearby chests.
    //
    // Two halves. "Can I?" is answered by extending the game's own requirement checks so
    // the container contents count as if they were in the inventory. "Consume" hooks the
    // single method every consumption path funnels through (Inventory.RemoveItem by
    // name): while a craft or build is consuming resources, whatever the inventory can't
    // supply is taken from the containers instead.
    public static class Crafting
    {
        public static bool Enabled => HoardConfig.CraftFromContainers.Value && Player.m_localPlayer;

        private static bool StationMatches(Piece.Requirement req, CraftingStation station)
        {
            if (station != null && station.m_upgrader != req.m_upgraderResource) return false;
            if (station == null && req.m_upgraderResource) return false;
            return req.m_resItem;
        }

        // Inventory + containers, best single quality level (the game requires all of a
        // requirement to come from one quality level).
        private static int BestAvailable(Player player, Piece.Requirement req, List<Container> near)
        {
            string name = req.m_resItem.m_itemData.m_shared.m_name;
            int best = 0;
            int maxQ = Mathf.Max(1, req.m_resItem.m_itemData.m_shared.m_maxQuality);
            for (int q = 1; q <= maxQ; q++)
            {
                int n = player.m_inventory.CountItems(name, q) + Containers.CountAll(near, name, q);
                if (n > best) best = n;
            }
            return best;
        }

        public static int TotalAvailable(Player player, string sharedName)
            => player.m_inventory.CountItems(sharedName) + Containers.CountAll(Containers.ForCrafting(), sharedName);

        // ---- "Can I craft this" (recipes)

        [HarmonyPatch(typeof(Player), nameof(Player.HaveRequirementItems))]
        private static class Player_HaveRequirementItems
        {
            private static void Postfix(Player __instance, Recipe piece, bool discover, int qualityLevel, int amount, ref bool __result)
            {
                if (__result || discover || !Enabled || __instance != Player.m_localPlayer) return;
                var near = Containers.ForCrafting();
                if (near.Count == 0) return;
                var station = __instance.GetCurrentCraftingStation();
                bool any = false;
                foreach (var req in piece.m_resources)
                {
                    if (!StationMatches(req, station)) continue;
                    int need = req.GetAmount(qualityLevel) * amount;
                    if (need <= 0) continue;
                    int have = BestAvailable(__instance, req, near);
                    if (piece.m_requireOnlyOneIngredient)
                    {
                        if (have >= need) { any = true; break; }
                    }
                    else if (have < need) return;
                    else any = true;
                }
                if (piece.m_requireOnlyOneIngredient && !any) return;
                __result = true;
            }
        }

        // Single-ingredient recipes (e.g. cooking anything into a stew): the game picks
        // which ingredient to use and how much; if the inventory alone can't supply one,
        // offer the first ingredient the containers can cover.
        [HarmonyPatch(typeof(Player), nameof(Player.GetFirstRequiredItem))]
        private static class Player_GetFirstRequiredItem
        {
            private static void Postfix(Player __instance, Inventory inventory, Recipe recipe, int qualityLevel, ref int amount, ref int extraAmount, int craftMultiplier, ref ItemDrop.ItemData __result)
            {
                if (__result != null || !Enabled || __instance != Player.m_localPlayer) return;
                var near = Containers.ForCrafting();
                if (near.Count == 0) return;
                var station = __instance.GetCurrentCraftingStation();
                foreach (var req in recipe.m_resources)
                {
                    if (!StationMatches(req, station)) continue;
                    string name = req.m_resItem.m_itemData.m_shared.m_name;
                    int need = req.GetAmount(qualityLevel) * craftMultiplier;
                    int maxQ = Mathf.Max(1, req.m_resItem.m_itemData.m_shared.m_maxQuality);
                    for (int q = 1; q <= maxQ; q++)
                    {
                        if (inventory.CountItems(name, q) + Containers.CountAll(near, name, q) < need) continue;
                        var item = inventory.GetItem(name, q);
                        if (item == null)
                        {
                            foreach (var c in near)
                            {
                                item = c.GetInventory().GetItem(name, q);
                                if (item != null) break;
                            }
                        }
                        if (item == null) continue;
                        amount = need;
                        extraAmount = req.m_extraAmountOnlyOneIngredient;
                        __result = item;
                        return;
                    }
                }
            }
        }

        // ---- "Can I build this" (pieces)

        [HarmonyPatch(typeof(Player), nameof(Player.HaveRequirements), typeof(Piece), typeof(Player.RequirementMode))]
        private static class Player_HaveRequirements_Piece
        {
            private static void Postfix(Player __instance, Piece piece, Player.RequirementMode mode, ref bool __result)
            {
                if (__result || !Enabled || __instance != Player.m_localPlayer || piece == null) return;
                if (mode == Player.RequirementMode.IsKnown) return;
                if (piece.m_craftingStation)
                {
                    if (mode == Player.RequirementMode.CanAlmostBuild)
                    {
                        if (!__instance.m_knownStations.ContainsKey(piece.m_craftingStation.m_name)) return;
                    }
                    else if (!CraftingStation.HaveBuildStationInRange(piece.m_craftingStation.m_name, __instance.transform.position)
                             && !ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoWorkbench)) return;
                }
                if (piece.m_dlc.Length > 0 && !DLCMan.instance.IsDLCInstalled(piece.m_dlc)) return;
                var near = Containers.ForCrafting();
                if (near.Count == 0) return;
                foreach (var req in piece.m_resources)
                {
                    if (!req.m_resItem || req.m_amount <= 0) continue;
                    string name = req.m_resItem.m_itemData.m_shared.m_name;
                    int have = __instance.m_inventory.CountItems(name) + Containers.CountAll(near, name);
                    if (mode == Player.RequirementMode.CanAlmostBuild ? have <= 0 : have < req.m_amount) return;
                }
                __result = true;
            }
        }

        // ---- Consumption

        // Set while a craft/build is paying its resources; the RemoveItem patch below only
        // reaches into containers inside that window.
        private static int _consuming;
        public static bool Consuming => _consuming > 0;

        [HarmonyPatch(typeof(Player), nameof(Player.ConsumeResources))]
        private static class Player_ConsumeResources
        {
            private static void Prefix(Player __instance) { if (__instance == Player.m_localPlayer) _consuming++; }
            private static void Finalizer(Player __instance) { if (__instance == Player.m_localPlayer) _consuming--; }
        }

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.DoCrafting))]
        private static class InventoryGui_DoCrafting
        {
            private static void Prefix() => _consuming++;
            private static void Finalizer() => _consuming--;
        }

        // Order of preference when paying: unlocked stacks in the inventory, then nearby
        // chests, then locked slots (Locks.Inventory_RemoveItem_ByName hides those from the
        // game's removal loop whenever the rest suffices). Priority.High so this runs before
        // that guard and it sees the reduced amount.
        [HarmonyPatch(typeof(Inventory), nameof(Inventory.RemoveItem), typeof(string), typeof(int), typeof(int), typeof(bool))]
        private static class Inventory_RemoveItem_ByName
        {
            [HarmonyPriority(Priority.High)]
            private static void Prefix(Inventory __instance, string name, ref int amount, int itemQuality, bool worldLevelBased)
            {
                if (!Consuming || !Enabled) return;
                var p = Player.m_localPlayer;
                if (!p || __instance != p.m_inventory) return;
                int unlocked = Locks.For(p).CountUnlocked(__instance, name, itemQuality);
                int shortfall = amount - unlocked;
                if (shortfall <= 0) return;
                int taken = Containers.Remove(Containers.ForCrafting(), name, shortfall, itemQuality);
                amount -= taken;
                if (taken < shortfall && __instance.CountItems(name, itemQuality, worldLevelBased) < amount)
                    Log.Warn($"Consumed {taken}/{shortfall} {name} from containers; short by {amount - __instance.CountItems(name, itemQuality, worldLevelBased)}");
            }
        }

        // ---- UI: requirement rows show inventory+containers, build menu shows how many you can afford

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.SetupRequirement))]
        private static class InventoryGui_SetupRequirement
        {
            private static void Postfix(Transform elementRoot, Piece.Requirement req, Player player, bool craft, int quality, int craftMultiplier, bool __result)
            {
                if (!__result || !Enabled || !HoardConfig.ShowContainerCounts.Value) return;
                if (req?.m_resItem == null || player != Player.m_localPlayer) return;
                var text = elementRoot.Find("res_amount")?.GetComponent<TMP_Text>();
                if (!text) return;
                int need = req.GetAmount(quality) * craftMultiplier;
                if (need <= 0) return;
                string name = req.m_resItem.m_itemData.m_shared.m_name;
                int have = TotalAvailable(player, name);
                text.text = $"{Compact(have)}/{need}";
                bool free = craft ? ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoCraftCost) : ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoBuildCost);
                text.color = have < need && !free ? (Mathf.Sin(Time.time * 10f) > 0f ? Color.red : Color.white) : Color.white;
            }

            private static string Compact(int n) => n < 10000 ? n.ToString() : (n / 1000f).ToString("0.#") + "k";
        }

        [HarmonyPatch(typeof(Hud), nameof(Hud.SetupPieceInfo))]
        private static class Hud_SetupPieceInfo
        {
            private static void Postfix(Hud __instance, Piece piece)
            {
                if (!Enabled || !HoardConfig.ShowBuildCount.Value || piece == null || !__instance.m_buildSelection) return;
                if (piece.m_name == "$piece_repair" || piece.m_resources == null || piece.m_resources.Length == 0) return;
                var p = Player.m_localPlayer;
                int crafts = int.MaxValue;
                foreach (var req in piece.m_resources)
                {
                    if (!req.m_resItem || req.m_amount <= 0) continue;
                    int have = TotalAvailable(p, req.m_resItem.m_itemData.m_shared.m_name);
                    crafts = Mathf.Min(crafts, have / req.m_amount);
                }
                if (crafts == int.MaxValue) return;
                string color = crafts > 0 ? "#80ff80" : "#ff8080";
                __instance.m_buildSelection.text = Localization.instance.Localize(piece.m_name) + $" (<color={color}>{crafts}</color>)";
            }
        }
    }
}
