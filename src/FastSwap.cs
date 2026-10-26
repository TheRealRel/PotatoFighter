// PotatoFighter - fast swap: changing hand weapons without being slowed to a walk, on the run, and cut short by an attack.
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
        private const float SheathSeconds = 0.9f;      // the equip_hip animation, about
        private const float InterruptSeconds = 1f;     // longest wait for the animator to leave the cut-short state

        private static readonly AccessTools.FieldRef<Player, List<Player.MinorActionData>> Queue =
            AccessTools.FieldRefAccess<Player, List<Player.MinorActionData>>("m_actionQueue");
        private static readonly AccessTools.FieldRef<Player, string> ActionAnimation = AccessTools.FieldRefAccess<Player, string>("m_actionAnimation");
        private static readonly AccessTools.FieldRef<Character, ZSyncAnimation> Zanim = AccessTools.FieldRefAccess<Character, ZSyncAnimation>("m_zanim");

        private static float sheathUntil = -1f, interruptUntil = -1f;

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

        // The minor action playing now is a hand switch or the sheathing animation.
        private static bool Swapping(Player player)
        {
            if (Time.time < sheathUntil) return true;
            var queue = Queue(player);
            return queue != null && queue.Count > 0 && HandAction(queue[0]);
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
                if (__result && On(__instance) && Swapping(__instance)) __result = false;
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
                if (player == null || !On(player))
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

        // A switch started on the run pays its stamina (or is refused).
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
                if (!On(__instance) || !HandItem(item) || !__instance.IsRunning()) return;
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
                sheathUntil = Time.time + SheathSeconds;
                if (player.IsRunning()) PaySprint(player);
            }
        }

        // An attack cuts a hand switch short: the switch finishes at once and the attack goes ahead with the new weapon.
        internal static void Interrupt(Player player)
        {
            if (!On(player) || !Swapping(player)) return;
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
            sheathUntil = -1f;
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
                if (On(__instance) && Bows.AttackHold(__instance) && Swapping(__instance)) Interrupt(__instance);
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
