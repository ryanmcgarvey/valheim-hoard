using HarmonyLib;

namespace Hoard
{
    // Valheim 1.0 stops awarding Steam achievements once it decides the session is not
    // clean. With this on, Achievements.CanGetAchievements always answers yes, so a
    // modded game earns achievements like a vanilla one. Off by default: whether a modded
    // run should count is a choice the player makes, not the mod.
    //
    // Same effect as DarkmoonBlade's Valheim Achievements Enabler, reimplemented.
    public static class ModdedAchievements
    {
        [HarmonyPatch(typeof(global::Achievements), nameof(global::Achievements.CanGetAchievements))]
        private static class Achievements_CanGetAchievements
        {
            private static bool Prefix(ref bool __result)
            {
                if (!HoardConfig.AchievementsWhileModded.Value) return true;
                __result = true;
                return false;
            }
        }
    }
}
