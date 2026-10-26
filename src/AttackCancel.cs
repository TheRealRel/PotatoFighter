// PotatoFighter - cancel an attack into a block (moved here from Potato Tweaks 1.5.16).
// Adapted from EpicLoot Progression Fix 0.1.281 (attack-cancel.cs, queued-bow-cancellation.cs), MIT License,
// Copyright (c) 2026 EpicLoot Progression Fix contributors. See LICENSE.
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace PotatoFighter
{
    // Raising the guard during an attack's windup or recovery abandons the attack, so a player can parry mid-swing. The swing
    // ends where it is: a hit it has not reached never lands, while paid stamina and a hit already dealt stay. Its animation
    // blends into the guard for everyone and the combo starts over. After a hit, the next attack waits until the abandoned
    // animation would have ended, so cancelling never attacks faster; before one, it waits only for the blend. A held attack
    // button is ignored until it is released, so the guard stays up.
    internal static class AttackCancel
    {
        private const float BlendSeconds = .1f;
        // The blend and the frame before it begins, after which the abandoned animation's events can no longer reach a new swing.
        private const float WindupWait = 2f * BlendSeconds;
        private const string Rpc = "PotatoFighter_AttackCancel";
        private static readonly int AttackTag = ZSyncAnimation.GetHash("attack");
        private static readonly int Movement = Animator.StringToHash("Base Layer.Movement");
        private static readonly int UpperBodyIdle = Animator.StringToHash("upperbody.None");
        private static readonly AccessTools.FieldRef<Character, Animator> CharacterAnimator = AccessTools.FieldRefAccess<Character, Animator>("m_animator");
        private static readonly AccessTools.FieldRef<Character, bool> Blocking = AccessTools.FieldRefAccess<Character, bool>("m_blocking");
        private static readonly AccessTools.FieldRef<Character, bool> AttackHold = AccessTools.FieldRefAccess<Character, bool>("m_attackHold");
        private static readonly AccessTools.FieldRef<Character, bool> SecondaryAttackHold = AccessTools.FieldRefAccess<Character, bool>("m_secondaryAttackHold");
        private static readonly AccessTools.FieldRef<Humanoid, Attack> CurrentAttack = AccessTools.FieldRefAccess<Humanoid, Attack>("m_currentAttack");
        private static readonly AccessTools.FieldRef<Humanoid, Attack> PreviousAttack = AccessTools.FieldRefAccess<Humanoid, Attack>("m_previousAttack");
        private static readonly AccessTools.FieldRef<Player, float> QueuedAttack = AccessTools.FieldRefAccess<Player, float>("m_queuedAttackTimer");
        private static readonly AccessTools.FieldRef<Player, float> QueuedSecondAttack = AccessTools.FieldRefAccess<Player, float>("m_queuedSecondAttackTimer");
        private static readonly AccessTools.FieldRef<Attack, float> AttackTime = AccessTools.FieldRefAccess<Attack, float>("m_time");
        private static readonly ConditionalWeakTable<Attack, object> Landed = new ConditionalWeakTable<Attack, object>();
        // An abandoned attack may still receive a delayed animation callback after it stops being current.
        private static readonly ConditionalWeakTable<Attack, object> Cancelled = new ConditionalWeakTable<Attack, object>();
        private static readonly object Marker = new object();
        internal static float AttackAllowedAt;
        private static bool holdIgnored;

        internal static bool IsCancelled(Attack attack) => attack != null && Cancelled.TryGetValue(attack, out _);

        internal static void MarkCancelled(Attack attack)
        {
            if (attack != null && !Cancelled.TryGetValue(attack, out _)) Cancelled.Add(attack, Marker);
        }

        // The guard's own conditions apart from the attack it replaces.
        private static bool CanCancel(Player player) => CurrentAttack(player) != null && player.InAttack() && !player.IsDead() &&
            !player.InDodge() && !player.InPlaceMode() && !player.IsEncumbered() && !player.InMinorAction() && !player.IsStaggering();

        private static void Cancel(Player player)
        {
            var attack = CurrentAttack(player);
            attack.Stop();
            MarkCancelled(attack);
            QueuedBowRelease.Forget(attack);
            PreviousAttack(player) = null;
            CurrentAttack(player) = null;
            QueuedAttack(player) = 0f;
            QueuedSecondAttack(player) = 0f;
            AttackAllowedAt = Time.time + (Landed.TryGetValue(attack, out _) ? Remaining(CharacterAnimator(player), AttackTime(attack)) : WindupWait);
            player.GetComponent<ZNetView>().InvokeRPC(ZNetView.Everybody, Rpc, attack.m_attackAnimation ?? "");
        }

        // Seconds until the attack animations playing now would end, at the pace they have kept since the attack began, at
        // least the windup's wait. Animations change their own speed as they play, so their current speed tells less.
        private static float Remaining(Animator animator, float elapsed)
        {
            float remaining = WindupWait;
            if (!animator) return remaining;
            for (int layer = 0; layer < animator.layerCount; layer++)
            {
                var state = State(animator, layer);
                float progress = Mathf.Clamp01(state.normalizedTime);
                if (state.tagHash == AttackTag && progress > 0f) remaining = Mathf.Max(remaining, (1f - progress) * elapsed / progress);
            }
            return remaining;
        }

        private static AnimatorStateInfo State(Animator animator, int layer) =>
            animator.IsInTransition(layer) ? animator.GetNextAnimatorStateInfo(layer) : animator.GetCurrentAnimatorStateInfo(layer);

        // Runs on every game. A trigger the attack set that has not played yet would restart it, so it is dropped too.
        private static void Interrupt(Player player, string animation)
        {
            var animator = CharacterAnimator(player);
            if (!animator) return;
            if (!string.IsNullOrEmpty(animation))
                foreach (var parameter in animator.parameters.Where(parameter => parameter.type == AnimatorControllerParameterType.Trigger && parameter.name.StartsWith(animation)))
                    animator.ResetTrigger(parameter.nameHash);
            Blend(animator, 0, Movement);
            int upperBody = animator.GetLayerIndex("upperbody");
            if (upperBody > 0) Blend(animator, upperBody, UpperBodyIdle);
        }

        private static void Blend(Animator animator, int layer, int state)
        {
            if (State(animator, layer).tagHash == AttackTag && animator.HasState(layer, state)) animator.CrossFadeInFixedTime(state, BlendSeconds, layer);
        }

        // An abandoned attack never reaches its hit, even from a late animation event. Runs before other mods' prefixes.
        [HarmonyPatch(typeof(Attack), nameof(Attack.OnAttackTrigger))]
        internal static class SkipCancelled
        {
            [HarmonyPriority(Priority.First)]
            private static bool Prefix(Attack __instance)
            {
                QueuedBowRelease.BeforeTrigger(__instance);
                return !IsCancelled(__instance);
            }

            private static void Postfix(Attack __instance, bool __runOriginal)
            {
                if (__runOriginal && !Landed.TryGetValue(__instance, out _)) Landed.Add(__instance, Marker);
            }
        }

        [HarmonyPatch(typeof(Player), "Awake")]
        internal static class Register
        {
            private static void Postfix(Player __instance)
            {
                var view = __instance.GetComponent<ZNetView>();
                if (view != null) view.Register<string>(Rpc, (sender, animation) => Interrupt(__instance, animation));
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.SetControls))]
        internal static class Controls
        {
            private static void Prefix(Player __instance, out bool __state) => __state = Blocking(__instance);

            private static void Postfix(Player __instance, bool __state)
            {
                if (__instance != Player.m_localPlayer) return;
                if (Plugin.AttackCancelEnabled.Value && !__state && Blocking(__instance) && CanCancel(__instance))
                {
                    Cancel(__instance);
                    holdIgnored = true;
                }
                if (!holdIgnored) return;
                if (!AttackHold(__instance) && !SecondaryAttackHold(__instance)) holdIgnored = false;
                AttackHold(__instance) = false;
                SecondaryAttackHold(__instance) = false;
            }
        }

        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.StartAttack))]
        internal static class Waiting
        {
            private static bool Prefix(Humanoid __instance, ref bool __result)
            {
                if (__instance != Player.m_localPlayer || Time.time >= AttackAllowedAt) return true;
                __result = false;
                return false;
            }
        }
    }
}
