// PotatoFighter - combat pieces and weapons for Valheim. Weapon recall is written from scratch; attack cancel and the bow
// fixes moved here from Potato Tweaks (adapted from EpicLoot Progression Fix, MIT; see LICENSE).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Jotunn.Utils;
using UnityEngine;

namespace PotatoFighter
{
    // Pieces will come later (they must be on the server and every player), so no process filter: dedicated servers
    // load it too, and server settings sync to players.
    [BepInPlugin(Id, "PotatoFighter", Version)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Id = "potato.fighter", Version = "1.0.0";
        // Test builds let every thrown weapon be recalled (the game's spears, modded javelins and throwing axes) so the
        // recall can be tried before PotatoFighter has weapons of its own. Release builds recall only OwnWeapons.
        internal const bool TestBuild = false;
        // Prefab names of PotatoFighter's own returning weapons; each weapon adds itself when it's made (KrakenAxe.Create).
        internal static readonly System.Collections.Generic.HashSet<string> OwnWeapons = new System.Collections.Generic.HashSet<string>();
        // Two sections, so Configuration Manager stays short; settings sharing one start with what they belong to.
        // No apostrophes or = \ " [ ] in names: BepInEx rejects them.
        private const string WeaponSection = "1 - Weapons";
        private const string CombatSection = "2 - Combat";

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> RecallEnabled, RecallPin, Beam, AttackCancelEnabled, KeepFastShots, FastSwapEnabled, FastSwapSprint, FastSwapAttack;
        internal static ConfigEntry<int> FastSwapStamina;
        internal static ConfigEntry<Color> BeamColor;
        internal static ConfigEntry<int> ExplosionSize;
        internal static ConfigEntry<bool> RecallStamina;
        internal const float RecallStaminaCost = 15f;   // at skill 0; less with skill, free at 100

        private void Awake()
        {
            Log = Logger;
            var serverSet = new ConfigurationManagerAttributes { IsAdminOnly = true };
            RecallEnabled = Config.Bind(WeaponSection, "Weapon recall", true,
                new ConfigDescription("After a throw, the throw button (middle mouse, or the gamepad's) calls a thrown weapon back to the hotbar slot it came from, while the hand it was thrown from is empty. Set by the server.", null, serverSet));
            RecallStamina = Config.Bind(WeaponSection, "Recall uses stamina", true,
                new ConfigDescription("Calling a weapon back costs stamina: 15 at skill 0 in the weapon's own skill (Axes for an axe), less as the skill grows, nothing at 100. Off: free. Set by the server.", null, serverSet));
            RecallPin = Config.Bind(WeaponSection, "Recall map pin", true, "Mark a thrown weapon on the map until it's back in your hands.");
            Beam = Config.Bind(WeaponSection, "Light beam", true, "Show a beam of light over a thrown weapon lying on the ground.");
            BeamColor = Config.Bind(WeaponSection, "Light beam color", new Color(1f, 0.85f, 0.45f, 0.85f),
                "The beam's colour. Its alpha is how strong the beam is at the bottom; it fades out towards the top.");
            bool hadCancel = File.Exists(Config.ConfigFilePath) && File.ReadAllText(Config.ConfigFilePath).Contains("[" + CombatSection + "]");
            AttackCancelEnabled = Config.Bind(CombatSection, "Attack cancel", true,
                new ConfigDescription("Pressing block during an attack's windup or recovery abandons the attack and raises the guard at once, so you can parry mid-swing. A hit the swing hasn't reached never lands; stamina already paid and a hit already dealt stay. Cancelling never lets you attack faster. Set by the server.", null, serverSet));
            KeepFastShots = Config.Bind(CombatSection, "Keep fast bow shots", true,
                "With a fast draw (high Bows skill, Quick Draw), letting go of a fully drawn arrow before the last shot's animation ends no longer loses the shot: the arrow stays drawn and flies as soon as the bow can fire. Blocking, switching weapons or dying before then cancels it without using an arrow.");
            FastSwapEnabled = Config.Bind(CombatSection, "Fast swap", true,
                new ConfigDescription("Switching weapons, shields, torches and tools, and sheathing or drawing with R, no longer slows you from a run to a walk. Armor keeps its normal equip time. Off by itself when Skarif's FastSwap is installed. Set by the server.", null, serverSet));
            FastSwapSprint = Config.Bind(CombatSection, "Fast swap while sprinting", false,
                new ConfigDescription("A weapon switch made while sprinting goes ahead instead of being cancelled by the sprint, for the stamina in Fast swap stamina while sprinting. Needs Fast swap. Set by the server.", null, serverSet));
            FastSwapStamina = Config.Bind(CombatSection, "Fast swap stamina while sprinting", 10,
                new ConfigDescription("Stamina a weapon switch or sheathing costs while sprinting on the ground, with Fast swap while sprinting on. Without that much stamina, the switch doesn't happen. 0: free. Set by the server.", new AcceptableValueRange<int>(0, 50), serverSet));
            FastSwapAttack = Config.Bind(CombatSection, "Attack cuts a swap short", false,
                new ConfigDescription("Attacking (or drawing a bow) during a weapon switch finishes the switch at once and goes ahead with the new weapon, instead of waiting for it. Needs Fast swap. Set by the server.", null, serverSet));
            ExplosionSize = Config.Bind(WeaponSection, "Explosion size", 100,
                new ConfigDescription("The size of every PotatoFighter weapon's explosions, in percent of its own size (the Kraken Axe's ice explosion: 2.9 m at 100). Both the reach of the damage and the effect grow and shrink with it. Set by the server.", new AcceptableValueRange<int>(50, 200), serverSet));
            if (!hadCancel) CarryOverFromTweaks();

            harmony = new Harmony(Id);
            var nested = typeof(Recall).GetNestedTypes(AccessTools.all).Concat(typeof(Flight).GetNestedTypes(AccessTools.all))
                .Concat(typeof(AttackCancel).GetNestedTypes(AccessTools.all)).Concat(typeof(FastSwap).GetNestedTypes(AccessTools.all)).Concat(typeof(KrakenAxe).GetNestedTypes(AccessTools.all));
            foreach (var type in nested) Patch(type);
            foreach (var type in new[] { typeof(Probe), typeof(BowRelease), typeof(QueuedBowRelease), typeof(TripleShotCancel) }) Patch(type);
            gameObject.AddComponent<Recall>();
            Assets.Dir = Path.Combine(Path.GetDirectoryName(Info.Location), "assets");
            KrakenAxe.Register();
        }

        private Harmony harmony;

        // One feature at a time, so a patch that no longer fits the game turns off only its own feature.
        private void Patch(Type type)
        {
            if (!type.GetCustomAttributes(typeof(HarmonyPatch), true).Any()) return;
            try { harmony.CreateClassProcessor(type).Patch(); }
            catch (Exception e) { Log.LogError($"{type.Name} is disabled: {e.GetBaseException().Message}"); }
        }

        // Attack cancel and the bow fix came from Potato Tweaks (up to 1.5.16): the first time, take the player's choices
        // from its settings file. Potato Tweaks 1.5.17 then drops its old attack-cancel line.
        private void CarryOverFromTweaks()
        {
            var tweaks = Read(Path.Combine(BepInEx.Paths.ConfigPath, "potato.tweaks.cfg"));
            bool changed = false;
            if (tweaks.TryGetValue("8 - Attack cancel|Enabled", out var cancel) && bool.TryParse(cancel, out bool on)) { AttackCancelEnabled.Value = on; changed = true; }
            if ((tweaks.TryGetValue("10 - Bug fixes|Game bug fixes", out var fixes) || tweaks.TryGetValue("1 - General|Game bug fixes", out fixes)) && bool.TryParse(fixes, out bool fix)) { KeepFastShots.Value = fix; changed = true; }
            if (changed)
            {
                Config.Save();
                Log.LogInfo($"Attack cancel ({AttackCancelEnabled.Value}) and keeping fast bow shots ({KeepFastShots.Value}) carried over from Potato Tweaks' settings.");
            }
        }

        // "section|key" -> value from a BepInEx .cfg file; empty when it doesn't exist.
        private static Dictionary<string, string> Read(string path)
        {
            var values = new Dictionary<string, string>();
            try
            {
                if (!File.Exists(path)) return values;
                string section = "";
                foreach (var raw in File.ReadAllLines(path))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    if (line.StartsWith("[") && line.EndsWith("]")) { section = line.Substring(1, line.Length - 2); continue; }
                    int equals = line.IndexOf('=');
                    if (equals > 0) values[section + "|" + line.Substring(0, equals).Trim()] = line.Substring(equals + 1).Trim();
                }
            }
            catch (Exception e) { Log.LogWarning($"Couldn't read {Path.GetFileName(path)}: {e.Message}"); }
            return values;
        }
    }
}
