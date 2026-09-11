using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace Hoard
{
    // A quick slot hotkey must not also trigger the vanilla action bound to the same key
    // (Z = sit, V = toggle walk, ...). The game's button table is reverse-mapped from key
    // paths to button names and the low-level state getters return false for those buttons
    // on the frames a quick slot hotkey (with an item in the slot) fires.
    public static class PreventSimilarHotkeys
    {
        private static readonly HashSet<string> _buttonNames = new HashSet<string>();
        private static readonly HashSet<KeyCode> _keys = new HashSet<KeyCode>();
        private static bool _anyDown, _anyHeld;
        private static int _downToken = -1, _heldToken = -1;
        private static int _skip;

        private static bool IsDedicated => SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null;

        public static bool IsShortcutDown(KeyboardShortcut k) => Active(k, held: false);
        public static bool IsShortcutHeld(KeyboardShortcut k) => Active(k, held: true);

        private static bool Active(KeyboardShortcut k, bool held)
        {
            if (k.MainKey == KeyCode.None) return false;
            _skip++;
            try
            {
                bool main = held ? ZInput.GetKey(k.MainKey, false) : ZInput.GetKeyDown(k.MainKey, false);
                if (!main) return false;
                foreach (var m in k.Modifiers) if (!ZInput.GetKey(m, false)) return false;
                return true;
            }
            finally { _skip--; }
        }

        private static int Token() => (Time.frameCount << 1) | (Time.inFixedTimeStep ? 1 : 0);

        private static void ResetState()
        {
            _downToken = _heldToken = -1;
            _anyDown = _anyHeld = false;
            ButtonState.CheckHeld = ButtonState.Skip = false;
            KeyState.CheckHeld = KeyState.Skip = false;
        }

        public static void Refresh() => Fill(ZInput.instance);

        private static void Fill(ZInput zinput)
        {
            // ZInput can initialize before this plugin's config is bound; never let a
            // finalizer on the game's input setup throw.
            if (IsDedicated || HoardConfig.QuickSlotKeys[0] == null) return;
            try { FillInner(zinput); }
            catch (System.Exception e) { Log.Warn($"Hotkey conflict table not built: {e.Message}"); }
        }

        private static void FillInner(ZInput zinput)
        {
            Sanitize();
            _buttonNames.Clear();
            _keys.Clear();
            ResetState();
            if (zinput?.m_buttons == null) return;
            var pathToNames = new Dictionary<string, HashSet<string>>();
            foreach (var kv in zinput.m_buttons)
            {
                // A def can legitimately have no bindings mid-rebind; GetActionPath would throw.
                if (kv.Value?.ButtonAction == null || kv.Value.ButtonAction.bindings.Count == 0) continue;
                Add(kv.Value.GetActionPath(true), kv.Key);
                Add(kv.Value.GetActionPath(false), kv.Key);
            }
            foreach (var s in Slots.All)
            {
                if (!s.IsQuick) continue;
                var key = s.Shortcut.MainKey;
                if (key == KeyCode.None) continue;
                _keys.Add(key);
                if (pathToNames.TryGetValue(ZInput.KeyCodeToPath(key, false), out var names)) _buttonNames.UnionWith(names);
            }

            void Add(string path, string name)
            {
                if (string.IsNullOrEmpty(path)) return;
                if (!pathToNames.TryGetValue(path, out var set)) pathToNames[path] = set = new HashSet<string>();
                set.Add(name);
            }
        }

        private static void Sanitize()
        {
            foreach (var entry in HoardConfig.QuickSlotKeys)
            {
                if (entry == null) continue;
                var key = entry.Value.MainKey;
                if (key != KeyCode.None && !ZInput.IsKeyCodeValid(key))
                {
                    Log.Warn($"Invalid key on {entry.Definition}: {entry.Value}; cleared");
                    entry.Value = KeyboardShortcut.Empty;
                }
                // "LeftAlt + Z" stored with the modifier as main key never fires; swap it around.
                if (Keys.IsModifier(entry.Value.MainKey))
                {
                    var main = entry.Value.Modifiers.FirstOrDefault(m => !Keys.IsModifier(m));
                    if (main != KeyCode.None)
                        entry.Value = new KeyboardShortcut(main, entry.Value.Modifiers.Where(m => m != main).Append(entry.Value.MainKey).ToArray());
                }
            }
        }

        private static bool AnyQuickHotkey(bool held)
        {
            int token = Token();
            if (held)
            {
                if (_heldToken == token) return _anyHeld;
                _heldToken = token;
                return _anyHeld = Slots.All.Any(s => s.IsQuick && s.IsActive && s.Item != null && IsShortcutHeld(s.Shortcut));
            }
            if (_downToken == token) return _anyDown;
            _downToken = token;
            return _anyDown = Slots.All.Any(s => s.IsQuick && s.IsActive && s.Item != null && IsShortcutDown(s.Shortcut));
        }

        [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetButtonUp))] private static class B_Up { private static void Prefix() => ButtonState.Skip = true; }
        [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetButton))] private static class B_Held { private static void Prefix() => ButtonState.CheckHeld = true; }
        [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetMouseButton))] private static class M_Held { private static void Prefix() => ButtonState.CheckHeld = true; }
        [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetMouseButtonUp))] private static class M_Up { private static void Prefix() => ButtonState.Skip = true; }

        [HarmonyPatch(typeof(ZInput), nameof(ZInput.TryGetButtonState))]
        private static class ButtonState
        {
            internal static bool CheckHeld, Skip;
            private static void Postfix(string name, ref bool __result)
            {
                bool held = CheckHeld, skip = Skip;
                CheckHeld = Skip = false;
                if (!skip && __result && _buttonNames.Contains(name)) __result = !AnyQuickHotkey(held);
            }
        }

        [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetKey))] private static class K_Held { private static void Prefix() => KeyState.CheckHeld = true; }
        [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetKeyUp))] private static class K_Up { private static void Prefix() => KeyState.Skip = true; }

        [HarmonyPatch(typeof(ZInput), nameof(ZInput.TryGetKeyStateLowLevel))]
        private static class KeyState
        {
            internal static bool CheckHeld, Skip;
            private static void Postfix(KeyCode keyCode, ref bool __result)
            {
                bool held = CheckHeld, skip = Skip;
                CheckHeld = Skip = false;
                if (_skip == 0 && !skip && __result && _keys.Contains(keyCode)) __result = !AnyQuickHotkey(held);
            }
        }

        [HarmonyPatch]
        private static class ZInput_Update
        {
            private static IEnumerable<MethodBase> TargetMethods()
            {
                yield return AccessTools.Method(typeof(ZInput), nameof(ZInput.InternalUpdate));
                yield return AccessTools.Method(typeof(ZInput), nameof(ZInput.InternalUpdateFixed));
            }
            private static void Finalizer() => ResetState();
        }

        [HarmonyPatch]
        private static class ZInput_Bindings
        {
            private static IEnumerable<MethodBase> TargetMethods()
            {
                yield return AccessTools.Method(typeof(ZInput), nameof(ZInput.ResetKBMButtons));
                yield return AccessTools.Method(typeof(ZInput), nameof(ZInput.AddGenericGamepadButtons));
                yield return AccessTools.Method(typeof(ZInput), nameof(ZInput.AddGamepadClassicButtons));
                yield return AccessTools.Method(typeof(ZInput), nameof(ZInput.AddGamepadAlt1Buttons));
                yield return AccessTools.Method(typeof(ZInput), nameof(ZInput.AddGamepadAlt2Buttons));
                yield return AccessTools.Method(typeof(ZInput), nameof(ZInput.OnRebindComplete));
                yield return AccessTools.Method(typeof(ZInput), nameof(ZInput.ResetToDefault));
                yield return AccessTools.Method(typeof(ZInput), nameof(ZInput.Load));
            }
            private static void Finalizer(ZInput __instance) => Fill(__instance);
        }
    }
}
