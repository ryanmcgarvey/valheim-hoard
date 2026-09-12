using HarmonyLib;

namespace Hoard
{
    // Keyboard dispatch for the inventory features. Quick slot hotkeys live in Slots.
    public static class Hotkeys
    {
        [HarmonyPatch(typeof(Player), nameof(Player.Update))]
        private static class Player_Update
        {
            private static void Postfix(Player __instance)
            {
                if (__instance != Player.m_localPlayer || GameState.IgnoreKeys()) return;
                if (ConfigWindow.Visible) return;

                if (HoardConfig.QuickStackEnabled.Value && HoardConfig.QuickStackKey.Value.IsDown()) { QuickStack.Run(__instance); return; }
                if (HoardConfig.RestockEnabled.Value && HoardConfig.RestockKey.Value.IsDown()) { Restock.Run(__instance); return; }

                if (!InventoryGui.IsVisible()) return;
                if (HoardConfig.SortEnabled.Value && HoardConfig.SortKey.Value.IsDown()) { Sorting.Run(__instance); return; }
                if (HoardConfig.TrashEnabled.Value)
                {
                    if (HoardConfig.QuickTrashKey.Value.IsDown()) { Trash.OnQuickTrashPressed(); return; }
                    if (HoardConfig.TrashKey.Value.IsDown()) { Trash.OnTrashPressed(fromHotkey: true); return; }
                }
                if (HoardConfig.StoreAllKey.Value.IsDown()) { StoreTakeAll.StoreAll(__instance); return; }
                if (HoardConfig.RecycleEnabled.Value && HoardConfig.RecycleKey.Value.IsDown()) { Recycle.OnRecyclePressed(); return; }
            }
        }
    }
}
