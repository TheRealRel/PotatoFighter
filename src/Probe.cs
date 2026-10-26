// Test-build data capture (nothing on screen): once per game start, writes how the game's own axes, spears and frost
// mace are built (attacks, animations, models, materials, lights, particles, upgrade glow) to
// BepInEx/PotatoFighter-probe.log, so the Kraken Axe is built from real numbers. Not applied in releases.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace PotatoFighter
{
    // Runs when the local player spawns: at ObjectDB.Awake (main menu) the game's own items aren't registered yet.
    [HarmonyPatch(typeof(Player), nameof(Player.OnSpawned))]
    internal static class Probe
    {
        private static bool done;
        private static readonly string[] Items =
        {
            "AxeStone", "AxeFlint", "AxeBronze", "AxeIron", "Battleaxe", "SpearFlint", "SpearBronze", "SpearElderbark",
            "SpearWolfFang", "MaceSilver", "KnifeSilver", "MaceIron", "AxeBlackMetal", "AxeJotunBane", "SpearCarapace", "SwordMistwalker", "KnifeSkollAndHati",
        };

        private static bool Prepare() => Plugin.TestBuild;

        private static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
        private static string V(Vector3 v) => $"({F(v.x)},{F(v.y)},{F(v.z)})";
        private static string C(Color c) => $"({F(c.r)},{F(c.g)},{F(c.b)},{F(c.a)})";

        private static void Postfix(Player __instance)
        {
            var db = ObjectDB.instance;
            if (done || __instance != Player.m_localPlayer || !db || !db.GetItemPrefab("AxeStone")) return;
            done = true;
            var __db = db;
            var sb = new StringBuilder();
            try
            {
                foreach (var name in Items)
                {
                    var go = __db.GetItemPrefab(name);
                    if (!go) { sb.AppendLine($"## {name}: not found"); continue; }
                    DumpItem(sb, go);
                }
                // Which items carry the upgrade glow, and how it's set (names only).
                var scaled = __db.m_items.Where(i => i && i.GetComponentInChildren<ParticleIntensityScaler>(true)).Select(i => i.name).ToList();
                sb.AppendLine($"## Items with ParticleIntensityScaler: {scaled.Count}: {string.Join(", ", scaled)}");
            }
            catch (Exception e) { sb.AppendLine("PROBE ERROR " + e); }
            File.WriteAllText(Path.Combine(Paths.BepInExRootPath, "PotatoFighter-probe.log"), sb.ToString());
            Plugin.Log.LogInfo("Probe written to PotatoFighter-probe.log");
        }

        private static void DumpItem(StringBuilder sb, GameObject go)
        {
            var drop = go.GetComponent<ItemDrop>();
            var s = drop.m_itemData.m_shared;
            sb.AppendLine($"## {go.name}: {Localization.instance.Localize(s.m_name)} type {s.m_itemType} anim {s.m_animationState} skill {s.m_skillType} attachOverride {s.m_attachOverride} " +
                $"maxQuality {s.m_maxQuality} weight {F(s.m_weight)} equipDuration {F(s.m_equipDuration)} durability {F(s.m_maxDurability)}+{F(s.m_durabilityPerLevel)} " +
                $"blockPower {F(s.m_blockPower)} parry {F(s.m_timedBlockBonus)} attackForce {F(s.m_attackForce)} backstab {F(s.m_backstabBonus)} toolTier {s.m_toolTier}");
            sb.AppendLine($"   damages {Dmg(s.m_damages)} perLevel {Dmg(s.m_damagesPerLevel)} attackStatus {(s.m_attackStatusEffect ? s.m_attackStatusEffect.name : "-")} equipStatus {(s.m_equipStatusEffect ? s.m_equipStatusEffect.name : "-")}");
            sb.AppendLine("   hit effects " + Effects(s.m_hitEffect) + " | blocked " + Effects(s.m_blockEffect) + " | trigger " + Effects(s.m_triggerEffect) + " | trail " + Effects(s.m_trailStartEffect));
            DumpAttack(sb, "primary", s.m_attack);
            DumpAttack(sb, "secondary", s.m_secondaryAttack);
            DumpTree(sb, go.transform, "   ");
        }

        private static string Effects(EffectList list) =>
            list?.m_effectPrefabs == null ? "-" : string.Join(",", list.m_effectPrefabs.Where(e => e?.m_prefab).Select(e => e.m_prefab.name));

        private static string Dmg(HitData.DamageTypes d) =>
            $"blunt {F(d.m_blunt)} slash {F(d.m_slash)} pierce {F(d.m_pierce)} chop {F(d.m_chop)} pickaxe {F(d.m_pickaxe)} fire {F(d.m_fire)} frost {F(d.m_frost)} lightning {F(d.m_lightning)} poison {F(d.m_poison)} spirit {F(d.m_spirit)}";

        private static void DumpAttack(StringBuilder sb, string label, Attack a)
        {
            if (a == null) { sb.AppendLine($"   {label}: none"); return; }
            sb.AppendLine($"   {label}: type {a.m_attackType} anim '{a.m_attackAnimation}' random {a.m_attackRandomAnimations} chain {a.m_attackChainLevels} stamina {F(a.m_attackStamina)} eitr {F(a.m_attackEitr)} " +
                $"dmg x{F(a.m_damageMultiplier)} force x{F(a.m_forceMultiplier)} stagger x{F(a.m_staggerMultiplier)} lastChain x{F(a.m_lastChainDamageMultiplier)} range {F(a.m_attackRange)} height {F(a.m_attackHeight)} " +
                $"angle {F(a.m_attackAngle)} ray {F(a.m_attackRayWidth)} speed {F(a.m_speedFactor)}/{F(a.m_speedFactorRotation)} consume {a.m_consumeItem} special {a.m_specialHitType}/{a.m_specialHitSkill} " +
                $"projectile {(a.m_attackProjectile ? a.m_attackProjectile.name : "-")} vel {F(a.m_projectileVelMin)}-{F(a.m_projectileVel)} acc {F(a.m_projectileAccuracy)}-{F(a.m_projectileAccuracyMin)} launch {F(a.m_launchAngle)} " +
                $"startFx {Effects(a.m_startEffect)} hitFx {Effects(a.m_hitEffect)} trigFx {Effects(a.m_triggerEffect)}");
            if (a.m_attackProjectile)
            {
                var p = a.m_attackProjectile.GetComponent<Projectile>();
                if (p) sb.AppendLine($"      projectile {p.name}: gravity {F(p.m_gravity)} drag {F(p.m_drag)} ttl {F(p.m_ttl)} rayRadius {F(p.m_rayRadius)} spin {F(p.m_rotateVisual)}/{F(p.m_rotateVisualY)}/{F(p.m_rotateVisualZ)} " +
                    $"respawnItem {p.m_respawnItemOnHit} stayStatic {p.m_stayAfterHitStatic} stayDynamic {p.m_stayAfterHitDynamic} attachBone {p.m_attachToClosestBone} visual {(p.m_visual ? p.m_visual.name : "-")} hitFx {Effects(p.m_hitEffects)}");
                DumpTree(sb, a.m_attackProjectile.transform, "      ");
            }
        }

        private static void DumpTree(StringBuilder sb, Transform t, string pad)
        {
            var comps = t.GetComponents<Component>().Where(c => c && !(c is Transform)).Select(c => c.GetType().Name);
            sb.AppendLine($"{pad}- {t.name} [{(t.gameObject.activeSelf ? "on" : "OFF")}] pos {V(t.localPosition)} rot {V(t.localEulerAngles)} scale {V(t.localScale)} layer {LayerMask.LayerToName(t.gameObject.layer)}: {string.Join(", ", comps)}");
            foreach (var r in t.GetComponents<Renderer>())
            {
                Mesh mesh = r is SkinnedMeshRenderer smr ? smr.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh) sb.AppendLine($"{pad}    mesh {mesh.name} verts {mesh.vertexCount} tris {(mesh.isReadable ? (mesh.triangles.Length / 3).ToString() : "?")} bounds {V(mesh.bounds.center)} size {V(mesh.bounds.size)} readable {mesh.isReadable}");
                sb.AppendLine($"{pad}    renderer shadows {r.shadowCastingMode} enabled {r.enabled}");
                foreach (var m in r.sharedMaterials) DumpMaterial(sb, m, pad + "    ");
            }
            var light = t.GetComponent<Light>();
            if (light) sb.AppendLine($"{pad}    light {light.type} color {C(light.color)} intensity {F(light.intensity)} range {F(light.range)} shadows {light.shadows} bounce {F(light.bounceIntensity)} enabled {light.enabled}");
            var ps = t.GetComponent<ParticleSystem>();
            if (ps)
            {
                var main = ps.main; var em = ps.emission; var sh = ps.shape;
                sb.AppendLine($"{pad}    particles startColor {C(main.startColor.color)}/{C(main.startColor.colorMin)}-{C(main.startColor.colorMax)} mode {main.startColor.mode} size {F(main.startSize.constant)}/{F(main.startSize.constantMin)}-{F(main.startSize.constantMax)} " +
                    $"life {F(main.startLifetime.constant)}/{F(main.startLifetime.constantMin)}-{F(main.startLifetime.constantMax)} speed {F(main.startSpeed.constant)} max {main.maxParticles} space {main.simulationSpace} gravity {F(main.gravityModifier.constant)} " +
                    $"rate {F(em.rateOverTime.constant)}/{F(em.rateOverTime.constantMax)} dist {F(em.rateOverDistance.constant)} shape {(sh.enabled ? sh.shapeType.ToString() : "off")} meshRenderer {(sh.meshRenderer ? sh.meshRenderer.name : "-")} skinned {(sh.skinnedMeshRenderer ? sh.skinnedMeshRenderer.name : "-")} mesh {(sh.mesh ? sh.mesh.name : "-")} radius {F(sh.radius)}");
            }
            var pis = t.GetComponent<ParticleIntensityScaler>();
            if (pis) sb.AppendLine($"{pad}    intensityScaler maxQuality {pis.maxQuality} perLevel {F(pis.itemDropLevelMultiplier)} lights {pis.scaleLights} lightMax {F(pis.lightIntensityMax)} colorMax {F(pis.intensityColorMax)} emission {pis.scaleEmission} size {pis.scaleSize} alpha {pis.scaleAlpha}");
            foreach (Transform c in t) DumpTree(sb, c, pad + "  ");
        }

        private static readonly HashSet<string> SeenMaterials = new HashSet<string>();

        private static void DumpMaterial(StringBuilder sb, Material m, string pad)
        {
            if (!m) { sb.AppendLine($"{pad}material null"); return; }
            if (!SeenMaterials.Add(m.name + m.GetInstanceID())) { sb.AppendLine($"{pad}material {m.name} (again)"); return; }
            var sh = m.shader;
            sb.AppendLine($"{pad}material {m.name} shader {sh.name} queue {m.renderQueue} keywords [{string.Join(" ", m.shaderKeywords)}]");
            var parts = new List<string>();
            for (int i = 0; i < sh.GetPropertyCount(); i++)
            {
                string n = sh.GetPropertyName(i);
                switch (sh.GetPropertyType(i))
                {
                    case UnityEngine.Rendering.ShaderPropertyType.Color: parts.Add($"{n}={C(m.GetColor(n))}"); break;
                    case UnityEngine.Rendering.ShaderPropertyType.Vector: parts.Add($"{n}={m.GetVector(n)}"); break;
                    case UnityEngine.Rendering.ShaderPropertyType.Float:
                    case UnityEngine.Rendering.ShaderPropertyType.Range: parts.Add($"{n}={F(m.GetFloat(n))}"); break;
                    case UnityEngine.Rendering.ShaderPropertyType.Texture:
                        var tex = m.GetTexture(n); parts.Add($"{n}={(tex ? $"{tex.name}:{tex.width}x{tex.height}" : "-")}"); break;
                }
            }
            sb.AppendLine($"{pad}  {string.Join(" ", parts)}");
        }
    }
}
