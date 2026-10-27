// PotatoFighter - crossbows stay loaded (Valheim). The game keeps the loaded weapon as one reference on the player
// (Player.m_weaponLoaded, set by SetWeaponLoaded, which also sets the synced "loaded" look) and clears it whenever a weapon
// is put away, so a loaded crossbow had to be reloaded after every switch. A crossbow put away loaded keeps a mark in its
// own item data (saved with it); taking it out again uses the mark up and loads it through the game's own SetWeaponLoaded.
// The bolt is only used when it fires (Attack.OnAttackTrigger), so a kept load can't fire one for free. Any weapon whose
// attack needs a reload counts, modded crossbows too. Both hooks are wrapped so a failure can never stop an equip.
using System;
using System.Reflection;
using HarmonyLib;

namespace PotatoFighter
{
    internal static class CrossbowLoad
    {
        private const string Key = "PotatoFighter_Loaded";
        internal const bool Diagnostics = false;   // test builds: one log line when a load is kept and when it's restored
        private static readonly AccessTools.FieldRef<Player, ItemDrop.ItemData> Loaded = AccessTools.FieldRefAccess<Player, ItemDrop.ItemData>("m_weaponLoaded");
        private static readonly MethodInfo SetLoaded = AccessTools.Method(typeof(Player), "SetWeaponLoaded");

        private static bool Reloads(ItemDrop.ItemData item) => item?.m_shared?.m_attack != null && item.m_shared.m_attack.m_requiresReload;

        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.UnequipItem))]
        internal static class PutAway
        {
            private static void Prefix(Humanoid __instance, ItemDrop.ItemData item)
            {
                try
                {
                    if (!Plugin.CrossbowsStayLoaded.Value || !(__instance is Player player) || player != Player.m_localPlayer || !Reloads(item)) return;
                    if (item.m_customData == null || Loaded(player) != item) return;
                    item.m_customData[Key] = "1";
                    if (Diagnostics) Plugin.Log.LogInfo($"[Crossbow] {item.m_shared.m_name} put away loaded");
                }
                catch (Exception e) { Plugin.Log.LogWarning($"Crossbows stay loaded: {e.Message}"); }
            }
        }

        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.EquipItem))]
        internal static class TakeOut
        {
            private static void Postfix(Humanoid __instance, ItemDrop.ItemData item, bool __result)
            {
                try
                {
                    if (!__result || !(__instance is Player player) || player != Player.m_localPlayer || item?.m_customData == null || !item.m_customData.Remove(Key)) return;
                    if (!Plugin.CrossbowsStayLoaded.Value || !Reloads(item) || SetLoaded == null) return;
                    SetLoaded.Invoke(player, new object[] { item });
                    if (Diagnostics) Plugin.Log.LogInfo($"[Crossbow] {item.m_shared.m_name} taken out still loaded");
                }
                catch (Exception e) { Plugin.Log.LogWarning($"Crossbows stay loaded: {e.Message}"); }
            }
        }
    }
}
