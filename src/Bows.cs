// PotatoFighter - bow shots the game drops, and Epic Loot's Triple Shot after a cancelled attack (moved here from
// Potato Tweaks 1.5.16, together with attack cancel, which they work with).
// Adapted from EpicLoot Progression Fix 0.1.281 (bow-release.cs, queued-bow-cancellation.cs), MIT License,
// Copyright (c) 2026 EpicLoot Progression Fix contributors. See LICENSE.
using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace PotatoFighter
{
    internal static class Bows
    {
        internal static readonly AccessTools.FieldRef<Character, bool> AttackHold = AccessTools.FieldRefAccess<Character, bool>("m_attackHold");
        internal static readonly AccessTools.FieldRef<Character, bool> Blocking = AccessTools.FieldRefAccess<Character, bool>("m_blocking");
        internal static readonly AccessTools.FieldRef<Humanoid, Attack> CurrentAttack = AccessTools.FieldRefAccess<Humanoid, Attack>("m_currentAttack");
    }

    // ------------------------------------------------------------------ bow release
    // A bow fires again only once the previous shot's animation ends, about 0.8 seconds after the shot, but the next draw
    // and its crosshair fill from the moment the button goes down. From Bows skill 85, or lower with Quick Draw or a Bows
    // skill bonus, the draw is full before then, and the game drops a release in that time: no arrow, no ammunition used and
    // no message. Keep such an arrow drawn until the bow can fire and loose it then, as the game already keeps a melee click
    // made during an attack. A release refused for another reason, such as a dodge or no stamina, is dropped as before.
    [HarmonyPatch(typeof(Player), "UpdateAttackBowDraw")]
    internal static class BowRelease
    {
        private const float MaxWait = 1f;
        private static readonly AccessTools.FieldRef<Humanoid, float> DrawTime = AccessTools.FieldRefAccess<Humanoid, float>("m_attackDrawTime");
        private static float releaseBy = -1f;

        private struct DrawState
        {
            internal bool AttackHeld;
            internal bool QueuedRelease;
            internal Attack PreviousAttack;
        }

        private static void Prefix(Player __instance, out DrawState __state)
        {
            __state = new DrawState { AttackHeld = Bows.AttackHold(__instance) };
            if (__instance != Player.m_localPlayer) return;
            if (!Plugin.KeepFastShots.Value)
            {
                releaseBy = -1f;
                return;
            }
            if (DrawTime(__instance) <= 0f)
            {
                releaseBy = -1f;
                return;
            }
            bool heldBack = releaseBy >= 0f;
            if (!heldBack && __state.AttackHeld) return;
            // An attack abandoned by raising the guard holds a release back for as long as it delays the next attack.
            if ((__instance.InAttack() || Time.time < AttackCancel.AttackAllowedAt) && (!heldBack || Time.time < releaseBy))
            {
                if (!heldBack) releaseBy = Time.time + MaxWait;
                Bows.AttackHold(__instance) = true;
                return;
            }
            releaseBy = -1f;
            Bows.AttackHold(__instance) = false;
            __state.QueuedRelease = heldBack;
            __state.PreviousAttack = Bows.CurrentAttack(__instance);
        }

        private static void Postfix(Player __instance, DrawState __state)
        {
            Bows.AttackHold(__instance) = __state.AttackHeld;
            if (!__state.QueuedRelease) return;
            var attack = Bows.CurrentAttack(__instance);
            if (attack != null && attack != __state.PreviousAttack && attack.GetWeapon()?.m_shared.m_skillType == Skills.SkillType.Bows)
                QueuedBowRelease.Track(__instance, attack);
        }
    }

    // A release held back above fires a little late; blocking, switching weapons or dying before it fires cancels it, so it
    // spends no arrow and launches nothing.
    [HarmonyPatch]
    internal static class QueuedBowRelease
    {
        private static Player queuedPlayer;
        private static Attack queuedAttack;

        internal static void Track(Player player, Attack attack)
        {
            queuedPlayer = player;
            queuedAttack = attack;
        }

        internal static void Forget(Attack attack)
        {
            if (attack == queuedAttack) Clear();
        }

        private static void Clear()
        {
            queuedPlayer = null;
            queuedAttack = null;
        }

        private static void Check()
        {
            if (queuedAttack == null) return;
            if (queuedPlayer && queuedPlayer == Player.m_localPlayer && !queuedPlayer.IsDead() &&
                Bows.CurrentAttack(queuedPlayer) == queuedAttack && queuedPlayer.GetCurrentWeapon() == queuedAttack.GetWeapon() &&
                !Bows.Blocking(queuedPlayer)) return;
            CancelPending();
        }

        private static void CancelPending()
        {
            if (queuedAttack == null) return;
            AttackCancel.MarkCancelled(queuedAttack);
            queuedAttack.Abort();
            queuedAttack.Stop();
            Clear();
        }

        // Called from the attack-cancel hook on Attack.OnAttackTrigger, before the attack's hit or shot.
        internal static void BeforeTrigger(Attack attack)
        {
            Check();
            if (attack == queuedAttack) Clear();
        }

        [HarmonyPatch(typeof(ZNet), "Shutdown"), HarmonyPrefix]
        private static void Leaving() => CancelPending();

        [HarmonyPatch(typeof(Player), nameof(Player.SetControls)), HarmonyPostfix]
        private static void ControlsUpdated(Player __instance)
        {
            if (__instance == queuedPlayer || queuedPlayer != Player.m_localPlayer) Check();
        }
    }

    // ------------------------------------------------------------------ Epic Loot's Triple Shot
    // Epic Loot's Triple Shot runs its own prefix on the attack's hit; an attack abandoned by blocking must not fire a volley.
    // Found by name, so PotatoFighter needs no Epic Loot to build or load; without Epic Loot it does nothing.
    [HarmonyPatch]
    internal static class TripleShotCancel
    {
        private static MethodBase target;

        private static bool Prepare()
        {
            var type = AccessTools.TypeByName("EpicLoot.MagicItemEffects.MultiShot");
            target = type == null ? null : AccessTools.Method(type, "Attack_OnAttackTrigger_Prefix");
            return target != null;
        }

        private static MethodBase TargetMethod() => target;

        [HarmonyPriority(Priority.First)]
        private static bool Prefix(Attack __0) => !AttackCancel.IsCancelled(__0);
    }
}
