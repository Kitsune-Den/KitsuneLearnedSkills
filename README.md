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

### Learn by crafting (KitsuneLearnedSkills.dll)

Every completed craft reads the crafted item's (or block's) `UnlockedBy`
property and adds progress to that crafting skill. On the default pace,
6 crafted items = +1 level in the owning skill.

## Layout

- `KitsuneLearnedSkills/` — the deployable mod payload (extract into `Mods/`)
- `KLS_CraftLearning.cs` + `KitsuneLearnedSkills.csproj` — DLL source; builds
  straight into the payload folder. Needs the sister `KitsunePaintUnlocked`
  repo cloned alongside for the 7DTD reference binaries.

## Known limitation (0.6.x)

Five skills have no reachable learn-by-doing source below their first unlock:
Workstations (forge unlocks at 4), Vehicles (5), Seeds (2), Electrician (25),
Traps (25). 0.6.0 removed their magazines from loot and traders, so those
lines are currently stranded at level 1. Fixed in 0.7.0.
