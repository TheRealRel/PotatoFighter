// PotatoFighter - combat pieces and weapons for Valheim, written from scratch. First feature: weapon recall.
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Jotunn.Managers;
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
        internal static ConfigEntry<bool> RecallEnabled, RecallPin, RecallWithThrow;
        private static readonly Dictionary<string, ConfigEntry<bool>> beams = new Dictionary<string, ConfigEntry<bool>>();
        private static Plugin instance;
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
            instance = this;
            ItemManager.OnItemsRegistered += AddBeamSettings;
            new Harmony(Id).PatchAll(typeof(Plugin).Assembly);
            gameObject.AddComponent<Recall>();
        }

        // One "Light beam" setting per weapon that can be thrown and lands as an item (the game's spears and modded
        // throwing weapons), found once every mod's items are in.
        private static void AddBeamSettings()
        {
            var db = ObjectDB.instance;
            if (!db || instance == null) return;
            foreach (var prefab in db.m_items.Where(x => x))
            {
                var shared = prefab.GetComponent<ItemDrop>()?.m_itemData?.m_shared;
                if (shared == null || beams.ContainsKey(prefab.name) || !Throwable(shared)) continue;
                string english = Localization.instance.Localize(shared.m_name);
                beams[prefab.name] = instance.Config.Bind(BeamSection, SafeName(english == shared.m_name ? prefab.name : english), true,
                    $"Show a beam of light over a thrown {english} ({prefab.name}) lying on the ground.");
            }
        }

        private static bool Throwable(ItemDrop.ItemData.SharedData shared) =>
            new[] { shared.m_attack, shared.m_secondaryAttack }.Any(a => a?.m_attackProjectile && a.m_attackProjectile.GetComponent<Projectile>() is Projectile p && p.m_respawnItemOnHit);

        // Setting names can't hold = \ " ' [ ] or line breaks.
        private static string SafeName(string name) => new string(name.Where(c => c >= ' ' && "=\\\"'[]".IndexOf(c) < 0).ToArray()).Trim();

        internal static bool BeamFor(string prefab) => prefab == null || !beams.TryGetValue(prefab, out var entry) || entry.Value;
    }
}
