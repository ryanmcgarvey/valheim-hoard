using System;
using System.Linq;
using HarmonyLib;

namespace Hoard
{
    // Safety net: on every save the slot-region contents are serialized into player custom
    // data. Custom data survives the mod being removed (vanilla load drops out-of-range
    // items but never touches unknown keys), so a reinstall can put the items back.
    public static class SlotBackup
    {
        public const string Key = "hoard_backup";
        public const string RowsKey = "hoard_visible_rows";
        private const int Version = 1;

        private class Envelope
        {
            public string date, world;
            public int items, width, height, visibleRows;
            public ZPackage inventory;
        }

        private static string Serialize(Inventory inv)
        {
            int width = Slots.Width, height = Slots.HiddenRows;
            var backup = new Inventory(Key, null, width, height);
            foreach (var item in inv.GetAllItemsInGridOrder().Where(i => i.m_gridPos.y >= Slots.VisibleRows))
            {
                var clone = item.Clone();
                backup.AddItem(clone, new Vector2i(clone.m_gridPos.x, clone.m_gridPos.y - Slots.VisibleRows));
            }
            var pkg = new ZPackage();
            backup.Save(pkg);
            var env = new ZPackage();
            env.Write(Version);
            env.Write(DateTime.Now.ToString("u"));
            env.Write(ZNet.instance?.GetWorldName() ?? "");
            env.Write(backup.NrOfItems());
            env.Write(width);
            env.Write(height);
            env.Write(Slots.VisibleRows);
            env.WriteCompressed(pkg);
            return env.GetBase64();
        }

        private static bool TryRead(Player player, out Envelope env)
        {
            env = null;
            if (!player.m_customData.TryGetValue(Key, out var b64) || string.IsNullOrEmpty(b64)) return false;
            try
            {
                var pkg = new ZPackage(b64);
                if (pkg.ReadInt() != Version) return false;
                env = new Envelope
                {
                    date = pkg.ReadString(), world = pkg.ReadString(), items = pkg.ReadInt(),
                    width = pkg.ReadInt(), height = pkg.ReadInt(), visibleRows = pkg.ReadInt(),
                    inventory = pkg.ReadCompressedPackage(),
                };
                return env.items > 0;
            }
            catch (Exception e)
            {
                Log.Warn($"Could not read slot backup: {e.Message}");
                return false;
            }
        }

        private static bool TryRestore(Player player)
        {
            if (!TryRead(player, out var env)) return false;
            var inv = player.m_inventory;
            try
            {
                var backup = new Inventory(Key, null, env.width, env.height);
                backup.Load(env.inventory);
                if (backup.NrOfItems() == 0) return false;
                // Rows grew since the backup: the old slot items may have landed in the visible grid.
                if (Slots.VisibleRows > env.visibleRows && backup.GetAllItems().All(b =>
                        inv.GetItemAt(b.m_gridPos.x, b.m_gridPos.y + env.visibleRows) is ItemDrop.ItemData p
                        && p.m_shared.m_name == b.m_shared.m_name && p.m_stack == b.m_stack && p.m_quality == b.m_quality))
                    return false;
                int restored = 0;
                foreach (var b in backup.GetAllItemsInGridOrder())
                {
                    var pos = new Vector2i(b.m_gridPos.x, b.m_gridPos.y + Slots.VisibleRows);
                    var item = b.Clone();
                    if (!inv.AddItem(item, pos)) continue;
                    restored++;
                    item = inv.GetItemAt(pos.x, pos.y) ?? item;
                    if (item.IsEquipable() && item.m_equipped)
                    {
                        item.m_equipped = false;
                        if (!player.EquipItem(item, false)) item.m_equipped = false;
                    }
                }
                if (restored > 0)
                {
                    Log.Info($"Restored {restored} slot items from backup ({env.date}, world {env.world})");
                    SlotValidation.MarkItemsDirty();
                }
                return restored > 0;
            }
            catch (Exception e)
            {
                Log.Warn($"Could not restore slot backup: {e.Message}");
                return false;
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.Save))]
        private static class Player_Save
        {
            [HarmonyPriority(Priority.Last)]
            private static void Prefix(Player __instance)
            {
                if (__instance != Slots.CurrentPlayer) return;
                __instance.m_customData[RowsKey] = Slots.VisibleRows.ToString();
                if (HoardConfig.SlotBackup.Value) __instance.m_customData[Key] = Serialize(__instance.m_inventory);
            }
        }

        // The slot rows move when the visible row count changes between sessions (config
        // edit, rows mod). Saved positions are absolute, so shift the region to follow.
        [HarmonyPatch(typeof(Player), nameof(Player.Load))]
        private static class Player_Load
        {
            [HarmonyPriority(Priority.High)]
            private static void Postfix(Player __instance)
            {
                if (!FejdStartup.instance && !Slots.IsLocal(__instance)) return;
                Slots.LoadingPlayer = __instance;
                try
                {
                    Slots.SetBaseRowsFromSave(__instance);
                    if (__instance.m_customData.TryGetValue(RowsKey, out var s) && int.TryParse(s, out int prev) && prev > 0 && prev != Slots.VisibleRows)
                        ShiftRows(__instance, prev);
                    else if (!__instance.m_customData.ContainsKey(RowsKey) && __instance.m_customData.TryGetValue("eaqs_visible_rows", out var es) && int.TryParse(es, out int eprev) && eprev > 0 && eprev != Slots.VisibleRows)
                        ShiftRows(__instance, eprev);

                    if (HoardConfig.SlotBackup.Value && __instance.m_customData.ContainsKey(Key) && !__instance.m_inventory.GetAllItems().Any(Slots.IsInSlot))
                        TryRestore(__instance);
                }
                finally { Slots.LoadingPlayer = null; }
            }
        }

        private static void ShiftRows(Player player, int previousVisibleRows)
        {
            int delta = Slots.VisibleRows - previousVisibleRows;
            int moved = 0;
            foreach (var item in player.m_inventory.m_inventory)
            {
                if (item.m_gridPos.y < previousVisibleRows) continue;
                item.m_gridPos = new Vector2i(item.m_gridPos.x, item.m_gridPos.y + delta);
                moved++;
            }
            player.m_customData[RowsKey] = Slots.VisibleRows.ToString();
            if (moved == 0) return;
            Slots.ClearCache();
            player.m_inventory.Changed();
            SlotValidation.MarkItemsDirty();
            SlotValidation.MarkSlotsDirty();
            Log.Info($"Visible rows changed {previousVisibleRows} -> {Slots.VisibleRows}; moved {moved} slot item(s) with the region");
        }
    }
}
