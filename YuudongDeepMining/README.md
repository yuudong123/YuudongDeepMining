# Yuudong Deep Mining

A RimWorld 1.7 mod: dig down through a mine shaft and mine ore on underground levels instead of tearing up the mountains around your base. Requires Harmony. No DLC required.

## Features

| Item | Details |
| --- | --- |
| Mine shaft (surface) | 2x2. Steel 100, wood 150, work 8000, Construction 4. One per map. Misc tab. |
| Deep mine shaft (underground) | 2x2. Steel 200, wood 250, components 4, work 16000, Construction 8. Only on levels above the maximum depth (default 5). |
| Underground level | Vanilla underground map (Underground biome, cave tunnels) plus extra ore veins by depth. Deeper levels have more veins and more valuable ore. 15 °C, dark (bring lights). |
| Hardness | Damage to rock and ore is divided by (1 + 0.5 × depth), fixed from level 4 (level 1 ×1.5 … level 4 ×3 by default). |
| Infestations | On levels with colonists: every 15 days on average at level 1, × 0.75 per level, fixed from level 4 (~6.3 days). At least 3 days apart. |
| Work level | Colonist button to pick a level. Colonists climb the ladders to get there. If hungry or tired with no food or bed on that level, they go up one level. "Free" never moves them. |
| Send resources up | Items with no storage on an underground level, chosen in the ladder's storage tab (default: raw resources and components), are carried up and dropped next to the shaft above. |
| Seal / destroy | Collapses every level below, deepest first. Items are lost; creatures die from the "cave-in" damage ("buried alive when the mine shaft caved in"). A letter lists the victims. Can also be sealed from the ladder below. |
| Shaft damage | Only takes damage when deliberately attacked. Enemy trash/breach AI and tantrums never pick it, shots aimed elsewhere pass over it, and explosions or fire aimed elsewhere are absorbed. The underground ladder cannot be damaged or deconstructed at all. |
| Raids | When no active colonists are on the surface but some are underground, sapper and breacher raids climb down once they can reach the shaft. Other raiders never notice the underground. Storyteller raids never target underground levels directly. |

Settings: maximum depth, new level size, hardness per level and cap, infestation interval / multiplier / cap / cooldown, raiders following you down, indestructible shafts.

## Layout

- `Source/YuudongDeepMining/`: `Building_MineShaft` (vanilla `MapPortal`), `Building_MineShaftExit` (`PocketMapExit` + storage settings), `MapComponent_DeepLayer` (depth, infestations), `RaidDescent`, `LayerAssignment` (work level, think node), `HaulUp`, `GenSteps`, `Patches` (hardness, stray shots, colonist button).
- `Defs/`: buildings, underground map generator, damage / duty / job / work giver defs, think tree insertion (`Humanlike_PreMain`).
- Build: run `dotnet build -c Release` in `Source/YuudongDeepMining`, which writes `Assemblies/YuudongDeepMining.dll`.

## Notes

- Every underground level is a vanilla pocket map, so each one runs as its own map. More levels cost more performance.
- Removing the mod from a save with underground levels breaks the save.
- Underground levels have thick overhead mountain everywhere. Mining large areas can collapse the roof just like on the surface, so leave pillars.
