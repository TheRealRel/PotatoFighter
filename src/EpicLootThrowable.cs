// PotatoFighter - the Kraken Axe and Epic Loot's Throwable enchantment.
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
        private const string KrakenName = "$item_potato_kraken_axe";
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
                if (names.Contains(KrakenName)) return;
                names.Add(KrakenName);
                Plugin.Log.LogInfo("The Kraken Axe is kept out of Epic Loot's Throwable enchantment.");
            }
        }

        [HarmonyPatch]
        internal static class KrakenKeepsItsAttacks
        {
            private static MethodBase TargetMethod() => AccessTools.Method(AccessTools.TypeByName("EpicLoot.MagicItemEffects.Throwable_Humanoid_StartAttack_Patch"), "Prefix");
            private static bool Prepare() => EpicLoot && TargetMethod() != null;

            // Our prefix on Epic Loot's prefix: __args are its arguments (the humanoid first), __result its return value
            // (true lets the game's StartAttack run).
            private static bool Prefix(object[] __args, ref bool __result)
            {
                if (__args == null || __args.Length == 0 || !(__args[0] is Humanoid humanoid) || !KrakenAxe.Is(humanoid.GetCurrentWeapon())) return true;
                __result = true;
                return false;
            }
        }
    }
}
