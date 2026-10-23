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

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> RecallEnabled, RecallPin, RecallBeam;
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
            RecallKey = Config.Bind(RecallSection, "Recall key", new KeyboardShortcut(KeyCode.H),
                "Press to call your thrown weapons back to the hotbar slots they came from.");
            RecallPin = Config.Bind(RecallSection, "Map pin", true, "Mark a thrown weapon on the map until it's back in your hands.");
            RecallBeam = Config.Bind(RecallSection, "Light beam", true, "Show a beam of light over a thrown weapon lying on the ground.");
            new Harmony(Id).PatchAll(typeof(Plugin).Assembly);
            gameObject.AddComponent<Recall>();
        }
    }
}
