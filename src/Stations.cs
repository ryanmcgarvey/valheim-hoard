using HarmonyLib;
using UnityEngine;

namespace Hoard
{
    // Smelters, kilns, blast furnaces, fireplaces, cooking stations and fermenters take
    // their ore/fuel/food from nearby containers when the inventory has none. The pull
    // goes container -> player inventory -> station, so every station keeps running its
    // own unmodified checks (allowed item, full, fire lit) against an item it can see.
    //
    // Holding the fill-all modifier while using a smelter/kiln/fire/cooking-station fuel
    // switch keeps adding until the station is full or the supply runs out.
    public static class Stations
    {
        private static bool Enabled => HoardConfig.CraftFromContainers.Value && HoardConfig.CraftFuelStations.Value && Player.m_localPlayer;
        private static bool FillAll => HoardConfig.FillAllKey.Value.IsHeld();
        private static bool IsLocal(Humanoid user) => user != null && user == Player.m_localPlayer;

        private static bool EnsureOne(Humanoid user, string sharedName, Component station)
        {
            if (user.GetInventory().HaveItem(sharedName)) return true;
            var near = Containers.Nearby(station.transform.position, HoardConfig.CraftRange.Value);
            return Containers.PullOne(near, sharedName, user.GetInventory());
        }

        // How many of `sharedName` can the user get their hands on right now.
        private static int Available(Humanoid user, string sharedName, Component station)
            => user.GetInventory().CountItems(sharedName) + Containers.CountAll(Containers.Nearby(station.transform.position, HoardConfig.CraftRange.Value), sharedName);

        // ---- Smelter / kiln / blast furnace

        private static bool _inSmelterLoop;

        [HarmonyPatch(typeof(Smelter), nameof(Smelter.OnAddFuel))]
        private static class Smelter_OnAddFuel
        {
            private static bool Prefix(Smelter __instance, Switch sw, Humanoid user, ItemDrop.ItemData item, ref bool __result)
            {
                if (!Enabled || !IsLocal(user) || item != null || _inSmelterLoop || !__instance.m_fuelItem) return true;
                string fuel = __instance.m_fuelItem.m_itemData.m_shared.m_name;
                if (!FillAll)
                {
                    EnsureOne(user, fuel, __instance);
                    return true;
                }
                int free = __instance.m_maxFuel - Mathf.CeilToInt(__instance.GetFuel());
                int added = 0;
                _inSmelterLoop = true;
                try
                {
                    for (int i = 0; i < free; i++)
                    {
                        if (!EnsureOne(user, fuel, __instance)) break;
                        if (!__instance.OnAddFuel(sw, user, null)) break;
                        added++;
                    }
                }
                finally { _inSmelterLoop = false; }
                if (added == 0) return true; // let vanilla produce its own message
                user.Message(MessageHud.MessageType.Center, Localization.instance.Localize($"$msg_added {fuel} x{added}"));
                __result = true;
                return false;
            }
        }

        [HarmonyPatch(typeof(Smelter), nameof(Smelter.OnAddOre))]
        private static class Smelter_OnAddOre
        {
            private static bool Prefix(Smelter __instance, Switch sw, Humanoid user, ItemDrop.ItemData item, ref bool __result)
            {
                if (!Enabled || !IsLocal(user) || item != null || _inSmelterLoop) return true;
                if (!FillAll)
                {
                    if (__instance.FindCookableItem(user.GetInventory()) == null) PullAnyOre(__instance, user);
                    return true;
                }
                int free = __instance.m_maxOre - __instance.GetQueueSize();
                int added = 0;
                _inSmelterLoop = true;
                try
                {
                    for (int i = 0; i < free; i++)
                    {
                        if (__instance.FindCookableItem(user.GetInventory()) == null && !PullAnyOre(__instance, user)) break;
                        if (!__instance.OnAddOre(sw, user, null)) break;
                        added++;
                    }
                }
                finally { _inSmelterLoop = false; }
                if (added == 0) return true;
                __result = true;
                return false;
            }

            private static bool PullAnyOre(Smelter smelter, Humanoid user)
            {
                var near = Containers.Nearby(smelter.transform.position, HoardConfig.CraftRange.Value);
                foreach (var conv in smelter.m_conversion)
                {
                    if (conv.m_from == null) continue;
                    if (Containers.PullOne(near, conv.m_from.m_itemData.m_shared.m_name, user.GetInventory())) return true;
                }
                return false;
            }
        }

        [HarmonyPatch(typeof(Smelter), nameof(Smelter.OnHoverAddFuel))]
        private static class Smelter_OnHoverAddFuel
        {
            private static void Postfix(Smelter __instance, ref string __result)
            {
                if (!Enabled || !__instance.m_fuelItem || !Player.m_localPlayer) return;
                string fuel = __instance.m_fuelItem.m_itemData.m_shared.m_name;
                int avail = Available(Player.m_localPlayer, fuel, __instance);
                int free = __instance.m_maxFuel - Mathf.CeilToInt(__instance.GetFuel());
                int n = Mathf.Min(avail, free);
                if (n <= 0) return;
                __result += Localization.instance.Localize($"\n[<color=yellow><b>{HoardConfig.FillAllKey.Value.Label()} + $KEY_Use</b></color>] $piece_smelter_add {fuel} x{n} (inventory + chests)");
            }
        }

        [HarmonyPatch(typeof(Smelter), nameof(Smelter.OnHoverAddOre))]
        private static class Smelter_OnHoverAddOre
        {
            private static void Postfix(Smelter __instance, ref string __result)
            {
                if (!Enabled || !Player.m_localPlayer) return;
                int free = __instance.m_maxOre - __instance.GetQueueSize();
                if (free <= 0) return;
                var parts = new System.Collections.Generic.List<string>();
                foreach (var conv in __instance.m_conversion)
                {
                    if (conv.m_from == null) continue;
                    int n = Mathf.Min(free, Available(Player.m_localPlayer, conv.m_from.m_itemData.m_shared.m_name, __instance));
                    if (n > 0) parts.Add($"{conv.m_from.m_itemData.m_shared.m_name} x{n}");
                }
                if (parts.Count == 0) return;
                __result += Localization.instance.Localize($"\n[<color=yellow><b>{HoardConfig.FillAllKey.Value.Label()} + $KEY_Use</b></color>] {__instance.m_addOreTooltip} {string.Join(", ", parts)} (inventory + chests)");
            }
        }

        // ---- Fireplace (campfire, hearth, torches)

        private static bool _inFireLoop;

        [HarmonyPatch(typeof(Fireplace), nameof(Fireplace.Interact))]
        private static class Fireplace_Interact
        {
            private static bool Prefix(Fireplace __instance, Humanoid user, bool hold, bool alt, ref bool __result)
            {
                if (!Enabled || !IsLocal(user) || hold || _inFireLoop || !__instance.m_canRefill || __instance.m_infiniteFuel || !__instance.m_fuelItem) return true;
                float fuelNow = __instance.m_nview.GetZDO().GetFloat(ZDOVars.s_fuel);
                string fuel = __instance.m_fuelItem.m_itemData.m_shared.m_name;
                // A fire that can be turned off toggles on a plain use once it has any fuel;
                // only the very first log goes in through the refill branch, so for those
                // fires just make sure that one log is in the inventory and let vanilla run.
                if (__instance.m_canTurnOff && !alt)
                {
                    if (fuelNow <= 0f) EnsureOne(user, fuel, __instance);
                    return true;
                }
                if (!FillAll)
                {
                    EnsureOne(user, fuel, __instance);
                    return true;
                }
                int free = Mathf.FloorToInt(__instance.m_maxFuel - fuelNow);
                int added = 0;
                _inFireLoop = true;
                try
                {
                    for (int i = 0; i < free; i++)
                    {
                        if (!EnsureOne(user, fuel, __instance)) break;
                        if (!__instance.Interact(user, false, alt)) break;
                        added++;
                    }
                }
                finally { _inFireLoop = false; }
                if (added == 0) return true;
                __result = true;
                return false;
            }
        }

        // ---- Cooking station

        private static bool _inCookLoop;

        [HarmonyPatch(typeof(CookingStation), nameof(CookingStation.OnAddFuelSwitch))]
        private static class CookingStation_OnAddFuelSwitch
        {
            private static bool Prefix(CookingStation __instance, Switch sw, Humanoid user, ItemDrop.ItemData item, ref bool __result)
            {
                if (!Enabled || !IsLocal(user) || item != null || _inCookLoop || !__instance.m_fuelItem) return true;
                string fuel = __instance.m_fuelItem.m_itemData.m_shared.m_name;
                if (!FillAll)
                {
                    EnsureOne(user, fuel, __instance);
                    return true;
                }
                int free = __instance.m_maxFuel - Mathf.CeilToInt(__instance.GetFuel());
                int added = 0;
                _inCookLoop = true;
                try
                {
                    for (int i = 0; i < free; i++)
                    {
                        if (!EnsureOne(user, fuel, __instance)) break;
                        if (!__instance.OnAddFuelSwitch(sw, user, null)) break;
                        added++;
                    }
                }
                finally { _inCookLoop = false; }
                if (added == 0) return true;
                __result = true;
                return false;
            }
        }

        // FindCookableItem is also a read-only query (hover text, radial menu); only pull
        // from chests when the player is actually using the station.
        private static bool _cookInteract;

        [HarmonyPatch(typeof(CookingStation), nameof(CookingStation.OnInteract))]
        private static class CookingStation_OnInteract
        {
            private static void Prefix(Humanoid user) => _cookInteract = IsLocal(user);
            private static void Finalizer() => _cookInteract = false;
        }

        [HarmonyPatch(typeof(CookingStation), nameof(CookingStation.FindCookableItem))]
        private static class CookingStation_FindCookableItem
        {
            private static void Postfix(CookingStation __instance, Inventory inventory, ref ItemDrop.ItemData __result)
            {
                if (__result != null || !Enabled || !_cookInteract || !Player.m_localPlayer || inventory != Player.m_localPlayer.GetInventory()) return;
                if (__instance.m_requireFire && !__instance.IsFireLit()) return;
                if (__instance.GetFreeSlot() == -1) return;
                var near = Containers.Nearby(__instance.transform.position, HoardConfig.CraftRange.Value);
                foreach (var conv in __instance.m_conversion)
                {
                    string name = conv.m_from.m_itemData.m_shared.m_name;
                    if (!Containers.PullOne(near, name, inventory)) continue;
                    __result = inventory.GetItem(name);
                    return;
                }
            }
        }

        // ---- Fermenter

        [HarmonyPatch(typeof(Fermenter), nameof(Fermenter.FindCookableItem))]
        private static class Fermenter_FindCookableItem
        {
            private static void Postfix(Fermenter __instance, Inventory inventory, ref ItemDrop.ItemData __result)
            {
                if (__result != null || !Enabled || !Player.m_localPlayer || inventory != Player.m_localPlayer.GetInventory()) return;
                if (__instance.GetStatus() != Fermenter.Status.Empty) return;
                var near = Containers.Nearby(__instance.transform.position, HoardConfig.CraftRange.Value);
                foreach (var conv in __instance.m_conversion)
                {
                    string name = conv.m_from.m_itemData.m_shared.m_name;
                    if (!Containers.PullOne(near, name, inventory)) continue;
                    __result = inventory.GetItem(name);
                    return;
                }
            }
        }
    }
}
