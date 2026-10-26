Weapons and combat fixes for Valheim. The first weapon is the Kraken Axe, a frost axe you throw and call back to your hand. Attack cancel and the fast bow shot fix, formerly in Potato Tweaks, live here now.

![](https://raw.githubusercontent.com/TheRealRel/PotatoFighter/main/docs/images/icon-axe-front.png) ![](https://raw.githubusercontent.com/TheRealRel/PotatoFighter/main/docs/images/icon-axe-turned.png) ![](https://raw.githubusercontent.com/TheRealRel/PotatoFighter/main/docs/images/icon-axe-back.png)

**The Kraken Axe is a one-handed frost axe that always comes back.** It swings with the game's own axe attacks and fells trees. Middle mouse throws it in a straight, spinning line, and it hits whatever it meets. With your hand empty, press middle mouse again and it turns in the air, or lifts off the ground, and flies back to your hand, hitting enemies on the way. If it flies about 55 metres without hitting anything, it turns round by itself.

**Shift and middle mouse is the overhead chop.** Where it lands, the ice bursts: one blast of frost reaching about three metres around it, which slows what it hits. The chop is cheaper than usual as your Axes skill grows, down to half its stamina at 100.

The frost in its grooves glows softly. With Potato Tweaks installed, the glow follows its "Upgrade glow" settings, like the game's other upgraded weapons.

| | Level 1 | Each level up |
|---|---|---|
| Slash | 70 | +5 |
| Chop | 60 | +3 |
| Frost | 30 | +3 |
| Spirit | 20 | +2 |
| Ice explosion (frost) | 60 | +8 |
| Durability | 250 | +50 |
| Block | 48 | |

Made at the Black Forge (level 1) from 10 black metal, 6 Yggdrasil wood, 4 eitr, 4 freeze glands and 2 serpent scales. Each upgrade costs 5 black metal, 2 eitr and 2 freeze glands. It can be enchanted with Epic Loot, and an enchanted axe stays enchanted through every throw. Reforged Potential treats it as a Mistlands weapon, upgraded further with the Mistlands weapon idol.

**Calling it back.** The call costs a little stamina: 15 at Axes 0, less as the skill grows, nothing at 100. A Kraken Axe lying on the ground has a beam of light over it and a pin on your map until it's back in your hand, and it's remembered through a relog. It only answers when it's nearby, in the part of the world that's loaded around you.

![](https://raw.githubusercontent.com/TheRealRel/PotatoFighter/main/docs/images/icon-shield.png)

**Attack cancel.** Press block during an attack's windup or recovery and the attack stops at once, with your guard up, so you can parry mid-swing. A hit the swing hasn't reached never lands, while stamina already paid and a hit already dealt stay. Cancelling never lets you attack faster.

![](https://raw.githubusercontent.com/TheRealRel/PotatoFighter/main/docs/images/icon-bow.png)

**Fast bow shots aren't lost.** With a quick draw (high Bows skill, or Epic Loot's Quick Draw), the next arrow is fully drawn before the last shot's animation ends, and the game quietly dropped a release made then. Now the arrow stays drawn and flies as soon as the bow can fire. Blocking, switching weapons or dying before then cancels it without using the arrow. With Epic Loot, a cancelled attack no longer fires a Triple Shot volley.

If you had Potato Tweaks before, PotatoFighter takes over your attack cancel and bow choices from its settings the first time it starts.

## Settings

Under PotatoFighter in Configuration Manager. The ones marked server are set by the server and sent to every player.

| Setting | Default | What it does |
|---|---|---|
| Weapon recall: Enabled (server) | On | Middle mouse calls a thrown Kraken Axe back while the hand it was thrown from is empty. |
| Weapon recall: Uses stamina (server) | On | The call costs stamina: 15 at skill 0 in the weapon's own skill, less with skill, nothing at 100. Off: free. |
| Weapon recall: Map pin | On | A thrown weapon has a pin on the map until it's back in your hands. |
| Light beam | On | A beam of light over a thrown weapon lying on the ground. |
| Light beam: Color | Gold | The beam's colour. Its alpha is how strong the beam is at the bottom. |
| Attack cancel: Enabled (server) | On | Block cancels an attack's windup or recovery. |
| Bows: Keep fast shots | On | A fully drawn arrow let go too early waits and flies as soon as the bow can fire. |
| Explosions: Explosion size (server) | 100 | The size of every PotatoFighter weapon's explosions, in percent of their own size (50-200). At 100 the Kraken Axe's ice burst reaches 2.9 m. |

Bug reports go to [GitHub Issues](https://github.com/TheRealRel/PotatoFighter/issues), where the source lives too. Questions are welcome on Discord: [relanebeis](https://discord.com/users/373574370267889674).

Every player and the server need PotatoFighter, along with [BepInExPack Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/) and [Jötunn](https://thunderstore.io/c/valheim/p/ValheimModding/Jotunn/). Attack cancel and the bow fix are adapted from EpicLoot Progression Fix (MIT).

The code is under the MIT License. The Kraken Axe's model and textures, the icon, page icons and screenshots are © TheRealRel under [CC BY-NC 4.0](https://creativecommons.org/licenses/by-nc/4.0/): free to share and adapt with credit, but not for commercial use. See LICENSE for the details.
