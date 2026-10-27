// PotatoFighter - showing creature levels above the game's two stars (Valheim): extra star icons on the health bar, the
// modifier in front of the name and in the stars' colour, and the creature's look. The game has looks (size, colour, extra parts) for its own levels
// only; a creature above them keeps the highest one and grows 5% for each star past it (not in dungeons, where a bigger
// creature could get stuck).
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace PotatoFighter
{
    internal static class CreatureHud
    {
        private const float GrowthPerStar = 0.05f, DungeonHeight = 4500f;
        // EnemyHud.HudData is private: its fields are found by name.
        private static readonly AccessTools.FieldRef<EnemyHud, System.Collections.IDictionary> Huds = AccessTools.FieldRefAccess<EnemyHud, System.Collections.IDictionary>("m_huds");
        private static readonly System.Type HudType = AccessTools.Inner(typeof(EnemyHud), "HudData");
        private static readonly FieldInfo Level2 = AccessTools.Field(HudType, "m_level2"), Level3 = AccessTools.Field(HudType, "m_level3"), Name = AccessTools.Field(HudType, "m_name");
        private static bool logged;

        [HarmonyPatch(typeof(EnemyHud), "UpdateHuds")]
        internal static class Stars
        {
            private static bool Prepare() => Level2 != null && Level3 != null && Name != null;

            private static void Postfix(EnemyHud __instance)
            {
                if (!Creatures.On) return;
                var huds = Huds(__instance);
                if (huds == null) return;
                foreach (System.Collections.DictionaryEntry pair in huds)
                {
                    var c = pair.Key as Character;
                    var hud = pair.Value;
                    var level3 = hud == null ? null : Level3.GetValue(hud) as RectTransform;
                    if (!c || !level3) continue;
                    int stars = c.GetLevel() - 1;
                    if (stars >= 2)
                    {
                        (Level2.GetValue(hud) as RectTransform)?.gameObject.SetActive(false);
                        level3.gameObject.SetActive(true);
                        ShowExtraStars(level3, stars);
                    }
                    else ShowExtraStars(level3, 2);
                    var modifier = Creatures.Get(c);
                    Tint(Level2.GetValue(hud) as RectTransform, modifier);
                    Tint(level3, modifier);
                    if (modifier != Creatures.Modifier.None && Name.GetValue(hud) is TMP_Text name)
                    {
                        string prefix = Creatures.Names[(int)modifier] + " ";
                        if (!name.text.StartsWith(prefix)) name.text = prefix + name.text;
                    }
                }
            }
        }

        // Stars take the modifier's colour, like CLLC's: Armored blue, Quick magenta, Fierce red, Regenerating green,
        // Splitting white. Plain stars keep the game's own colour (read once from its star image before any tint).
        private static readonly Color[] ModifierColors =
        {
            default, new Color(0.35f, 0.6f, 1f), new Color(1f, 0.35f, 1f), new Color(1f, 0.25f, 0.2f), new Color(0.35f, 1f, 0.35f), Color.white,
        };
        private static Color? starColor;

        private static void Tint(RectTransform group, Creatures.Modifier modifier)
        {
            if (!group) return;
            foreach (var image in group.GetComponentsInChildren<UnityEngine.UI.Image>(true))
            {
                if (starColor == null) starColor = image.color;
                var color = modifier == Creatures.Modifier.None ? starColor.Value : ModifierColors[(int)modifier];
                if (image.color != color) image.color = color;
            }
        }

        // The game's 2-star group holds two star images side by side; copies of the last one, one step further each, make
        // the rest. Copies beyond the creature's stars are hidden, so a hud reused for another creature shows the right count.
        private static void ShowExtraStars(RectTransform group, int stars)
        {
            int own = 0;
            RectTransform last = null, before = null;
            foreach (RectTransform child in group)
            {
                if (child.name.StartsWith("PotatoStar")) continue;
                own++;
                before = last;
                last = child;
            }
            if (Creatures.Diagnostics && !logged)
            {
                logged = true;
                Plugin.Log.LogInfo($"[Levels] health bar star group '{group.name}': {own} star image(s){(last ? $", last at {last.anchoredPosition}" : "")}{(before ? $", one before at {before.anchoredPosition}" : "")}");
            }
            if (!last || !before) return;
            var step = last.anchoredPosition - before.anchoredPosition;
            for (int i = own; i < Mathf.Max(stars, own) || group.Find("PotatoStar" + i); i++)
            {
                var star = group.Find("PotatoStar" + i) as RectTransform;
                if (!star && i < stars)
                {
                    star = Object.Instantiate(last, group);
                    star.name = "PotatoStar" + i;
                    star.anchoredPosition = last.anchoredPosition + step * (i - own + 1);
                }
                if (star) star.gameObject.SetActive(i < stars);
            }
        }

        // Above the game's own levels: show the highest look it has, then grow a little per extra star.
        [HarmonyPatch(typeof(LevelEffects), nameof(LevelEffects.SetupLevelVisualization))]
        internal static class Look
        {
            private static void Prefix(LevelEffects __instance, ref int level, out int __state)
            {
                __state = level;
                if (!Creatures.On || __instance.m_levelSetups == null) return;
                int highest = __instance.m_levelSetups.Count + 1;
                if (level > highest) level = highest;
            }

            private static void Postfix(LevelEffects __instance, int level, int __state)
            {
                // Only where the game set the size itself just now (it has looks for this creature), so it never compounds.
                if (!Creatures.On || __state <= level || level < 2 || __instance.transform.position.y > DungeonHeight) return;
                __instance.transform.localScale *= 1f + GrowthPerStar * (__state - level);
            }
        }
    }
}
