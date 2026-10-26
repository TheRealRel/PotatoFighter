// PotatoFighter - our throwing weapons and Epic Loot's Throwable enchantment (the user's rule, 2026-10-26: every throwing
// weapon PotatoFighter makes is kept out of Throwable). Each weapon adds its item name to ThrowingWeapons when it's made.
// Throwable takes over a weapon's middle mouse attack: Epic Loot's own StartAttack prefix throws the weapon with a flint
// spear's throw and skips the weapon's own attack. The Kraken Axe already throws (and comes back), so on it Throwable only
// replaced our throw and the Shift + middle mouse chop with a plain spear throw. Two fixes, both found by name, so
// PotatoFighter needs no Epic Loot to build or load; without Epic Loot (or if it changes) they do nothing:
//  - the Kraken Axe can't roll or be enchanted with Throwable (added to the effect's excluded item names each time Epic Loot
//    sets its effect list up, which also happens when a server sends its own);
//  - on a Kraken Axe that already has Throwable, Epic Loot's throw steps aside and the axe's own attacks run.
using System.Collections;
using System.Reflection;
using HarmonyLib;

namespace PotatoFighter
{
    internal static class EpicLootThrowable
    {
        // Item names ($item_...) of PotatoFighter's throwing weapons.
        internal static readonly System.Collections.Generic.HashSet<string> ThrowingWeapons = new System.Collections.Generic.HashSet<string>();
        // Looking a missing type up by name logs a warning, so only when Epic Loot is installed.
        private static bool EpicLoot => BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey("randyknapp.mods.epicloot");

        [HarmonyPatch]
        internal static class NoThrowableRoll
        {
            private static MethodBase TargetMethod() => AccessTools.Method(AccessTools.TypeByName("EpicLoot.MagicItemEffectDefinitions"), "Initialize");
            private static bool Prepare() => EpicLoot && TargetMethod() != null;

            private static void Postfix() => Exclude();

            internal static void Exclude()
            {
                if (!EpicLoot) return;
                var all = AccessTools.Field(AccessTools.TypeByName("EpicLoot.MagicItemEffectDefinitions"), "AllDefinitions")?.GetValue(null) as IDictionary;
                if (all == null || !all.Contains("Throwable")) return;
                var definition = all["Throwable"];
                var requirements = AccessTools.Field(definition.GetType(), "Requirements")?.GetValue(definition);
                if (requirements == null) return;
                var field = AccessTools.Field(requirements.GetType(), "ExcludedItemNames");
                if (!(field?.GetValue(requirements) is IList names))
                {
                    names = (IList)System.Activator.CreateInstance(field?.FieldType ?? typeof(System.Collections.Generic.List<string>));
                    field?.SetValue(requirements, names);
                }
                int added = 0;
                foreach (var name in ThrowingWeapons)
                    if (!names.Contains(name)) { names.Add(name); added++; }
                if (added > 0) Plugin.Log.LogInfo($"{added} PotatoFighter throwing weapon(s) kept out of Epic Loot's Throwable enchantment.");
            }
        }

        [HarmonyPatch]
        internal static class KrakenKeepsItsAttacks
        {
            private static MethodBase TargetMethod() => AccessTools.Method(AccessTools.TypeByName("EpicLoot.MagicItemEffects.Throwable_Humanoid_StartAttack_Patch"), "Prefix");
            private static bool Prepare() => EpicLoot && TargetMethod() != null;

            // Our prefix on Epic Loot's prefix bool Prefix(Humanoid __instance, ref bool __result, bool secondaryAttack):
            // __0 is its humanoid and our __result is its return value (true lets the game's StartAttack run). Its
            // arguments are taken by position, not as __args: Harmony can't box its ref bool into an object array
            // ("Invalid IL code ... box", which switched this fix off).
            private static bool Prefix(Humanoid __0, ref bool __result)
            {
                if (__0 == null) return true;
                var weapon = __0.GetCurrentWeapon();
                if (weapon?.m_shared == null || !ThrowingWeapons.Contains(weapon.m_shared.m_name)) return true;
                __result = true;
                return false;
            }
        }
    }
}
