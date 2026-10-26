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
        private const string Model = "kraken_axe";
        private const float BalanceZ = 0.45f;     // the balance point along the haft (heavy head), metres from the hand
        private const float SpinPerSecond = 1000f; // degrees a second, end over end (about 2.8 turns)
        private static readonly Color FrostColor = new Color(0.35f, 0.75f, 1f);
        private const float GlowStrength = 1.4f;
        private const float LightIntensity = 0.6f, LightRange = 3f;

        private static Mesh mesh;
        private static Material material;
        private static Attack throwAttack, axeSpecial;

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
                    MinStationLevel = 1,
                };
                config.AddRequirement(new RequirementConfig("BlackMetal", 10, 5));
                config.AddRequirement(new RequirementConfig("YggdrasilWood", 6, 0));
                config.AddRequirement(new RequirementConfig("Eitr", 4, 2));
                config.AddRequirement(new RequirementConfig("FreezeGland", 4, 2));
                config.AddRequirement(new RequirementConfig("SerpentScale", 2, 0));
                var item = new CustomItem(PrefabName, "AxeIron", config);
                var projectile = MakeProjectile();
                Setup(item.ItemPrefab, projectile);
                ItemManager.Instance.AddItem(item);
                Plugin.OwnWeapons.Add(PrefabName);
                Glow.Watch();
                ItemManager.OnItemsRegistered += RenderIcon;
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

        // The item: the iron axe's prefab with our model, Mistlands-tier numbers and two middle-mouse attacks.
        private static void Setup(GameObject prefab, GameObject projectile)
        {
            var shared = prefab.GetComponent<ItemDrop>().m_itemData.m_shared;
            shared.m_damages = new HitData.DamageTypes { m_slash = 70f, m_chop = 60f, m_frost = 30f, m_spirit = 20f };
            shared.m_damagesPerLevel = new HitData.DamageTypes { m_slash = 5f, m_chop = 3f, m_frost = 3f, m_spirit = 2f };
            shared.m_maxDurability = 250f; shared.m_durabilityPerLevel = 50f;
            shared.m_blockPower = 48f; shared.m_attackForce = 60f; shared.m_toolTier = 4; shared.m_maxQuality = 4;
            shared.m_attack.m_attackStamina = 16f;
            axeSpecial = shared.m_secondaryAttack.Clone();
            axeSpecial.m_attackStamina = 32f;
            var spear = PrefabManager.Cache.GetPrefab<GameObject>("SpearBronze").GetComponent<ItemDrop>().m_itemData.m_shared.m_secondaryAttack;
            throwAttack = spear.Clone();
            throwAttack.m_attackProjectile = projectile;
            throwAttack.m_projectileVel = 28f;
            throwAttack.m_projectileVelMin = 28f;
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

        // The inventory icon, rendered from the finished model once the game's items are in.
        private static void RenderIcon()
        {
            ItemManager.OnItemsRegistered -= RenderIcon;
            try
            {
                var prefab = PrefabManager.Instance.GetPrefab(PrefabName);
                var sprite = RenderManager.Instance.Render(prefab, RenderManager.IsometricRotation);
                if (sprite) prefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_icons = new[] { sprite };
            }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Kraken Axe icon: {e.Message}"); }
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

    // The frost glow's strength follows Potato Tweaks' "7 - Visual effects" glow settings, live: "Upgrade glow" on uses
    // "Upgrade glow strength" (0 = off, 1 = as made, 2 = twice); off or without Tweaks it's as made.
    internal static class Glow
    {
        private const string TweaksId = "potato.tweaks", Section = "7 - Visual effects";
        private static ConfigEntry<bool> enabled;
        private static ConfigEntry<float> strength;
        private static readonly HashSet<Lamp> lamps = new HashSet<Lamp>();

        internal static float Factor => enabled != null && strength != null && enabled.Value ? Mathf.Max(0f, strength.Value) : 1f;

        internal static void Watch()
        {
            if (Chainloader.PluginInfos.TryGetValue(TweaksId, out var info) && info.Instance)
            {
                var config = info.Instance.Config;
                config.TryGetEntry(Section, "Upgrade glow", out enabled);
                config.TryGetEntry(Section, "Upgrade glow strength", out strength);
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
