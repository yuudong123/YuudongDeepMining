# Yuudong Deep Mining

A RimWorld 1.7 mod: dig down through a mine shaft and mine ore on underground levels instead of tearing up the mountains around your base. Requires Harmony. No DLC required.

## Features

| Item | Details |
| --- | --- |
| Mine shaft | 2x2. Steel 100, wood 150, work 8000, Construction 4. One per map, on the surface or on an underground level above the maximum depth (default 5). Misc tab. |
| Underground level | Vanilla underground map (Underground biome, cave tunnels) plus extra ore veins by depth. Deeper levels have more veins and more valuable ore. 15 °C, dark (bring lights). |
| Hardness | Damage to rock and ore is divided by (1 + 0.5 × depth), fixed from level 4 (level 1 ×1.5 … level 4 ×3 by default). |
| Infestations | On levels with colonists: every 15 days on average at level 1, × 0.75 per level, fixed from level 4 (~6.3 days). At least 3 days apart. |
| Work across levels | When a colonist has nothing to do on their level, the vanilla work search is run on each other level of the shaft chain (nearest first) as if they stood at that level's ladder. If it finds work, they climb there. A trip that finds nothing on arrival makes that level skipped for that colonist for a while (2500 ticks), which prevents pointless back-and-forth. |
| Hauling across levels | Items with no storage on their level are carried by hand, through the ladders, to the nearest level that has storage for them (up or down), then stored there. |
| Home level | Colonist button to pick the level they mainly stay on. They leave it for work on other levels and come back once idle. If hungry or tired with no food or bed on that level, they go up. "Free" has no home level. |
| Level panel & remote orders | Top-right panel (or `=` / `-`) switches the viewed level. Selected colonists stay "remote-selected". Right-clicking shows the vanilla order menu built as if they stood at this level's ladder, plus "go here". They climb the ladders and, on arrival, pick the same order (prioritized work, equip, attack, ...). They are re-selected when they arrive if you are still looking. Another order given on the way cancels it. |
| Seal / destroy | Collapses every level below, deepest first. Items are lost; creatures die from the "cave-in" damage ("buried alive when the mine shaft caved in"). A letter lists the victims. Can also be sealed from the ladder below. Sealing leaves a "sealed mine shaft" (earth and boards, standable, deconstruct to clear); destruction leaves rubble. |
| Shaft damage | Only takes damage when deliberately attacked. Enemy trash/breach AI and tantrums never pick it, shots aimed elsewhere pass over it, and explosions or fire aimed elsewhere are absorbed. The underground ladder cannot be damaged or deconstructed at all. |
| Raids | When no active colonists are on the surface but some are underground, sapper and breacher raids climb down once they can reach the shaft. Other raiders never notice the underground. Storyteller raids never target underground levels directly. |

Settings: maximum depth, new level size, hardness per level and cap, infestation interval / multiplier / cap / cooldown, raiders following you down, indestructible shafts.

## Layout

- `Source/YuudongDeepMining/`: `Building_MineShaft` (vanilla `MapPortal`), `Building_MineShaftExit` (`PocketMapExit`), `MapComponent_DeepLayer` (depth, infestations), `RaidDescent`, `LayerTravel` (home level, travel orders, think nodes), `HaulAcross` (hauling through ladders), `RemoteControl` (level panel, remote selection and orders), `GhostUtility` (evaluates vanilla work search / order menu as if a colonist stood on another level), `GenSteps`, `Patches` (hardness, stray shots, colonist button).
- `Defs/`: buildings, underground map generator, damage / duty / job / work giver / key binding defs, think tree insertions (`Humanlike_PreMain`, `Humanlike_PostMain`).
- Build: run `dotnet build -c Release` in `Source/YuudongDeepMining`, which writes `Assemblies/YuudongDeepMining.dll`.

## Notes

- Every underground level is a vanilla pocket map, so each one runs as its own map. More levels cost more performance.
- Removing the mod from a save with underground levels breaks the save.
- Underground levels have thick overhead mountain everywhere. Mining large areas can collapse the roof just like on the surface, so leave pillars.
