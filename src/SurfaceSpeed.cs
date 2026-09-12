using HarmonyLib;
using UnityEngine;

namespace Hoard
{
    // Run faster on made surfaces: dirt paths, paved (stone) paths, wood and stone
    // floors. The bonus multiplies the game's own run speed factor (skill + equipment)
    // and only applies while running.
    //
    // The ground is identified the way the game's footstep system does it: the collider
    // the character last stood on is either a Heightmap (terrain, where the paint mask
    // says dirt path / cultivated / paved), a piece (WearNTear material), or something else.
    public static class SurfaceSpeed
    {
        public enum Surface { None, Ground, DirtPath, PavedPath, Cultivated, Wood, Stone, Metal }

        public static Surface Current { get; private set; }
        private static int _frame = -1;

        public static Surface Detect(Character c)
        {
            if (c == null || !c.IsOnGround() || c.InWater() || c.InLiquid()) return Surface.None;
            var col = c.GetLastGroundCollider();
            if (col == null) return Surface.None;
            var hm = col.GetComponent<Heightmap>();
            if (hm != null)
            {
                Color paint = hm.GetPaintMask(c.transform.position);
                if (paint.b > 0.5f) return Surface.PavedPath;
                if (paint.r > 0.5f) return Surface.DirtPath;
                if (paint.g > 0.5f) return Surface.Cultivated;
                return Surface.Ground;
            }
            var fsc = col.GetComponent<FootStepCollider>();
            if (fsc != null) return FromMaterial(fsc.m_material);
            var wnt = col.GetComponentInParent<WearNTear>();
            if (wnt != null)
            {
                switch (wnt.m_materialType)
                {
                    case WearNTear.MaterialType.Wood:
                    case WearNTear.MaterialType.HardWood: return Surface.Wood;
                    case WearNTear.MaterialType.Stone:
                    case WearNTear.MaterialType.Marble:
                    case WearNTear.MaterialType.Ashstone:
                    case WearNTear.MaterialType.Ancient: return Surface.Stone;
                    case WearNTear.MaterialType.Iron: return Surface.Metal;
                }
            }
            return Surface.Ground;
        }

        private static Surface FromMaterial(FootStep.GroundMaterial m)
        {
            if ((m & FootStep.GroundMaterial.Wood) != 0) return Surface.Wood;
            if ((m & FootStep.GroundMaterial.Stone) != 0) return Surface.Stone;
            if ((m & FootStep.GroundMaterial.Metal) != 0) return Surface.Metal;
            return Surface.Ground;
        }

        public static float BonusPercent(Surface s)
        {
            switch (s)
            {
                case Surface.DirtPath: return HoardConfig.SpeedDirtPath.Value;
                case Surface.PavedPath: return HoardConfig.SpeedPavedPath.Value;
                case Surface.Cultivated: return HoardConfig.SpeedCultivated.Value;
                case Surface.Wood: return HoardConfig.SpeedWood.Value;
                case Surface.Stone: return HoardConfig.SpeedStone.Value;
                case Surface.Metal: return HoardConfig.SpeedMetal.Value;
                default: return 0f;
            }
        }

        // Detected once per frame; the factor is asked for on every movement update.
        private static Surface CurrentFor(Player p)
        {
            if (_frame != Time.frameCount)
            {
                _frame = Time.frameCount;
                Current = Detect(p);
            }
            return Current;
        }

        // Running on a made surface also costs less stamina per second (one factor for all
        // surfaces that have a speed bonus), so a road is cheaper per metre, not just faster.
        [HarmonyPatch(typeof(SEMan), nameof(SEMan.ModifyRunStaminaDrain))]
        private static class SEMan_ModifyRunStaminaDrain
        {
            private static void Postfix(SEMan __instance, ref float drain)
            {
                if (!HoardConfig.SurfaceSpeedEnabled.Value) return;
                var p = Player.m_localPlayer;
                if (!p || __instance.m_character != p) return;
                if (BonusPercent(CurrentFor(p)) <= 0f) return;
                drain *= Mathf.Clamp(HoardConfig.SpeedStaminaPercent.Value, 0f, 200f) / 100f;
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.GetRunSpeedFactor))]
        private static class Player_GetRunSpeedFactor
        {
            private static void Postfix(Player __instance, ref float __result)
            {
                if (!HoardConfig.SurfaceSpeedEnabled.Value || __instance != Player.m_localPlayer) return;
                float bonus = BonusPercent(CurrentFor(__instance));
                if (bonus != 0f) __result *= 1f + bonus / 100f;
            }
        }
    }
}
