Combat changes for Valheim. Creatures spawn with more stars the further you travel, up to five, and some with a modifier you can read from their coloured name: Armored, Quick, Fierce, Regenerating or Splitting. Their young can inherit it. Weapon switches no longer slow you to a walk, a loaded crossbow stays loaded, and blocking cancels an attack mid-swing. PotatoFighter also contains custom weapons and mechanics, starting with the Kraken Axe, a frost axe you throw and call back to your hand. A few fixes for bows round it out.

![](https://raw.githubusercontent.com/TheRealRel/PotatoFighter/main/docs/images/icon-stars.png)

**Creatures get more stars the further you travel.** A fresh spawn can get extra stars on top of the game's own, up to five: more often in later biomes, and a little more often once that biome's boss is beaten. Creatures the game never gives stars (Lox, Deathsquitos, Serpents, Drakes and Wraiths) get them less often. Tamed animals, bosses, bounty targets and creatures already in your world keep their level. Wild creatures above the server's star limit get one new roll when they load, so a world coming from another level mod settles down by itself.

| Biome | Chance of a star | Each further star | Most stars |
|---|---|---|---|
| Meadows | 15% | 30% | 2 |
| Black Forest | 27% | 38% | 3 |
| Swamp and the ocean | 38% | 45% | 4 |
| Mountain | 48% | 53% | 5 |
| Plains | 60% | 60% | 5 |
| Mistlands and later | 68% | 68% | 5 |

The game already gives a starred creature more health (+100% a star) and harder hits (+50% a star). Loot grows more gently than the game's doubling: half again per star, so a 2-star creature drops about twice the normal amount instead of four times.

**Some come with a modifier,** shown by the colour of their name. A creature with one star has one 35% of the time, and 10% more for every further star.

| Modifier | Name colour | What it does |
|---|---|---|
| Armored | Blue | Takes half damage and moves a quarter slower. Never on very big creatures or bosses. |
| Quick | Magenta | Moves about a third faster. |
| Fierce | Red | Hits 30% harder. |
| Regenerating | Green | Heals 1% of its health every second. |
| Splitting | Cyan | On death, splits into two with half its stars. |

**Their young can inherit it.** A newborn gets its parent's stars, as in the game, and keeps its modifier half the time. A youngster always keeps its own when it grows up. Eggs pass on stars only.

Creature levels stay off by themselves while StarLevelSystem or Creature Level and Loot Control is installed.

![](https://raw.githubusercontent.com/TheRealRel/PotatoFighter/main/docs/images/icon-axe-front.png) ![](https://raw.githubusercontent.com/TheRealRel/PotatoFighter/main/docs/images/icon-axe-turned.png) ![](https://raw.githubusercontent.com/TheRealRel/PotatoFighter/main/docs/images/icon-axe-back.png)

**The Kraken Axe is a one-handed frost axe that always comes back.** It swings with the game's own axe attacks and fells trees. Middle mouse throws it in a straight, spinning line, and it hits whatever it meets. With your hand empty, press middle mouse again and it turns in the air, or lifts off the ground, and flies back to your hand, hitting enemies on the way. If it flies about 55 metres without hitting anything, it turns round by itself.

![Throwing the Kraken Axe out over the sea and catching it on the way back](https://raw.githubusercontent.com/TheRealRel/PotatoFighter/main/docs/images/catch.gif)

![Fighting Greydwarves with the Kraken Axe](https://raw.githubusercontent.com/TheRealRel/PotatoFighter/main/docs/images/fight.gif)

**Shift and middle mouse is the overhead chop.** Where it lands, the ice bursts: one blast of frost reaching about three metres around it, which slows what it hits. The chop is cheaper than usual as your Axes skill grows, down to half its stamina at 100.

![The ice burst from the overhead chop](https://raw.githubusercontent.com/TheRealRel/PotatoFighter/main/docs/images/ice-burst.gif) ![The ice burst on two Greydwarves](https://raw.githubusercontent.com/TheRealRel/PotatoFighter/main/docs/images/ice-greydwarves.gif)

The frost in its grooves glows softly. With Potato Tweaks installed, the glow follows its "Upgrade glow" settings, like the game's other upgraded weapons.

| | Level 1 | Each level up |
|---|---|---|
| Slash | 100 | +6 |
| Chop | 80 | +4 |
| Frost | 40 | +4 |
| Spirit | 20 | +2 |
| Ice explosion (frost) | 90 | +10 |
| Durability | 300 | +50 |
| Block | 57 | |

An Ashlands weapon, made at the Black Forge (level 3) from 16 flametal, 10 silver, 6 freeze glands and 4 serpent scales. Each upgrade costs 6 flametal, 4 silver and 3 freeze glands. It can be enchanted with Epic Loot, and an enchanted axe stays enchanted through every throw. Epic Loot's Throwable enchantment isn't offered for it, since the axe already throws. Reforged Potential treats it as an Ashlands weapon, upgraded further with the Flametal Battle Idol.

**Calling it back.** The call costs a little stamina: 15 at Axes 0, less as the skill grows, nothing at 100. A Kraken Axe lying on the ground has a beam of light over it and a pin on your map until it's back in your hand, and it's remembered through a relog. It only answers when it's nearby, in the part of the world that's loaded around you.

![](https://raw.githubusercontent.com/TheRealRel/PotatoFighter/main/docs/images/icon-shield.png)

**Attack cancel.** Press block during an attack's windup or recovery and the attack stops at once, with your guard up, so you can parry mid-swing. A hit the swing hasn't reached never lands, while stamina already paid and a hit already dealt stay. Cancelling never lets you attack faster.

![](https://raw.githubusercontent.com/TheRealRel/PotatoFighter/main/docs/images/icon-bow.png)

**Fast bow shots aren't lost.** With a quick draw (high Bows skill, or Epic Loot's Quick Draw), the next arrow is fully drawn before the last shot's animation ends, and the game quietly dropped a release made then. Now the arrow stays drawn and flies as soon as the bow can fire. Blocking, switching weapons or dying before then cancels it without using the arrow. With Epic Loot, a cancelled attack no longer fires a Triple Shot volley.

**Weapon switches don't slow you.** Switching weapons, shields, torches or tools, and sheathing or drawing with R, no longer drops you from a run to a walk while the animation plays. Armor keeps the game's own equip time. Two choices are off by default: a switch made while sprinting can go ahead for a little stamina instead of being cancelled, and an attack can cut a switch short to swing the new weapon at once. The idea comes from Skarif's FastSwap; PotatoFighter's version is its own code, and it steps aside when FastSwap is installed.

**A loaded crossbow stays loaded.** Switch to something else and back, and it's still loaded. Each crossbow keeps its own load, modded ones too, and the bolt is only used when you fire.

If you had Potato Tweaks before, PotatoFighter takes over your attack cancel and bow choices from its settings the first time it starts.

## Settings

Under PotatoFighter in Configuration Manager, in three sections. The ones marked server are set by the server and sent to every player.

| Setting | Default | What it does |
|---|---|---|
| **1 - Weapons** | | |
| Weapon recall (server) | On | Middle mouse calls a thrown Kraken Axe back while the hand it was thrown from is empty. |
| Recall uses stamina (server) | On | The call costs stamina: 15 at skill 0 in the weapon's own skill, less with skill, nothing at 100. Off: free. |
| Recall map pin | On | A thrown weapon has a pin on the map until it's back in your hands. |
| Light beam | On | A beam of light over a thrown weapon lying on the ground. |
| Light beam color | Gold | The beam's colour. Its alpha is how strong the beam is at the bottom. |
| Explosion size (server) | 100 | The size of every PotatoFighter weapon's explosions, in percent of their own size (50-200). At 100 the Kraken Axe's ice burst reaches 2.9 m. |
| **2 - Combat** | | |
| Attack cancel (server) | On | Block cancels an attack's windup or recovery. |
| Keep fast bow shots | On | A fully drawn arrow let go too early waits and flies as soon as the bow can fire. |
| Fast swap (server) | On | Weapon switches and sheathing with R don't slow you to a walk. |
| Fast swap while sprinting (server) | Off | A switch made while sprinting goes ahead instead of being cancelled by the sprint, for stamina. |
| Fast swap stamina while sprinting (server) | 10 | The stamina that costs (0-50). Without that much, the switch doesn't happen. |
| Attack cuts a swap short (server) | Off | Attacking or drawing a bow during a switch finishes it at once and goes ahead with the new weapon. |
| Crossbows stay loaded (server) | On | A loaded crossbow stays loaded when you switch away and back. |
| **3 - Creatures** | | |
| Creature levels (server) | On | Extra stars and modifiers on fresh spawns, as above. |
| Creature max stars (server) | 5 | Most stars a creature can spawn with (2-10). |
| Creature star chance (server) | 100 | How likely the first extra star is, in percent of the table's chance (0: only the game's own stars). |
| Creature modifier chance (server) | 100 | How likely a starred creature gets a modifier, in percent of the normal chance (0: none). |
| Offspring modifier chance (server) | 50 | Chance in percent that a newborn keeps its parent's modifier. |

Bug reports go to [GitHub Issues](https://github.com/TheRealRel/PotatoFighter/issues), where the source lives too. Questions are welcome on Discord: [relanebeis](https://discord.com/users/373574370267889674).

Every player and the server need PotatoFighter, along with [BepInExPack Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/) and [Jötunn](https://thunderstore.io/c/valheim/p/ValheimModding/Jotunn/). Attack cancel and the bow fix are adapted from EpicLoot Progression Fix (MIT).

The code is under the MIT License. The Kraken Axe's model and textures, the icon, page icons and screenshots are © TheRealRel under [CC BY-NC 4.0](https://creativecommons.org/licenses/by-nc/4.0/): free to share and adapt with credit, but not for commercial use. See LICENSE for the details.
