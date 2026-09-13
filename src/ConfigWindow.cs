using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace Hoard
{
    // In-game settings window (IMGUI). Every entry in the config file is editable; changes
    // apply immediately and are saved to the .cfg. Game input is muted while it is open.
    public static class ConfigWindow
    {
        public static bool Visible { get; private set; }

        // Laid out in 1080p-equivalent units and scaled with the game's own UI scale.
        private static Rect _rect = new Rect(60f, 60f, 960f, 900f);
        private static Vector2 _scroll;
        private static string _search = "";
        private static readonly HashSet<string> _collapsed = new HashSet<string>();
        private static ConfigEntryBase _binding;
        private static readonly Dictionary<ConfigEntryBase, string> _textBuffers = new Dictionary<ConfigEntryBase, string>();
        private static GUIStyle _header, _section, _desc, _box, _jump;
        // Contents bar: section -> y of its header inside the scroll view (recorded on repaint),
        // and the section a click asked to scroll to.
        private static readonly Dictionary<string, float> _sectionY = new Dictionary<string, float>();
        private static string _jumpTo;
        private static Texture2D _bg;
        private static int _bypass;

        public static bool Bypass => _bypass > 0;

        public static void Toggle() => SetVisible(!Visible);

        public static void SetVisible(bool on)
        {
            if (Visible == on) return;
            Visible = on;
            _binding = null;
            _textBuffers.Clear();
            ZInput.ResetAllButtonStates();
            if (on && InventoryGui.instance) InventoryGui.instance.SetupDragItem(null, null, 0);
            if (on) { ZCursor.LockState = CursorLockMode.None; ZCursor.Show(); }
        }

        // Called from the plugin's Update: the toggle key is read through the bypass so the
        // input mute doesn't swallow it.
        public static void PollToggle()
        {
            _bypass++;
            try
            {
                if (HoardConfig.ConfigWindowKey.Value.IsDown()) Toggle();
                else if (Visible && ZInput.GetKeyDown(KeyCode.Escape, false)) SetVisible(false);
            }
            finally { _bypass--; }
        }

        private static void EnsureStyles()
        {
            if (_header != null) return;
            _bg = new Texture2D(1, 1);
            _bg.SetPixel(0, 0, new Color(0.08f, 0.07f, 0.06f, 0.96f));
            _bg.Apply();
            _box = new GUIStyle(GUI.skin.window) { normal = { background = _bg }, onNormal = { background = _bg }, padding = new RectOffset(10, 10, 24, 10) };
            _header = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold };
            _section = new GUIStyle(GUI.skin.button) { alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold, fontSize = 14 };
            _desc = new GUIStyle(GUI.skin.label) { wordWrap = true, fontSize = 11, normal = { textColor = new Color(0.75f, 0.75f, 0.7f) } };
            _jump = new GUIStyle(GUI.skin.button) { fontSize = 11, padding = new RectOffset(6, 6, 2, 2), margin = new RectOffset(2, 2, 2, 2) };
        }

        public static void OnGUI()
        {
            if (!Visible) return;
            EnsureStyles();
            // A bind in progress swallows the next key press.
            if (_binding != null && Event.current.type == EventType.KeyDown && Event.current.keyCode != KeyCode.None && !Keys.IsModifier(Event.current.keyCode))
            {
                var e = Event.current;
                if (e.keyCode == KeyCode.Escape) _binding = null;
                else if (e.keyCode == KeyCode.Backspace || e.keyCode == KeyCode.Delete) { _binding.BoxedValue = KeyboardShortcut.Empty; _binding = null; }
                else
                {
                    var mods = new List<KeyCode>();
                    if (e.shift) mods.Add(KeyCode.LeftShift);
                    if (e.control) mods.Add(KeyCode.LeftControl);
                    if (e.alt) mods.Add(KeyCode.LeftAlt);
                    _binding.BoxedValue = new KeyboardShortcut(e.keyCode, mods.ToArray());
                    _binding = null;
                }
                e.Use();
                return;
            }
            // Same factor GuiScaler applies to the game's canvases (screen / 1920x1080, times the
            // user's GUI scale), so the window matches the rest of the UI on any display.
            float scale = Mathf.Min(Screen.width / 1920f, Screen.height / 1080f) * GuiScaler.m_largeGuiScale * HoardConfig.ConfigWindowScale.Value;
            if (scale <= 0.05f) scale = 1f;
            var saved = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float maxW = Screen.width / scale - 20f, maxH = Screen.height / scale - 20f;
            _rect.width = Mathf.Min(_rect.width, maxW);
            _rect.height = Mathf.Min(_rect.height, maxH);
            _rect.x = Mathf.Clamp(_rect.x, 0f, Mathf.Max(0f, maxW - _rect.width + 10f));
            _rect.y = Mathf.Clamp(_rect.y, 0f, Mathf.Max(0f, maxH - _rect.height + 10f));
            _rect = GUILayout.Window(0x484F41, _rect, Draw, $"{Plugin.Name} {Plugin.Version} — settings apply live, {HoardConfig.ConfigWindowKey.Value.Label()} or Esc closes", _box);
            GUI.FocusWindow(0x484F41);
            GUI.matrix = saved;
        }

        private static void Draw(int id)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Search", GUILayout.Width(50));
            _search = GUILayout.TextField(_search ?? "", GUILayout.ExpandWidth(true));
            if (GUILayout.Button("Clear", GUILayout.Width(60))) _search = "";
            if (GUILayout.Button("Expand all", GUILayout.Width(90))) _collapsed.Clear();
            if (GUILayout.Button("Collapse all", GUILayout.Width(90))) foreach (var s in Sections()) _collapsed.Add(s);
            if (GUILayout.Button("Close", GUILayout.Width(60))) SetVisible(false);
            GUILayout.EndHorizontal();

            DrawContents();

            _scroll = GUILayout.BeginScrollView(_scroll);
            string q = (_search ?? "").Trim().ToLowerInvariant();
            foreach (var section in Sections())
            {
                var entries = HoardConfig.File.Where(kv => kv.Key.Section == section)
                    .Select(kv => kv.Value)
                    .Where(e => q.Length == 0 || e.Definition.Key.ToLowerInvariant().Contains(q) || (e.Description?.Description ?? "").ToLowerInvariant().Contains(q))
                    .ToList();
                if (entries.Count == 0) continue;
                bool collapsed = _collapsed.Contains(section) && q.Length == 0;
                if (GUILayout.Button((collapsed ? "▸ " : "▾ ") + section, _section))
                {
                    if (collapsed) _collapsed.Remove(section); else _collapsed.Add(section);
                }
                if (Event.current.type == EventType.Repaint) _sectionY[section] = GUILayoutUtility.GetLastRect().y;
                if (collapsed) continue;
                foreach (var e in entries) DrawEntry(e);
                GUILayout.Space(8);
            }
            GUILayout.EndScrollView();
            // Positions come from this repaint; the scroll takes effect on the next one.
            if (_jumpTo != null && Event.current.type == EventType.Repaint && _sectionY.TryGetValue(_jumpTo, out float y))
            {
                _scroll.y = y;
                _jumpTo = null;
            }
            GUI.DragWindow(new Rect(0, 0, 10000, 22));
        }

        // Sections are named "N - Title"; order by N, so 10 comes after 9 and not after 1.
        private static IEnumerable<string> Sections() =>
            HoardConfig.File.Keys.Select(k => k.Section).Distinct().OrderBy(SectionNumber).ThenBy(s => s, StringComparer.Ordinal);

        private static int SectionNumber(string section)
        {
            int dash = section.IndexOf(" - ", StringComparison.Ordinal);
            return dash > 0 && int.TryParse(section.Substring(0, dash), out int n) ? n : int.MaxValue;
        }

        private static string SectionTitle(string section)
        {
            int dash = section.IndexOf(" - ", StringComparison.Ordinal);
            return dash > 0 ? section.Substring(dash + 3) : section;
        }

        // One small button per section, wrapped into as many rows as the window width needs.
        // Clicking scrolls the list to that section (and expands it, and clears the search so
        // it can't be filtered out).
        private static void DrawContents()
        {
            float avail = _rect.width - 30f, used = 0f;
            GUILayout.BeginHorizontal();
            foreach (var section in Sections())
            {
                var label = new GUIContent(SectionTitle(section));
                float w = _jump.CalcSize(label).x + 4f;
                if (used > 0f && used + w > avail)
                {
                    GUILayout.EndHorizontal();
                    GUILayout.BeginHorizontal();
                    used = 0f;
                }
                if (GUILayout.Button(label, _jump, GUILayout.Width(w - 4f)))
                {
                    _jumpTo = section;
                    _collapsed.Remove(section);
                    _search = "";
                }
                used += w;
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Space(4);
        }

        private static void DrawEntry(ConfigEntryBase e)
        {
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.BeginHorizontal();
            GUILayout.Label(e.Definition.Key, GUILayout.Width(230));
            DrawControl(e);
            bool isDefault = Equals(e.BoxedValue, e.DefaultValue);
            GUI.enabled = !isDefault;
            if (GUILayout.Button("Reset", GUILayout.Width(52))) e.BoxedValue = e.DefaultValue;
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(e.Description?.Description)) GUILayout.Label(e.Description.Description, _desc);
            GUILayout.EndVertical();
        }

        private static void DrawControl(ConfigEntryBase e)
        {
            var t = e.SettingType;
            if (t == typeof(bool))
            {
                bool v = (bool)e.BoxedValue;
                bool nv = GUILayout.Toggle(v, v ? " On" : " Off", GUILayout.Width(80));
                if (nv != v) e.BoxedValue = nv;
                GUILayout.FlexibleSpace();
            }
            else if (t.IsEnum)
            {
                var values = Enum.GetValues(t);
                int idx = Array.IndexOf(values, e.BoxedValue);
                if (GUILayout.Button("◂", GUILayout.Width(26))) e.BoxedValue = values.GetValue((idx - 1 + values.Length) % values.Length);
                GUILayout.Label(e.BoxedValue.ToString(), GUILayout.Width(180));
                if (GUILayout.Button("▸", GUILayout.Width(26))) e.BoxedValue = values.GetValue((idx + 1) % values.Length);
                GUILayout.FlexibleSpace();
            }
            else if (t == typeof(int) || t == typeof(float))
            {
                var range = e.Description?.AcceptableValues;
                if (range is AcceptableValueRange<int> ri)
                {
                    int v = (int)e.BoxedValue;
                    int nv = Mathf.RoundToInt(GUILayout.HorizontalSlider(v, ri.MinValue, ri.MaxValue, GUILayout.Width(220)));
                    if (nv != v) e.BoxedValue = nv;
                    GUILayout.Label(v.ToString(), GUILayout.Width(50));
                }
                else if (range is AcceptableValueRange<float> rf)
                {
                    float v = (float)e.BoxedValue;
                    float nv = GUILayout.HorizontalSlider(v, rf.MinValue, rf.MaxValue, GUILayout.Width(220));
                    if (Mathf.Abs(nv - v) > 0.0001f) e.BoxedValue = (float)Math.Round(nv, 1);
                    GUILayout.Label(v.ToString("0.#"), GUILayout.Width(50));
                }
                else TextEdit(e, 120);
                GUILayout.FlexibleSpace();
            }
            else if (t == typeof(KeyboardShortcut))
            {
                var k = (KeyboardShortcut)e.BoxedValue;
                bool binding = _binding == e;
                if (GUILayout.Button(binding ? "press a key (Esc cancels, Backspace clears)" : (k.MainKey == KeyCode.None ? "unbound" : k.ToString()), GUILayout.Width(300)))
                    _binding = binding ? null : e;
                if (!binding && k.MainKey != KeyCode.None && GUILayout.Button("Unbind", GUILayout.Width(60))) e.BoxedValue = KeyboardShortcut.Empty;
                GUILayout.FlexibleSpace();
            }
            else if (t == typeof(Color))
            {
                var c = (Color)e.BoxedValue;
                var old = GUI.color;
                GUI.color = c;
                GUILayout.Box("", GUILayout.Width(28), GUILayout.Height(20));
                GUI.color = old;
                TextEdit(e, 120);
                GUILayout.FlexibleSpace();
            }
            else TextEdit(e, 300);
        }

        // Free-text editing via the TOML converter: the value only commits when it parses.
        private static void TextEdit(ConfigEntryBase e, int width)
        {
            string current = TomlTypeConverter.ConvertToString(e.BoxedValue, e.SettingType);
            if (!_textBuffers.TryGetValue(e, out var buf)) buf = current;
            GUI.SetNextControlName("hoard_" + e.Definition.Key);
            string nb = GUILayout.TextField(buf, GUILayout.Width(width));
            bool focused = GUI.GetNameOfFocusedControl() == "hoard_" + e.Definition.Key;
            if (nb != buf) _textBuffers[e] = nb;
            else if (!focused) _textBuffers.Remove(e);
            if (nb != current && (!focused || (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return)))
            {
                try { e.BoxedValue = TomlTypeConverter.ConvertToValue(nb, e.SettingType); _textBuffers.Remove(e); }
                catch { /* keep typing */ }
            }
        }

        // ---- game input mute while open

        [HarmonyPatch]
        private static class ZInput_Mute
        {
            private static IEnumerable<MethodBase> TargetMethods()
            {
                yield return AccessTools.Method(typeof(ZInput), nameof(ZInput.TryGetButtonState));
                yield return AccessTools.Method(typeof(ZInput), nameof(ZInput.TryGetKeyStateLowLevel));
            }

            [HarmonyPriority(Priority.First)]
            private static bool Prefix(ref bool __result)
            {
                if (!Visible || Bypass) return true;
                __result = false;
                return false;
            }
        }

        [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetMouseDelta))]
        private static class ZInput_GetMouseDelta
        {
            private static void Postfix(ref Vector2 __result) { if (Visible) __result = Vector2.zero; }
        }

        [HarmonyPatch(typeof(TextInput), nameof(TextInput.IsVisible))]
        private static class TextInput_IsVisible
        {
            [HarmonyPriority(Priority.Last)]
            private static void Postfix(ref bool __result) { if (Visible) __result = true; }
        }

        [HarmonyPatch]
        private static class Cursor_Override
        {
            private static IEnumerable<MethodBase> TargetMethods()
            {
                yield return AccessTools.Method(typeof(GameCamera), nameof(GameCamera.UpdateMouseCapture));
                yield return AccessTools.Method(typeof(Menu), nameof(Menu.UpdateCursor));
                yield return AccessTools.Method(typeof(FejdStartup), nameof(FejdStartup.UpdateCursor));
            }

            [HarmonyPriority(Priority.First)]
            private static bool Prefix()
            {
                if (!Visible) return true;
                ZCursor.LockState = CursorLockMode.None;
                ZCursor.Show();
                return false;
            }
        }
    }
}
