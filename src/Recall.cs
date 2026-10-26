// Weapon recall: a thrown weapon (the game's spears, and modded javelins and throwing axes, which all use the same
// "drop the item where the projectile lands" path) can be called back to the hotbar slot it came from with the throw
// button (middle mouse), while the hand it was thrown from is empty. Release builds only do this for PotatoFighter's
// own returning weapons (Plugin.OwnWeapons); test builds for every thrown weapon.
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

        private enum Hand { Right, Left, Both }

        private sealed class Thrown
        {
            public Hand Hand;
            public string Name;
            public string Prefab;
            public Vector2i Slot;
            public Projectile Flying;             // still in the air
            public ItemDrop.ItemData FlyingItem;  // the item the projectile will drop
            public ZDOID Drop = ZDOID.None;       // the item lying in the world
            public Vector3 Where;
            public bool Called;
            public float CalledAt;
            public float ThrownAt;
            public Minimap.PinData Pin;
            public GameObject Beam;
        }

        private static readonly List<Thrown> thrown = new List<Thrown>();
        // After a weapon comes back, the throw button does nothing until it's let go and a moment has passed, so the
        // press (or hold) that called it back doesn't throw it again straight away.
        private const float ThrowPause = 0.4f;
        private static bool waitForRelease;
        private static float pauseUntil;
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
                Tick(player);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Weapon recall is disabled: {e}");
                Plugin.RecallEnabled.Value = false;
            }
        }

        private static readonly AccessTools.FieldRef<Humanoid, ItemDrop.ItemData> HiddenRight = AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_hiddenRightItem");
        private static readonly AccessTools.FieldRef<Humanoid, ItemDrop.ItemData> HiddenLeft = AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_hiddenLeftItem");

        internal static bool Recallable(ItemDrop.ItemData item) =>
            Plugin.TestBuild || (item?.m_dropPrefab && Plugin.OwnWeapons.Contains(item.m_dropPrefab.name));

        // The hand(s) a weapon is held in, as Humanoid.EquipItem places it: bows and shields left, two-handed weapons both.
        private static Hand HandOf(ItemDrop.ItemData.ItemType type)
        {
            switch (type)
            {
                case ItemDrop.ItemData.ItemType.Bow:
                case ItemDrop.ItemData.ItemType.Shield: return Hand.Left;
                case ItemDrop.ItemData.ItemType.TwoHandedWeapon:
                case ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft: return Hand.Both;
                default: return Hand.Right;
            }
        }

        private static Hand HandOf(string prefab)
        {
            var go = string.IsNullOrEmpty(prefab) || !ObjectDB.instance ? null : ObjectDB.instance.GetItemPrefab(prefab);
            var drop = go ? go.GetComponent<ItemDrop>() : null;
            return drop?.m_itemData?.m_shared != null ? HandOf(drop.m_itemData.m_shared.m_itemType) : Hand.Right;
        }

        // A hand holding nothing, sheathed weapons included (pressing the throw button then just draws them as usual). A bow
        // or other two-handed weapon fills both hands, as the game treats it: equipping one empties the other hand, and its
        // throw button belongs to it (Better Archery's kick with a bow, a battleaxe's own secondary).
        private static bool HandEmpty(Player player, Hand hand)
        {
            var rightItem = player.RightItem ?? HiddenRight(player);
            var leftItem = player.LeftItem ?? HiddenLeft(player);
            bool bothTaken = Fills(rightItem) || Fills(leftItem);
            bool right = rightItem == null && !bothTaken;
            bool left = leftItem == null && !bothTaken;
            return hand == Hand.Right ? right : hand == Hand.Left ? left : right && left;
        }

        private static bool Fills(ItemDrop.ItemData item) => item?.m_shared != null && HandOf(item.m_shared.m_itemType) == Hand.Both ||
            item?.m_shared?.m_itemType == ItemDrop.ItemData.ItemType.Bow;

        private static bool CanCall(Player player, Thrown t) => !t.Called && HandEmpty(player, t.Hand);

        private static void CallAll(Player player)
        {
            int called = 0;
            float cost = Plugin.RecallStamina.Value;
            foreach (var t in thrown)
            {
                if (!CanCall(player, t)) continue;
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

                // In the air: wait for it to land (the drop is caught as it happens). A magical axe turns round in the air
                // when called, and on its own once it has flown its range without hitting anything.
                if (t.FlyingItem != null && t.Drop.IsNone())
                {
                    if (t.Flying && t.Prefab == KrakenAxe.PrefabName && (t.Called || Time.time - t.ThrownAt > KrakenAxe.MaxFlightSeconds))
                    {
                        var from = t.Flying.transform.position;
                        var item = t.FlyingItem;
                        var slot = new Thrown { Name = t.Name, Prefab = t.Prefab, Slot = t.Slot, Hand = t.Hand };
                        var view = t.Flying.GetComponent<ZNetView>();
                        if (view && view.IsValid() && view.IsOwner()) ZNetScene.instance.Destroy(t.Flying.gameObject);
                        else Destroy(t.Flying.gameObject);
                        Forget(i);
                        changed = true;
                        ReturnFlight.Begin(player, item, slot, from);
                        continue;
                    }
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
                if (t.Prefab == KrakenAxe.PrefabName)
                {
                    // A magical axe flies back to the hand: take the item now (nobody else can pick it up on the way)
                    // and hand it over when the flight arrives.
                    var item = drop.m_itemData;
                    var from = go.transform.position;
                    ZNetScene.instance.Destroy(go);
                    var slot = new Thrown { Name = t.Name, Prefab = t.Prefab, Slot = t.Slot, Hand = t.Hand };
                    Forget(i);
                    changed = true;
                    ReturnFlight.Begin(player, item, slot, from);
                    continue;
                }
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
            if (held != null && held.IsWeapon() && HandEmpty(player, t.Hand))
                player.EquipItem(held);
            player.m_pickupEffects.Create(player.transform.position, Quaternion.identity);
            waitForRelease = true;
            pauseUntil = Time.time + ThrowPause;
            return true;
        }

        // The thrown axe's way back: a spinning copy flies from where it lay to the right hand (about 25 m a second, at
        // least a third of a second), then the item goes into its slot. Only drawn on this player's screen.
        private sealed class ReturnFlight : MonoBehaviour
        {
            private const float Speed = 25f, MinTime = 0.35f, MaxTime = 1.2f, Spin = 792f; // same tumble as the throw
            private Player player;
            private ItemDrop.ItemData item;
            private Thrown slot;
            private Vector3 from;
            private float time, duration;
            private Transform pivot;
            private bool delivered;
            // The catching arm: only the right arm reaches out toward the incoming axe, laid over whatever the body is
            // doing (shoulder and elbow aimed at it after the animator has posed the character). Eases in, holds, eases out.
            private Transform upperArm, foreArm, hand;
            private float reach;
            private Vector3 aim = Vector3.forward;
            private const float ReachIn = 0.15f, ReachOut = 0.25f;
            private Vector3 last;
            private readonly HashSet<Character> struck = new HashSet<Character>();
            private const float ReturnDamage = 0.75f, HitRadius = 0.45f;
            private static int characterMask;

            internal static void Begin(Player player, ItemDrop.ItemData item, Thrown slot, Vector3 from)
            {
                var go = new GameObject("PotatoFighter return");
                go.transform.position = from;
                var flight = go.AddComponent<ReturnFlight>();
                flight.player = player; flight.item = item; flight.slot = slot; flight.from = from;
                flight.duration = Mathf.Clamp(Vector3.Distance(from, Hand(player)) / Speed, MinTime, MaxTime);
                flight.pivot = KrakenAxe.SpinningModel(go.transform, "spin").transform;
                flight.FindArm();
                flight.last = from;
                if (characterMask == 0) characterMask = LayerMask.GetMask("character", "character_net", "character_ghost", "hitbox", "character_noenv");
            }

            private static Vector3 Hand(Player p)
            {
                var vis = p ? p.GetComponent<VisEquipment>() : null;
                return vis && vis.m_rightHand ? vis.m_rightHand.position : (p ? p.transform.position + Vector3.up * 1.2f : Vector3.zero);
            }

            private void Update()
            {
                if (!player) { Destroy(gameObject); return; }
                time += Time.deltaTime;
                float k = Mathf.Clamp01(time / duration);
                float eased = k * k * (3f - 2f * k);
                var to = Hand(player);
                var pos = Vector3.Lerp(from, to, eased) + Vector3.up * Mathf.Sin(k * Mathf.PI) * 0.6f;
                var dir = to - pos;
                if (dir.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.LookRotation(dir);
                transform.position = pos;
                Strike(last, pos);
                last = pos;
                if (pivot) pivot.Rotate(Spin * Time.deltaTime, 0f, 0f, Space.Self);
                if (k >= 1f && !delivered)
                {
                    Deliver();
                    if (pivot) pivot.gameObject.SetActive(false);
                    Destroy(gameObject, ReachOut);
                }
            }

            private void OnDestroy() => Deliver();

            internal void FindArm()
            {
                var vis = player ? player.GetComponent<VisEquipment>() : null;
                hand = vis ? vis.m_rightHand : null;
                // m_rightHand is the attach point inside the hand; walk up to the hand, forearm and upper arm bones.
                var h = hand;
                while (h && !h.name.ToLowerInvariant().Contains("hand")) h = h.parent;
                if (h && h.name.ToLowerInvariant().EndsWith("_attach")) h = h.parent;
                hand = h;
                foreArm = hand ? hand.parent : null;
                upperArm = foreArm ? foreArm.parent : null;
            }

            private void LateUpdate()
            {
                if (!player || !upperArm || !foreArm || !hand) return;
                reach = Mathf.MoveTowards(reach, delivered ? 0f : 1f, Time.deltaTime / (delivered ? ReachOut : ReachIn));
                if (reach <= 0f) return;
                // Toward the axe, but never more than 70 degrees off where the player faces (no arm twisted behind the back);
                // after the catch, ease down from the last direction.
                if (!delivered)
                {
                    var toAxe = transform.position - upperArm.position;
                    var facing = player.transform.forward;
                    aim = Vector3.RotateTowards(facing, toAxe.sqrMagnitude > 1e-6f ? toAxe.normalized : facing, 70f * Mathf.Deg2Rad, 0f);
                }
                var target = upperArm.position + aim * 2f;
                Aim(upperArm, foreArm.position, target);
                Aim(foreArm, hand.position, target);
            }

            private void Aim(Transform bone, Vector3 child, Vector3 target)
            {
                var now = child - bone.position;
                var want = target - bone.position;
                if (now.sqrMagnitude < 1e-6f || want.sqrMagnitude < 1e-6f) return;
                bone.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(now, want), reach) * bone.rotation;
            }


            // Enemies the axe passes through on its way back take a hit (once each, 75% of a swing's damage), like the
            // throw; tamed animals, other players and anything the game doesn't count as an enemy are left alone.
            private void Strike(Vector3 a, Vector3 b)
            {
                var dir = b - a;
                float dist = dir.magnitude;
                if (dist < 1e-4f || item?.m_shared == null) return;
                foreach (var hit in Physics.SphereCastAll(a, HitRadius, dir / dist, dist, characterMask, QueryTriggerInteraction.Ignore))
                {
                    var c = hit.collider ? hit.collider.GetComponentInParent<Character>() : null;
                    if (!c || c == player || c.IsDead() || struck.Contains(c) || !BaseAI.IsEnemy(player, c)) continue;
                    struck.Add(c);
                    var data = new HitData();
                    data.m_damage = item.GetDamage();
                    data.m_damage.Modify(ReturnDamage * player.GetRandomSkillFactor(item.m_shared.m_skillType));
                    data.m_point = hit.point == Vector3.zero ? c.GetCenterPoint() : hit.point;
                    data.m_dir = dir / dist;
                    data.m_pushForce = item.m_shared.m_attackForce * 0.5f;
                    data.m_skill = item.m_shared.m_skillType;
                    data.m_toolTier = (short)item.m_shared.m_toolTier;
                    data.m_hitType = HitData.HitType.PlayerHit;
                    data.m_ranged = true;
                    data.SetAttacker(player);
                    c.Damage(data);
                    item.m_shared.m_hitEffect.Create(data.m_point, Quaternion.identity);
                    player.RaiseSkill(item.m_shared.m_skillType, 1f);
                }
            }

            private void Deliver()
            {
                if (delivered) return;
                delivered = true;
                var p = player ? player : Player.m_localPlayer;
                if (p && !GiveBack(p, item, slot))
                    ItemDrop.DropItem(item, item.m_stack, p.transform.position + p.transform.forward + Vector3.up, Quaternion.identity);
            }
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
            if (!go || !Plugin.Beam.Value)
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
            var color = Plugin.BeamColor.Value;
            line.startColor = color;
            line.endColor = new Color(color.r, color.g, color.b, 0f);
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
                var c = Plugin.BeamColor.Value;
                line.startColor = c;
                line.endColor = new Color(c.r, c.g, c.b, 0f);
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
                string prefab = f.Length > 8 ? f[8] : null;
                thrown.Add(new Thrown { Drop = new ZDOID(user, id), Slot = new Vector2i(x, y), Where = new Vector3(px, py, pz), Name = f[7], Prefab = prefab, Hand = HandOf(prefab) });
            }
        }

        // A throw by this player whose projectile drops the weapon where it lands.
        [HarmonyPatch(typeof(Projectile), nameof(Projectile.Setup))]
        private static class Throw
        {
            private static void Postfix(Projectile __instance, Character owner, ItemDrop.ItemData item)
            {
                if (!Plugin.RecallEnabled.Value || item?.m_shared == null || !__instance.m_respawnItemOnHit || owner == null || owner != Player.m_localPlayer || !Recallable(item)) return;
                thrown.Add(new Thrown { Hand = HandOf(item.m_shared.m_itemType), Name = item.m_shared.m_name, Prefab = item.m_dropPrefab ? item.m_dropPrefab.name : null, Slot = item.m_gridPos, Flying = __instance, FlyingItem = item, Where = owner.transform.position, ThrownAt = Time.time });
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

        // The throw button calls back every thrown weapon whose hand is empty, instead of attacking. With nothing to call
        // back (a second spear in that hand, or a weapon already on its way), the button does what it always does.
        [HarmonyPatch(typeof(Player), nameof(Player.SetControls))]
        private static class ThrowButton
        {
            private static void Prefix(Player __instance, ref bool secondaryAttack, ref bool secondaryAttackHold)
            {
                if (__instance == Player.m_localPlayer && (waitForRelease || Time.time < pauseUntil))
                {
                    if (!secondaryAttack && !secondaryAttackHold) waitForRelease = false;
                    if (waitForRelease || Time.time < pauseUntil)
                    {
                        secondaryAttack = false;
                        secondaryAttackHold = false;
                        return;
                    }
                }
                if (!secondaryAttack || __instance != Player.m_localPlayer || !Plugin.RecallEnabled.Value || __instance.IsDead()) return;
                if (!thrown.Any(t => CanCall(__instance, t))) return;
                CallAll(__instance);
                waitForRelease = true;
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
