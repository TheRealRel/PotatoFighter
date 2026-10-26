// PotatoFighter - our throwing weapons and Epic Loot's Throwable enchantment (the user's rule, 2026-10-26: every throwing
// weapon PotatoFighter makes is kept out of Throwable). Each weapon adds its item name to ThrowingWeapons when it's made.
// Throwable takes over a weapon's middle mouse attack with a flint spear's throw, which on the Kraken Axe replaced our own
// throw and the Shift + middle mouse chop. Our throwing weapons are added to the effect's excluded item names each time
// Epic Loot sets its effect list up (also when a server sends its own), so they can't roll or be enchanted with it. Found
// by name, so PotatoFighter needs no Epic Loot to build or load; without Epic Loot (or if it changes) this does nothing.
// A second hook on Epic Loot's own StartAttack prefix, for a weapon that already had Throwable, was removed: once it ran,
// weapon switching and moving inventory items stopped working (2026-10-26 test), and the exclusion alone covers released
// weapons.
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
    }
}
