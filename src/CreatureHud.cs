// PotatoFighter - showing creature levels above the game's two stars (Valheim): extra star icons on the health bar, the
// modifier in front of the name with the whole name in the modifier's colour, and the creature's look. The game has looks (size, colour,
// extra parts) for its own levels only; a creature above them keeps the highest one and grows 5% for each star past it
// (not in dungeons, where a bigger creature could get stuck).
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
        // How many stars each health bar last showed, so the extra stars are only rebuilt on a change.
        private static readonly Dictionary<object, int> shown = new Dictionary<object, int>();

        [HarmonyPatch(typeof(EnemyHud), "UpdateHuds")]
        internal static class Stars
        {
            private static bool Prepare() => Level2 != null && Level3 != null && Name != null;

            private static void Postfix(EnemyHud __instance)
            {
                if (!Creatures.On) return;
                var huds = Huds(__instance);
                if (huds == null) return;
                if (shown.Count > 4 * huds.Count + 64) shown.Clear();   // bars of creatures that have gone
                foreach (System.Collections.DictionaryEntry pair in huds)
                {
                    var c = pair.Key as Character;
                    var hud = pair.Value;
                    var level3 = hud == null ? null : Level3.GetValue(hud) as RectTransform;
                    if (!c || !level3) continue;
                    int stars = c.GetLevel() - 1;
                    var modifier = Creatures.Get(c);
                    // The game switches its star groups each frame (level 2 and 3 only), so the 3-and-up case does too.
                    if (stars >= 2)
                    {
                        (Level2.GetValue(hud) as RectTransform)?.gameObject.SetActive(false);
                        level3.gameObject.SetActive(true);
                    }
                    if (!shown.TryGetValue(hud, out int last) || last != stars)
                    {
                        shown[hud] = stars;
                        ShowExtraStars(level3, Mathf.Max(stars, 2));
                    }
                    if (modifier != Creatures.Modifier.None && Name.GetValue(hud) is TMP_Text name && !name.text.StartsWith("<color="))
                        name.text = $"<color=#{ColorUtility.ToHtmlStringRGB(ModifierColors[(int)modifier])}>{Creatures.Names[(int)modifier]} {name.text}</color>";
                }
            }
        }

        // The name takes the modifier's colour (the stars stay the game's own): Armored blue, Quick magenta, Fierce red,
        // Regenerating green, Splitting cyan.
        private static readonly Color[] ModifierColors =
        {
            Color.white, new Color(0.35f, 0.6f, 1f), new Color(1f, 0.35f, 1f), new Color(1f, 0.3f, 0.25f), new Color(0.35f, 1f, 0.35f), new Color(0.4f, 0.95f, 1f),
        };

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
