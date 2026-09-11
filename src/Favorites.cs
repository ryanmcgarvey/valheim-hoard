using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace Hoard
{
    // Per-character favorites: favorited slots (by grid position), favorited item names
    // and trash-flagged item names. Favorites are never quick-stacked, sorted, stored or
    // trashed; trash-flagged items are what quick trash destroys.
    //
    // Stored as a small text file per character next to the BepInEx configs.
    public class Favorites
    {
        private static readonly Dictionary<long, Favorites> _byPlayer = new Dictionary<long, Favorites>();

        public static Favorites For(Player player) => For(player.GetPlayerID());

        public static Favorites For(long playerId)
        {
            if (!_byPlayer.TryGetValue(playerId, out var f))
            {
                f = new Favorites(playerId);
                _byPlayer[playerId] = f;
            }
            return f;
        }

        public static Favorites Local => Player.m_localPlayer ? For(Player.m_localPlayer) : null;

        private readonly string _path;
        private readonly HashSet<Vector2i> _slots = new HashSet<Vector2i>();
        private readonly HashSet<string> _items = new HashSet<string>();
        private readonly HashSet<string> _trash = new HashSet<string>();

        private Favorites(long playerId)
        {
            _path = Path.Combine(BepInEx.Paths.ConfigPath, $"Hoard_favorites_{playerId}.txt");
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
                        case "slot":
                            var xy = val.Split(',');
                            if (xy.Length == 2 && int.TryParse(xy[0], out int x) && int.TryParse(xy[1], out int y)) _slots.Add(new Vector2i(x, y));
                            break;
                        case "item": _items.Add(val); break;
                        case "trash": _trash.Add(val); break;
                    }
                }
            }
            catch (Exception e) { Log.Warn($"Could not read favorites {_path}: {e.Message}"); }
        }

        private void Save()
        {
            try
            {
                var lines = new List<string>();
                foreach (var s in _slots) lines.Add($"slot:{s.x},{s.y}");
                foreach (var s in _items) lines.Add($"item:{s}");
                foreach (var s in _trash) lines.Add($"trash:{s}");
                File.WriteAllLines(_path, lines);
            }
            catch (Exception e) { Log.Warn($"Could not write favorites {_path}: {e.Message}"); }
        }

        public bool IsSlotFavorite(Vector2i pos) => _slots.Contains(pos);
        public bool IsItemFavorite(ItemDrop.ItemData.SharedData shared) => _items.Contains(shared.m_name);
        public bool IsFavorite(ItemDrop.ItemData item) => IsItemFavorite(item.m_shared) || IsSlotFavorite(item.m_gridPos);
        public bool IsTrashFlagged(ItemDrop.ItemData.SharedData shared) => _trash.Contains(shared.m_name);

        public bool IsConsideredTrash(ItemDrop.ItemData.SharedData shared)
        {
            if (!HoardConfig.TrashEnabled.Value) return false;
            if (_trash.Contains(shared.m_name)) return true;
            return HoardConfig.TrophiesAreTrash.Value && shared.m_itemType == ItemDrop.ItemData.ItemType.Trophy && !IsItemFavorite(shared);
        }

        public void ToggleSlot(Vector2i pos)
        {
            if (!_slots.Remove(pos)) _slots.Add(pos);
            Save();
        }

        // Returns false if the item is trash-flagged (can't be both).
        public bool ToggleItem(ItemDrop.ItemData.SharedData shared)
        {
            if (_trash.Contains(shared.m_name)) return false;
            if (!_items.Remove(shared.m_name)) _items.Add(shared.m_name);
            Save();
            return true;
        }

        public bool ToggleTrash(ItemDrop.ItemData.SharedData shared)
        {
            if (_items.Contains(shared.m_name)) return false;
            if (!_trash.Remove(shared.m_name)) _trash.Add(shared.m_name);
            Save();
            return true;
        }

        public void ResetAll()
        {
            _slots.Clear(); _items.Clear(); _trash.Clear();
            Save();
        }

        // ---- favoriting mode: hold the modifier and click

        public static bool InFavoritingMode() => HoardConfig.FavoriteModifier.Value.IsHeld();

        // Left-click with the modifier favorites the item under the cursor; right-click
        // favorites the slot. Both swallow the click so nothing gets picked up.
        [HarmonyPatch(typeof(InventoryGrid), nameof(InventoryGrid.OnLeftDown))]
        private static class InventoryGrid_OnLeftDown
        {
            private static bool Prefix(InventoryGrid __instance, UIInputHandler clickHandler) => HandleClick(__instance, clickHandler.gameObject, true);
        }

        [HarmonyPatch(typeof(InventoryGrid), nameof(InventoryGrid.OnRightDown))]
        private static class InventoryGrid_OnRightDown
        {
            private static bool Prefix(InventoryGrid __instance, UIInputHandler element) => HandleClick(__instance, element.gameObject, false);
        }

        private static bool HandleClick(InventoryGrid grid, GameObject go, bool left)
        {
            var gui = InventoryGui.instance;
            var player = Player.m_localPlayer;
            if (!gui || !player || grid != gui.m_playerGrid) return true;
            if (gui.m_dragGo || player.IsTeleporting() || !InFavoritingMode()) return true;
            Vector2i pos = grid.GetButtonPos(go);
            if (pos.x < 0) return true;
            var fav = For(player);
            if (!left)
            {
                fav.ToggleSlot(pos);
                return false;
            }
            var item = grid.m_inventory.GetItemAt(pos.x, pos.y);
            if (item == null) return true;
            if (!fav.ToggleItem(item.m_shared)) Msg.Center("Can't favorite a trash-flagged item");
            return false;
        }

        // ---- tooltips

        [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetTooltip), typeof(ItemDrop.ItemData), typeof(int), typeof(bool), typeof(float), typeof(int), typeof(bool))]
        private static class ItemData_GetTooltip
        {
            private static void Postfix(ItemDrop.ItemData item, bool crafting, ref string __result)
            {
                if (crafting || !HoardConfig.FavoriteTooltips.Value || !Player.m_localPlayer) return;
                var fav = Local;
                if (fav == null) return;
                if (fav.IsItemFavorite(item.m_shared))
                    __result += $"\n<color=#{ColorUtility.ToHtmlStringRGB(HoardConfig.FavoriteItemColor.Value)}>Favorited: never stacked, sorted, stored or trashed</color>";
                else if (fav.IsConsideredTrash(item.m_shared))
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
                var fav = For(player);
                int width = __instance.m_inventory.GetWidth();
                foreach (var el in __instance.m_elements)
                {
                    if (!el) continue;
                    var img = BorderFor(el);
                    bool slotFav = fav.IsSlotFavorite(el.Position);
                    img.enabled = slotFav;
                    if (slotFav) img.color = HoardConfig.FavoriteSlotColor.Value;
                }
                foreach (var item in __instance.m_inventory.m_inventory)
                {
                    int idx = item.GridIndex(width);
                    if (idx < 0 || idx >= __instance.m_elements.Count) continue;
                    var img = BorderFor(__instance.m_elements[idx]);
                    if (fav.IsItemFavorite(item.m_shared))
                    {
                        img.color = img.enabled ? HoardConfig.FavoriteBothColor.Value : HoardConfig.FavoriteItemColor.Value;
                        img.enabled = true;
                    }
                    else if (fav.IsConsideredTrash(item.m_shared))
                    {
                        img.color = HoardConfig.TrashFlagColor.Value;
                        img.enabled = true;
                    }
                }
            }
        }

        // Trash-flagged items can be left on the ground.
        [HarmonyPatch(typeof(Player), nameof(Player.AutoPickup))]
        private static class Player_AutoPickup
        {
            // Wrap the whole pass: temporarily clear m_autoPickup on trash-flagged drops
            // in range, restore afterwards. Cheaper to reason about than a transpiler.
            private static readonly List<ItemDrop> _touched = new List<ItemDrop>();

            private static void Prefix(Player __instance)
            {
                if (__instance != Player.m_localPlayer || !HoardConfig.NoAutoPickupOfTrash.Value) return;
                var fav = For(__instance);
                var hits = Physics.OverlapSphere(__instance.transform.position + Vector3.up, __instance.m_autoPickupRange, __instance.m_autoPickupMask);
                foreach (var col in hits)
                {
                    var drop = col.attachedRigidbody ? col.attachedRigidbody.GetComponent<ItemDrop>() : null;
                    if (!drop || !drop.m_autoPickup || drop.m_itemData?.m_shared == null) continue;
                    if (!fav.IsConsideredTrash(drop.m_itemData.m_shared)) continue;
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
    }
}
