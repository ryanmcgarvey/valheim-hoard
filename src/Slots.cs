using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using UnityEngine;

namespace Hoard
{
    // Equipment and quick slots are real cells of the player's one Inventory, in two hidden
    // rows appended below the visible grid. The panel UI relocates those cells' widgets
    // out of the grid; nothing else about the inventory changes, so saves, tombstones and
    // every vanilla item operation keep working on plain grid positions.
    //
    // Layout of the hidden region (index -> cell is fixed math and load-bearing for saved
    // characters; it matches EquipmentAndQuickSlots 3.x so a character coming from that mod
    // finds every item already in the right cell):
    //
    //   row VisibleRows+0:  0-5 Quick1..Quick6, 6-7 unused
    //   row VisibleRows+1:  8 Helmet, 9 Chest, 10 Legs, 11 Shoulder, 12 Utility, 13 Trinket, 14-15 unused
    public static partial class Slots
    {
        public const int MaxQuickSlots = 6;
        public const int MaxExtraRows = 5;
        // Rows needed to hold SlotCount cells at the current grid width (2 for the vanilla 8-wide grid).
        public static int HiddenRows => Mathf.Max(1, Mathf.CeilToInt(SlotCount / (float)Width));
        public const int SlotCount = 16;
        public const int QuickStart = 0;
        public const int EquipStart = 8;
        public const int EquipCount = 6;
        public const int VanillaRows = 4;
        public const int VanillaWidth = 8;

        public static readonly Vector2i NoPosition = new Vector2i(-1, -1);

        // Per-item memory of the slot it was in, so items coming back from a grave return
        // to their cell. Only honored for the same character.
        public const string KeyPlayer = "hoard_player";
        public const string KeySlot = "hoard_slot";
        public const string KeyWeapon = "hoard_weapon";
        public const string KeyParked = "hoard_parked";
        // Read-only compatibility with EquipmentAndQuickSlots tags left on items.
        public const string EaqsKeyPlayer = "eaqs_player";
        public const string EaqsKeySlot = "eaqs_slot";

        public static readonly ItemDrop.ItemData.ItemType[] EquipTypes =
        {
            ItemDrop.ItemData.ItemType.Helmet, ItemDrop.ItemData.ItemType.Chest, ItemDrop.ItemData.ItemType.Legs,
            ItemDrop.ItemData.ItemType.Shoulder, ItemDrop.ItemData.ItemType.Utility, ItemDrop.ItemData.ItemType.Trinket,
        };
        private static readonly string[] EquipNames = { "Head", "Chest", "Legs", "Shoulders", "Utility", "Trinket" };
        private static readonly string[] EquipIds = { "Helmet", "Chest", "Legs", "Shoulder", "Utility", "Trinket" };

        public class Slot
        {
            public readonly int Index;
            public readonly string Id;
            private Vector2i _pos = NoPosition;

            public Slot(int index, string id) { Index = index; Id = id; }

            public Vector2i Position => _pos;
            public bool IsQuick => Index >= QuickStart && Index < QuickStart + MaxQuickSlots;
            public bool IsEquipment => Index >= EquipStart && Index < EquipStart + EquipCount;
            public bool IsUnused => !IsQuick && !IsEquipment;
            public int QuickIndex => Index - QuickStart;
            public ItemDrop.ItemData.ItemType EquipType => IsEquipment ? EquipTypes[Index - EquipStart] : ItemDrop.ItemData.ItemType.None;

            public bool IsActive
            {
                get
                {
                    if (IsQuick) return HoardConfig.QuickSlots.Value && QuickIndex < HoardConfig.QuickSlotCount.Value;
                    if (IsEquipment) return HoardConfig.EquipmentSlots.Value;
                    return false;
                }
            }

            public string Name
            {
                get
                {
                    if (IsEquipment) return EquipNames[Index - EquipStart];
                    if (IsQuick) return QuickLabel(QuickIndex);
                    return "";
                }
            }

            public KeyboardShortcut Shortcut => IsQuick ? HoardConfig.QuickSlotKeys[QuickIndex].Value : KeyboardShortcut.Empty;

            internal void UpdatePosition(bool carryItem = true)
            {
                var item = carryItem ? Item : null;
                _pos = new Vector2i(Index % Width, VisibleRows + Index / Width);
                if (item != null) item.m_gridPos = _pos;
            }

            public ItemDrop.ItemData Item
            {
                get
                {
                    var inv = PlayerInventory;
                    if (inv == null || _pos == NoPosition) return null;
                    if (_cache.TryGetValue(_pos, out var cached)) return cached;
                    var item = inv.GetItemAt(_pos.x, _pos.y);
                    _cache[_pos] = item;
                    return item;
                }
            }

            public bool IsFree => Item == null;
            public void ClearCache() => _cache.Remove(_pos);

            // May this item be put into this cell at all (type only for equipment cells).
            public bool Fits(ItemDrop.ItemData item)
            {
                if (item == null || !IsActive) return false;
                if (IsEquipment) return item.m_shared.m_itemType == EquipType;
                return IsQuick;
            }

            // May this item stay here: equipment cells hold equipped items only, except an
            // item whose equip is queued, or armor "parked" here by a tombstone pickup.
            public bool Belongs(ItemDrop.ItemData item)
                => Fits(item) && (!IsEquipment || IsEquipped(item) || IsEquipQueued(item) || IsParkedHere(item));

            public bool IsParkedHere(ItemDrop.ItemData item)
                => item != null && item.m_customData.TryGetValue(KeyParked, out var s) && s == Id;

            public void Park(ItemDrop.ItemData item) { if (item != null) item.m_customData[KeyParked] = Id; }

            public bool IsFreeQuick => IsQuick && IsActive && IsFree;

            public override string ToString() => Id;
        }

        public static readonly Slot[] All = new Slot[SlotCount];
        private static readonly Dictionary<Vector2i, ItemDrop.ItemData> _cache = new Dictionary<Vector2i, ItemDrop.ItemData>();

        // The character's vanilla row count (Haldor sells rows) plus the configured extras.
        public static int BaseRows { get; private set; } = VanillaRows;
        public static int ExtraRows => Mathf.Clamp(HoardConfig.ExtraInventoryRows?.Value ?? 0, 0, MaxExtraRows);
        public static int VisibleRows => BaseRows + ExtraRows;
        public static int FullHeight => VisibleRows + HiddenRows;
        public static int Width => PlayerInventory?.GetWidth() ?? VanillaWidth;
        public static int VisibleCells => VisibleRows * Width;
        public static int ActiveCells => VisibleCells + All.Count(s => s.IsActive);

        // While a character is loading in the main menu / on spawn there may be no
        // m_localPlayer yet; patches set this for the duration.
        public static Player LoadingPlayer;
        public static Player CurrentPlayer => Player.m_localPlayer ?? LoadingPlayer;
        public static Inventory PlayerInventory => CurrentPlayer?.m_inventory;

        public static PlayerProfile CurrentProfile
            => Game.instance ? Game.instance.GetPlayerProfile()
               : (FejdStartup.instance && FejdStartup.instance.m_profiles != null && FejdStartup.instance.m_profileIndex >= 0 && FejdStartup.instance.m_profileIndex < FejdStartup.instance.m_profiles.Count
                   ? FejdStartup.instance.m_profiles[FejdStartup.instance.m_profileIndex] : null);

        public static bool IsLocal(Character c)
            => c != null && c.IsPlayer() && Player.m_localPlayer == c && c.m_nview && c.m_nview.IsValid() && c.m_nview.IsOwner();

        static Slots()
        {
            for (int i = 0; i < SlotCount; i++)
            {
                string id = i < MaxQuickSlots ? $"Quick{i + 1}"
                          : i >= EquipStart && i < EquipStart + EquipCount ? EquipIds[i - EquipStart]
                          : $"Unused{i}";
                All[i] = new Slot(i, id);
            }
            UpdatePositions();
        }

        public static string QuickLabel(int i)
        {
            string label = HoardConfig.QuickSlotLabels[i]?.Value;
            if (!string.IsNullOrEmpty(label)) return label;
            return HoardConfig.QuickSlotKeys[i].Value.Label();
        }

        public static void ClearCache() => _cache.Clear();

        // carryItems: residents move with their slots (a live row change). False when the
        // items are already at the right absolute positions (a save being loaded).
        public static void UpdatePositions(bool carryItems = true)
        {
            ClearCache();
            if (carryItems) foreach (var s in All) _ = s.Item; // cache residents at the old positions
            foreach (var s in All) s.UpdatePosition(carryItems);
            ClearCache();
            SlotPatches.ApplyInventoryHeight();
        }

        // While set, nothing falls back into a free quick slot (auto-pickup with the option
        // on, and Take All from a chest).
        public static bool SuppressQuickSlotFallback;
        public static bool NoQuickSlotFallback => SuppressQuickSlotFallback || SlotPatches.Player_AutoPickup.Active;

        // ---- queries

        public static bool IsSlotCell(Vector2i pos) => pos.y >= VisibleRows;
        public static bool IsEquipmentCell(Vector2i pos) => pos.y == VisibleRows + 1 && pos.x < EquipCount;
        public static bool IsInSlot(ItemDrop.ItemData item) => item != null && IsSlotCell(item.m_gridPos);

        public static Slot At(Vector2i pos)
        {
            if (!IsSlotCell(pos)) return null;
            foreach (var s in All) if (s.Position == pos) return s;
            return null;
        }

        public static Slot Of(ItemDrop.ItemData item)
        {
            if (!IsInSlot(item)) return null;
            var inv = PlayerInventory;
            if (inv == null || !inv.ContainsItem(item)) return null;
            return At(item.m_gridPos);
        }

        public static IEnumerable<Slot> EquipmentSlots(bool onlyActive = true) => All.Where(s => s.IsEquipment && (!onlyActive || s.IsActive));
        public static IEnumerable<Slot> QuickSlots() => All.Where(s => s.IsQuick);
        public static Slot Find(string id) => All.FirstOrDefault(s => s.Id == id);

        public static bool IsEquipmentSlotItem(ItemDrop.ItemData item) => All.Any(s => s.IsEquipment && s.IsActive && s.Fits(item));

        // "Would this item belong here once equipped" - the drag-to-equip check.
        public static bool WouldFitEquipment(Slot slot, ItemDrop.ItemData item)
            => item != null && slot.IsEquipment && slot.IsActive && item.m_shared.m_itemType == slot.EquipType;

        public static bool IsEquipped(ItemDrop.ItemData item) => item != null && (item.m_equipped || CurrentPlayer?.IsItemEquiped(item) == true);

        private static bool IsActionQueued(ItemDrop.ItemData item, Player.MinorActionData.ActionType type)
        {
            var p = CurrentPlayer;
            if (item == null || p == null || p.m_actionQueue == null) return false;
            foreach (var a in p.m_actionQueue) if (a.m_item == item && a.m_type == type) return true;
            return false;
        }

        public static bool IsEquipQueued(ItemDrop.ItemData item) => IsActionQueued(item, Player.MinorActionData.ActionType.Equip);
        public static bool IsUnequipQueued(ItemDrop.ItemData item) => IsActionQueued(item, Player.MinorActionData.ActionType.Unequip);

        public static void QueueEquip(Player p, ItemDrop.ItemData item)
        {
            if (p == null || item == null || p.IsItemEquiped(item) || IsEquipQueued(item)) return;
            if (IsUnequipQueued(item)) { p.RemoveEquipAction(item); return; }
            p.QueueEquipAction(item);
        }

        public static void QueueUnequip(Player p, ItemDrop.ItemData item)
        {
            if (p == null || item == null || !p.IsItemEquiped(item) || IsUnequipQueued(item)) return;
            if (IsEquipQueued(item)) { p.RemoveEquipAction(item); return; }
            p.QueueUnequipAction(item);
        }

        // ---- finding homes for items

        public static bool TryRememberedSlot(ItemDrop.ItemData item, out Slot slot)
        {
            slot = null;
            string me = CurrentProfile?.GetPlayerID().ToString();
            if (me == null) return false;
            if (item.m_customData.TryGetValue(KeyPlayer, out var pid) && pid == me && item.m_customData.TryGetValue(KeySlot, out var sid))
                slot = Find(sid);
            else if (item.m_customData.TryGetValue(EaqsKeyPlayer, out var epid) && epid == me && item.m_customData.TryGetValue(EaqsKeySlot, out var esid))
                slot = Find(esid);
            return slot != null;
        }

        public static bool TryFindFreeSlot(ItemDrop.ItemData item, out Slot slot)
        {
            slot = null;
            if (item == null) return false;
            if (TryRememberedSlot(item, out var prev) && prev.IsActive && prev.Belongs(item) && (prev.IsFree || item == prev.Item))
            {
                slot = prev;
                return true;
            }
            slot = All.FirstOrDefault(s => s.IsActive && s.IsFree && s.Belongs(item));
            return slot != null;
        }

        public static bool TryFindFreeEquipmentSlot(ItemDrop.ItemData item, out Slot slot)
        {
            slot = item == null ? null : EquipmentSlots().FirstOrDefault(s => s.IsFree && s.Belongs(item));
            return slot != null;
        }

        // An equipment cell of the right type whose occupant is not worn (and not about to be).
        public static bool TryFindUnequippedOccupiedSlot(ItemDrop.ItemData item, out Slot slot)
        {
            slot = item == null ? null : EquipmentSlots().FirstOrDefault(s => !s.IsFree && s.Fits(item) && !CurrentPlayer.IsItemEquiped(s.Item) && !IsEquipQueued(s.Item));
            return slot != null;
        }

        public static bool TryFindEmptyQuickSlot(out Slot slot)
        {
            slot = All.FirstOrDefault(s => s.IsFreeQuick);
            return slot != null;
        }

        public static Vector2i EmptyQuickSlot() => TryFindEmptyQuickSlot(out var s) ? s.Position : NoPosition;
        public static int EmptyQuickSlots() => All.Count(s => s.IsFreeQuick);

        // Free a visible cell (bottom-right first) by pushing a visible item into a free slot.
        public static bool TryMakeRoomInGrid(out Vector2i pos)
        {
            pos = NoPosition;
            var inv = PlayerInventory;
            if (inv == null) return false;
            var visible = new List<ItemDrop.ItemData>();
            for (int y = VisibleRows - 1; y >= 0; y--)
                for (int x = Width - 1; x >= 0; x--)
                {
                    var item = inv.GetItemAt(x, y);
                    if (item == null) { pos = new Vector2i(x, y); return true; }
                    visible.Add(item);
                }
            ClearCache();
            foreach (var item in visible)
            {
                if (!TryFindFreeSlot(item, out var slot)) continue;
                pos = item.m_gridPos;
                item.m_gridPos = slot.Position;
                ClearCache();
                return true;
            }
            return false;
        }

        // ---- slot memory tags

        public static void RememberSlots()
        {
            var profile = CurrentProfile;
            if (profile == null) return;
            string pid = profile.GetPlayerID().ToString();
            foreach (var s in All)
            {
                var item = s.Item;
                if (item == null) continue;
                item.m_customData[KeyPlayer] = pid;
                item.m_customData[KeySlot] = s.Id;
            }
        }

        public static void ForgetSlot(ItemDrop.ItemData item)
        {
            if (item == null) return;
            item.m_customData.Remove(KeyPlayer);
            item.m_customData.Remove(KeySlot);
            item.m_customData.Remove(EaqsKeyPlayer);
            item.m_customData.Remove(EaqsKeySlot);
        }

        // ---- row changes

        // Player.Awake: the prefab inventory is 4 rows; the character's own count follows
        // once its save is read (SetBaseRowsFromSave) or bought (SetBaseRows).
        internal static void CaptureBaseRows(Inventory inv)
        {
            BaseRows = Mathf.Max(1, inv.m_height);
        }

        // Called right after Player.Load, before anything else looks at grid positions: the
        // saved items already sit at absolute positions computed with the character's real
        // row count ("invrows" unique key, which vanilla only applies at OnSpawned), so only
        // the slot coordinates are recomputed - nothing moves.
        internal static void SetBaseRowsFromSave(Player player)
        {
            if (!player.TryGetUniqueKeyValue("invrows", out var v) || !int.TryParse(v, out int rows)) return;
            rows = Mathf.Clamp(rows, 1, 9);
            if (rows == BaseRows) return;
            Log.Info($"Character has {rows} inventory rows (prefab {BaseRows}); slot region placed below them");
            BaseRows = rows;
            UpdatePositions(carryItems: false);
        }

        // A live change (Haldor sells a row): residents move with their slots.
        internal static void SetBaseRows(int rows)
        {
            rows = Mathf.Max(1, rows);
            if (rows == BaseRows) return;
            Log.Info($"Vanilla inventory rows {BaseRows} -> {rows}; slot region moves with them");
            BaseRows = rows;
            OnVisibleRowsChanged();
        }

        // The slot region moved. Residents move with their slots; anything from the old
        // visible grid now sitting on a slot cell is moved back into the grid.
        public static void OnVisibleRowsChanged()
        {
            var inv = PlayerInventory;
            if (inv == null) { UpdatePositions(); return; }
            ClearCache();
            var residents = All.ToDictionary(s => s, s => s.Item);
            UpdatePositions();
            bool changed = false;
            foreach (var item in inv.m_inventory.ToList())
            {
                if (!IsSlotCell(item.m_gridPos)) continue;
                var slot = At(item.m_gridPos);
                residents.TryGetValue(slot ?? All[0], out var resident);
                bool intruder = slot == null || (resident != null && resident != item) || (resident == null && !slot.Belongs(item));
                if (!intruder) continue;
                var free = inv.FindEmptySlot(true);
                if (free.x >= 0) item.m_gridPos = free;
                else if (TryFindFreeSlot(item, out var fs)) item.m_gridPos = fs.Position;
                else if (item.m_gridPos.y >= FullHeight || item.m_gridPos.x >= Width) item.m_gridPos = new Vector2i(Width - 1, FullHeight - 1);
                ClearCache();
                changed = true;
            }
            if (changed) inv.Changed();
            SlotValidation.MarkItemsDirty();
            SlotValidation.MarkSlotsDirty();
        }

        public static void OnSlotActivationChanged()
        {
            if (PlayerInventory == null) return;
            ClearCache();
            SlotValidation.MarkItemsDirty();
            SlotValidation.MarkSlotsDirty();
            PlayerInventory.Changed();
        }

        // ---- quick slot hotkeys (called from the plugin's Update)

        public static void HandleHotkeys()
        {
            var p = Player.m_localPlayer;
            if (p == null || !p.TakeInput() || !HoardConfig.QuickSlots.Value) return;
            foreach (var s in All)
            {
                if (!s.IsQuick || !s.IsActive) continue;
                var item = s.Item;
                if (item == null) continue;
                if (PreventSimilarHotkeys.IsShortcutDown(s.Shortcut))
                    p.UseItem(null, item, false);
            }
        }
    }
}
