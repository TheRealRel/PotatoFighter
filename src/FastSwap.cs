// PotatoFighter - fast swap: changing hand weapons without being slowed from a run to a walk; optionally also on the
// sprint ("Fast swap while sprinting", off by default) and cut short by an attack ("Attack cuts a swap short", off).
// Written from scratch; the idea comes from FastSwap by Skarif. Steps aside when FastSwap itself is installed.
//
// How the game does it: a weapon, shield, torch or tool has a short equip time (0.2 s for an axe), so selecting it puts an
// "equipping" action in the player's action queue (Player.QueueEquipAction). While that plays, the animator is in a
// minor-action state: Player.InMinorActionSlowdown slows walking to a crawl, StartAttack and the bow draw refuse to start
// (Player.InMinorAction), and starting to sprint clears the whole queue (Player.CheckRun -> ClearActionQueue), so a swap
// made on the run never happens. Sheathing and drawing with R (Humanoid.HideHandItems / ShowHandItems) switch at once but
// play an "equip_hip" animation with the same slowdown.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;

namespace PotatoFighter
{
    internal static class FastSwap
    {
        internal const string OtherModId = "com.skarif.fastswap";
        private const float SwapCap = 2.5f;            // longest a switch or sheathing animation is treated as one
        private const float InterruptSeconds = 1f;     // longest wait for the animator to leave the cut-short state

        private static readonly AccessTools.FieldRef<Player, List<Player.MinorActionData>> Queue =
            AccessTools.FieldRefAccess<Player, List<Player.MinorActionData>>("m_actionQueue");
        private static readonly AccessTools.FieldRef<Player, string> ActionAnimation = AccessTools.FieldRefAccess<Player, string>("m_actionAnimation");
        private static readonly AccessTools.FieldRef<Character, ZSyncAnimation> Zanim = AccessTools.FieldRefAccess<Character, ZSyncAnimation>("m_zanim");

        // A hand switch's queue entry lasts only its equip time (0.2 s for an axe), but the animation it starts, which is what
        // slows walking, plays on well after that. So a switch counts from when it's queued until the animator has entered
        // and left the slowing state (or SwapCap seconds, whichever is first). Sheathing with R starts the same way.
        private static bool swapActive, sawSlowdown;
        private static float swapStarted = -1f, lastSlowdown = -1f, interruptUntil = -1f;
        private const float SettledSeconds = 0.25f;    // no slowdown for this long after it began: the switch is over

        // Test builds only (removed for the release): one log line per switch, how long it slowed and how much we lifted.
        internal const bool Diagnostics = true;
        private static int slowFrames, liftedFrames;
        // Test builds: a slowdown the switch didn't cover, logged once it ends, with the animation clip(s) on the layer the
        // game reads (layer 1, the upper body) so it's clear what slowed and how long after the last switch.
        private static float swapEnded = -1f, outsideStarted = -1f;
        private static int outsideFrames;
        private static string outsideClips;

        private static void OutsideSlowdown(Player player)
        {
            if (outsideFrames++ > 0) return;
            outsideStarted = Time.time;
            outsideClips = Clips(player);
        }

        private static void EndOutsideSlowdown()
        {
            if (outsideFrames == 0) return;
            string after = swapEnded < 0f ? "no switch yet" : $"{outsideStarted - swapEnded:0.00} s after the last switch ended";
            Plugin.Log.LogInfo($"[Swap] slowed outside a switch for {Time.time - outsideStarted:0.00} s ({outsideFrames} frames), {after}; layer 1: {outsideClips}");
            outsideFrames = 0;
        }

        private static string Clips(Player player)
        {
            var animator = player.GetComponentInChildren<UnityEngine.Animator>();
            if (!animator) return "no animator";
            string Names(UnityEngine.AnimatorClipInfo[] infos) => infos == null || infos.Length == 0 ? "-" : string.Join("+", System.Array.ConvertAll(infos, i => i.clip ? i.clip.name : "?"));
            string now = Names(animator.GetCurrentAnimatorClipInfo(1));
            return animator.IsInTransition(1) ? $"{now} -> {Names(animator.GetNextAnimatorClipInfo(1))}" : now;
        }

        private static void StartSwap()
        {
            slowFrames = liftedFrames = 0;
            swapActive = true;
            sawSlowdown = false;
            swapStarted = Time.time;
        }

        private static bool On(Character character) =>
            Plugin.FastSwapEnabled.Value && character && character == Player.m_localPlayer && !Chainloader.PluginInfos.ContainsKey(OtherModId);

        // Weapons, shields, bows, torches and tools; armor, capes and utility items keep the game's own equip time and rules.
        internal static bool HandItem(ItemDrop.ItemData item)
        {
            if (item?.m_shared == null) return false;
            switch (item.m_shared.m_itemType)
            {
                case ItemDrop.ItemData.ItemType.OneHandedWeapon:
                case ItemDrop.ItemData.ItemType.TwoHandedWeapon:
                case ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft:
                case ItemDrop.ItemData.ItemType.Bow:
                case ItemDrop.ItemData.ItemType.Shield:
                case ItemDrop.ItemData.ItemType.Torch:
                case ItemDrop.ItemData.ItemType.Tool:
                    return true;
                default:
                    return false;
            }
        }

        private static bool HandAction(Player.MinorActionData action) =>
            action != null && action.m_type != Player.MinorActionData.ActionType.Reload && HandItem(action.m_item);

        // The minor action playing now is a hand switch or the sheathing animation (an armor change queued first isn't).
        private static bool Swapping(Player player)
        {
            var queue = Queue(player);
            if (queue != null && queue.Count > 0) return HandAction(queue[0]);
            return swapActive && Time.time - swapStarted < SwapCap;
        }

        // On the run on the ground, a swap costs stamina; without enough, it's refused like the game refuses it on the run.
        private static bool PaySprint(Player player)
        {
            if (!player.IsRunning() || !player.IsOnGround()) return true;
            float cost = Plugin.FastSwapStamina.Value;
            if (cost <= 0f) return true;
            if (!player.HaveStamina(cost))
            {
                Hud.instance?.StaminaBarEmptyFlash();
                return false;
            }
            player.UseStamina(cost);
            return true;
        }

        // Walking speed: no slowdown for a hand switch or sheathing.
        [HarmonyPatch(typeof(Player), nameof(Player.InMinorActionSlowdown))]
        internal static class NoSlowdown
        {
            private static void Postfix(Player __instance, ref bool __result)
            {
                if (__instance != Player.m_localPlayer) return;
                if (swapActive)
                {
                    // Over once the animator has been in the slowing state and left it (or never entered it in half a second).
                    // The equip animation can leave the slowing state for a moment between its parts, so the switch is
                    // only over once the slowdown has stayed away for SettledSeconds.
                    if (__result) { sawSlowdown = true; slowFrames++; lastSlowdown = Time.time; }
                    else if (sawSlowdown ? Time.time - lastSlowdown > SettledSeconds : Time.time - swapStarted > 0.5f)
                    {
                        swapActive = false;
                        swapEnded = Time.time;
                        if (Diagnostics) Plugin.Log.LogInfo($"[Swap] over after {Time.time - swapStarted:0.00} s: the game slowed {slowFrames} frames, fast swap lifted {liftedFrames}");
                    }
                }
                if (__result && On(__instance) && Swapping(__instance)) { __result = false; liftedFrames++; }
                else if (__result && Diagnostics) OutsideSlowdown(__instance);
                if (Diagnostics && !__result) EndOutsideSlowdown();
            }
        }

        // Sprinting no longer throws away a queued hand switch; armor and crossbow reloading are still cancelled by it.
        [HarmonyPatch(typeof(Player), "CheckRun")]
        internal static class KeepOnRun
        {
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var clear = AccessTools.Method(typeof(Humanoid), "ClearActionQueue");
                var code = instructions.ToList();
                int found = 0;
                foreach (var instruction in code)
                    if (instruction.Calls(clear))
                    {
                        instruction.opcode = OpCodes.Call;
                        instruction.operand = AccessTools.Method(typeof(KeepOnRun), nameof(ClearForRun));
                        found++;
                    }
                if (found != 1) throw new InvalidOperationException($"expected one action-queue clear in Player.CheckRun, found {found}");
                return code;
            }

            private static void ClearForRun(Humanoid humanoid)
            {
                var player = humanoid as Player;
                if (player == null || !On(player) || !Plugin.FastSwapSprint.Value)
                {
                    Clear(humanoid);
                    return;
                }
                var queue = Queue(player);
                if (queue == null || queue.All(HandAction)) return;
                Clear(humanoid);
            }

            private static readonly MethodInfo ClearMethod = AccessTools.Method(typeof(Humanoid), "ClearActionQueue");
            private static void Clear(Humanoid humanoid) => ClearMethod.Invoke(humanoid, null);
        }

        // A hand switch starts counting here; one started on the run pays its stamina (or is refused).
        [HarmonyPatch]
        internal static class SprintCost
        {
            private static IEnumerable<MethodBase> TargetMethods()
            {
                yield return AccessTools.Method(typeof(Player), "QueueEquipAction");
                yield return AccessTools.Method(typeof(Player), "QueueUnequipAction");
            }

            private static void Postfix(Player __instance, ItemDrop.ItemData item)
            {
                if (!On(__instance) || !HandItem(item)) return;
                StartSwap();
                if (!Plugin.FastSwapSprint.Value || !__instance.IsRunning()) return;
                var queue = Queue(__instance);
                if (queue == null || queue.Count == 0) return;
                var added = queue[queue.Count - 1];
                if (added.m_item != item || PaySprint(__instance)) return;
                queue.Remove(added);
            }
        }

        // Sheathing and drawing with R: no slowdown while its animation plays, and on the run it costs the same stamina.
        [HarmonyPatch]
        internal static class Sheath
        {
            private static IEnumerable<MethodBase> TargetMethods()
            {
                yield return AccessTools.Method(typeof(Humanoid), nameof(Humanoid.HideHandItems));
                yield return AccessTools.Method(typeof(Humanoid), "ShowHandItems");
            }

            private static void Postfix(Humanoid __instance)
            {
                var player = __instance as Player;
                if (!On(player)) return;
                StartSwap();
                if (player.IsRunning() && Plugin.FastSwapSprint.Value) PaySprint(player);
            }
        }

        // An attack cuts a hand switch short: the switch finishes at once and the attack goes ahead with the new weapon.
        internal static void Interrupt(Player player)
        {
            if (!On(player) || !Plugin.FastSwapAttack.Value || !Swapping(player)) return;
            var queue = Queue(player);
            if (queue != null && queue.Count > 0 && HandAction(queue[0]))
            {
                var action = queue[0];
                queue.RemoveAt(0);
                if (action.m_type == Player.MinorActionData.ActionType.Equip) player.EquipItem(action.m_item);
                else player.UnequipItem(action.m_item);
            }
            string animation = ActionAnimation(player);
            if (animation != null)
            {
                Zanim(player)?.SetBool(animation, false);
                ActionAnimation(player) = null;
            }
            swapActive = false;
            interruptUntil = Time.time + InterruptSeconds;
        }

        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.StartAttack))]
        internal static class AttackInterrupts
        {
            private static void Prefix(Humanoid __instance)
            {
                if (__instance is Player player) Interrupt(player);
            }

            private static void Postfix(bool __result)
            {
                if (__result) interruptUntil = -1f;
            }
        }

        // Holding attack with a bow during a switch starts the draw (the game waits for the switch to end).
        [HarmonyPatch(typeof(Player), "UpdateAttackBowDraw")]
        internal static class BowInterrupts
        {
            private static void Prefix(Player __instance)
            {
                if (On(__instance) && Plugin.FastSwapAttack.Value && Bows.AttackHold(__instance) && Swapping(__instance)) Interrupt(__instance);
            }
        }

        // Until the animator has left the cut-short state, the game shouldn't count it as a minor action, or the attack
        // and the bow draw would still be refused.
        [HarmonyPatch(typeof(Player), nameof(Player.InMinorAction))]
        internal static class NotInAction
        {
            private static bool Prefix(Player __instance, ref bool __result)
            {
                if (Time.time >= interruptUntil || !On(__instance)) return true;
                __result = false;
                return false;
            }
        }
    }
}
