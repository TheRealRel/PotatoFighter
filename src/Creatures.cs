// PotatoFighter - creature levels (Valheim), written from scratch to replace StarLevelSystem and Creature Level & Loot
// Control with something small. Fresh spawns roll stars from the creature's biome and the world's boss progress, up to
// Max stars; a few get one modifier. The game itself already gives health (+100% a star) and damage (+50% a star) at any
// level and passes a parent's level to its young, so none of that is redone here. Steps aside when StarLevelSystem or
// CLLC is installed.
using System;
using System.Collections.Generic;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;

namespace PotatoFighter
{
    internal static class Creatures
    {
        internal const string SlsId = "MidnightsFX.StarLevelSystem", CllcId = "org.bepinex.plugins.creaturelevelcontrol";
        internal static bool Diagnostics = true;   // test builds: one log line per roll, re-roll and split

        // ZDO keys: the modifier (0 none), and "rolled" so a creature is never rolled twice.
        internal static readonly int ModKey = "PotatoFighter_Modifier".GetStableHashCode();
        private static readonly int RolledKey = "PotatoFighter_Rolled".GetStableHashCode();
        private static readonly int BountyKey = "BountyID".GetStableHashCode();   // Epic Loot's bounty creatures
        private const float FreshSeconds = 15f, OwnerWait = 10f;

        internal enum Modifier { None, Armored, Quick, Fierce, Regenerating, Splitting }
        internal static readonly string[] Names = { "", "Armored", "Quick", "Fierce", "Regenerating", "Splitting" };
        private const float ArmoredMaxBaseHealth = 800f;   // big creatures (Lox, Serpent and the like) never get Armored

        private static readonly AccessTools.FieldRef<Character, ZNetView> View = AccessTools.FieldRefAccess<Character, ZNetView>("m_nview");
        private static readonly List<KeyValuePair<Character, float>> waiting = new List<KeyValuePair<Character, float>>();
        private static Dictionary<string, bool> canHaveStars;

        internal static bool StandAside => Chainloader.PluginInfos.ContainsKey(SlsId) || Chainloader.PluginInfos.ContainsKey(CllcId);
        internal static bool On => Plugin.CreatureLevels.Value && !StandAside;
        internal static int MaxLevel => 1 + Plugin.CreatureMaxStars.Value;

        internal static ZDO Zdo(Character c) => c ? View(c)?.GetZDO() : null;
        internal static Modifier Get(Character c) => (Modifier)(Zdo(c)?.GetInt(ModKey) ?? 0);

        // ------------------------------------------------------------------ rolls
        // Character.Awake reads the saved level; spawners set theirs right after Instantiate. Look a frame later.
        [HarmonyPatch(typeof(Character), "Awake")]
        internal static class Loaded
        {
            private static void Postfix(Character __instance)
            {
                if (!__instance.IsPlayer() && __instance.GetComponent<BaseAI>())
                    waiting.Add(new KeyValuePair<Character, float>(__instance, Time.time + OwnerWait));
            }
        }

        // Called every frame by Ticker. Only the owner sets levels; a creature loaded while someone else owns it waits a
        // few seconds for ownership to come over.
        private static void CheckWaiting()
        {
            for (int i = waiting.Count - 1, done = 0; i >= 0 && done < 20; i--)
            {
                var c = waiting[i].Key;
                var view = c ? View(c) : null;
                if (view == null || !view.IsValid() || !On) { waiting.RemoveAt(i); continue; }
                if (!view.IsOwner())
                {
                    if (Time.time > waiting[i].Value) waiting.RemoveAt(i);
                    continue;
                }
                waiting.RemoveAt(i);
                done++;
                Check(c, view.GetZDO());
            }
        }

        private static void Check(Character c, ZDO zdo)
        {
            if (zdo.GetBool(RolledKey) || c.IsTamed() || c.IsBoss() || c.m_faction == Character.Faction.Players || zdo.GetString(BountyKey).Length > 0) return;
            var ai = c.GetComponent<BaseAI>();
            int level = c.GetLevel();
            bool fresh = ai && ai.GetTimeSinceSpawned().TotalSeconds < FreshSeconds;
            if (!fresh && level <= MaxLevel) return;   // loaded from the save: left as it is
            int rolled = 1 + RollStars(c);
            int now = fresh ? Mathf.Min(Mathf.Max(level, rolled), MaxLevel) : rolled;
            if (now != level) c.SetLevel(now);
            zdo.Set(RolledKey, true);
            if (fresh && now > 1 && zdo.GetInt(ModKey) == 0) Assign(c, RollModifier(c, now - 1));
            if (Diagnostics)
                Plugin.Log.LogInfo($"[Levels] {Utils.GetPrefabName(c.gameObject)} {(fresh ? "spawned" : "over the cap")}: {Stars(level)} -> {Stars(now)}{(Get(c) != Modifier.None ? ", " + Get(c) : "")} (world level {WorldLevel(c.transform.position)}{(CanHaveStars(c) ? "" : ", no stars in vanilla")})");
        }

        private static string Stars(int level) => level - 1 == 1 ? "1 star" : $"{level - 1} stars";

        // How far the world has come: the furthest boss defeated (0 Eikthyr ... 6 Fader), -1 for none.
        private static readonly string[] Bosses = { "defeated_eikthyr", "defeated_gdking", "defeated_bonemass", "defeated_dragon", "defeated_goblinking", "defeated_queen", "defeated_fader" };

        private static int Progress()
        {
            for (int i = Bosses.Length - 1; i >= 0; i--)
                if ((ZoneSystem.instance && ZoneSystem.instance.GetGlobalKey(Bosses[i])) || (Player.m_localPlayer && Player.m_localPlayer.HaveUniqueKey(Bosses[i]))) return i;
            return -1;
        }

        private static int BiomeIndex(Heightmap.Biome biome)
        {
            switch (biome)
            {
                case Heightmap.Biome.Meadows: return 0;
                case Heightmap.Biome.BlackForest: return 1;
                case Heightmap.Biome.Swamp: return 2;
                case Heightmap.Biome.Plains: return 4;
                case Heightmap.Biome.Mistlands: return 5;
                case Heightmap.Biome.AshLands: return 6;
                case Heightmap.Biome.DeepNorth: return 7;
                default: return 3;   // Mountain, Ocean
            }
        }

        // The biome you're pushing into (the one after the last boss) is world level 1; every biome behind it one more,
        // up to 6. Biomes further ahead are 1 as well: they're hard enough already.
        internal static int WorldLevel(Vector3 position)
        {
            int behind = Progress() + 1 - BiomeIndex(Heightmap.FindBiome(position));
            return Mathf.Clamp(1 + behind, 1, 6);
        }

        // First star 8% at world level 1, +4% a level; each further star 30% of the time. Creatures the game never gives
        // stars roll two world levels lower (none until world level 3). Star chance scales both.
        private static int RollStars(Character c)
        {
            int wl = WorldLevel(c.transform.position) - (CanHaveStars(c) ? 0 : 2);
            if (wl < 1) return 0;
            float scale = Plugin.CreatureStarChance.Value / 100f;
            float first = Mathf.Min(0.95f, (0.08f + 0.04f * (wl - 1)) * scale), next = Mathf.Min(0.95f, 0.3f * scale);
            int stars = 0;
            while (stars < Plugin.CreatureMaxStars.Value && UnityEngine.Random.value < (stars == 0 ? first : next)) stars++;
            return stars;
        }

        // Like CLLC: a creature the game's spawn lists never give stars counts as one that can't have them. Creatures not in
        // the lists at all (camps, dungeons) can.
        private static bool CanHaveStars(Character c)
        {
            if (canHaveStars == null)
            {
                canHaveStars = new Dictionary<string, bool>();
                var lists = ZoneSystem.instance && ZoneSystem.instance.m_zoneCtrlPrefab ? ZoneSystem.instance.m_zoneCtrlPrefab.GetComponent<SpawnSystem>()?.m_spawnLists : null;
                if (lists != null)
                    foreach (var list in lists)
                        if (list != null)
                            foreach (var data in list.m_spawners)
                                if (data?.m_prefab != null)
                                {
                                    canHaveStars.TryGetValue(data.m_prefab.name, out bool any);
                                    canHaveStars[data.m_prefab.name] = any || data.m_maxLevel > 1;
                                }
            }
            return !canHaveStars.TryGetValue(Utils.GetPrefabName(c.gameObject), out bool stars) || stars;
        }

        // ------------------------------------------------------------------ modifiers
        // 10% + 5% a star (15% at 1 star, 35% at 5), scaled by Modifier chance; one at most.
        private static Modifier RollModifier(Character c, int stars)
        {
            if (UnityEngine.Random.value >= (0.10f + 0.05f * stars) * Plugin.CreatureModifierChance.Value / 100f) return Modifier.None;
            var options = new List<Modifier> { Modifier.Quick, Modifier.Fierce, Modifier.Regenerating, Modifier.Splitting };
            if (c.m_health < ArmoredMaxBaseHealth) options.Add(Modifier.Armored);
            return options[UnityEngine.Random.Range(0, options.Count)];
        }

        internal static void Assign(Character c, Modifier modifier)
        {
            var zdo = Zdo(c);
            if (zdo == null || modifier == Modifier.None) return;
            zdo.Set(ModKey, (int)modifier);
            Apply(c);
        }

        // Speeds are set on the creature itself, once per loaded copy; damage, healing and splitting read the ZDO.
        private static readonly HashSet<int> applied = new HashSet<int>();

        private static void Apply(Character c)
        {
            var modifier = Get(c);
            if (modifier == Modifier.None || !applied.Add(c.GetInstanceID())) return;
            float speed = modifier == Modifier.Quick ? 1.35f : modifier == Modifier.Armored ? 0.75f : 1f;
            if (speed == 1f) return;
            c.m_speed *= speed; c.m_walkSpeed *= speed; c.m_runSpeed *= speed; c.m_crouchSpeed *= speed;
            c.m_swimSpeed *= speed; c.m_flySlowSpeed *= speed; c.m_flyFastSpeed *= speed;
        }

        // Armored takes half damage; a Fierce attacker hits 30% harder. On the owner, where damage is applied.
        [HarmonyPatch(typeof(Character), "RPC_Damage")]
        internal static class Damage
        {
            private static void Prefix(Character __instance, HitData hit)
            {
                if (!On || hit == null) return;
                if (Get(__instance) == Modifier.Armored) hit.ApplyModifier(0.5f);
                var attacker = hit.GetAttacker();
                if (attacker && Get(attacker) == Modifier.Fierce) hit.ApplyModifier(1.3f);
            }
        }

        // Splitting: on death, two of the same creature with half its stars (rounded down) take its place. Each keeps
        // Splitting, so a 4 star splits into 2 stars, then 1, then 0, which can't split.
        [HarmonyPatch(typeof(Character), nameof(Character.OnDeath))]
        internal static class Split
        {
            private static void Prefix(Character __instance)
            {
                if (!On || Get(__instance) != Modifier.Splitting || __instance.GetLevel() < 2 || !View(__instance).IsOwner()) return;
                var prefab = ZNetScene.instance?.GetPrefab(Utils.GetPrefabName(__instance.gameObject));
                if (!prefab) return;
                int level = 1 + (__instance.GetLevel() - 1) / 2;
                bool tamed = __instance.IsTamed();
                for (int i = 0; i < 2; i++)
                {
                    var offset = Quaternion.Euler(0f, i * 180f + UnityEngine.Random.Range(-40f, 40f), 0f) * Vector3.forward * 0.8f;
                    var copy = UnityEngine.Object.Instantiate(prefab, __instance.transform.position + offset + Vector3.up * 0.3f, __instance.transform.rotation).GetComponent<Character>();
                    if (!copy) continue;
                    Zdo(copy)?.Set(RolledKey, true);
                    if (tamed) copy.SetTamed(true);
                    copy.SetLevel(level);
                    Assign(copy, Modifier.Splitting);
                }
                if (Diagnostics) Plugin.Log.LogInfo($"[Levels] {Utils.GetPrefabName(__instance.gameObject)} split into two with {Stars(level)}");
            }
        }

        // ------------------------------------------------------------------ offspring
        // The game gives a newborn its parent's level (Procreation), a grown-up its youngster's (Growup) and a hatchling
        // its egg's quality, each with SetLevel right after making it. While one of those runs, SetLevel on the new
        // creature takes the modifier along: a newborn keeps its parent's at Offspring modifier chance, a grown-up always
        // keeps its own. Eggs can't carry one (their stacks merge), so hatchlings get stars only.
        private static Character carryFrom;
        private static bool carryAlways;

        [HarmonyPatch(typeof(Procreation), "Procreate")]
        internal static class Birth
        {
            private static readonly AccessTools.FieldRef<Procreation, Character> Parent = AccessTools.FieldRefAccess<Procreation, Character>("m_character");
            private static void Prefix(Procreation __instance) { carryFrom = Parent(__instance); carryAlways = false; }
            private static void Finalizer() => carryFrom = null;
        }

        [HarmonyPatch(typeof(Growup), "GrowUpdate")]
        internal static class GrowUp
        {
            private static void Prefix(Growup __instance) { carryFrom = __instance.GetComponent<Character>(); carryAlways = true; }
            private static void Finalizer() => carryFrom = null;
        }

        [HarmonyPatch(typeof(Character), nameof(Character.SetLevel))]
        internal static class Carry
        {
            private static void Postfix(Character __instance)
            {
                if (!On || carryFrom == null || __instance == carryFrom) return;
                var from = carryFrom;
                carryFrom = null;   // one new creature per call
                Zdo(__instance)?.Set(RolledKey, true);
                var modifier = Get(from);
                if (modifier == Modifier.None || modifier == Modifier.Armored && __instance.m_health >= ArmoredMaxBaseHealth) return;
                if (!carryAlways && UnityEngine.Random.value * 100f >= Plugin.OffspringModifierChance.Value) return;
                Assign(__instance, modifier);
                if (Diagnostics) Plugin.Log.LogInfo($"[Levels] {Utils.GetPrefabName(__instance.gameObject)} {(carryAlways ? "grew up" : "was born")} {modifier}");
            }
        }

        // ------------------------------------------------------------------ every frame / every second
        internal class Ticker : MonoBehaviour
        {
            private float nextSecond;

            private void Update()
            {
                if (!On) return;
                if (waiting.Count > 0) CheckWaiting();
                if (Time.time < nextSecond) return;
                nextSecond = Time.time + 1f;
                foreach (var c in Character.GetAllCharacters())
                {
                    if (!c || c.IsPlayer()) continue;
                    var modifier = Get(c);
                    if (modifier == Modifier.None) continue;
                    Apply(c);
                    // Regenerating: 1% of its health a second (at least 1), on the owner.
                    if (modifier == Modifier.Regenerating && !c.IsDead() && View(c).IsOwner() && c.GetHealth() < c.GetMaxHealth())
                        c.Heal(Mathf.Max(1f, c.GetMaxHealth() * 0.01f), false);
                }
            }
        }
    }
}
