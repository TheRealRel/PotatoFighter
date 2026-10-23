// Weapon recall: a thrown weapon (the game's spears, and modded javelins and throwing axes, which all use the same
// "drop the item where the projectile lands" path) can be called back to the hotbar slot it came from with one key.
// Each throw is followed from the projectile to the item it drops; the drop's network id is kept (also in the
// character's save), so a weapon left behind can be recalled after a relog or a trip away, once you're near it again.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace PotatoFighter
{
    internal sealed class Recall : MonoBehaviour
    {
        private const string SaveKey = "PotatoFighter.Thrown";
        private const float GiveUpAfter = 5f;       // seconds to wait for another player's game to hand a weapon over
        private const float CheckGoneWithin = 40f;  // a weapon this close that no longer exists was picked up or destroyed

        private sealed class Thrown
        {
            public string Name;
            public string Prefab;
            public Vector2i Slot;
            public Projectile Flying;             // still in the air
            public ItemDrop.ItemData FlyingItem;  // the item the projectile will drop
            public ZDOID Drop = ZDOID.None;       // the item lying in the world
            public Vector3 Where;
            public bool Called;
            public float CalledAt;
            public Minimap.PinData Pin;
            public GameObject Beam;
        }

        private static readonly List<Thrown> thrown = new List<Thrown>();
        private static Projectile landing;   // the projectile whose item is being dropped right now
        private static Player loadedFor;
        private static Material beamMaterial;

        private void Update()
        {
            var player = Player.m_localPlayer;
            if (!player || !ZNetScene.instance || !ZNet.instance)
            {
                if (!player) loadedFor = null;
                return;
            }
            try
            {
                if (loadedFor != player) Load(player);
                if (Plugin.RecallEnabled.Value && Plugin.RecallKey.Value.IsDown() && CanTakeInput() && !player.IsDead()) CallAll(player);
                Tick(player);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Weapon recall is disabled: {e}");
                Plugin.RecallEnabled.Value = false;
            }
        }

        private static bool CanTakeInput() =>
            !(Chat.instance && Chat.instance.HasFocus()) && !global::Console.IsVisible() && !TextInput.IsVisible() && !Menu.IsVisible() &&
            !InventoryGui.IsVisible() && !Minimap.IsOpen() && !StoreGui.IsVisible();

        private static void CallAll(Player player)
        {
            int called = 0;
            float cost = Plugin.RecallStamina.Value;
            foreach (var t in thrown)
            {
                if (t.Called) continue;
                if (cost > 0f && !player.HaveStamina(cost))
                {
                    if (called == 0) Hud.instance?.StaminaBarEmptyFlash();
                    break;
                }
                if (cost > 0f) player.UseStamina(cost);
                t.Called = true;
                t.CalledAt = Time.time;
                called++;
            }
        }

        private static void Tick(Player player)
        {
            bool changed = false;
            for (int i = thrown.Count - 1; i >= 0; i--)
            {
                var t = thrown[i];
                GameObject go = t.Drop.IsNone() ? null : ZNetScene.instance.FindInstance(t.Drop);
                if (go) t.Where = go.transform.position;

                // In the air: wait for it to land (the drop is caught as it happens).
                if (t.FlyingItem != null && t.Drop.IsNone())
                {
                    if (t.Flying) continue;
                    // The projectile is gone without dropping anything (lost in lava, or it timed out). The weapon no
                    // longer exists anywhere, so a recall this session can still bring it back.
                    if (t.Called && GiveBack(player, t.FlyingItem.Clone(), t)) Forget(i);
                    continue;
                }

                // Lying in the world but not here: picked up by someone, or just not loaded.
                if (!go)
                {
                    bool near = Vector3.Distance(player.transform.position, t.Where) < CheckGoneWithin;
                    if (near && ZDOMan.instance.GetZDO(t.Drop) == null) { Forget(i); changed = true; continue; }
                    if (t.Called)
                    {
                        player.Message(MessageHud.MessageType.Center, Localization.instance.Localize(t.Name) + " is too far away to call back");
                        t.Called = false;
                    }
                    ShowMarkers(t, null);
                    continue;
                }

                ShowMarkers(t, go);
                if (!t.Called) continue;
                var drop = go.GetComponent<ItemDrop>();
                if (!drop) { Forget(i); changed = true; continue; }
                if (!drop.CanPickup(false))
                {
                    // Another player's game owns it: ask for it, and keep trying for a few seconds.
                    if (Time.time - t.CalledAt < GiveUpAfter) drop.RequestOwn();
                    else t.Called = false;
                    continue;
                }
                drop.Load();
                if (GiveBack(player, drop.m_itemData, t))
                {
                    ZNetScene.instance.Destroy(go);
                    Forget(i);
                    changed = true;
                }
            }
            if (changed) Save(player);
        }

        // Into the slot it was thrown from if that's free, otherwise anywhere; in hand if the hands are empty.
        private static bool GiveBack(Player player, ItemDrop.ItemData item, Thrown t)
        {
            var inventory = player.GetInventory();
            bool slotFree = t.Slot.x >= 0 && t.Slot.y >= 0 && t.Slot.x < inventory.GetWidth() && t.Slot.y < inventory.GetHeight() &&
                inventory.GetItemAt(t.Slot.x, t.Slot.y) == null;
            bool added = slotFree ? inventory.AddItem(item, t.Slot) : inventory.AddItem(item);
            if (!added)
            {
                player.Message(MessageHud.MessageType.Center, "$msg_noroom");
                t.Called = false;
                return false;
            }
            var held = inventory.GetAllItems().FirstOrDefault(x => x == item) ?? inventory.GetItemAt(t.Slot.x, t.Slot.y);
            if (held != null && held.IsWeapon() && player.GetCurrentWeapon() == player.m_unarmedWeapon?.m_itemData)
                player.EquipItem(held);
            player.m_pickupEffects.Create(player.transform.position, Quaternion.identity);
            return true;
        }

        private static void Forget(int index)
        {
            var t = thrown[index];
            if (t.Pin != null && Minimap.instance) Minimap.instance.RemovePin(t.Pin);
            if (t.Beam) Destroy(t.Beam);
            thrown.RemoveAt(index);
        }

        private static void ShowMarkers(Thrown t, GameObject go)
        {
            if (Minimap.instance)
            {
                if (Plugin.RecallPin.Value && t.Pin == null)
                    t.Pin = Minimap.instance.AddPin(t.Where, Minimap.PinType.Icon3, Localization.instance.Localize(t.Name), false, false);
                else if (!Plugin.RecallPin.Value && t.Pin != null) { Minimap.instance.RemovePin(t.Pin); t.Pin = null; }
                if (t.Pin != null) t.Pin.m_pos = t.Where;
            }
            if (!go || !Plugin.BeamFor(t.Prefab))
            {
                if (t.Beam) Destroy(t.Beam);
                return;
            }
            if (t.Beam && t.Beam.transform.parent == go.transform) return;
            if (t.Beam) Destroy(t.Beam);
            t.Beam = MakeBeam(go.transform);
        }

        // A thin column of warm light from the weapon up into the sky, only on this player's screen.
        private static GameObject MakeBeam(Transform parent)
        {
            if (!beamMaterial)
            {
                var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Legacy Shaders/Particles/Additive");
                if (!shader) return null;
                beamMaterial = new Material(shader) { name = "potato_recall_beam" };
            }
            var beam = new GameObject("PotatoRecallBeam");
            beam.transform.SetParent(parent, false);
            var line = beam.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.SetPosition(0, parent.position);
            line.SetPosition(1, parent.position + Vector3.up * 14f);
            line.startWidth = 0.07f;
            line.endWidth = 0.02f;
            line.startColor = new Color(1f, 0.85f, 0.45f, 0.85f);
            line.endColor = new Color(1f, 0.85f, 0.45f, 0f);
            line.sharedMaterial = beamMaterial;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            beam.AddComponent<BeamFollow>();
            return beam;
        }

        private sealed class BeamFollow : MonoBehaviour
        {
            private LineRenderer line;
            private void Awake() => line = GetComponent<LineRenderer>();
            private void LateUpdate()
            {
                var p = transform.parent ? transform.parent.position : transform.position;
                line.SetPosition(0, p);
                line.SetPosition(1, p + Vector3.up * 14f);
            }
        }

        // Kept in the character's save as "userID:id:x:y:posX:posY:posZ:name|...", so weapons left behind are remembered.
        private static void Save(Player player)
        {
            var parts = thrown.Where(t => !t.Drop.IsNone()).Select(t => string.Join(":", new[]
            {
                t.Drop.UserID.ToString(CultureInfo.InvariantCulture), t.Drop.ID.ToString(CultureInfo.InvariantCulture),
                t.Slot.x.ToString(CultureInfo.InvariantCulture), t.Slot.y.ToString(CultureInfo.InvariantCulture),
                t.Where.x.ToString("0.##", CultureInfo.InvariantCulture), t.Where.y.ToString("0.##", CultureInfo.InvariantCulture),
                t.Where.z.ToString("0.##", CultureInfo.InvariantCulture), t.Name.Replace(":", "").Replace("|", ""),
                (t.Prefab ?? "").Replace(":", "").Replace("|", ""),
            }));
            string text = string.Join("|", parts);
            if (text.Length == 0) player.m_customData.Remove(SaveKey);
            else player.m_customData[SaveKey] = text;
        }

        private static void Load(Player player)
        {
            for (int i = thrown.Count - 1; i >= 0; i--) Forget(i);
            loadedFor = player;
            if (!player.m_customData.TryGetValue(SaveKey, out var text) || string.IsNullOrEmpty(text)) return;
            foreach (var part in text.Split('|'))
            {
                var f = part.Split(':');
                if (f.Length < 8) continue;
                var inv = CultureInfo.InvariantCulture;
                if (!long.TryParse(f[0], NumberStyles.Integer, inv, out long user) || !uint.TryParse(f[1], NumberStyles.Integer, inv, out uint id) ||
                    !int.TryParse(f[2], NumberStyles.Integer, inv, out int x) || !int.TryParse(f[3], NumberStyles.Integer, inv, out int y) ||
                    !float.TryParse(f[4], NumberStyles.Float, inv, out float px) || !float.TryParse(f[5], NumberStyles.Float, inv, out float py) ||
                    !float.TryParse(f[6], NumberStyles.Float, inv, out float pz)) continue;
                thrown.Add(new Thrown { Drop = new ZDOID(user, id), Slot = new Vector2i(x, y), Where = new Vector3(px, py, pz), Name = f[7], Prefab = f.Length > 8 ? f[8] : null });
            }
        }

        // A throw by this player whose projectile drops the weapon where it lands.
        [HarmonyPatch(typeof(Projectile), nameof(Projectile.Setup))]
        private static class Throw
        {
            private static void Postfix(Projectile __instance, Character owner, ItemDrop.ItemData item)
            {
                if (!Plugin.RecallEnabled.Value || item?.m_shared == null || !__instance.m_respawnItemOnHit || owner == null || owner != Player.m_localPlayer) return;
                thrown.Add(new Thrown { Name = item.m_shared.m_name, Prefab = item.m_dropPrefab ? item.m_dropPrefab.name : null, Slot = item.m_gridPos, Flying = __instance, FlyingItem = item, Where = owner.transform.position });
            }
        }

        [HarmonyPatch(typeof(Projectile), "SpawnOnHit")]
        private static class Landing
        {
            private static void Prefix(Projectile __instance) => landing = __instance;
            private static void Finalizer() => landing = null;
        }

        // The weapon lands: follow the item it became.
        [HarmonyPatch(typeof(ItemDrop), nameof(ItemDrop.DropItem))]
        private static class Landed
        {
            private static void Postfix(ItemDrop.ItemData item, ItemDrop __result)
            {
                if (!landing || !__result) return;
                var t = thrown.FirstOrDefault(x => x.Flying == landing && x.FlyingItem == item);
                var view = __result.GetComponent<ZNetView>();
                if (t == null || !view || view.GetZDO() == null) return;
                t.Drop = view.GetZDO().m_uid;
                t.Where = __result.transform.position;
                t.Flying = null;
                t.FlyingItem = null;
                if (Player.m_localPlayer) Save(Player.m_localPlayer);
            }
        }

        // The throw button with empty hands calls thrown weapons back instead of attacking. Holding a weapon that has its
        // own throw or other secondary attack (a second spear) keeps that attack.
        [HarmonyPatch(typeof(Player), nameof(Player.SetControls))]
        private static class ThrowButton
        {
            private static void Prefix(Player __instance, ref bool secondaryAttack, ref bool secondaryAttackHold)
            {
                if (!secondaryAttack || __instance != Player.m_localPlayer || !Plugin.RecallEnabled.Value || !Plugin.RecallWithThrow.Value) return;
                if (!thrown.Any(t => !t.Called) || __instance.IsDead()) return;
                var weapon = __instance.GetCurrentWeapon();
                bool emptyHands = weapon == null || weapon == __instance.m_unarmedWeapon?.m_itemData;
                if (!emptyHands && weapon.HaveSecondaryAttack()) return;
                CallAll(__instance);
                secondaryAttack = false;
                secondaryAttackHold = false;
            }
        }

        // Picked up the normal way: nothing left to recall.
        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.Pickup))]
        private static class PickedUp
        {
            private static void Prefix(Humanoid __instance, GameObject go, out ZDOID __state)
            {
                __state = ZDOID.None;
                var view = go ? go.GetComponent<ZNetView>() : null;
                if (__instance == Player.m_localPlayer && view && view.GetZDO() != null) __state = view.GetZDO().m_uid;
            }

            private static void Postfix(Humanoid __instance, bool __result, ZDOID __state)
            {
                if (!__result || __state.IsNone()) return;
                int i = thrown.FindIndex(x => x.Drop == __state);
                if (i < 0) return;
                Forget(i);
                if (Player.m_localPlayer) Save(Player.m_localPlayer);
            }
        }
    }
}
