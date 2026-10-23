// Test-build logging: how each of this player's throws is launched and how it flies, to tune PotatoFighter's own
// weapons (a spear's arch, a magical axe's straighter flight). Off in releases (Diagnostics = false).
using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using UnityEngine;

namespace PotatoFighter
{
    internal static class Flight
    {
        internal const bool Diagnostics = true;

        private sealed class Track { public Vector3 Start; public float Time; public string Name; public float Peak; }
        private static readonly Dictionary<Projectile, Track> tracks = new Dictionary<Projectile, Track>();
        private static readonly AccessTools.FieldRef<Humanoid, Attack> CurrentAttack = AccessTools.FieldRefAccess<Humanoid, Attack>("m_currentAttack");
        private static readonly AccessTools.FieldRef<Projectile, Vector3> Velocity = AccessTools.FieldRefAccess<Projectile, Vector3>("m_vel");

        private static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        [HarmonyPatch(typeof(Projectile), nameof(Projectile.Setup))]
        private static class Launched
        {
            private static void Postfix(Projectile __instance, Character owner, ItemDrop.ItemData item, Vector3 velocity)
            {
                if (!Diagnostics || owner == null || owner != Player.m_localPlayer || item?.m_shared == null) return;
                var attack = owner is Humanoid h ? CurrentAttack(h) : null;
                var p = __instance;
                float speed = velocity.magnitude;
                float pitch = speed > 0f ? Mathf.Asin(Mathf.Clamp(velocity.y / speed, -1f, 1f)) * Mathf.Rad2Deg : 0f;
                Plugin.Log.LogInfo($"[Flight] {Localization.instance.Localize(item.m_shared.m_name)} ({item.m_dropPrefab?.name}) thrown: speed {F(speed)} m/s at {F(pitch)} deg, " +
                    $"projectile {p.name}: gravity {F(p.m_gravity)}, drag {F(p.m_drag)}, ttl {F(p.m_ttl)}, ray radius {F(p.m_rayRadius)}, spin {F(p.m_rotateVisual)}/{F(p.m_rotateVisualY)}/{F(p.m_rotateVisualZ)}, " +
                    $"drops item {p.m_respawnItemOnHit}, stays in walls {p.m_stayAfterHitStatic}, sticks {p.m_attachToClosestBone}, bounce {p.m_bounce}" +
                    (attack == null ? "" : $"; attack {attack.m_attackAnimation}: vel {F(attack.m_projectileVelMin)}-{F(attack.m_projectileVel)}, random {attack.m_randomVelocity}, " +
                        $"accuracy {F(attack.m_projectileAccuracy)}-{F(attack.m_projectileAccuracyMin)}, launch angle {F(attack.m_launchAngle)}, height {F(attack.m_attackHeight)}, " +
                        $"stamina {F(attack.m_attackStamina)}, consumes item {attack.m_consumeItem}"));
                tracks[p] = new Track { Start = p.transform.position, Time = Time.time, Name = Localization.instance.Localize(item.m_shared.m_name), Peak = p.transform.position.y };
            }
        }

        [HarmonyPatch(typeof(Projectile), "FixedUpdate")]
        private static class Flying
        {
            private static void Postfix(Projectile __instance)
            {
                if (Diagnostics && tracks.TryGetValue(__instance, out var t) && __instance.transform.position.y > t.Peak) t.Peak = __instance.transform.position.y;
            }
        }

        [HarmonyPatch(typeof(Projectile), "OnHit")]
        private static class Landed
        {
            private static void Prefix(Projectile __instance)
            {
                if (!Diagnostics || !tracks.TryGetValue(__instance, out var t)) return;
                tracks.Remove(__instance);
                var end = __instance.transform.position;
                var flat = new Vector3(end.x - t.Start.x, 0f, end.z - t.Start.z).magnitude;
                Plugin.Log.LogInfo($"[Flight] {t.Name} landed after {F(Time.time - t.Time)} s: {F(flat)} m away, {F(end.y - t.Start.y)} m height change, " +
                    $"peak {F(t.Peak - t.Start.y)} m above the throw, speed at impact {F(Velocity(__instance).magnitude)} m/s");
            }
        }
    }
}
