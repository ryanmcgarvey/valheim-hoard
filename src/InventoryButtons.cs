using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Hoard
{
    // Buttons on the inventory panel (sort / quick stack / restock / trash) and the
    // container panel (store all / quick stack / restock / sort). All are clones of the
    // game's own Take All button so they pick up the UI skin.
    public static class InventoryButtons
    {
        private static Button _sortInv, _stackInv, _restockInv, _trashInv;
        private static Button _storeAll, _stackCont, _restockCont, _sortCont;
        private static Vector3 _takeAllOrigPos;
        private static Vector2 _takeAllOrigSize;
        private static bool _takeAllMeasured;

        private static Button Clone(InventoryGui gui, string name, Transform parent, string label, UnityEngine.Events.UnityAction onClick)
        {
            var pad = gui.m_takeAllButton.GetComponent<UIGamePad>();
            bool padEnabled = pad && pad.enabled;
            if (pad) pad.enabled = false;
            var b = Object.Instantiate(gui.m_takeAllButton, parent);
            if (pad) pad.enabled = padEnabled;
            b.name = name;
            b.onClick.RemoveAllListeners();
            b.onClick.AddListener(onClick);
            var clonePad = b.GetComponent<UIGamePad>();
            if (clonePad)
            {
                if (clonePad.m_hint) clonePad.m_hint.SetActive(false);
                clonePad.enabled = false;
            }
            var text = b.GetComponentInChildren<TMP_Text>();
            if (text)
            {
                text.text = label;
                text.enableAutoSizing = true;
                text.fontSizeMin = 10f;
                text.fontSizeMax = text.fontSize;
            }
            var tip = b.GetComponent<UITooltip>();
            if (tip) tip.m_text = "";
            return b;
        }

        private static void Size(Button b, float w, float h)
        {
            var rt = (RectTransform)b.transform;
            rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, w);
            rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, h);
        }

        private static void Build(InventoryGui gui)
        {
            if (!gui.m_takeAllButton) return;
            var takeAll = (RectTransform)gui.m_takeAllButton.transform;
            if (!_takeAllMeasured)
            {
                _takeAllOrigPos = takeAll.localPosition;
                _takeAllOrigSize = takeAll.sizeDelta;
                _takeAllMeasured = true;
            }
            var p = () => Player.m_localPlayer;

            // Container panel: in 1.0 the Take All button sits at the top-left of the panel,
            // above the chest grid. Store all goes beside it; Stack / Restock / Sort form a
            // second row directly below, still above the grid.
            float w = _takeAllOrigSize.x, h = _takeAllOrigSize.y, gap = 4f;
            var parent = takeAll.parent;
            float px = takeAll.pivot.x;
            float leftEdge = _takeAllOrigPos.x - w * px;
            _storeAll = Clone(gui, "HoardStoreAll", parent, "Store all", () => StoreTakeAll.StoreAll(p()));
            Size(_storeAll, w, h);
            _storeAll.transform.localPosition = _takeAllOrigPos + new Vector3(w + gap, 0f);
            float rowWidth = 2f * w + gap;
            float tw = (rowWidth - 2f * gap) / 3f;
            _stackCont = Clone(gui, "HoardStackContainer", parent, "Stack", () => QuickStack.Run(p(), onlyOpenContainer: true));
            _restockCont = Clone(gui, "HoardRestockContainer", parent, "Restock", () => Restock.Run(p(), onlyOpenContainer: true));
            _sortCont = Clone(gui, "HoardSortContainer", parent, "Sort", () => { var c = InventoryGui.instance?.m_currentContainer; if (c) Sorting.SortContainer(c); });
            Button[] row = { _stackCont, _restockCont, _sortCont };
            for (int i = 0; i < row.Length; i++)
            {
                Size(row[i], tw, h);
                row[i].transform.localPosition = new Vector3(leftEdge + tw * px + i * (tw + gap), _takeAllOrigPos.y - (h + gap), _takeAllOrigPos.z);
            }

            // Inventory panel: a column of small buttons in the right-hand strip, between the
            // armor and weight readouts. That strip belongs to the inventory panel alone, so
            // the buttons never collide with an open chest.
            var armor = gui.m_player.Find("Armor");
            var weight = gui.m_player.Find("Weight");
            _sortInv = Clone(gui, "HoardSortInventory", gui.m_player, "Sort", () => Sorting.SortPlayer(p()));
            _stackInv = Clone(gui, "HoardStackInventory", gui.m_player, "Stack", () => QuickStack.Run(p()));
            _restockInv = Clone(gui, "HoardRestockInventory", gui.m_player, "Restock", () => Restock.Run(p()));
            _trashInv = Clone(gui, "HoardTrash", gui.m_player, "Trash", () => Trash.OnTrashPressed());
            Button[] mini = { _sortInv, _stackInv, _restockInv, _trashInv };
            Vector3 top = armor ? armor.localPosition : (weight ? weight.localPosition + new Vector3(0f, 300f, 0f) : Vector3.zero);
            float colX = weight ? weight.localPosition.x : top.x;
            for (int i = 0; i < mini.Length; i++)
            {
                var rt = (RectTransform)mini[i].transform;
                rt.localScale = Vector3.one;
                Size(mini[i], 84f, 32f);
                rt.localPosition = new Vector3(colX, top.y - 60f - i * 38f, 0f);
                var t = mini[i].GetComponentInChildren<TMP_Text>();
                if (t) t.fontSizeMin = 8f;
            }
            var trashText = _trashInv.GetComponentInChildren<TMP_Text>();
            if (trashText) trashText.color = new Color(1f, 0.6f, 0.3f);
        }

        private static void Destroy()
        {
            foreach (var b in new[] { _sortInv, _stackInv, _restockInv, _trashInv, _storeAll, _stackCont, _restockCont, _sortCont })
                if (b) Object.Destroy(b.gameObject);
            _sortInv = _stackInv = _restockInv = _trashInv = _storeAll = _stackCont = _restockCont = _sortCont = null;
            if (_takeAllMeasured && InventoryGui.instance && InventoryGui.instance.m_takeAllButton)
            {
                var rt = (RectTransform)InventoryGui.instance.m_takeAllButton.transform;
                rt.localPosition = _takeAllOrigPos;
                Size(InventoryGui.instance.m_takeAllButton, _takeAllOrigSize.x, _takeAllOrigSize.y);
            }
        }

        private static bool _builtWithButtons;

        // Runs every frame the inventory is visible: creates/destroys on config change and
        // keeps visibility in step with the feature toggles and the open container.
        public static void Refresh(InventoryGui gui)
        {
            bool want = HoardConfig.ShowButtons.Value;
            if (want && _storeAll == null) { Build(gui); _builtWithButtons = true; }
            else if (!want && _builtWithButtons) { Destroy(); _builtWithButtons = false; }
            if (!want) return;

            bool container = gui.m_currentContainer;
            bool area = !container;
            Set(_sortInv, HoardConfig.SortEnabled.Value);
            Set(_stackInv, HoardConfig.QuickStackEnabled.Value && (area || !HoardConfig.QuickStackOnlyToOpenContainer.Value));
            Set(_restockInv, HoardConfig.RestockEnabled.Value && (area || !HoardConfig.RestockOnlyFromOpenContainer.Value));
            Set(_trashInv, HoardConfig.TrashEnabled.Value);
            Set(_storeAll, container && HoardConfig.StoreAllButton.Value);
            Set(_stackCont, container && HoardConfig.QuickStackEnabled.Value);
            Set(_restockCont, container && HoardConfig.RestockEnabled.Value);
            Set(_sortCont, container && HoardConfig.SortEnabled.Value);
            if (gui.m_stackAllButton) gui.m_stackAllButton.gameObject.SetActive(!(HoardConfig.HideVanillaStackAllButton.Value && HoardConfig.QuickStackEnabled.Value) && container);
        }

        private static void Set(Button b, bool on) { if (b && b.gameObject.activeSelf != on) b.gameObject.SetActive(on); }

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Update))]
        private static class InventoryGui_Update
        {
            private static void Postfix(InventoryGui __instance)
            {
                if (!Player.m_localPlayer || !InventoryGui.IsVisible()) return;
                Refresh(__instance);
            }
        }

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.OnDestroy))]
        private static class InventoryGui_OnDestroy
        {
            private static void Postfix()
            {
                _sortInv = _stackInv = _restockInv = _trashInv = _storeAll = _stackCont = _restockCont = _sortCont = null;
                _takeAllMeasured = false;
                _builtWithButtons = false;
            }
        }
    }
}
