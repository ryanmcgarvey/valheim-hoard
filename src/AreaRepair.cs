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
            int count = Repair(player, HoardConfig.RepairRange.Value);
            if (HoardConfig.RepairMessages.Value)
                Msg.Center(count == 0 ? "Nothing to repair" : $"Repaired {count} piece{(count == 1 ? "" : "s")}");
            Log.Debug($"Area repair: {count} pieces within {HoardConfig.RepairRange.Value} m");
        }

        public static int Repair(Player player, float radius)
        {
            if (!player) return 0;
            int count = 0;
            var seen = new HashSet<WearNTear>();
            foreach (var col in Physics.OverlapSphere(player.transform.position, radius, Mask))
            {
                var wnt = col.GetComponentInParent<WearNTear>();
                if (!wnt || !seen.Add(wnt)) continue;
                var piece = wnt.GetComponent<Piece>();
                if (!piece) continue;
                if (!HoardConfig.RepairOthersPieces.Value && !piece.IsCreator()) continue;
                // Same gate the hammer uses: the piece's crafting station must be in range.
                if (HoardConfig.RepairNeedsStation.Value && !player.CheckCanRemovePiece(piece)) continue;
                var nview = wnt.GetComponent<ZNetView>();
                if (nview == null || !nview.IsValid()) continue;
                if (wnt.Repair()) count++;
            }
            return count;
        }
    }
}
