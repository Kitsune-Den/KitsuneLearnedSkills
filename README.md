# KitsuneLearnedSkills

Replaces 7 Days to Die's crafting-skill magazine grind with **learn by doing**.
Crafting skills level up by using tools and weapons and by crafting — not by
reading. Perk books (Fireman's Almanac etc.) are untouched.

Built for 7DTD V2.6+. Requires EAC off (ships a DLL).

## Learning speed (Settings.xml)

Pick the pace in `Settings.xml` in the mod folder:

```xml
<KitsuneLearnedSkills preset="MEDIUM" />
```

| Source | SLOW | MEDIUM (default) | FAST |
|---|---|---|---|
| `weapon` kills with that weapon type | 16 | 12 | 4 |
| `tool` block hits / repairs with that tool | 80 | 60 | 20 |
| `use` food / medical items consumed | 8 | 6 | 2 |
| `craft` items crafted (per skill) | 8 | 6 | 2 |
| `station` workstation queue completions (weighed, see below) | 16 | 8 | 4 |
| `vehicle` vehicle repairs | 4 | 2 | 1 |
| `wire` wire connections | 4 | 2 | 1 |
| `loot` fresh loot containers opened | 30 | 20 | 8 |
| `wreck` salvage-tool hits on vehicle wrecks | 80 | 60 | 20 |
| `harvest` crops harvested (farm or wild) | 24 | 16 | 6 |
| `repair` armor / robotics items repaired | 8 | 6 | 2 |

Each number is actions per +1 level (higher = slower). Any source can be
overridden on top of a preset, e.g. `<KitsuneLearnedSkills preset="MEDIUM"
weapon="24" />`.

A workstation completion is weighed by what was made. The weight is the
recipe's craft time as the game defines it (before perks and tools speed it
up) divided by `stationtime`, capped at `stationcap`:

| Setting | Default | Meaning |
|---|---|---|
| `stationtime` | 15 | seconds of craft time that count as one completion |
| `stationcap` | 4 | the most completions a single craft can count as |

| Craft | Craft time | Counts as |
|---|---|---|
| Cobblestone | 0.4 s | 0.03 |
| Forged iron | 7.5 s | 0.5 |
| Boiled water | 10 s | 0.7 |
| Grilled meat | 15 s | 1 |
| Pistol | 53 s | 3.5 |
| Anything of a minute or more | 60 s+ | 4 |

Both settings are the same in every preset and can be overridden like the
paces, e.g. `<KitsuneLearnedSkills preset="SLOW" stationtime="30" />`.

- **Multiplayer:** only the server's `Settings.xml` counts. The DLL bakes it
  into the `buffs.xml` the server sends to every client, so players can't
  change their own pace. Restart the server after editing.
- **Single player:** takes effect the next time you load a world.
- Existing characters pick up the new pace on their next login.

## Seeing your progress

Open the console (F1) and type `kls`. It lists every crafting skill with its
level and how far each source is toward the next one:

```
Workstations 12/75: station work 5.9/8 (73%)
Clubs 4/75: kills 7/12 (58%), crafts 2/6 (33%)
```

`kls work` shows only the skills with "work" in the name. It works in single
player and on servers, for every player, and only reads your own character.

## How skills level

Two mechanisms, both always active:

### Learn by using (Config/buffs.xml)

| Skill group | Levels from | MEDIUM pace |
|---|---|---|
| Weapon crafting (clubs, blades, spears, sledges, knuckles, bows, handguns, shotguns, rifles, machine guns, explosives) | kills while holding that weapon type | 12 kills per level |
| Tool crafting (harvesting, salvage, repair) | block hits with harvesting and salvage tools; repairs and finished block upgrades with a stone axe, claw hammer or nailgun | 60 per level |
| Salvage Tools, before the wrench | hits on a car, truck or bus wreck with a harvesting tool (stone axe, pickaxe, shovel), until level 4 where the wrench recipe unlocks | 60 hits per level |
| Food + medical crafting | consuming food / medical items | 6 uses per level |
| Vehicles | salvaging a car, truck or bus wreck with a wrench, ratchet or impact driver | 60 hits per level |

Pace is set in `Settings.xml` (see above); MEDIUM numbers shown.

### Learn by crafting + doing (KitsuneLearnedSkills.dll)

Every completed craft reads the crafted item's (or block's) `UnlockedBy`
property and adds progress to that crafting skill — 6 crafted items = +1
level on MEDIUM. Both the backpack queue and workstation queues count.

These skill lines have no craftable below their first unlock, so the DLL
gives them dedicated "doing" hooks that work from level 1:

| Skill | Levels from | MEDIUM pace | Bootstrap |
|---|---|---|---|
| Workstations | any workstation queue completion (campfire, forge, workbench, cement mixer, chem station), weighed by the recipe's craft time | 8 completions per level (one completion = 15 s of craft time) | cook on a campfire → forge unlocks at 4 |
| Vehicles | repairing a vehicle with a repair kit (wreck salvage counts too, see above) | 2 repairs per level | repair a looted bicycle, or salvage wrecks → parts unlock at 5 |
| Electrician | completing a wire connection with the wire tool | 2 connections per level | loot/buy a wire tool → own gear unlocks at 25 |
| Traps | crafting spikes / barbed fence (always unlocked) | 6 crafts per level | dart traps and turrets unlock at 25 |
| Seeds | crafting farm plots (always unlocked), and harvesting crops (farm or wild) | 6 crafts or 16 crops per level | seed recipes unlock from 2 |
| Armor | repairing armor (on top of crafting it) | 6 repairs per level | primitive armor is craftable from 1 |
| Robotics | kills with a baton, junk sledge or junk turret (held or deployed); crafting pipe batons (always unlocked); repairing a baton, junk sledge, junk turret or junk drone | 12 kills, or 6 crafts or repairs per level | junk sledge unlocks at 11 |
| Backpacks | opening a fresh loot container (first open only; your own storage never counts) | 20 containers per level | small backpack unlocks at 5 |

Armor, Backpacks and Robotics keep their magazines (Pack Rat for backpacks)
as an extra path (deliberate safety holdouts).

## Layout

- `KitsuneLearnedSkills/` — the deployable mod payload (extract into `Mods/`)
- `KitsuneLearnedSkills/Settings.xml` — learning-speed preset and overrides
- `KitsuneLearnedSkills/Config/Localization.csv` + `Localization.txt` —
  message text in the 12 languages besides English (V3.x reads the `.csv`,
  V2.x the `.txt`; keep them in sync)
- `KLS_CraftLearning.cs`, `KLS_Settings.cs` + `KitsuneLearnedSkills.csproj` —
  DLL source; builds straight into the payload folder. References the
  installed 7DTD client's `Managed` folder (override with `-p:GameDir=...`).

## History note

0.6.x claimed every skill had a learn-by-doing source, but for Workstations,
Vehicles, Seeds, Electrician, and Traps that source sat entirely above the
line's first unlock level — with magazines removed from loot and traders,
those five were stranded at level 1 (the forge, first unlock in the
Workstations line at level 4, was unreachable). 0.6.x also never hooked the
backpack crafting queue, so backpack crafts taught nothing. Both fixed in
0.7.0.

0.9.0: the mod was built around 23 crafting skills and missed the 24th,
Backpacks. The magazine sweep still removed its Pack Rat magazine, and the
first backpack recipe unlocks at level 5, so Backpacks was stranded. It now
levels from opening fresh loot containers and keeps its magazine.

0.10.0: Vehicles also levels from salvaging vehicle wrecks with a salvage
tool, so the skill can grow from the scavenging you already do instead of
only from repair kits. Vanilla tags every car, truck and bus wreck with
`challenge_cars`, so this is pure XML (`TriggerHasTags` on block hits). Its
pace is the new `wreck` key in `Settings.xml`. Suggested by Hank on Discord.

0.10.1: three skills that a player reported as stuck. Workstations only
counted crafts that finished while the station was closed (with the window
open the game skips the hook 0.10.0 used), and counted a whole closed-window
batch as one. Repair Tools listened for `repairingSkill`, which vanilla only
puts on the nailgun, so the claw hammer and stone axe never counted; it now
also counts block upgrades. Salvage Tools missed the hit that breaks a block,
which is most of the hits on small salvage. Reported by Havox1023.

0.11.0: Seeds also levels from harvesting crops, farm or wild, counted once
per crop picked (new `harvest` pace key). Before this, the only way up was
crafting farm plots and then seeds, so a player who farmed a lot learned
nothing from the harvest itself. Armor and Robotics also level from repairing
those items (new `repair` pace key), and the Robotics magazine is back in loot
and traders as a second path, like Armor and Pack Rat. Robotics had been
stranded at level 1 since 0.6.0: its level-1 item, the pipe baton, carries no
`UnlockedBy`, so crafting it taught nothing, and the junk sledge (the first
item that does) unlocks at 11. Crafting a pipe baton now counts as a gateway
craft, like farm plots for Seeds.

0.12.0: workstation completions are weighed by the recipe's craft time.
Before this every completion counted as 1, so a base's worth of cobblestone
(0.4 s each) took Workstations to its cap before the first blood moon, and
one pistol taught exactly as much as one cobblestone. Each completion now
counts for its base craft time divided by `stationtime` (15 s), up to
`stationcap` (4): 1200 cobblestones are worth 2 levels on SLOW, not 75.
Cooking is about where it was, so the campfire road to the forge is
unchanged. Progress already earned is kept.

0.13.0: the "knowledge grows" messages are translated into German, Spanish,
French, Italian, Japanese, Korean, Polish, Brazilian Portuguese, Russian,
Turkish, and Simplified and Traditional Chinese (`Config/Localization.csv`
for V3.x, `Localization.txt` for V2.x). They were hardcoded English before, and
the crafting one mixed English with the game's translated skill name. A
dedicated server sends its translations to players along with its configs.

0.13.0 also has three fixes. The learn-by-using level-ups (kills, tool hits, eating)
use vanilla's add-a-level action, which on crafting skills quietly gives the
world's Magazine Progress setting instead of 1, so a world set to 3 gave 3
levels each time. They now give exactly one. Each DLL source also keeps its
own count now: before, a skill with two sources at different paces (Seeds
from crafting and harvesting, Workstations, Vehicles, Electrician, Backpacks)
shared one count, so mixing them levelled faster or slower than either pace
says. Progress already counted for those skills stays with crafting. And
DLL level-ups now show the vanilla skill notification and new-recipe
messages, and reach the server straight away instead of waiting for some
other progression change.

0.14.0: Robotics levels from kills. Batons, the junk sledge and the junk
turret were the only weapons whose kills taught nothing: the skill only grew
from crafting pipe batons and repairing robotics items. A kill with any of
them now counts at the `weapon` pace, including kills by a turret or sledge
you have deployed. Reported by Psi Chaotic.

0.15.0: a `kls` console command shows progress toward the next level of
every crafting skill, per source. Before this the only feedback was the
message when a level landed, so slow sources (Workstations above all) looked
stuck. Suggested by Mitch1985. MEDIUM is now the default preset instead of
SLOW: the `Settings.xml` that ships with the mod says MEDIUM, and so does the
fallback when the file is missing. If you install over an older version and
want to stay on SLOW, keep your own `Settings.xml` or set `preset="SLOW"`.

0.16.0: Salvage Tools can start without a wrench. The skill only levelled
from hits with a wrench, ratchet or impact driver, and the wrench recipe
unlocks at level 4, so a player who never looted or bought a wrench was stuck
at level 1. Below level 4, hitting a vehicle wreck with a harvesting tool
(the starting stone axe included) now counts as a salvage hit, at the `tool`
pace. From level 4 on it is salvage tools only, as before. Asked by
Tekitou1000.

## License

[CC BY-NC-SA 4.0](LICENSE), with an extra permission for servers that make money: see [NOTICE.md](NOTICE.md). In short: play it, change it and share it, with credit, not for sale, and with changed versions under the same license.
