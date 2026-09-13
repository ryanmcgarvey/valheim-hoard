using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Hoard
{
    // Killed creatures drop their loot at once instead of when the ragdoll times out
    // (the game waits Ragdoll.m_ttl, several seconds, before DestroyNow spawns the loot).
    // The ragdoll itself is removed shortly after, so bodies don't pile up either.
    //
    // Only the ragdoll's owner spawns loot, exactly as the game does, so in multiplayer
    // nothing is duplicated. Once this module has taken over a ragdoll the game's own
    // DestroyNow is skipped for it, even if the setting is turned off in between - otherwise
    // the loot would be spawned twice.
    //
    // Replaces aedenthorn's Instant Monster Drop (public domain).
    public static class InstantLoot
    {
        private static readonly HashSet<Ragdoll> _handled = new HashSet<Ragdoll>();

        [HarmonyPatch(typeof(Ragdoll), nameof(Ragdoll.Awake))]
        private static class Ragdoll_Awake
        {
            private static void Postfix(Ragdoll __instance)
            {
                if (!HoardConfig.InstantLootEnabled.Value || !ZNetScene.instance) return;
                _handled.Add(__instance);
                __instance.StartCoroutine(DropThenRemove(__instance));
            }
        }

        [HarmonyPatch(typeof(Ragdoll), nameof(Ragdoll.DestroyNow))]
        private static class Ragdoll_DestroyNow
        {
            private static bool Prefix(Ragdoll __instance) => !_handled.Contains(__instance);
        }

        private static IEnumerator DropThenRemove(Ragdoll ragdoll)
        {
            // One frame so Setup() has stored the loot list and the bodies have a position.
            yield return null;
            if (!ragdoll) yield break;
            var nview = ragdoll.m_nview;
            if (nview == null || !nview.IsValid() || !nview.IsOwner())
            {
                // Not ours to handle: let the game's own timer deal with it.
                _handled.Remove(ragdoll);
                yield break;
            }

            bool spawned;
            try
            {
                Vector3 pos = ragdoll.m_lootSpawnJoint != null ? ragdoll.m_lootSpawnJoint.transform.position : ragdoll.GetAverageBodyPosition();
                ragdoll.SpawnLoot(pos);
                spawned = true;
            }
            catch (Exception e)
            {
                Log.Warn($"Instant loot: spawning loot failed, leaving the ragdoll to the game: {e.Message}");
                spawned = false;
            }
            if (!spawned)
            {
                _handled.Remove(ragdoll);
                yield break;
            }

            float linger = Mathf.Max(0f, HoardConfig.InstantLootRagdollSeconds.Value);
            if (linger > 0f) yield return new WaitForSeconds(linger);

            if (!ragdoll) yield break;
            if (nview.IsValid() && nview.IsOwner())
            {
                if (ragdoll.m_removeEffect != null) ragdoll.m_removeEffect.Create(ragdoll.GetAverageBodyPosition(), Quaternion.identity);
                ZNetScene.instance.Destroy(ragdoll.gameObject);
            }
            _handled.Remove(ragdoll);
        }
    }
}
