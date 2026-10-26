// The Kraken Axe: a magical one-handed throwing axe (frost, slash, spirit). Built from the game's iron axe, so it swings
// with the axe's own animations and keeps the game's upgrade glow; our own model (Meshy, see tools/) replaces the iron
// axe's. Middle mouse throws it (the spear's throw, a straighter, spinning flight); Shift + middle mouse is the axe's
// own overhead chop; with the hand empty, middle mouse calls it back (Recall.cs) and it flies back to the hand.
// The frost glow in its grooves and its soft light follow Potato Tweaks' "Upgrade glow" settings.
using System.Collections.Generic;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using HarmonyLib;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace PotatoFighter
{
    internal static class KrakenAxe
    {
        internal const string PrefabName = "potato_kraken_axe";              // never rename: worlds save items by it
        internal const string ProjectileName = "potato_kraken_axe_projectile";
        internal const string IceBurstName = "potato_kraken_iceburst";
        private const string IceBurstEffect = "fx_DvergerMage_Ice_hit"; // a single, sharp ice impact with a flash of light
        private const float IceBurstFrost = 90f, IceBurstFrostPerLevel = 10f, IceBurstReach = 1.8f;
        private const float IceBurstRadius = 2.9f;  // at "Explosion size" 100
        private const float IceLookRadius = 2.5f;   // the reach the ice effect's own size matches; it's scaled from there
        private const string Model = "kraken_axe";
        private const float BalanceZ = 0.45f;     // the balance point along the haft (heavy head), metres from the hand
        private const float SpinPerSecond = 792f;  // degrees a second, end over end: 2 turns, 12 deg a frame at 60 fps (no strobing at 30)
        private static readonly Color FrostColor = new Color(0.35f, 0.75f, 1f);
        private const float GlowStrength = 1.4f;
        private const float LightIntensity = 0.6f, LightRange = 3f;
        internal const float MaxFlightSeconds = 1.7f; // about 55 m: past that without a hit, it turns round by itself
        internal const float ThrowSpeed = 32f;      // m/s leaving the hand (the spear: 20)
        private const float ThrowAnimSpeed = 1.49f; // the spear's throw animation played faster: a snappier arm, earlier release
        private const float ChopAnimSpeed = 1.3f;   // the axe's overhead chop (Shift + middle mouse), quicker
        private const float ChopSkillSaving = 0.5f; // the chop's stamina falls by half at Axes 100 (the game's own: a third)

        private static Mesh mesh;
        private static Material material;
        private static Attack throwAttack, axeSpecial;
        private static GameObject iceBurst, iceEffect;
        private static Light[] iceLights;
        private static float[] iceLightRanges;

        internal static void Register()
        {
            var loc = LocalizationManager.Instance.GetLocalization();
            loc.AddTranslation("English", new Dictionary<string, string>
            {
                { "item_potato_kraken_axe", "Kraken Axe" },
                { "item_potato_kraken_axe_description", "Cold as the deep and never far from its owner's hand. Throw it with the middle mouse button and call it back the same way once your hand is empty. Shift and middle mouse chops." },
            });
            PrefabManager.OnVanillaPrefabsAvailable += Create;
        }

        private static void Create()
        {
            PrefabManager.OnVanillaPrefabsAvailable -= Create;
            try
            {
                mesh = Assets.LoadMesh(Model);
                if (!mesh) return;
                var config = new ItemConfig
                {
                    Name = "$item_potato_kraken_axe",
                    Description = "$item_potato_kraken_axe_description",
                    CraftingStation = CraftingStations.BlackForge,
                    MinStationLevel = 3,                     // Ashlands, like the Berserkir Axes
                };
                config.AddRequirement(new RequirementConfig("FlametalNew", 16, 6));
                config.AddRequirement(new RequirementConfig("Silver", 10, 4));
                config.AddRequirement(new RequirementConfig("FreezeGland", 6, 3));
                config.AddRequirement(new RequirementConfig("SerpentScale", 4, 0));
                var item = new CustomItem(PrefabName, "AxeIron", config);
                var projectile = MakeProjectile();
                iceBurst = MakeIceBurst();
                Setup(item.ItemPrefab, projectile);
                ItemManager.Instance.AddItem(item);
                Plugin.OwnWeapons.Add(PrefabName);
                Glow.Watch();
                ItemManager.OnItemsRegistered += AddUpgradeIdol;
            }
            catch (System.Exception e) { Plugin.Log.LogError($"The Kraken Axe couldn't be made: {e}"); }
        }

        private static Material Material()
        {
            if (material) return material;
            var template = PrefabManager.Cache.GetPrefab<GameObject>("AxeIron")?.GetComponentInChildren<MeshRenderer>(true)?.sharedMaterial;
            material = template ? new Material(template) : new Material(Shader.Find("Standard"));
            material.name = "potato_kraken_axe";
            Set("_MainTex", Assets.LoadTexture(Model + "_d.png", false));
            Set("_BumpMap", Assets.LoadTexture(Model + "_n.png", true));
            Set("_MetallicGlossMap", Assets.LoadTexture(Model + "_m.png", true));
            Set("_EmissionMap", Assets.LoadTexture(Model + "_e.png", false));
            if (material.HasProperty("_Color")) material.SetColor("_Color", Color.white);
            Glow.Apply();
            return material;
        }

        private static void Set(string property, Texture2D texture)
        {
            if (texture && material.HasProperty(property)) material.SetTexture(property, texture);
        }

        internal static void ApplyGlow(float factor)
        {
            if (material && material.HasProperty("_EmissionColor")) material.SetColor("_EmissionColor", FrostColor * GlowStrength * factor);
        }

        // The item: the iron axe's prefab with our model, Ashlands-tier numbers and two middle-mouse attacks.
        private static void Setup(GameObject prefab, GameObject projectile)
        {
            var shared = prefab.GetComponent<ItemDrop>().m_itemData.m_shared;
            shared.m_damages = new HitData.DamageTypes { m_slash = 100f, m_chop = 80f, m_frost = 40f, m_spirit = 20f };
            shared.m_damagesPerLevel = new HitData.DamageTypes { m_slash = 6f, m_chop = 4f, m_frost = 4f, m_spirit = 2f };
            shared.m_maxDurability = 300f; shared.m_durabilityPerLevel = 50f;
            shared.m_blockPower = 57f; shared.m_attackForce = 60f; shared.m_toolTier = 4; shared.m_maxQuality = 4;
            shared.m_attack.m_attackStamina = 16f;
            SetIcon(shared);
            axeSpecial = shared.m_secondaryAttack.Clone();
            axeSpecial.m_attackStamina = 32f;
            var spear = PrefabManager.Cache.GetPrefab<GameObject>("SpearBronze").GetComponent<ItemDrop>().m_itemData.m_shared.m_secondaryAttack;
            throwAttack = spear.Clone();
            throwAttack.m_attackProjectile = projectile;
            throwAttack.m_projectileVel = ThrowSpeed;
            throwAttack.m_projectileVelMin = ThrowSpeed;
            throwAttack.m_attackStamina = 18f;
            shared.m_secondaryAttack = throwAttack;

            var attach = prefab.transform.Find("attach");
            var old = attach.Find("Viking_Axe");
            old.name = "kraken_axe";
            old.localPosition = Vector3.zero; old.localRotation = Quaternion.identity; old.localScale = Vector3.one;
            old.GetComponent<MeshFilter>().sharedMesh = mesh;
            old.GetComponent<MeshRenderer>().sharedMaterial = Material();
            // The game's upgrade glow (ParticleIntensityScaler): its particles rise from the model, so point them at ours.
            var upgrade = attach.Find("UpgraderGlow");
            if (upgrade)
            {
                foreach (var ps in upgrade.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var shape = ps.shape;
                    if (shape.shapeType == ParticleSystemShapeType.Mesh) shape.mesh = mesh;
                    ps.transform.localPosition = Vector3.zero; ps.transform.localRotation = Quaternion.identity; ps.transform.localScale = Vector3.one;
                }
                foreach (var light in upgrade.GetComponentsInChildren<Light>(true)) light.transform.localPosition = new Vector3(0f, 0f, 0.6f);
            }
            var box = attach.Find("collider")?.GetComponent<BoxCollider>();
            if (box)
            {
                box.transform.localPosition = Vector3.zero; box.transform.localRotation = Quaternion.identity; box.transform.localScale = Vector3.one;
                box.center = mesh.bounds.center; box.size = mesh.bounds.size;
            }
            var trail = attach.Find("equiped/trail");
            if (trail)
            {
                var b = trail.Find("base"); var t = trail.Find("tip");
                if (b) b.localPosition = new Vector3(0.21f, 0f, 0.45f);
                if (t) t.localPosition = new Vector3(0.24f, 0f, 0.72f);
            }
            AddFrostLight(attach);
        }

        // A soft blue light at the head, always on (dimmed or switched off with Tweaks' glow settings).
        private static void AddFrostLight(Transform parent)
        {
            var go = new GameObject("frost light");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, 0f, 0.6f);
            var light = go.AddComponent<Light>();
            light.type = LightType.Point; light.color = new Color(0.48f, 0.8f, 1f); light.range = LightRange; light.intensity = LightIntensity;
            light.shadows = LightShadows.None;
            go.AddComponent<Glow.Lamp>().Base = LightIntensity;
        }

        // The spear's projectile with our spinning axe in place of the spear: flies straighter (low gravity, faster) and
        // turns end over end, head coming over forward, around its balance point.
        private static GameObject MakeProjectile()
        {
            var go = PrefabManager.Instance.CreateClonedPrefab(ProjectileName, "bronzespear_projectile");
            var projectile = go.GetComponent<Projectile>();
            projectile.m_gravity = 1.5f;
            projectile.m_rotateVisual = SpinPerSecond;
            projectile.m_rotateVisualY = 0f; projectile.m_rotateVisualZ = 0f;
            if (projectile.m_visual) Object.DestroyImmediate(projectile.m_visual);
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                if (!(r is ParticleSystemRenderer) && !(r is TrailRenderer)) r.enabled = false;
            projectile.m_visual = SpinningModel(go.transform, "kraken_visual");
            AddFrostLight(projectile.m_visual.transform);
            PrefabManager.Instance.AddPrefab(go);
            return go;
        }

        // The game's own recipes list an upgrade idol that only its upgrade station asks for (forges and workbenches skip it),
        // and Reforged Potential reads the same idol for the item's tier. An Ashlands weapon takes the tier 6 weapon idol
        // (the Flametal battle idol), Upgrader6Weapon; the amounts and the upgrade cap come from the Berserkir Axes. Runs each time
        // the item database is built, after every mod's items are in.
        private static void AddUpgradeIdol()
        {
            try
            {
                var db = ObjectDB.instance;
                if (!db) return;
                var ours = db.m_recipes.Find(r => r && r.m_item && r.m_item.name == PrefabName);
                if (!ours || System.Array.Exists(ours.m_resources, r => r.m_upgraderResource)) return;
                var idol = db.GetItemPrefab("Upgrader6Weapon")?.GetComponent<ItemDrop>();
                if (!idol) { Plugin.Log.LogInfo("No upgrade idols in this game: the Kraken Axe upgrades at the forge only."); return; }
                var reference = db.m_recipes.Find(r => r && r.m_item && r.m_item.name == "AxeBerzerkr");
                var like = reference ? System.Array.Find(reference.m_resources, r => r.m_upgraderResource) : null;
                var list = new List<Piece.Requirement>(ours.m_resources)
                {
                    new Piece.Requirement { m_resItem = idol, m_amount = like?.m_amount ?? 1, m_amountPerLevel = like?.m_amountPerLevel ?? 0, m_recover = like?.m_recover ?? false, m_upgraderResource = true }
                };
                ours.m_resources = list.ToArray();
                var item = db.GetItemPrefab(PrefabName)?.GetComponent<ItemDrop>();
                int cap = reference ? reference.m_item.m_itemData.m_shared.m_maxQuality : 0;
                if (item && cap > item.m_itemData.m_shared.m_maxQuality) item.m_itemData.m_shared.m_maxQuality = cap;
                Plugin.Log.LogInfo($"Kraken Axe: upgrade idol Upgrader6Weapon x{like?.m_amount ?? 1} (+{like?.m_amountPerLevel ?? 0} a level, {(like != null ? "as the Berserkir Axes" : "no Berserkir Axes recipe to copy")}), upgrade cap {item?.m_itemData.m_shared.m_maxQuality}.");
            }
            catch (System.Exception e) { Plugin.Log.LogWarning($"The Kraken Axe's upgrade idol couldn't be added: {e.Message}"); }
        }

        // The chop's ice explosion: one hit of frost damage (its slow comes with the frost) to enemies within reach, and one
        // ice impact effect, both the moment the chop lands. A plain Aoe made here (the frost nova it started from plays
        // three timed waves, so its look trailed the damage and repeated). Made by the attacking player's game and synced.
        private static GameObject MakeIceBurst()
        {
            var go = PrefabManager.Instance.CreateEmptyPrefab(IceBurstName, true);
            var aoe = go.AddComponent<Aoe>();
            aoe.m_name = "$item_potato_kraken_axe";
            aoe.m_useAttackSettings = false;
            aoe.m_damage = new HitData.DamageTypes { m_frost = IceBurstFrost };
            aoe.m_damagePerLevel = new HitData.DamageTypes { m_frost = IceBurstFrostPerLevel };
            aoe.m_hitEnemy = true; aoe.m_hitFriendly = false; aoe.m_hitOwner = false; aoe.m_hitSame = false; aoe.m_hitParent = false;
            aoe.m_hitCharacters = true; aoe.m_hitProps = false; aoe.m_hitTerrain = false;
            aoe.m_hitInterval = 0f; aoe.m_ttl = 0.5f; aoe.m_activationDelay = 0f;
            aoe.m_multiSpawnMin = 0; aoe.m_multiSpawnMax = 0; aoe.m_chainStartChance = 0f;
            aoe.m_skill = Skills.SkillType.Axes;
            aoe.m_attackForce = 10f;
            var burst = PrefabManager.Cache.GetPrefab<GameObject>(IceBurstEffect);
            aoe.m_initiateEffect = new EffectList();
            aoe.m_hitEffects = new EffectList();
            if (burst)
            {
                // Our own copy of the ice impact, so it can grow with the reach. Its particles scale with the whole
                // object (by default each keeps its own size); lights don't scale at all, so their reach is set by hand.
                iceEffect = PrefabManager.Instance.CreateClonedPrefab(IceBurstName + "_fx", burst);
                foreach (var ps in iceEffect.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var main = ps.main;
                    main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                }
                iceLights = iceEffect.GetComponentsInChildren<Light>(true);
                iceLightRanges = System.Array.ConvertAll(iceLights, l => l.range);
                aoe.m_initiateEffect.m_effectPrefabs = new[] { new EffectList.EffectData { m_prefab = iceEffect, m_enabled = true, m_variant = -1 } };
            }
            else Plugin.Log.LogWarning($"The ice effect {IceBurstEffect} wasn't found; the burst still hits.");
            // Jotunn's empty prefab carries a placeholder cube: nothing should be drawn or collide but the effect.
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true)) Object.DestroyImmediate(r);
            foreach (var f in go.GetComponentsInChildren<MeshFilter>(true)) Object.DestroyImmediate(f);
            PrefabManager.Instance.AddPrefab(go);
            iceBurst = go;
            ApplyIceRadius();
            Plugin.ExplosionSize.SettingChanged += (_, __) => ApplyIceRadius();
            return go;
        }

        // The "Explosion size" setting (set by the server, so every player's game draws the same size): the reach of the
        // damage, and the ice effect scaled to match.
        private static void ApplyIceRadius()
        {
            if (!iceBurst) return;
            float radius = IceBurstRadius * Plugin.ExplosionSize.Value / 100f;
            var aoe = iceBurst.GetComponent<Aoe>();
            if (aoe) aoe.m_radius = radius;
            if (!iceEffect) return;
            float scale = radius / IceLookRadius;
            iceEffect.transform.localScale = Vector3.one * scale;
            for (int i = 0; i < iceLights.Length; i++) if (iceLights[i]) iceLights[i].range = iceLightRanges[i] * scale;
        }

        // A spinning pivot at the balance point holding the axe with its blades upright in the flight plane. The pivot turns
        // about its own X axis (across the flight), so the axe tumbles within the blades' plane. Also used for the return.
        internal static GameObject SpinningModel(Transform parent, string name)
        {
            var pivot = new GameObject(name);
            pivot.transform.SetParent(parent, false);
            var model = new GameObject("kraken_axe");
            model.transform.SetParent(pivot.transform, false);
            model.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            model.transform.localPosition = new Vector3(0f, 0f, -BalanceZ);
            model.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = model.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = Material();
            pivot.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f); // head up when it leaves the hand
            return pivot;
        }

        internal static bool Is(ItemDrop.ItemData item) => item?.m_dropPrefab && item.m_dropPrefab.name == PrefabName;

        // The inventory icon: rendered from our model beforehand (tools/icon.py) in the game's own layout, diagonal with
        // the head top right. No icon on a dedicated server (no graphics); the iron axe's stays there.
        private static void SetIcon(ItemDrop.ItemData.SharedData shared)
        {
            var texture = Assets.LoadTexture(Model + "_icon.png", false);
            if (!texture) return;
            texture.filterMode = FilterMode.Bilinear;
            shared.m_icons = new[] { Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f)) };
        }

        // The throw's arm swing, played faster. ZSyncAnimation's speed is synced, so everyone sees the same throw; it goes
        // back to normal as soon as the throw ends (or is cancelled).
        [HarmonyPatch(typeof(Player), "Update")]
        private static class ThrowAnimation
        {
            private static readonly AccessTools.FieldRef<Character, ZSyncAnimation> Anim = AccessTools.FieldRefAccess<Character, ZSyncAnimation>("m_zanim");
            private static readonly AccessTools.FieldRef<Humanoid, Attack> Current = AccessTools.FieldRefAccess<Humanoid, Attack>("m_currentAttack");
            private static float current = 1f;

            private static void Postfix(Player __instance)
            {
                if (__instance != Player.m_localPlayer || throwAttack == null) return;
                var attack = Current(__instance);
                float speed = 1f;
                if (attack != null && __instance.InAttack() && Is(attack.GetWeapon()))
                {
                    if (attack.m_attackAnimation == throwAttack.m_attackAnimation) speed = ThrowAnimSpeed;
                    else if (axeSpecial != null && attack.m_attackAnimation == axeSpecial.m_attackAnimation) speed = ChopAnimSpeed;
                }
                if (Mathf.Approximately(speed, current)) return;
                current = speed;
                Anim(__instance)?.SetSpeed(speed);
            }
        }

        // The chop gets cheaper with Axes skill than other attacks: half price at 100 instead of the game's two thirds. The
        // game's own saving (a third, times the skill factor) is already in the result, so it's swapped for ours, and any
        // other mod's changes to the cost are kept.
        [HarmonyPatch(typeof(Attack), "GetAttackStamina")]
        private static class ChopStamina
        {
            private static readonly AccessTools.FieldRef<Attack, Humanoid> Owner = AccessTools.FieldRefAccess<Attack, Humanoid>("m_character");

            private static void Postfix(Attack __instance, ref float __result)
            {
                if (axeSpecial == null || __instance.m_attackAnimation != axeSpecial.m_attackAnimation || !Is(__instance.GetWeapon())) return;
                var owner = Owner(__instance);
                if (!owner) return;
                float skill = owner.GetSkillFactor(Skills.SkillType.Axes);
                __result *= (1f - ChopSkillSaving * skill) / (1f - 0.33f * skill);
            }
        }

        // When the chop lands (its trigger, the same moment its hit is checked), the ice bursts in front of the player.
        [HarmonyPatch(typeof(Attack), nameof(Attack.OnAttackTrigger))]
        private static class Chop
        {
            private static readonly AccessTools.FieldRef<Attack, Humanoid> Owner = AccessTools.FieldRefAccess<Attack, Humanoid>("m_character");

            private static Attack lastBurst;   // the chop's hit can be signalled more than once: one burst per chop
            private static float lastBurstTime;

            private static void Postfix(Attack __instance)
            {
                if (!iceBurst || axeSpecial == null || __instance.m_attackAnimation != axeSpecial.m_attackAnimation || AttackCancel.IsCancelled(__instance)) return;
                if (__instance == lastBurst || Time.time - lastBurstTime < 0.5f) return;
                var owner = Owner(__instance);
                if (!owner || owner != Player.m_localPlayer) return;
                var weapon = __instance.GetWeapon();
                if (!Is(weapon)) return;
                lastBurst = __instance;
                lastBurstTime = Time.time;
                var t = owner.transform;
                ApplyIceRadius();
                var go = Object.Instantiate(iceBurst, t.position + t.forward * IceBurstReach + Vector3.up * 0.2f, t.rotation);
                go.GetComponent<IProjectile>()?.Setup(owner, Vector3.zero, -1f, null, weapon, null);
                if (Plugin.TestBuild) Plugin.Log.LogInfo($"[Ice] burst at the chop ({Time.frameCount}), {IceBurstRadius * Plugin.ExplosionSize.Value / 100f:0.0} m");
            }
        }

        // Middle mouse throws; Shift (the run key) + middle mouse is the axe's own chop. The attack is picked just before
        // the game starts it, from the item's shared data, on the attacking player's own game.
        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.StartAttack))]
        private static class Attacks
        {
            private static void Prefix(Humanoid __instance, bool secondaryAttack)
            {
                if (!secondaryAttack || __instance != Player.m_localPlayer || throwAttack == null) return;
                var weapon = __instance.GetCurrentWeapon();
                if (!Is(weapon)) return;
                bool shift = ZInput.GetButton("Run") || ZInput.GetButton("JoyRun");
                weapon.m_shared.m_secondaryAttack = shift ? axeSpecial : throwAttack;
            }
        }
    }

    // The frost glow's strength follows Potato Tweaks' visual effects glow settings, live: "Upgrade glow" on uses
    // "Upgrade glow strength" (0 = off, 1 = as made, 2 = twice); off or without Tweaks it's as made.
    internal static class Glow
    {
        private const string TweaksId = "potato.tweaks";
        private static readonly string[] Sections = { "2 - Visual effects", "7 - Visual effects" };   // Tweaks 1.5.17 on, and before
        private static ConfigEntry<bool> enabled;
        private static ConfigEntry<float> strength;
        private static readonly HashSet<Lamp> lamps = new HashSet<Lamp>();

        internal static float Factor => enabled != null && strength != null && enabled.Value ? Mathf.Max(0f, strength.Value) : 1f;

        internal static void Watch()
        {
            if (Chainloader.PluginInfos.TryGetValue(TweaksId, out var info) && info.Instance)
            {
                var config = info.Instance.Config;
                foreach (var section in Sections)
                    if (enabled == null && config.TryGetEntry(section, "Upgrade glow", out enabled)) config.TryGetEntry(section, "Upgrade glow strength", out strength);
                if (enabled != null) enabled.SettingChanged += (_, __) => Apply();
                if (strength != null) strength.SettingChanged += (_, __) => Apply();
            }
            Apply();
        }

        internal static void Apply()
        {
            float f = Factor;
            KrakenAxe.ApplyGlow(f);
            foreach (var lamp in lamps) if (lamp) lamp.Set(f);
        }

        internal sealed class Lamp : MonoBehaviour
        {
            public float Base = 0.6f;
            private Light light;
            private void Awake() { light = GetComponent<Light>(); lamps.Add(this); Set(Factor); }
            private void OnDestroy() => lamps.Remove(this);
            internal void Set(float factor)
            {
                if (!light) return;
                light.intensity = Base * factor;
                light.enabled = factor > 0.001f;
            }
        }
    }
}
