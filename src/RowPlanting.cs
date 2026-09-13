using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Hoard
{
    // Plant a whole row (or a block of rows) with the cultivator in one click.
    //
    // While a plant is the selected piece: hold the row modifier and scroll to set how
    // many plants long the row is, hold the rows modifier and scroll to set how many rows
    // deep, or press the fill key to toggle "as far as it goes": the row extends until it
    // hits ground that won't take the plant, something in the way, or the end of your
    // seeds (nearby chests count when craft-from-containers is on). Extra ghosts show the
    // result; invalid or unaffordable ones are tinted red and skipped on placement.
    public static class RowPlanting
    {
        public static int Length = 1;
        public static int Rows = 1;
        public static bool Fill;

        private static readonly List<GameObject> _ghosts = new List<GameObject>();
        private static readonly List<Vector3> _positions = new List<Vector3>();
        private static readonly List<bool> _valid = new List<bool>();
        private static GameObject _source;
        private static float _scroll;
        private static int _spaceMask;
        private static readonly Collider[] _hits = new Collider[32];

        private static bool Enabled => HoardConfig.RowPlantingEnabled.Value;

        private static Plant PlantOf(Player p)
        {
            var ghost = p?.m_placementGhost;
            return ghost ? ghost.GetComponent<Plant>() : null;
        }

        public static bool Active(Player p) => Enabled && p && p.InPlaceMode() && PlantOf(p) != null;

        // Row length in effect this frame (Fill mode computes it from what is possible).
        public static int EffectiveLength => Mathf.Clamp(Length, 1, HoardConfig.RowMaxLength.Value);
        public static int EffectiveRows => Mathf.Clamp(Rows, 1, HoardConfig.RowMaxRows.Value);
        public static int Extra => _positions.Count;

        // ---- input

        // Scrolling with a modifier held goes to us, not to piece rotation.
        [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetMouseScrollWheel))]
        private static class ZInput_GetMouseScrollWheel
        {
            private static void Postfix(ref float __result)
            {
                if (__result == 0f || !Active(Player.m_localPlayer)) return;
                if (HoardConfig.RowModifier.Value.IsHeld() || HoardConfig.RowsModifier.Value.IsHeld())
                {
                    _scroll += __result;
                    __result = 0f;
                }
            }
        }

        public static void HandleInput(Player p)
        {
            if (!Active(p)) { _scroll = 0f; return; }
            if (HoardConfig.RowFillKey.Value.IsDown())
            {
                Fill = !Fill;
                Msg.Center(Fill ? "Row planting: fill as far as possible" : $"Row planting: {EffectiveLength} x {EffectiveRows}");
            }
            if (Mathf.Abs(_scroll) >= 0.5f)
            {
                int step = _scroll > 0f ? 1 : -1;
                _scroll = 0f;
                if (HoardConfig.RowsModifier.Value.IsHeld()) // the more specific chord wins
                    Rows = Mathf.Clamp(Rows + step, 1, HoardConfig.RowMaxRows.Value);
                else if (HoardConfig.RowModifier.Value.IsHeld())
                {
                    Fill = false;
                    Length = Mathf.Clamp(Length + step, 1, HoardConfig.RowMaxLength.Value);
                }
                Msg.Center(Fill ? $"Row planting: fill x {EffectiveRows} rows" : $"Row planting: {EffectiveLength} x {EffectiveRows}");
            }
        }

        // ---- ghosts

        private static void ClearGhosts()
        {
            foreach (var g in _ghosts) if (g) Object.Destroy(g);
            _ghosts.Clear();
            _positions.Clear();
            _valid.Clear();
            _source = null;
        }

        // A visual-only copy of the placement ghost. The ghost still carries the prefab's
        // scripts (Plant, Piece, ...) whose Awake expects a live ZNetView and throws on a
        // clone, so the copy is made inactive, stripped of every script, then activated.
        private static GameObject Ghost(int i, GameObject source)
        {
            while (_ghosts.Count <= i)
            {
                bool wasActive = source.activeSelf;
                source.SetActive(false);
                GameObject g;
                try
                {
                    ZNetView.m_forceDisableInit = true;
                    g = Object.Instantiate(source, source.transform.parent);
                }
                finally
                {
                    ZNetView.m_forceDisableInit = false;
                    source.SetActive(wasActive);
                }
                g.name = source.name + "_hoard_row";
                foreach (var mb in g.GetComponentsInChildren<MonoBehaviour>(true)) Object.DestroyImmediate(mb);
                foreach (var col in g.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(col);
                g.SetActive(true);
                _ghosts.Add(g);
            }
            return _ghosts[i];
        }

        private static void Tint(GameObject g, bool invalid)
        {
            if (!MaterialMan.instance) return;
            if (invalid)
            {
                MaterialMan.instance.SetValue(g, ShaderProps._Color, Color.red);
                MaterialMan.instance.SetValue(g, ShaderProps._EmissionColor, Color.red * 0.7f);
            }
            else
            {
                MaterialMan.instance.ResetValue(g, ShaderProps._Color);
                MaterialMan.instance.ResetValue(g, ShaderProps._EmissionColor);
            }
        }

        // Extra positions on the grid; index 0 is the primary ghost (not included).
        private static float Spacing(Plant plant) => Mathf.Max(0.25f, plant.m_growRadius * 2f * HoardConfig.RowSpacing.Value);

        private static bool Valid(Player p, Piece piece, Plant plant, Vector3 pos)
        {
            if (Location.IsInsideNoBuildLocation(pos)) return false;
            if (!PrivateArea.CheckAccess(pos, 0f, false, false)) return false;
            var hm = Heightmap.FindHeightmap(pos);
            if ((piece.m_groundOnly || piece.m_groundPiece || piece.m_cultivatedGroundOnly) && hm == null) return false;
            if (piece.m_cultivatedGroundOnly && !hm.IsCultivated(pos)) return false;
            if (piece.m_onlyInBiome != Heightmap.Biome.None && (Heightmap.FindBiome(pos) & piece.m_onlyInBiome) == 0) return false;
            if (_spaceMask == 0) _spaceMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid");
            int n = Physics.OverlapSphereNonAlloc(pos, plant.m_growRadius, _hits, _spaceMask);
            for (int i = 0; i < n; i++)
            {
                var other = _hits[i].GetComponent<Plant>();
                if (!other || other.GetStatus() == Plant.Status.Healthy) return false;
            }
            return true;
        }

        private static int Affordable(Player p, Piece piece)
        {
            if (p.m_noPlacementCost || ZoneSystem.instance.GetGlobalKey(piece.FreeBuildKey())) return int.MaxValue;
            int n = int.MaxValue;
            foreach (var req in piece.m_resources)
            {
                if (!req.m_resItem || req.m_amount <= 0) continue;
                string name = req.m_resItem.m_itemData.m_shared.m_name;
                int have = Crafting.Enabled ? Crafting.TotalAvailable(p, name) : p.m_inventory.CountItems(name);
                n = Mathf.Min(n, have / req.m_amount);
            }
            return n;
        }

        [HarmonyPatch(typeof(Player), nameof(Player.UpdatePlacementGhost))]
        private static class Player_UpdatePlacementGhost
        {
            private static int _errors;

            private static void Postfix(Player __instance)
            {
                if (__instance != Player.m_localPlayer) return;
                try { Update(__instance); }
                catch (System.Exception e)
                {
                    if (_errors++ < 3) Log.Error($"Row planting preview failed: {e}");
                    ClearGhosts();
                }
            }

            private static void Update(Player __instance)
            {
                var ghost = __instance.m_placementGhost;
                var plant = Active(__instance) ? ghost.GetComponent<Plant>() : null;
                if (plant == null || !ghost.activeSelf)
                {
                    if (_ghosts.Count > 0) ClearGhosts();
                    return;
                }
                if (_source != ghost) { ClearGhosts(); _source = ghost; }

                var piece = ghost.GetComponent<Piece>();
                float spacing = Spacing(plant);
                Vector3 origin = ghost.transform.position;
                Vector3 right = ghost.transform.right;
                Vector3 forward = ghost.transform.forward;
                int rows = EffectiveRows;
                int length = Fill ? HoardConfig.RowMaxLength.Value : EffectiveLength;
                // Budget: everything the primary ghost isn't already paying for.
                int budget = __instance.m_placementStatus == Player.PlacementStatus.Valid ? Affordable(__instance, piece) - 1 : Affordable(__instance, piece);

                _positions.Clear();
                _valid.Clear();
                for (int r = 0; r < rows; r++)
                {
                    for (int i = 0; i < length; i++)
                    {
                        if (r == 0 && i == 0) continue;
                        Vector3 pos = origin + right * (spacing * i) + forward * (spacing * r);
                        if (ZoneSystem.instance.GetGroundHeight(pos, out float h)) pos.y = h;
                        bool ok = Valid(__instance, piece, plant, pos);
                        if (Fill && !ok) break; // fill mode: a row ends at the first bad spot
                        if (ok && budget <= 0) ok = false;
                        if (ok) budget--;
                        _positions.Add(pos);
                        _valid.Add(ok);
                        if (Fill && budget <= 0) break;
                    }
                    if (Fill && budget <= 0) break;
                }

                for (int i = 0; i < _positions.Count; i++)
                {
                    var g = Ghost(i, ghost);
                    g.SetActive(true);
                    g.transform.position = _positions[i];
                    g.transform.rotation = ghost.transform.rotation;
                    Tint(g, !_valid[i]);
                }
                for (int i = _positions.Count; i < _ghosts.Count; i++) if (_ghosts[i]) _ghosts[i].SetActive(false);
            }
        }

        // The primary went down: place every valid extra, paying for each.
        [HarmonyPatch(typeof(Player), nameof(Player.TryPlacePiece))]
        private static class Player_TryPlacePiece
        {
            // Plants have m_randomInitBuildRotation, so every PlacePiece re-rolls the ghost
            // rotation - and with it the direction the row points. Keep the rotation the
            // player set for as long as a plant is selected.
            private static void Prefix(Player __instance, ref int __state) => __state = __instance.m_placeRotation;

            private static void Postfix(Player __instance, Piece piece, bool __result, int __state)
            {
                if (__instance != Player.m_localPlayer || !Active(__instance)) return;
                try { PlaceExtras(__instance, piece, __result); }
                finally { if (HoardConfig.RowKeepRotation.Value) __instance.m_placeRotation = __state; }
            }

            private static void PlaceExtras(Player __instance, Piece piece, bool placedPrimary)
            {
                if (!placedPrimary || _positions.Count == 0) return;
                bool free = __instance.m_noPlacementCost || ZoneSystem.instance.GetGlobalKey(piece.FreeBuildKey());
                var rot = __instance.m_placementGhost.transform.rotation;
                // Snapshot: paying for a plant changes the inventory, which makes the game rebuild
                // the placement ghost (Player.UpdateAvailablePiecesList -> SetupPlacementGhost),
                // and that clears the live lists while this loop is still walking them.
                var positions = _positions.ToArray();
                var valid = _valid.ToArray();
                int placed = 0;
                for (int i = 0; i < positions.Length; i++)
                {
                    if (!valid[i]) continue;
                    // The primary's cost is consumed by the caller after this; keep one set aside.
                    if (!free && Affordable(__instance, piece) < 2) break;
                    __instance.PlacePiece(piece, positions[i], rot, doAttack: false);
                    if (!free) __instance.ConsumeResources(piece.m_resources, 0);
                    placed++;
                }
                if (placed > 0)
                {
                    Game.instance.IncrementPlayerStat(PlayerStatType.Builds, placed);
                    Log.Debug($"row planting placed {placed} extra {piece.m_name}");
                }
            }
        }

        // The ghost is rebuilt whenever the inventory changes (so after every planting), and
        // the rebuild re-rolls the rotation of random-rotation pieces. Keep ours.
        [HarmonyPatch(typeof(Player), nameof(Player.SetupPlacementGhost))]
        private static class Player_SetupPlacementGhost
        {
            private static void Prefix(Player __instance, ref int __state)
            {
                ClearGhosts();
                __state = __instance.m_placeRotation;
            }

            private static void Postfix(Player __instance, int __state)
            {
                if (!Enabled || !HoardConfig.RowKeepRotation.Value || __instance != Player.m_localPlayer) return;
                if (PlantOf(__instance) != null) __instance.m_placeRotation = __state;
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.OnDestroy))]
        private static class Player_OnDestroy { private static void Postfix() => ClearGhosts(); }

        [HarmonyPatch(typeof(Hud), nameof(Hud.SetupPieceInfo))]
        private static class Hud_SetupPieceInfo
        {
            [HarmonyPriority(Priority.Low)]
            private static void Postfix(Hud __instance, Piece piece)
            {
                if (!Active(Player.m_localPlayer) || !__instance.m_buildSelection) return;
                string mode = Fill ? "fill" : EffectiveLength.ToString();
                __instance.m_buildSelection.text += $"  <color=#d9a441>row {mode} x {EffectiveRows}</color>";
            }
        }
    }
}
