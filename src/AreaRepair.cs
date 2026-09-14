using System.Collections.Generic;
using UnityEngine;

namespace Hoard
{
    // Repair every damaged build piece around the player with one key press, instead of
    // walking up to each one with the hammer. The same rules as the hammer apply: a piece
    // that needs a crafting station is only repaired while that station is in range
    // (configurable), and by default only your own pieces are touched.
    //
    // Replaces aedenthorn's Instant Building Repair (public domain), whose behaviour this
    // follows.
    public static class AreaRepair
    {
        private static int _mask;

        private static int Mask
        {
            get
            {
                if (_mask == 0) _mask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid", "vehicle");
                return _mask;
            }
        }

        // Called from Hotkeys when the repair key is pressed.
        public static void Run(Player player)
        {
            var r = Repair(player, HoardConfig.RepairRange.Value);
            if (HoardConfig.RepairMessages.Value) Msg.Center(Summary(r));
            Log.Debug($"Area repair: {r.repaired} repaired, {r.noStation} without station, {r.notMine} not mine, within {HoardConfig.RepairRange.Value} m");
        }

        public struct Result { public int repaired, noStation, notMine; }

        // One line for the whole sweep: what was repaired and, when anything was skipped,
        // how many and why. The two skip reasons stay separate because they call for
        // different action from the player.
        private static string Summary(Result r)
        {
            var skipped = new List<string>();
            if (r.noStation > 0) skipped.Add($"{r.noStation} need a crafting station in range");
            if (r.notMine > 0) skipped.Add($"{r.notMine} not built by you");
            string skip = skipped.Count > 0 ? " (" + string.Join(", ", skipped) + ")" : "";
            if (r.repaired == 0) return skipped.Count > 0 ? "Nothing repaired" + skip : "Nothing to repair";
            return $"Repaired {r.repaired} piece{(r.repaired == 1 ? "" : "s")}" + skip;
        }

        public static Result Repair(Player player, float radius)
        {
            var r = new Result();
            if (!player) return r;
            var seen = new HashSet<WearNTear>();
            bool needStation = HoardConfig.RepairNeedsStation.Value && !player.m_noPlacementCost && !ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoWorkbench);
            foreach (var col in Physics.OverlapSphere(player.transform.position, radius, Mask))
            {
                var wnt = col.GetComponentInParent<WearNTear>();
                if (!wnt || !seen.Add(wnt)) continue;
                var piece = wnt.GetComponent<Piece>();
                if (!piece) continue;
                var nview = wnt.GetComponent<ZNetView>();
                if (nview == null || !nview.IsValid()) continue;
                // Only damaged pieces count as candidates, so the skip counts mean something.
                if (wnt.GetHealthPercentage() >= 1f) continue;
                if (!HoardConfig.RepairOthersPieces.Value && !piece.IsCreator()) { r.notMine++; continue; }
                // The hammer's own gate (Player.CheckCanRemovePiece), minus its per-piece HUD
                // message: a piece that needs a crafting station is repaired only with one in range.
                if (needStation && piece.m_craftingStation != null && !CraftingStation.HaveBuildStationInRange(piece.m_craftingStation.m_name, player.transform.position)) { r.noStation++; continue; }
                if (wnt.Repair()) r.repaired++;
            }
            return r;
        }
    }
}
