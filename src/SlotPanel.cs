using System;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Hoard
{
    // The floating panel next to the inventory: equipment cells in a two-column paperdoll
    // arrangement (head/chest/legs left, shoulders/utility/trinket right) with the quick
    // slots in a row below. The cells are the real InventoryGrid elements of the hidden
    // rows, relocated under a root of our own; drag/drop, tooltips and gamepad selection
    // keep working because nothing about them changed but their position.
    public static class SlotPanel
    {
        private static Vector2? _drag;
        private static Vector2 Base => _drag ?? HoardConfig.PanelPosition.Value;
        private static bool CanDrag => HoardConfig.PanelDraggable.Value || HoardConfig.PanelDragKey.Value.IsHeld();

        private const float Cell = 70f;
        private const float Pitch = Cell + 10f;
        private static readonly Vector2 EquipOrigin = new Vector2(60.5f, -27f);
        // Two aligned columns: Head/Chest/Legs left, Shoulders/Utility/Trinket right.
        private static readonly Vector2[] EquipOffsets =
        {
            new Vector2(0f, 0f), new Vector2(0f, -Pitch), new Vector2(0f, -2f * Pitch),
            new Vector2(Pitch, 0f), new Vector2(Pitch, -Pitch), new Vector2(Pitch, -2f * Pitch),
        };
        private static readonly Vector2 EquipBkgCenter = new Vector2(132.5f, -139f);
        private static readonly Vector2 EquipBkgSize = new Vector2(210f, 260f);
        private static readonly Vector2 EquipLabelPos = new Vector2(32f, 5f);
        private const float RowLeft = 25.5f, QuickRowTop = -302f, RowBkgLeft = 14.5f, RowBkgHeight = 90f, RowBkgPitch = 74f;

        private static RectTransform _invBkg, _invDarken, _invFrame, _equipBkg, _quickBkg, _slotRoot, _hiddenRoot;
        private static Image _invBkgImage;
        private static Vector2? _containerPivot;
        private static Color _normal = Color.clear, _highlight = Color.clear;
        private static float _labelSize;

        private static int ActiveQuick => HoardConfig.QuickSlots.Value ? HoardConfig.QuickSlotCount.Value : 0;

        public static Vector2 PositionOf(Slots.Slot s)
        {
            if (s.IsEquipment) return Base + EquipOrigin + EquipOffsets[s.Index - Slots.EquipStart];
            if (s.IsQuick) return Base + new Vector2(RowLeft + s.QuickIndex * Cell, QuickRowTop);
            return Base;
        }

        // From InventoryGui.Update while visible.
        private static void UpdateBackgrounds()
        {
            var gui = InventoryGui.instance;
            if (!gui || !gui.m_player) return;
            if (_invBkg == null)
            {
                _invBkg = gui.m_player.Find("Bkg")?.GetComponent<RectTransform>();
                _invBkgImage = _invBkg?.GetComponent<Image>();
                _invDarken = gui.m_player.Find("Darken")?.GetComponent<RectTransform>();
                _invFrame = gui.m_player.GetComponent<UIGroupHandler>()?.m_enableWhenActiveAndGamepad?.transform.GetChild(0) as RectTransform;
            }
            if (_invBkg == null) return;
            ExtendForExtraRows(gui);
            if (!_equipBkg) _equipBkg = CreateBackground("HoardEquipmentBkg");
            if (!_quickBkg) _quickBkg = CreateBackground("HoardQuickBkg");
            Sync(_equipBkg, HoardConfig.EquipmentSlots.Value, Base + EquipBkgCenter, EquipBkgSize);
            int q = ActiveQuick;
            Sync(_quickBkg, q > 0, Base + new Vector2(RowBkgLeft + (RowBkgPitch * q + 10f) / 2f, QuickRowTop - (Cell - 6f) / 2f), new Vector2(RowBkgPitch * q + 10f, RowBkgHeight));
        }

        private static void ExtendForExtraRows(InventoryGui gui)
        {
            int extra = Slots.ExtraRows;
            float anchorY = -1f * (extra / (float)Slots.BaseRows - 0.01f * Math.Max(extra - 1, 0));
            _invBkg.anchorMin = new Vector2(0f, anchorY);
            if (_invDarken) _invDarken.anchorMin = _invBkg.anchorMin;
            if (_invFrame) _invFrame.anchorMin = _invBkg.anchorMin;
            var container = gui.m_container;
            if (container)
            {
                _containerPivot ??= container.pivot;
                container.pivot = new Vector2(_containerPivot.Value.x, _containerPivot.Value.y + extra * 0.2f);
            }
        }

        private static RectTransform CreateBackground(string name)
        {
            var player = InventoryGui.instance.m_player;
            var frames = player.GetComponent<UIGroupHandler>()?.m_enableWhenActiveAndGamepad?.transform;
            var darken = player.Find("Darken");
            var bkg = UnityEngine.Object.Instantiate(_invBkg, player, false);
            bkg.name = name;
            int anchorIndex = frames != null ? frames.GetSiblingIndex() : darken != null ? darken.GetSiblingIndex() : _invBkg.GetSiblingIndex();
            bkg.SetSiblingIndex(anchorIndex + 1);
            bkg.anchorMin = bkg.anchorMax = new Vector2(0f, 1f);
            bkg.pivot = new Vector2(0.5f, 0.5f);
            bkg.localScale = Vector3.one;
            var img = bkg.GetComponent<Image>();
            if (img) img.raycastTarget = true;
            bkg.gameObject.AddComponent<DragHandle>();
            return bkg;
        }

        private class DragHandle : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
        {
            private bool _dragging;
            private float _scale = 1f;

            public void OnBeginDrag(PointerEventData e)
            {
                if (!CanDrag || e.button != PointerEventData.InputButton.Left) return;
                _dragging = true;
                _drag = Base;
                _scale = GetComponentInParent<Canvas>()?.scaleFactor ?? 1f;
                if (_scale <= 0f) _scale = 1f;
            }

            public void OnDrag(PointerEventData e)
            {
                if (_dragging && _drag != null) _drag = _drag.Value + e.delta / _scale;
            }

            public void OnEndDrag(PointerEventData e)
            {
                if (!_dragging) return;
                _dragging = false;
                if (_drag != null) HoardConfig.PanelPosition.Value = _drag.Value;
                _drag = null;
            }

            private void OnDisable() { if (_dragging) OnEndDrag(null); }
        }

        private static void Sync(RectTransform bkg, bool visible, Vector2 center, Vector2 size)
        {
            if (!bkg) return;
            bkg.gameObject.SetActive(visible);
            if (!visible) return;
            bkg.sizeDelta = size;
            bkg.anchoredPosition = center;
            var img = bkg.GetComponent<Image>();
            if (img && _invBkgImage)
            {
                img.sprite = _invBkgImage.sprite;
                img.overrideSprite = _invBkgImage.overrideSprite;
                img.color = _invBkgImage.color;
            }
        }

        // Cells get their own root (a sibling of the grid root, mirroring its resting rect)
        // so nothing that masks or scrolls the grid affects them.
        private static RectTransform EnsureSlotRoot(InventoryGrid grid)
        {
            var gridRoot = grid.m_gridRoot;
            if (!gridRoot || !gridRoot.parent || !InventoryGui.instance.m_player) return null;
            if (!_slotRoot)
            {
                _slotRoot = new GameObject("HoardSlotRoot", typeof(RectTransform)).GetComponent<RectTransform>();
                _slotRoot.SetParent(InventoryGui.instance.m_player, false);
                _slotRoot.localScale = Vector3.one;
                _slotRoot.localRotation = Quaternion.identity;
                _slotRoot.SetAsLastSibling();
            }
            _slotRoot.anchorMin = _slotRoot.anchorMax = Vector2.zero;
            _slotRoot.pivot = gridRoot.pivot;
            _slotRoot.sizeDelta = gridRoot.rect.size;
            Vector3 resting = gridRoot.localPosition - (Vector3)gridRoot.anchoredPosition;
            if (ToPanelSpace(gridRoot.parent, _slotRoot.parent, ref resting)) _slotRoot.localPosition = resting;
            else _slotRoot.position = gridRoot.parent.TransformPoint(resting);
            return _slotRoot;
        }

        private static bool ToPanelSpace(Transform from, Transform panel, ref Vector3 point)
        {
            var p = point;
            for (var t = from; t != null; t = t.parent)
            {
                if (t == panel) { point = p; return true; }
                p = t.localPosition + t.localRotation * Vector3.Scale(t.localScale, p);
            }
            return false;
        }

        // Inactive cells can't be destroyed (the grid indexes elements by position every
        // frame) so they are parked under an inactive holder far off-screen.
        private static RectTransform EnsureHiddenRoot()
        {
            if (_hiddenRoot || !InventoryGui.instance.m_player) return _hiddenRoot;
            _hiddenRoot = new GameObject("HoardHiddenRoot", typeof(RectTransform)).GetComponent<RectTransform>();
            _hiddenRoot.SetParent(InventoryGui.instance.m_player, false);
            _hiddenRoot.localScale = Vector3.one;
            _hiddenRoot.localRotation = Quaternion.identity;
            _hiddenRoot.anchorMin = _hiddenRoot.anchorMax = Vector2.zero;
            _hiddenRoot.anchoredPosition = new Vector2(-100000f, 0f);
            _hiddenRoot.gameObject.SetActive(false);
            return _hiddenRoot;
        }

        private static void Park(GameObject go, RectTransform holder)
        {
            if (!go) return;
            go.SetActive(false);
            if (holder && go.transform.parent != holder) go.transform.SetParent(holder, false);
        }

        // From InventoryGrid.UpdateGui on the player grid.
        private static void UpdateCells()
        {
            var grid = InventoryGui.instance.m_playerGrid;
            grid.m_gridRoot.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Slots.VisibleRows * grid.m_elementSpace);
            int start = Slots.VisibleCells;
            var drag = InventoryGui.instance.m_dragItem;
            var root = EnsureSlotRoot(grid);
            var hidden = EnsureHiddenRoot();
            Transform activeParent = root ? root : grid.m_gridRoot;
            for (int i = 0; i < Math.Min(Slots.All.Length, grid.m_elements.Count - start); i++)
            {
                var el = grid.m_elements[start + i];
                var slot = Slots.All[i];
                var go = el?.gameObject;
                if (!go) continue;
                if (!slot.IsActive) { Park(go, hidden); continue; }
                go.SetActive(true);
                if (activeParent && go.transform.parent != activeParent) go.transform.SetParent(activeParent, false);
                go.GetComponent<RectTransform>().anchoredPosition = PositionOf(slot);
                Label(go.transform.Find("binding"), slot);
                Tint(go.GetComponent<Button>(), drag != null && !(slot.IsEquipment ? Slots.WouldFitEquipment(slot, drag) : slot.Fits(drag)));
            }
            for (int i = start + Slots.All.Length; i < grid.m_elements.Count; i++) Park(grid.m_elements[i]?.gameObject, hidden);
        }

        private static void Label(Transform binding, Slots.Slot slot)
        {
            if (!binding) return;
            var text = binding.GetComponent<TMP_Text>();
            if (!text) return;
            if (_labelSize <= 0f && !text.enableAutoSizing) _labelSize = text.fontSize;
            binding.gameObject.SetActive(true);
            text.enabled = true;
            text.overflowMode = TextOverflowModes.Overflow;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.text = slot.Name;
            if (slot.IsEquipment) text.rectTransform.anchoredPosition = EquipLabelPos;
        }

        private static void Tint(Button b, bool unfit)
        {
            if (!b) return;
            if (_normal == Color.clear) { _normal = b.colors.normalColor; _highlight = b.colors.highlightedColor; }
            var c = b.colors;
            c.normalColor = unfit ? new Color(0.8f, 0.2f, 0.2f, 0.5f) : _normal;
            c.highlightedColor = unfit ? new Color(0.9f, 0.3f, 0.3f, 0.7f) : _highlight;
            b.colors = c;
        }

        private static void Clear()
        {
            _invBkg = _invDarken = _invFrame = _equipBkg = _quickBkg = _slotRoot = _hiddenRoot = null;
            _invBkgImage = null;
            _containerPivot = null;
            _drag = null;
            _normal = _highlight = Color.clear;
        }

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.OnDestroy))]
        private static class InventoryGui_OnDestroy { private static void Postfix() => Clear(); }

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Update))]
        private static class InventoryGui_Update
        {
            private static void Postfix()
            {
                if (Player.m_localPlayer && InventoryGui.IsVisible()) UpdateBackgrounds();
            }
        }

        [HarmonyPatch(typeof(InventoryGrid), nameof(InventoryGrid.UpdateGui))]
        private static class InventoryGrid_UpdateGui
        {
            private static void Postfix(InventoryGrid __instance)
            {
                if (InventoryGui.instance && __instance == InventoryGui.instance.m_playerGrid && Player.m_localPlayer) UpdateCells();
            }
        }

        // Gamepad selection must not land on hidden cells.
        [HarmonyPatch(typeof(InventoryGrid), nameof(InventoryGrid.UpdateGamepad))]
        private static class InventoryGrid_UpdateGamepad
        {
            private static void Postfix(InventoryGrid __instance)
            {
                if (!InventoryGui.instance || __instance != InventoryGui.instance.m_playerGrid) return;
                var sel = __instance.m_selected;
                if (sel.y < Slots.VisibleRows) return;
                int idx = (sel.y - Slots.VisibleRows) * Slots.Width + sel.x;
                if (idx >= 0 && idx < Slots.All.Length && Slots.All[idx].IsActive) return;
                int best = -1, bestDist = int.MaxValue;
                for (int i = 0; i < Slots.All.Length; i++)
                {
                    if (!Slots.All[i].IsActive) continue;
                    int d = Math.Abs(i - idx);
                    if (d < bestDist) { bestDist = d; best = i; }
                }
                __instance.m_selected = best >= 0 ? Slots.All[best].Position : new Vector2i(Math.Min(sel.x, Slots.Width - 1), Slots.VisibleRows - 1);
            }
        }
    }
}
