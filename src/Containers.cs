using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Hoard
{
    // Registry of loaded containers and the rules for which ones an area operation
    // (craft-from-containers, quick stack, restock) may touch.
    //
    // Multiplayer: a container's inventory is authoritative on whichever peer owns its
    // ZDO, so before touching a container we don't own we claim ownership - exactly what
    // the game itself does when you open a chest (Container.RPC_RequestOpen hands the
    // ZDO over) or take-all from it (RPC_TakeAllResponse calls ClaimOwnership). A
    // container that another player currently has open is never touched: the owner
    // publishes that in the ZDO's "InUse" field.
    public static class Containers
    {
        private static readonly List<Container> All = new List<Container>();

        [HarmonyPatch(typeof(Container), nameof(Container.Awake))]
        private static class Register
        {
            private static void Postfix(Container __instance)
            {
                if (__instance && !All.Contains(__instance)) All.Add(__instance);
            }
        }

        [HarmonyPatch(typeof(Container), nameof(Container.OnDestroyed))]
        private static class Unregister
        {
            private static void Postfix(Container __instance) => All.Remove(__instance);
        }

        public static void Prune() => All.RemoveAll(c => !c);

        public static bool IsValid(Container c)
            => c && c.m_nview && c.m_nview.IsValid() && c.GetInventory() != null;

        public static bool IsInUseByOther(Container c)
        {
            if (c.m_wagon && c.m_wagon.InUse()) return true;
            if (c.m_nview.IsOwner()) return c.m_inUse && InventoryGui.instance?.m_currentContainer != c;
            return c.m_nview.GetZDO().GetInt(ZDOVars.s_inUse) == 1;
        }

        // Access and policy checks that don't depend on distance.
        public static bool Allowed(Container c)
        {
            if (!IsValid(c)) return false;
            if (c.GetComponent<TombStone>() || c.GetComponentInParent<TombStone>()) return false;
            if (c.GetComponentInParent<Player>()) return false;
            long playerId = Game.instance ? Game.instance.GetPlayerProfile().GetPlayerID() : 0L;
            if (!c.CheckAccess(playerId)) return false;
            if (c.m_checkGuardStone && !PrivateArea.CheckAccess(c.transform.position, 0f, false, false)) return false;
            if (c.GetComponentInParent<Ship>()) return HoardConfig.ContainersIncludeShips.Value;
            if (c.m_wagon) return HoardConfig.ContainersIncludeCarts.Value;
            bool playerBuilt = c.m_piece ? c.m_piece.IsPlacedByPlayer() : c.m_nview.GetZDO().GetLong(ZDOVars.s_creator) != 0L;
            if (!playerBuilt && !HoardConfig.ContainersIncludeNonPlayerBuilt.Value) return false;
            return true;
        }

        public static List<Container> Nearby(Vector3 point, float range)
        {
            Prune();
            var result = new List<Container>();
            if (range <= 0f) return result;
            float r2 = range * range;
            foreach (var c in All)
            {
                if (!c) continue;
                if ((c.transform.position - point).sqrMagnitude > r2) continue;
                if (!Allowed(c)) continue;
                if (IsInUseByOther(c)) continue;
                result.Add(c);
            }
            result.Sort((a, b) => (a.transform.position - point).sqrMagnitude.CompareTo((b.transform.position - point).sqrMagnitude));
            return result;
        }

        // Per-frame cache: several patches ask for the same list in one frame (requirement
        // rows, craft button state, hover text).
        private static int _frame = -1;
        private static float _range;
        private static Vector3 _pos;
        private static List<Container> _cached = new List<Container>();

        public static List<Container> NearbyPlayer(float range)
        {
            var p = Player.m_localPlayer;
            if (!p) return new List<Container>();
            if (_frame == Time.frameCount && _range == range && _pos == p.transform.position) return _cached;
            _frame = Time.frameCount; _range = range; _pos = p.transform.position;
            _cached = Nearby(_pos, range);
            return _cached;
        }

        public static List<Container> ForCrafting() => NearbyPlayer(HoardConfig.CraftRange.Value);

        // Take ownership so that our edits to the inventory are the ones that get saved.
        // Returns false if the container is now known to be in use by someone else.
        public static bool Claim(Container c)
        {
            if (!IsValid(c)) return false;
            if (IsInUseByOther(c)) return false;
            if (!c.m_nview.IsOwner())
            {
                c.m_nview.ClaimOwnership();
                ZDOMan.instance.ForceSendZDO(c.m_nview.GetZDO().m_uid);
            }
            return true;
        }

        // Write the container's inventory to its ZDO now (the owner path of Container.Save)
        // and push it to peers, so a chest edited without being opened syncs right away.
        public static void Commit(Container c)
        {
            if (!IsValid(c) || !c.m_nview.IsOwner()) return;
            c.Save();
            ZDOMan.instance.ForceSendZDO(c.m_nview.GetZDO().m_uid);
        }

        // Counting helpers used by crafting.
        public static int Count(Container c, string sharedName, int quality = -1)
        {
            int n = c.GetInventory().CountItems(sharedName, quality);
            if (HoardConfig.CraftLeaveOne.Value && n > 0) n -= 1;
            return n;
        }

        public static int CountAll(IEnumerable<Container> list, string sharedName, int quality = -1)
        {
            int total = 0;
            foreach (var c in list) total += Count(c, sharedName, quality);
            return total;
        }

        // Remove up to `amount` of an item across containers; returns how many were removed.
        public static int Remove(IEnumerable<Container> list, string sharedName, int amount, int quality = -1)
        {
            int removed = 0;
            foreach (var c in list)
            {
                if (amount <= 0) break;
                int have = Count(c, sharedName, quality);
                if (have <= 0) continue;
                if (!Claim(c)) continue;
                int take = Mathf.Min(have, amount);
                c.GetInventory().RemoveItem(sharedName, take, quality);
                Commit(c);
                amount -= take;
                removed += take;
                Log.Debug($"took {take} {sharedName} from {c.m_name} @ {c.transform.position}");
            }
            return removed;
        }

        // Move one item of the given shared name from any container into the inventory.
        public static bool PullOne(IEnumerable<Container> list, string sharedName, Inventory into)
        {
            foreach (var c in list)
            {
                if (Count(c, sharedName) <= 0) continue;
                var item = c.GetInventory().GetItem(sharedName);
                if (item == null) continue;
                if (!into.CanAddItem(item, 1)) return false;
                if (!Claim(c)) continue;
                var one = item.Clone();
                one.m_stack = 1;
                if (!into.AddItem(one)) return false;
                c.GetInventory().RemoveItem(item, 1);
                Commit(c);
                return true;
            }
            return false;
        }
    }
}
