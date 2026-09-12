using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Hoard
{
    // A third tab on the crafting panel, next to Craft and Upgrade, shown only while you
    // stand at a crafting station. It covers the recipe area with a drop zone: drag an
    // item from your inventory onto it to recycle it.
    public static class RecycleTab
    {
        private static Button _tab;
        private static GameObject _zone;
        private static TMP_Text _zoneText;
        public static bool Active { get; private set; }

        private static bool AtStation => Player.m_localPlayer && Player.m_localPlayer.GetCurrentCraftingStation();

        private static Transform CommonAncestor(Transform a, Transform b)
        {
            for (var t = a; t != null; t = t.parent)
                if (b.IsChildOf(t)) return t;
            return null;
        }

        private static void Ensure(InventoryGui gui)
        {
            if (_tab || !gui.m_tabUpgrade || !gui.m_tabCraft) return;
            var upgrade = (RectTransform)gui.m_tabUpgrade.transform;
            _tab = Object.Instantiate(gui.m_tabUpgrade, upgrade.parent);
            _tab.name = "HoardTabRecycle";
            _tab.onClick.RemoveAllListeners();
            _tab.onClick.AddListener(() => Open(gui));
            var rt = (RectTransform)_tab.transform;
            rt.localPosition = upgrade.localPosition + new Vector3(upgrade.rect.width + 6f, 0f, 0f);
            var label = _tab.GetComponentInChildren<TMP_Text>();
            if (label) label.text = "Recycle";
            var pad = _tab.GetComponent<UIGamePad>();
            if (pad) { if (pad.m_hint) pad.m_hint.SetActive(false); pad.enabled = false; }

            // Drop zone over the recipe list + details area.
            var area = CommonAncestor(gui.m_recipeListRoot, gui.m_craftButton.transform) as RectTransform ?? (RectTransform)gui.m_recipeListRoot.parent;
            _zone = new GameObject("HoardRecycleZone", typeof(RectTransform));
            var zrt = _zone.GetComponent<RectTransform>();
            zrt.SetParent(area, false);
            zrt.anchorMin = Vector2.zero; zrt.anchorMax = Vector2.one;
            zrt.offsetMin = new Vector2(6f, 6f); zrt.offsetMax = new Vector2(-6f, -6f);
            zrt.SetAsLastSibling();
            var img = _zone.AddComponent<Image>();
            img.color = new Color(0.1f, 0.08f, 0.06f, 0.97f);
            var btn = _zone.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(() =>
            {
                if (!InventoryGui.instance || !InventoryGui.instance.m_dragGo) { Msg.Center("Drag an item here to recycle it"); return; }
                Recycle.OnRecyclePressed();
            });
            var textGo = Object.Instantiate(gui.m_recipeName.gameObject, zrt);
            textGo.name = "Text";
            _zoneText = textGo.GetComponent<TMP_Text>();
            var trt = (RectTransform)textGo.transform;
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(20f, 20f); trt.offsetMax = new Vector2(-20f, -20f);
            trt.pivot = new Vector2(0.5f, 0.5f);
            _zoneText.alignment = TextAlignmentOptions.Center;
            _zoneText.enableAutoSizing = true;
            _zoneText.fontSizeMin = 12f;
            _zoneText.fontSizeMax = 22f;
            _zoneText.textWrappingMode = TextWrappingModes.Normal;
            _zoneText.raycastTarget = false;
            _zone.SetActive(false);
        }

        private static void Open(InventoryGui gui)
        {
            if (!AtStation) return;
            gui.SetActiveGroup(gui.m_uiGroups[3]);
            gui.m_tabCraft.interactable = true;
            gui.m_tabUpgrade.interactable = true;
            _tab.interactable = false;
            Active = true;
            Refresh(gui);
        }

        internal static void Close(InventoryGui gui)
        {
            if (!Active) return;
            Active = false;
            if (_tab) _tab.interactable = true;
            if (_zone) _zone.SetActive(false);
            if (gui && gui.m_craftButton) gui.m_craftButton.gameObject.SetActive(true);
        }

        private static void Refresh(InventoryGui gui)
        {
            if (!_zone) return;
            _zone.SetActive(Active);
            if (!Active) return;
            gui.m_craftButton.gameObject.SetActive(false);
            var station = Player.m_localPlayer?.GetCurrentCraftingStation();
            string where = station ? Localization.instance.Localize(station.m_name) : "a crafting station";
            _zoneText.text = $"<size=26><b>Recycle</b></size>\n\nDrag an item from your inventory and drop it here.\nYou get back what it cost to craft and upgrade, at {HoardConfig.RecycleRate.Value:0}%.\n\n<color=#d9a441>Metals, ores and anything else that can't go through a portal are never returned.</color>\n\n<size=14>At: {where}</size>";
        }

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.UpdateCraftingPanel))]
        private static class InventoryGui_UpdateCraftingPanel
        {
            private static void Postfix(InventoryGui __instance)
            {
                if (!HoardConfig.RecycleEnabled.Value) { if (_tab) _tab.gameObject.SetActive(false); Close(__instance); return; }
                Ensure(__instance);
                if (!_tab) return;
                bool show = AtStation && __instance.m_tabUpgrade.gameObject.activeSelf;
                _tab.gameObject.SetActive(show);
                if (!show) Close(__instance);
                Refresh(__instance);
            }
        }

        // Vanilla re-enables the craft button every frame; keep it hidden while our tab is up.
        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.UpdateRecipe))]
        private static class InventoryGui_UpdateRecipe
        {
            private static void Postfix(InventoryGui __instance)
            {
                if (!Active) return;
                if (!AtStation) { Close(__instance); __instance.OnTabCraftPressed(); return; }
                __instance.m_craftButton.gameObject.SetActive(false);
                __instance.m_craftProgressPanel.gameObject.SetActive(false);
            }
        }

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.OnTabCraftPressed))]
        private static class InventoryGui_OnTabCraftPressed { private static void Prefix(InventoryGui __instance) => Close(__instance); }

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.OnTabUpgradePressed))]
        private static class InventoryGui_OnTabUpgradePressed { private static void Prefix(InventoryGui __instance) => Close(__instance); }

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Hide))]
        private static class InventoryGui_Hide { private static void Postfix(InventoryGui __instance) => Close(__instance); }

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.OnDestroy))]
        private static class InventoryGui_OnDestroy
        {
            private static void Postfix() { _tab = null; _zone = null; _zoneText = null; Active = false; }
        }
    }
}
