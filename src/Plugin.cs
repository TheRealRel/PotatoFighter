// PotatoFighter - combat pieces and weapons for Valheim, written from scratch. First feature: weapon recall.
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
        internal const bool TestBuild = true;
        // Prefab names of PotatoFighter's own returning weapons (the chained weapon, the Kraken Axe). None yet.
        internal static readonly System.Collections.Generic.HashSet<string> OwnWeapons = new System.Collections.Generic.HashSet<string>();
        private const string RecallSection = "1 - Weapon recall"; // No apostrophes: BepInEx rejects them in names.
        private const string BeamSection = "2 - Light beam";

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> RecallEnabled, RecallPin, Beam;
        internal static ConfigEntry<Color> BeamColor;
        internal static ConfigEntry<float> RecallStamina;

        private void Awake()
        {
            Log = Logger;
            var serverSet = new ConfigurationManagerAttributes { IsAdminOnly = true };
            RecallEnabled = Config.Bind(RecallSection, "Enabled", true,
                new ConfigDescription("After a throw, the throw button (middle mouse, or the gamepad's) calls a thrown weapon back to the hotbar slot it came from, while the hand it was thrown from is empty. Set by the server.", null, serverSet));
            RecallStamina = Config.Bind(RecallSection, "Stamina cost", 15f,
                new ConfigDescription("Stamina used for each weapon called back. 0 = free. Set by the server.", new AcceptableValueRange<float>(0f, 100f), serverSet));
            RecallPin = Config.Bind(RecallSection, "Map pin", true, "Mark a thrown weapon on the map until it's back in your hands.");
            Beam = Config.Bind(BeamSection, "Light beam", true, "Show a beam of light over a thrown weapon lying on the ground.");
            BeamColor = Config.Bind(BeamSection, "Color", new Color(1f, 0.85f, 0.45f, 0.85f),
                "The beam's colour. Its alpha is how strong the beam is at the bottom; it fades out towards the top.");
            new Harmony(Id).PatchAll(typeof(Plugin).Assembly);
            gameObject.AddComponent<Recall>();
        }
    }
}
