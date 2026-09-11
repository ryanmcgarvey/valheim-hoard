using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Hoard
{
    // Death: optionally keep slot items (pulled out of the inventory before anything else
    // touches it, put back afterwards), a tombstone big enough for the full-height
    // inventory, and re-equipping on pickup.
    public static class DeathPatches
    {
        private class Kept { public ItemDrop.ItemData item; public bool equipped; }
        private static readonly List<Kept> _kept = new List<Kept>();

        private static bool KeepSlot(Slots.Slot s)
            => s.Item != null && ((s.IsEquipment && HoardConfig.KeepEquipmentOnDeath.Value) || (s.IsQuick && HoardConfig.KeepQuickSlotsOnDeath.Value));

        private static void BeforeDeath(Player player)
        {
            if (_kept.Count != 0) return;
            Slots.RememberSlots();
            string pid = Game.instance.GetPlayerProfile().GetPlayerID().ToString();
            if (player.LeftItem != null) player.LeftItem.m_customData[Slots.KeyWeapon] = pid;
            if (player.RightItem != null) player.RightItem.m_customData[Slots.KeyWeapon] = pid;
            foreach (var s in Slots.All)
            {
                if (!KeepSlot(s)) continue;
                var item = s.Item;
                _kept.Add(new Kept { item = item, equipped = item.m_equipped || player.IsItemEquiped(item) });
                player.m_inventory.m_inventory.Remove(item);
            }
            Slots.ClearCache();
        }

        private static void AfterDeath(Player player)
        {
            if (_kept.Count == 0) return;
            foreach (var k in _kept)
            {
                k.item.m_equipped = k.equipped;
                player.m_inventory.m_inventory.Add(k.item);
            }
            _kept.Clear();
            Slots.ClearCache();
        }

        [HarmonyPatch(typeof(Character), nameof(Character.CheckDeath))]
        private static class Character_CheckDeath
        {
            [HarmonyPriority(Priority.First)]
            private static void Prefix(Character __instance)
            {
                if (Slots.IsLocal(__instance) && !__instance.IsDead() && __instance.GetHealth() <= 0f) BeforeDeath((Player)__instance);
            }

            [HarmonyPriority(Priority.Last)]
            private static Exception Finalizer(Character __instance, Exception __exception)
            {
                if (Slots.IsLocal(__instance)) AfterDeath((Player)__instance);
                return __exception;
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.OnDeath))]
        private static class Player_OnDeath
        {
            [HarmonyPriority(Priority.Last)]
            private static Exception Finalizer(Player __instance, Exception __exception)
            {
                if (Slots.IsLocal(__instance)) AfterDeath(__instance);
                return __exception;
            }
        }

        // ---- tombstone size

        private static int GraveHeight(int width) => (Slots.Width * Slots.FullHeight - 1) / Mathf.Max(1, width) + 1;

        [HarmonyPatch(typeof(Container), nameof(Container.Awake))]
        private static class Container_Awake
        {
            private static void Prefix(Container __instance)
            {
                if (__instance.m_name != "Grave" && !__instance.GetComponentInParent<TombStone>()) return;
                int h = GraveHeight(__instance.m_width);
                if (h > __instance.m_height) __instance.m_height = h;
            }

            private static void Postfix(Container __instance)
            {
                if (__instance.m_nview?.IsValid() != true || !__instance.m_nview.IsOwner() || !__instance.GetComponent<TombStone>() || __instance.m_height <= Slots.VanillaRows) return;
                PersistSize(__instance);
            }
        }

        private static void PersistSize(Container c)
        {
            string type = c.GetType().Name;
            var zdo = c.m_nview.GetZDO();
            zdo.Set(ZNetView.CustomFieldsStr, true);
            zdo.Set((ZNetView.CustomFieldsStr + type).GetStableHashCode(), true);
            zdo.Set(type + ".m_width", c.m_width);
            zdo.Set(type + ".m_height", c.m_height);
        }

        [HarmonyPatch(typeof(TombStone), nameof(TombStone.Setup))]
        private static class TombStone_Setup
        {
            private static void Postfix(TombStone __instance)
            {
                var c = __instance.m_container ? __instance.m_container : __instance.GetComponent<Container>();
                if (c == null || c.m_inventory == null) return;
                c.m_width = Mathf.Max(c.m_width, c.m_inventory.m_width);
                c.m_height = Mathf.Max(c.m_height, c.m_inventory.m_height);
                if (c.m_nview?.IsValid() == true && c.m_nview.IsOwner()) PersistSize(c);
            }
        }

        [HarmonyPatch(typeof(TombStone), nameof(TombStone.Interact))]
        private static class TombStone_Interact
        {
            [HarmonyPriority(Priority.First)]
            private static void Prefix(TombStone __instance, bool hold)
            {
                if (hold || !__instance.m_container) return;
                int h = GraveHeight(__instance.m_container.m_width);
                if (h <= __instance.m_container.m_height) return;
                __instance.m_container.m_height = h;
                __instance.m_container.m_inventory.m_height = h;
                __instance.m_container.m_lastRevision = 0;
                __instance.m_container.Load();
            }
        }

        // ---- "does it fit" for auto-loot: credit belts still in the grave, don't count
        // items that go straight into their own slot cell.

        [HarmonyPatch(typeof(TombStone), nameof(TombStone.EasyFitInInventory))]
        private static class TombStone_EasyFitInInventory
        {
            private static void Prefix(TombStone __instance, Player player, ref float __state)
            {
                if (!Slots.IsLocal(player)) return;
                __state = (__instance.m_lootStatusEffect as SE_Stats)?.m_addMaxCarryWeight ?? 0f;
                if (HoardConfig.ReequipBeltOnPickup.Value || HoardConfig.ReequipArmorOnPickup.Value)
                {
                    foreach (var item in __instance.m_container.GetInventory().GetAllItems())
                    {
                        if (item?.m_shared.m_equipStatusEffect == null || Slots.At(item.m_gridPos) is not Slots.Slot s || !s.IsEquipment) continue;
                        float limit = 0f;
                        item.m_shared.m_equipStatusEffect.ModifyMaxCarryWeight(0f, ref limit);
                        __state += limit;
                    }
                }
                player.m_maxCarryWeight += __state;
            }

            // The temporary carry-weight credit is undone in the finalizer so an exception in
            // the original can't leave it applied.
            private static void Finalizer(Player player, float __state)
            {
                if (Slots.IsLocal(player)) player.m_maxCarryWeight -= __state;
            }

            private static void Postfix(TombStone __instance, Player player, float __state, ref bool __result)
            {
                if (!Slots.IsLocal(player)) return;
                if (__result) return;
                var grave = __instance.m_container.GetInventory();
                if (grave.NrOfItems() > Slots.ActiveCells) return;
                int needCells = 0;
                var taken = new HashSet<Slots.Slot>();
                foreach (var item in grave.GetAllItemsInGridOrder())
                {
                    var s = Slots.At(item.m_gridPos);
                    if (s == null || taken.Contains(s)) { needCells++; continue; }
                    taken.Add(s);
                    if (s.IsEquipment && s.IsFree && Slots.WouldFitEquipment(s, item)) continue;
                    needCells++;
                }
                __result = needCells <= player.m_inventory.GetEmptySlots()
                           && player.m_inventory.GetTotalWeight() + grave.GetTotalWeight() < player.GetMaxCarryWeight(); // credit still applied here
                SlotValidation.MarkItemsDirty();
            }
        }

        // ---- pickup: re-equip per config, park the rest

        [HarmonyPatch(typeof(TombStone), nameof(TombStone.OnTakeAllSuccess))]
        private static class TombStone_OnTakeAllSuccess { private static void Postfix() => AfterPickup(); }

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.OnTakeAll))]
        private static class InventoryGui_OnTakeAll
        {
            private static void Postfix(InventoryGui __instance)
            {
                if (__instance.m_currentContainer && __instance.m_currentContainer.GetComponent<TombStone>()) AfterPickup();
            }
        }

        private static void AfterPickup()
        {
            var player = Slots.CurrentPlayer;
            var inv = Slots.PlayerInventory;
            if (player == null || inv == null || player.IsDead()) return;
            string pid = Game.instance.GetPlayerProfile().GetPlayerID().ToString();
            foreach (var item in inv.GetAllItems().ToList())
            {
                if (item == null) continue;
                if (item.m_equipped && !player.IsItemEquiped(item)) item.m_equipped = false;
                if (ShouldEquip(player, item, pid))
                {
                    item.m_customData.Remove(Slots.KeyWeapon);
                    if (!player.IsItemEquiped(item)) player.EquipItem(item, false);
                    continue;
                }
                if (!player.IsItemEquiped(item) && Slots.Of(item) is Slots.Slot s && s.IsEquipment && Slots.WouldFitEquipment(s, item)) s.Park(item);
            }
        }

        private static bool ShouldEquip(Player player, ItemDrop.ItemData item, string pid)
        {
            if (HoardConfig.ReequipBeltOnPickup.Value && item.m_shared.m_equipStatusEffect is SE_Stats se && se.m_addMaxCarryWeight > 0f) return true;
            if (HoardConfig.ReequipWeaponsOnPickup.Value && item.m_customData.TryGetValue(Slots.KeyWeapon, out var w) && w == pid) return true;
            if (HoardConfig.ReequipArmorOnPickup.Value && Slots.TryRememberedSlot(item, out var slot) && slot.IsEquipment) return true;
            return false;
        }
    }
}
