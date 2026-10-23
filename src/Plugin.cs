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
        private const string RecallSection = "1 - Weapon recall"; // No apostrophes: BepInEx rejects them in names.
        private const string BeamSection = "2 - Light beam";

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> RecallEnabled, RecallPin, RecallWithThrow, Beam;
        internal static ConfigEntry<Color> BeamColor;
        internal static ConfigEntry<KeyboardShortcut> RecallKey;
        internal static ConfigEntry<float> RecallStamina;

        private void Awake()
        {
            Log = Logger;
            var serverSet = new ConfigurationManagerAttributes { IsAdminOnly = true };
            RecallEnabled = Config.Bind(RecallSection, "Enabled", true,
                new ConfigDescription("Thrown weapons (spears, and modded javelins and throwing axes) can be called back to your hand. Set by the server.", null, serverSet));
            RecallStamina = Config.Bind(RecallSection, "Stamina cost", 15f,
                new ConfigDescription("Stamina used for each weapon called back. 0 = free. Set by the server.", new AcceptableValueRange<float>(0f, 100f), serverSet));
            RecallWithThrow = Config.Bind(RecallSection, "Recall with the throw button", true,
                "With your hands empty after a throw, the throw button (middle mouse, or the gamepad's) calls your thrown weapons back to the hotbar slots they came from.");
            RecallKey = Config.Bind(RecallSection, "Recall key", KeyboardShortcut.Empty,
                "An extra key that calls your thrown weapons back, whatever is in your hands. None by default.");
            RecallPin = Config.Bind(RecallSection, "Map pin", true, "Mark a thrown weapon on the map until it's back in your hands.");
            Beam = Config.Bind(BeamSection, "Light beam", true, "Show a beam of light over a thrown weapon lying on the ground.");
            BeamColor = Config.Bind(BeamSection, "Color", new Color(1f, 0.85f, 0.45f, 0.85f),
                "The beam's colour. Its alpha is how strong the beam is at the bottom; it fades out towards the top.");
            new Harmony(Id).PatchAll(typeof(Plugin).Assembly);
            gameObject.AddComponent<Recall>();
        }
    }
}
