![ForestCrawler banner](https://raw.githubusercontent.com/NorskIT/ForestCrawler/main/images/banner.png)

# ForestCrawler

A Valheim horror mod with rare, personal encounters for players exploring alone at night. For Valheim on Windows with BepInEx.

## Features

- Invisible audio teases and full encounters in Black Forest, Swamp and Mistlands.
- Music is silenced for the targeted player during encounters and restored afterwards.
- Directional whispers, a stalking creature, fast pursuit and distance-driven heartbeat audio.
- Native ground-enemy pursuit, with extended arms that can grab a visible, unreachable player within 30 metres after 30 seconds.
- A close-up jumpscare followed by teleportation to a validated dry location, without dealing damage.
- Server-controlled isolation checks, cooldowns and encounter frequency. Only the selected player sees and hears the encounter.

## Installation (manual)

1. Install BepInExPack Valheim (see Dependencies below).
2. Extract the ZIP's `BepInEx` folder into your Valheim installation or mod profile, merging it with the existing folder.
3. Install the same ForestCrawler version on the host/server and every connected player's client, then restart. Clients need both `ForestCrawler.dll` and `forestcrawler.assets`; dedicated servers only need the DLL.

Using a mod manager? Import the ZIP as a local mod.

## Dependencies

- [BepInExPack Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/)

Install it separately; its DLLs are not included in this package.

## Developer commands

Enter a world and press **F5**. In multiplayer, encounter and preview commands require the host or a server administrator. Status and clearing your own encounter remain available to you.

| Command | Description |
| --- | --- |
| `crawler_spawn` | Spawn a persistent idle preview 10-15 metres ahead. |
| `crawler_anim idle` | Test the idle animation. |
| `crawler_anim scream` | Test the reveal animation and scream. |
| `crawler_anim charge` | Test movement across real terrain with a safe stop. |
| `crawler_encounter` | Test the full event; bypass biome, night and cooldown, but retain isolation requirements. |
| `crawler_encounter tease` | Test one invisible audio tease. |
| `crawler_start` | Start a natural encounter using progression rules; advances shared world time to midnight. |
| `crawler_clear` | Remove your creature, audio and temporary encounter state. |
| `crawler_status` | Show readiness, encounter state, eligibility and native pursuit and arm-grab details. |

See [DEVELOPMENT.md](DEVELOPMENT.md) for builds and configuration, and [ATTRIBUTION.md](ATTRIBUTION.md) for asset credits.
