# KitsuneLearnedSkills

Replaces 7 Days to Die's crafting-skill magazine grind with **learn by doing**.
Crafting skills level up by using tools and weapons and by crafting — not by
reading. Perk books (Fireman's Almanac etc.) are untouched.

Built for 7DTD V2.6+. Requires EAC off (ships a DLL).

## Learning speed (Settings.xml)

Pick the pace in `Settings.xml` in the mod folder:

```xml
<KitsuneLearnedSkills preset="SLOW" />
```

| Source | SLOW (default) | MEDIUM | FAST |
|---|---|---|---|
| `weapon` kills with that weapon type | 16 | 12 | 4 |
| `tool` block hits / repairs with that tool | 80 | 60 | 20 |
| `use` food / medical items consumed | 8 | 6 | 2 |
| `craft` items crafted (per skill) | 8 | 6 | 2 |
| `station` workstation queue completions | 16 | 8 | 4 |
| `vehicle` vehicle repairs | 4 | 2 | 1 |
| `wire` wire connections | 4 | 2 | 1 |

Each number is actions per +1 level (higher = slower). Any source can be
overridden on top of a preset, e.g. `<KitsuneLearnedSkills preset="MEDIUM"
weapon="24" />`.

- **Multiplayer:** only the server's `Settings.xml` counts. The DLL bakes it
  into the `buffs.xml` the server sends to every client, so players can't
  change their own pace. Restart the server after editing.
- **Single player:** takes effect the next time you load a world.
- Existing characters pick up the new pace on their next login.

## How skills level

Two mechanisms, both always active:

### Learn by using (Config/buffs.xml)

| Skill group | Levels from | MEDIUM pace |
|---|---|---|
| Weapon crafting (clubs, blades, spears, sledges, knuckles, bows, handguns, shotguns, rifles, machine guns, explosives) | kills while holding that weapon type | 12 kills per level |
| Tool crafting (harvesting, salvage, repair) | block hits / repairs with that tool type | 60 hits per level |
| Food + medical crafting | consuming food / medical items | 6 uses per level |

Pace is set in `Settings.xml` (see above); MEDIUM numbers shown.

### Learn by crafting + doing (KitsuneLearnedSkills.dll)

Every completed craft reads the crafted item's (or block's) `UnlockedBy`
property and adds progress to that crafting skill — 6 crafted items = +1
level on MEDIUM. Both the backpack queue and workstation queues count.

Four skill lines have no craftable below their first unlock, so the DLL
gives them dedicated "doing" hooks that work from level 1:

| Skill | Levels from | MEDIUM pace | Bootstrap |
|---|---|---|---|
| Workstations | any workstation queue completion (campfire, forge, workbench, cement mixer, chem station) | 8 completions per level | cook on a campfire → forge unlocks at 4 |
| Vehicles | repairing a vehicle with a repair kit | 2 repairs per level | repair a looted bicycle → parts unlock at 5 |
| Electrician | completing a wire connection with the wire tool | 2 connections per level | loot/buy a wire tool → own gear unlocks at 25 |
| Traps | crafting spikes / barbed fence (always unlocked) | 6 crafts per level | dart traps and turrets unlock at 25 |
| Seeds | crafting farm plots (always unlocked) | 6 crafts per level | seed recipes unlock from 2 |

Armor keeps its magazines as an extra path (deliberate safety holdout).

## Layout

- `KitsuneLearnedSkills/` — the deployable mod payload (extract into `Mods/`)
- `KitsuneLearnedSkills/Settings.xml` — learning-speed preset and overrides
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
