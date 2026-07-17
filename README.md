# KitsuneLearnedSkills

Replaces 7 Days to Die's crafting-skill magazine grind with **learn by doing**.
Crafting skills level up by using tools and weapons and by crafting — not by
reading. Perk books (Fireman's Almanac etc.) are untouched.

Built for 7DTD V2.6+. Requires EAC off (ships a DLL).

## How skills level

Two mechanisms, both always active:

### Learn by using (Config/buffs.xml)

| Skill group | Levels from | MEDIUM pace (default) |
|---|---|---|
| Weapon crafting (clubs, blades, spears, sledges, knuckles, bows, handguns, shotguns, rifles, machine guns, explosives) | kills while holding that weapon type | 12 kills per level |
| Tool crafting (harvesting, salvage, repair) | block hits / repairs with that tool type | 60 hits per level |
| Food + medical crafting | consuming food / medical items | 6 uses per level |

Pace presets (SLOW / MEDIUM / FAST) live in `Config/buffs.xml` — uncomment
exactly one `buffKLSPace` block.

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
- `KLS_CraftLearning.cs` + `KitsuneLearnedSkills.csproj` — DLL source; builds
  straight into the payload folder. Needs the sister `KitsunePaintUnlocked`
  repo cloned alongside for the 7DTD reference binaries.

## History note

0.6.x claimed every skill had a learn-by-doing source, but for Workstations,
Vehicles, Seeds, Electrician, and Traps that source sat entirely above the
line's first unlock level — with magazines removed from loot and traders,
those five were stranded at level 1 (the forge, first unlock in the
Workstations line at level 4, was unreachable). 0.6.x also never hooked the
backpack crafting queue, so backpack crafts taught nothing. Both fixed in
0.7.0.
