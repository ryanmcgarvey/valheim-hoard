using System;
using System.Linq;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;

namespace Hoard
{
    public static class Log
    {
        public static ManualLogSource Source;
        public static void Info(string msg) => Source?.LogInfo(msg);
        public static void Warn(string msg) => Source?.LogWarning(msg);
        public static void Error(string msg) => Source?.LogError(msg);
        public static void Debug(string msg)
        {
            if (HoardConfig.DebugLogging != null && HoardConfig.DebugLogging.Value) Source?.LogInfo(msg);
        }
    }

    public static class Keys
    {
        // Uses the game's own input layer (ZInput) rather than UnityEngine.Input so the
        // hotkeys respect the same key state the game sees.
        public static bool IsDown(this KeyboardShortcut k)
            => k.MainKey != KeyCode.None && ZInput.GetKeyDown(k.MainKey, false) && k.Modifiers.All(m => ZInput.GetKey(m, false));

        public static bool IsHeld(this KeyboardShortcut k)
            => k.MainKey != KeyCode.None && ZInput.GetKey(k.MainKey, false) && k.Modifiers.All(m => ZInput.GetKey(m, false));

        public static string Label(this KeyboardShortcut k) => k.MainKey == KeyCode.None ? "" : k.ToString();

        public static bool IsModifier(KeyCode key) =>
            key == KeyCode.AltGr || key == KeyCode.LeftAlt || key == KeyCode.RightAlt ||
            key == KeyCode.LeftShift || key == KeyCode.RightShift ||
            key == KeyCode.LeftControl || key == KeyCode.RightControl ||
            key == KeyCode.LeftApple || key == KeyCode.RightApple ||
            key == KeyCode.LeftCommand || key == KeyCode.RightCommand ||
            key == KeyCode.LeftWindows || key == KeyCode.RightWindows;
    }

    public static class Msg
    {
        public static void Center(string text)
        {
            var p = Player.m_localPlayer;
            if (p != null) p.Message(MessageHud.MessageType.Center, text);
        }
    }

    public static class GameState
    {
        // True when the player should not be reacting to hotkeys at all.
        public static bool IgnoreKeys()
        {
            var p = Player.m_localPlayer;
            if (!p || p.InCutscene() || p.IsTeleporting() || p.IsDead() || p.InPlaceMode()) return true;
            if (!ZNetScene.instance) return true;
            if (Minimap.IsOpen() || Menu.IsVisible() || Console.IsVisible() || StoreGui.IsVisible() || TextInput.IsVisible()) return true;
            if (Chat.instance && Chat.instance.HasFocus()) return true;
            if (ZNet.instance && ZNet.instance.InPasswordDialog()) return true;
            if (TextViewer.instance && TextViewer.instance.IsVisible()) return true;
            return false;
        }

        // Like IgnoreKeys but allowing build/place mode (row planting keys live there).
        public static bool IgnoreKeysInPlaceMode()
        {
            var p = Player.m_localPlayer;
            if (!p || p.InCutscene() || p.IsTeleporting() || p.IsDead()) return true;
            if (!ZNetScene.instance) return true;
            if (Minimap.IsOpen() || Menu.IsVisible() || Console.IsVisible() || StoreGui.IsVisible() || TextInput.IsVisible() || InventoryGui.IsVisible()) return true;
            if (Chat.instance && Chat.instance.HasFocus()) return true;
            return false;
        }

        public static bool IsTrueSingleplayer()
            => !ZNet.m_openServer && !ZNet.m_publicServer && ZNet.instance && ZNet.instance.IsServer() && !ZNet.instance.IsDedicated() && ZNet.instance.GetConnectedPeers().Count == 0;
    }

    public static class ItemExt
    {
        public static bool HasCustomData(this ItemDrop.ItemData item) => item.m_customData != null && item.m_customData.Count > 0;
        public static int GridIndex(this ItemDrop.ItemData item, int width) => item.m_gridPos.y * width + item.m_gridPos.x;
    }
}
