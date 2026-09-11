using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace Hoard
{
    // On-screen quick slot bar: a clone of the vanilla hotbar whose items are the quick
    // slot cells. Positioned by config (anchor + offset), applied live.
    public static class QuickBar
    {
        private static HotkeyBar _bar;
        private static RectTransform _rect;
        private static TextAnchor _appliedAnchor;
        private static Vector2 _appliedPos;
        private static bool _placed;

        private static bool Enabled => HoardConfig.QuickSlots.Value && HoardConfig.QuickSlotCount.Value > 0;

        [HarmonyPatch(typeof(Hud), nameof(Hud.Awake))]
        private static class Hud_Awake
        {
            private static void Postfix(Hud __instance)
            {
                var vanilla = __instance.m_rootObject.transform.Find("HotKeyBar");
                if (vanilla == null)
                {
                    var found = __instance.GetComponentInChildren<HotkeyBar>(true);
                    vanilla = found ? found.transform : null;
                }
                if (vanilla == null) { Log.Warn("Hotkey bar not found; quick slot bar disabled"); return; }
                _rect = Object.Instantiate(vanilla.GetComponent<RectTransform>(), __instance.m_rootObject.transform, true);
                _rect.name = "HoardQuickBar";
                _rect.localPosition = Vector3.zero;
                _rect.SetSiblingIndex(vanilla.GetSiblingIndex() + 1);
                for (int i = _rect.childCount - 1; i >= 0; i--) Object.Destroy(_rect.GetChild(i).gameObject);
                _bar = _rect.GetComponent<HotkeyBar>();
                _placed = false;
                Place(force: true);
            }
        }

        [HarmonyPatch(typeof(Hud), nameof(Hud.OnDestroy))]
        private static class Hud_OnDestroy { private static void Postfix() { _bar = null; _rect = null; } }

        private static void Place(bool force = false)
        {
            if (!_rect) return;
            var anchor = HoardConfig.QuickBarAnchor.Value;
            var pos = HoardConfig.QuickBarPosition.Value;
            if (!force && _placed && anchor == _appliedAnchor && pos == _appliedPos) return;
            _appliedAnchor = anchor; _appliedPos = pos; _placed = true;
            Vector2 a = AnchorVector(anchor);
            _rect.anchorMin = _rect.anchorMax = a;
            _rect.pivot = a;
            _rect.anchoredPosition = new Vector2(a.x > 0.5f ? -pos.x : pos.x, a.y > 0.5f ? -pos.y : pos.y);
        }

        private static Vector2 AnchorVector(TextAnchor anchor)
        {
            switch (anchor)
            {
                case TextAnchor.UpperLeft: return new Vector2(0, 1);
                case TextAnchor.UpperCenter: return new Vector2(0.5f, 1);
                case TextAnchor.UpperRight: return new Vector2(1, 1);
                case TextAnchor.MiddleLeft: return new Vector2(0, 0.5f);
                case TextAnchor.MiddleCenter: return new Vector2(0.5f, 0.5f);
                case TextAnchor.MiddleRight: return new Vector2(1, 0.5f);
                case TextAnchor.LowerLeft: return new Vector2(0, 0);
                case TextAnchor.LowerCenter: return new Vector2(0.5f, 0);
                default: return new Vector2(1, 0);
            }
        }

        // The clone must never run the vanilla Update (it would render the hotbar a second
        // time); we drive it ourselves.
        [HarmonyPatch(typeof(HotkeyBar), nameof(HotkeyBar.Update))]
        private static class HotkeyBar_Update
        {
            [HarmonyPriority(Priority.First)]
            private static bool Prefix(HotkeyBar __instance)
            {
                if (__instance != _bar || _bar == null) return true;
                Place();
                var player = Player.m_localPlayer;
                if (!Enabled || player == null)
                {
                    if (__instance.m_elements.Count > 0)
                    {
                        foreach (var e in __instance.m_elements) Object.Destroy(e.m_go);
                        __instance.m_elements.Clear();
                    }
                    return false;
                }
                __instance.m_selected = -1;
                __instance.UpdateIcons(player);
                return false;
            }
        }

        // While the quick bar refreshes, the "bound items" are the quick slot items, whose
        // grid x is the slot index so the vanilla element math needs no adjustment.
        [HarmonyPatch(typeof(HotkeyBar), nameof(HotkeyBar.UpdateIcons))]
        private static class HotkeyBar_UpdateIcons
        {
            internal static bool InCall;

            private static void Prefix(HotkeyBar __instance) => InCall = __instance == _bar && _bar != null;

            private static void Postfix(HotkeyBar __instance)
            {
                if (!InCall) return;
                InCall = false;
                for (int i = 0; i < __instance.m_elements.Count; i++)
                {
                    var binding = __instance.m_elements[i].m_go.transform.Find("binding");
                    if (binding && binding.GetComponent<TMP_Text>() is TMP_Text t)
                        t.text = i < Slots.MaxQuickSlots ? Slots.QuickLabel(i) : "";
                }
            }

            private static void Finalizer() => InCall = false;
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.GetBoundItems))]
        private static class Inventory_GetBoundItems
        {
            private static bool Prefix(Inventory __instance, List<ItemDrop.ItemData> bound)
            {
                if (!HotkeyBar_UpdateIcons.InCall || __instance != Slots.PlayerInventory) return true;
                bound.Clear();
                foreach (var s in Slots.All)
                    if (s.IsQuick && s.IsActive && s.Item is ItemDrop.ItemData item) bound.Add(item);
                return false;
            }
        }
    }
}
