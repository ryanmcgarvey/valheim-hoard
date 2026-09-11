using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace Hoard
{
    // Per-character locked slots and trash-flagged item names.
    //
    // A locked slot is off limits to everything automatic: quick stack, sort, store all,
    // quick trash and the trash button. Things the player asks for by hand - crafting,
    // building, feeding a station - may still use its contents, but only after every
    // unlocked stack and every nearby chest has been used up. Items still stack INTO a
    // locked slot, and an empty locked slot is filled only when every other cell is taken.
    public class Locks
    {
        private static readonly Dictionary<long, Locks> _byPlayer = new Dictionary<long, Locks>();

        public static Locks For(Player player) => For(player.GetPlayerID());

        public static Locks For(long playerId)
        {
            if (!_byPlayer.TryGetValue(playerId, out var l))
            {
                l = new Locks(playerId);
                _byPlayer[playerId] = l;
            }
            return l;
        }

        public static Locks Local => Player.m_localPlayer ? For(Player.m_localPlayer) : null;

        private readonly string _path;
        private readonly HashSet<Vector2i> _locked = new HashSet<Vector2i>();
        private readonly HashSet<string> _trash = new HashSet<string>();

        private Locks(long playerId)
        {
            _path = Path.Combine(BepInEx.Paths.ConfigPath, $"Hoard_locks_{playerId}.txt");
            Load();
        }

        private void Load()
        {
            if (!File.Exists(_path)) return;
            try
            {
                foreach (var raw in File.ReadAllLines(_path))
                {
                    var line = raw.Trim();
                    int i = line.IndexOf(':');
                    if (i <= 0) continue;
                    string kind = line.Substring(0, i), val = line.Substring(i + 1);
                    switch (kind)
                    {
                        case "lock":
                            var xy = val.Split(',');
                            if (xy.Length == 2 && int.TryParse(xy[0], out int x) && int.TryParse(xy[1], out int y)) _locked.Add(new Vector2i(x, y));
                            break;
                        case "trash": _trash.Add(val); break;
                    }
                }
            }
            catch (Exception e) { Log.Warn($"Could not read {_path}: {e.Message}"); }
        }

        private void Save()
        {
            try
            {
                var lines = new List<string>();
                foreach (var s in _locked) lines.Add($"lock:{s.x},{s.y}");
                foreach (var s in _trash) lines.Add($"trash:{s}");
                File.WriteAllLines(_path, lines);
            }
            catch (Exception e) { Log.Warn($"Could not write {_path}: {e.Message}"); }
        }

        public bool IsLocked(Vector2i pos) => _locked.Contains(pos);
        public bool IsLocked(ItemDrop.ItemData item) => item != null && _locked.Contains(item.m_gridPos);
        public bool HasLocked => _locked.Count > 0;
        public bool IsTrashFlagged(ItemDrop.ItemData.SharedData shared) => _trash.Contains(shared.m_name);

        public bool IsConsideredTrash(ItemDrop.ItemData.SharedData shared)
        {
            if (!HoardConfig.TrashEnabled.Value) return false;
            if (_trash.Contains(shared.m_name)) return true;
            return HoardConfig.TrophiesAreTrash.Value && shared.m_itemType == ItemDrop.ItemData.ItemType.Trophy;
        }

        public void ToggleLock(Vector2i pos)
        {
            if (!_locked.Remove(pos)) _locked.Add(pos);
            Save();
        }

        public void ToggleTrash(ItemDrop.ItemData.SharedData shared)
        {
            if (!_trash.Remove(shared.m_name)) _trash.Add(shared.m_name);
            Save();
        }

        public void ResetAll()
        {
            _locked.Clear(); _trash.Clear();
            Save();
        }

        // How many of an item the inventory holds outside locked slots.
        public int CountUnlocked(Inventory inv, string name, int quality = -1)
        {
            int n = 0;
            foreach (var item in inv.m_inventory)
            {
                if (item.m_shared.m_name != name || IsLocked(item)) continue;
                if (quality >= 0 && item.m_quality != quality) continue;
                if (item.m_worldLevel < Game.m_worldLevel) continue;
                n += item.m_stack;
            }
            return n;
        }

        public static bool InLockMode() => HoardConfig.LockModifier.Value.IsHeld();

        // Lock modifier + right-click on a slot of the player grid toggles the lock. The
        // click is swallowed so the item isn't used.
        [HarmonyPatch(typeof(InventoryGrid), nameof(InventoryGrid.OnRightDown))]
        private static class InventoryGrid_OnRightDown
        {
            private static bool Prefix(InventoryGrid __instance, UIInputHandler element)
            {
                var gui = InventoryGui.instance;
                var player = Player.m_localPlayer;
                if (!gui || !player || __instance != gui.m_playerGrid) return true;
                if (gui.m_dragGo || player.IsTeleporting() || !InLockMode()) return true;
                Vector2i pos = __instance.GetButtonPos(element.gameObject);
                if (pos.x < 0) return true;
                For(player).ToggleLock(pos);
                return false;
            }
        }

        // ---- tooltips

        [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetTooltip), typeof(ItemDrop.ItemData), typeof(int), typeof(bool), typeof(float), typeof(int), typeof(bool))]
        private static class ItemData_GetTooltip
        {
            private static void Postfix(ItemDrop.ItemData item, bool crafting, ref string __result)
            {
                if (crafting || !HoardConfig.LockTooltips.Value || !Player.m_localPlayer) return;
                var l = Local;
                if (l == null) return;
                if (Player.m_localPlayer.m_inventory.ContainsItem(item) && l.IsLocked(item))
                    __result += $"\n<color=#{ColorUtility.ToHtmlStringRGB(HoardConfig.LockedSlotColor.Value)}>Locked slot: never stacked, sorted, stored or trashed</color>";
                else if (l.IsConsideredTrash(item.m_shared))
                    __result += $"\n<color=#{ColorUtility.ToHtmlStringRGB(HoardConfig.TrashFlagColor.Value)}>Trash-flagged: destroyed by quick trash</color>";
            }
        }

        // ---- borders in the grid

        private static Sprite _border;

        private static Sprite Border()
        {
            if (_border) return _border;
            const int size = 64, thick = 3;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    bool edge = x < thick || y < thick || x >= size - thick || y >= size - thick;
                    px[y * size + x] = edge ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, 0);
                }
            tex.SetPixels32(px);
            tex.Apply();
            _border = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(thick + 1, thick + 1, thick + 1, thick + 1));
            return _border;
        }

        private const string BorderName = "HoardBorder";

        private static Image BorderFor(InventoryElement el)
        {
            var t = el.transform.Find(BorderName);
            if (t) return t.GetComponent<Image>();
            var go = new GameObject(BorderName, typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(el.transform, false);
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(2f, 2f); rt.offsetMax = new Vector2(-2f, -2f);
            var img = go.AddComponent<Image>();
            img.sprite = Border();
            img.type = Image.Type.Sliced;
            img.raycastTarget = false;
            return img;
        }

        [HarmonyPatch(typeof(InventoryGrid), nameof(InventoryGrid.UpdateGui))]
        private static class InventoryGrid_UpdateGui
        {
            private static void Postfix(InventoryGrid __instance, Player player)
            {
                if (player == null || __instance.m_inventory != player.m_inventory) return;
                var l = For(player);
                int width = __instance.m_inventory.GetWidth();
                foreach (var el in __instance.m_elements)
                {
                    if (!el) continue;
                    var img = BorderFor(el);
                    bool locked = l.IsLocked(el.Position);
                    img.enabled = locked;
                    if (locked) img.color = HoardConfig.LockedSlotColor.Value;
                }
                foreach (var item in __instance.m_inventory.m_inventory)
                {
                    int idx = item.GridIndex(width);
                    if (idx < 0 || idx >= __instance.m_elements.Count) continue;
                    var img = BorderFor(__instance.m_elements[idx]);
                    if (img.enabled || !l.IsConsideredTrash(item.m_shared)) continue;
                    img.color = HoardConfig.TrashFlagColor.Value;
                    img.enabled = true;
                }
            }
        }

        // Trash-flagged items can be left on the ground.
        [HarmonyPatch(typeof(Player), nameof(Player.AutoPickup))]
        private static class Player_AutoPickup
        {
            private static readonly List<ItemDrop> _touched = new List<ItemDrop>();

            private static void Prefix(Player __instance)
            {
                if (__instance != Player.m_localPlayer || !HoardConfig.NoAutoPickupOfTrash.Value) return;
                var l = For(__instance);
                var hits = Physics.OverlapSphere(__instance.transform.position + Vector3.up, __instance.m_autoPickupRange, __instance.m_autoPickupMask);
                foreach (var col in hits)
                {
                    var drop = col.attachedRigidbody ? col.attachedRigidbody.GetComponent<ItemDrop>() : null;
                    if (!drop || !drop.m_autoPickup || drop.m_itemData?.m_shared == null) continue;
                    if (!l.IsConsideredTrash(drop.m_itemData.m_shared)) continue;
                    drop.m_autoPickup = false;
                    _touched.Add(drop);
                }
            }

            private static void Finalizer()
            {
                foreach (var d in _touched) if (d) d.m_autoPickup = true;
                _touched.Clear();
            }
        }

        // ---- consumption prefers unlocked stacks (and chests, see Crafting) over locked ones

        // While RemoveItem-by-name runs on the player's inventory and the unlocked stacks
        // alone can cover the amount, the locked items are lifted out of the list so the
        // game's own removal loop can't touch them. Runs AFTER Crafting's prefix (which has
        // already taken what it could from chests and shrunk `amount` accordingly).
        private static class LockGuard
        {
            private static readonly Stack<List<ItemDrop.ItemData>> _hidden = new Stack<List<ItemDrop.ItemData>>();

            public static void Hide(Inventory inv, Locks l)
            {
                List<ItemDrop.ItemData> list = null;
                for (int i = inv.m_inventory.Count - 1; i >= 0; i--)
                {
                    var item = inv.m_inventory[i];
                    if (!l.IsLocked(item)) continue;
                    (list ??= new List<ItemDrop.ItemData>()).Add(item);
                    inv.m_inventory.RemoveAt(i);
                }
                _hidden.Push(list);
            }

            public static void Restore(Inventory inv)
            {
                if (_hidden.Count == 0) return;
                var list = _hidden.Pop();
                if (list != null) inv.m_inventory.AddRange(list);
            }

            public static void RestoreAll(Inventory inv)
            {
                while (_hidden.Count > 0) Restore(inv);
            }
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.RemoveItem), typeof(string), typeof(int), typeof(int), typeof(bool))]
        private static class Inventory_RemoveItem_ByName
        {
            [HarmonyPriority(Priority.Low)]
            private static void Prefix(Inventory __instance, string name, int amount, int itemQuality, ref bool __state)
            {
                __state = false;
                var player = Player.m_localPlayer;
                if (!player || __instance != player.m_inventory) return;
                var l = For(player);
                if (!l.HasLocked || l.CountUnlocked(__instance, name, itemQuality) < amount) return;
                LockGuard.Hide(__instance, l);
                __state = true;
            }

            [HarmonyPriority(Priority.Low)]
            private static void Finalizer(Inventory __instance, bool __state)
            {
                if (__state) LockGuard.Restore(__instance);
            }
        }

        // RemoveItem ends with Changed(): weight and listeners must see the whole inventory.
        [HarmonyPatch(typeof(Inventory), nameof(Inventory.Changed))]
        private static class Inventory_Changed
        {
            [HarmonyPriority(Priority.First)]
            private static void Prefix(Inventory __instance) => LockGuard.RestoreAll(__instance);
        }

        // New items go to an empty locked cell only when no other cell is free.
        [HarmonyPatch(typeof(Inventory), nameof(Inventory.FindEmptySlot))]
        private static class Inventory_FindEmptySlot
        {
            [HarmonyPriority(Priority.Low)]
            private static void Postfix(Inventory __instance, bool topFirst, ref Vector2i __result)
            {
                var player = Player.m_localPlayer;
                if (!player || __instance != player.m_inventory || __result.x < 0) return;
                var l = For(player);
                if (!l.HasLocked || !l.IsLocked(__result)) return;
                int rows = Mathf.Min(__instance.m_height, Slots.VisibleRows);
                for (int r = 0; r < rows; r++)
                {
                    int y = topFirst ? r : rows - 1 - r;
                    for (int x = 0; x < __instance.m_width; x++)
                    {
                        var pos = new Vector2i(x, y);
                        if (l.IsLocked(pos) || __instance.GetItemAt(x, y) != null) continue;
                        __result = pos;
                        return;
                    }
                }
            }
        }
    }
}
