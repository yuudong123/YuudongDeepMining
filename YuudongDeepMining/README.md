# Yuudong Deep Mining

A RimWorld 1.7 mod: dig down through a mine shaft and mine ore on underground levels instead of tearing up the mountains around your base. Requires Harmony. No DLC required.

## Features

| Item | Details |
| --- | --- |
| Mine shaft | 2x2. Steel 100, wood 150, work 8000, Construction 4. One per map, on the surface or on an underground level above the maximum depth (default 5). Misc tab. |
| Underground level | Vanilla underground map (Underground biome, cave tunnels) plus extra ore veins by depth. Deeper levels have more veins and more valuable ore. 15 °C, dark (bring lights). |
| Hardness | Damage to rock and ore is divided by (1 + 0.5 × depth), fixed from level 4 (level 1 ×1.5 … level 4 ×3 by default). |
| Infestations | On levels with colonists: every 15 days on average at level 1, × 0.75 per level, fixed from level 4 (~6.3 days). At least 3 days apart. |
| Work across levels | Whenever the vanilla work search picks a job (or finds none), it is also run on each other level of the shaft chain (at most every 250 ticks per colonist and decision type) as if the colonist stood at that level's ladder. If the job there comes earlier in vanilla's own order (work priority, then work type order), or is in the same order slot but nearer by actual walk (to the ladder, through it, then to the job), they climb there instead. A trip that finds nothing on arrival makes that level skipped for that colonist for a while (2500 ticks), which prevents pointless back-and-forth. |
| Hauling across levels | Items with no storage on their level are carried by hand, through the ladders, to the level whose storage is the shortest actual walk away (up or down), then stored there. It sits in the same work order slot as vanilla general hauling (and wins inside that slot), so it happens as soon as hauling would. |
| Ingredients across levels | When a workbench's level can't supply a bill (vanilla ingredient search, run as if the hauler stood at the bench), and another level can (vanilla search with the bench treated as standing at that level's ladder), haulers carry the missing ingredients over and drop them next to the bench. Limitation: ingredients split over several levels are only moved when one level can supply them all. |
| Construction across levels | The build menu lets you pick a material that only exists on another level (vanilla checks only the current map). Haulers carry the missing materials for blueprints and frames from other levels and drop them next to the site; vanilla construction takes it from there. |
| People across levels | Doctors rescue downed allies to a bed on another level when there is none here (vanilla DoctorRescue decision and bed search run on the other level). Right-click adds "rescue to a bed on …" / "capture to a prison on …" when only another level has one. The person is carried through the ladders, then vanilla Rescue / Capture puts them in bed. |
| Counts across levels | Resource counts (top-left readout, low food alerts), "do until you have X" bill counts and colony wealth (raid size) count the whole shaft chain. Bills restricted to one stockpile still count only that stockpile. |
| Animals | Tamed animals whose master is on another level follow them down or up when vanilla's follow rules say so (drafted / fieldwork). |
| Needs across levels | Every job giver of vanilla's colonist behavior core (MainColonistBehaviorCore: eat, sleep, joy, drugs, ...) is also evaluated on other levels when it finds nothing here or only vanilla's fallback (sleeping on the floor). If another level gives a proper answer, the colonist goes there. No per-need code: the node types are read from that think tree. |
| Alerts across levels | While an alert recalculates, the top map of a shaft chain reports the colonist / prisoner / slave / animal lists and counts of the whole chain and the levels below report none, so alerts like "need warden" treat the chain as one colony. |
| Home level | Colonist button to pick the level they mainly stay on. They leave it for work or needs on other levels and come back once idle. "Free" has no home level. |
| Debug log | Setting that logs what colonists found on other levels (work, needs) and hauling decisions. |
| Performance measurement | Setting that shows, above the level panel, this mod's own time (ms per second) and calls per second by category over the last second, and logs it every 10 seconds. Nested measurements count only their own share. Measures the main thread only; costs almost nothing when off. |
| Level panel & remote orders | Top-right panel (or `=` / `-`) switches the viewed level. Selected colonists stay "remote-selected". Right-clicking shows the vanilla order menu built as if they stood at this level's ladder, plus "go here". They climb the ladders and, on arrival, pick the same order (prioritized work, equip, attack, ...). They are re-selected when they arrive if you are still looking. Another order given on the way cancels it. |
| Seal / destroy | Collapses every level below, deepest first. Items are lost; creatures die from the "cave-in" damage ("buried alive when the mine shaft caved in"). A letter lists the victims. Can also be sealed from the ladder below. Sealing leaves a "sealed mine shaft" (earth and boards, standable, deconstruct to clear); destruction leaves rubble. |
| Shaft damage | Only takes damage when deliberately attacked. Enemy trash/breach AI and tantrums never pick it, shots aimed elsewhere pass over it, and explosions or fire aimed elsewhere are absorbed. The underground ladder cannot be damaged or deconstructed at all. |
| Raids | When no active colonists are on the surface but some are underground, sapper and breacher raids climb down once they can reach the shaft. Other raiders never notice the underground. Storyteller raids never target underground levels directly. |

Settings: maximum depth, new level size, hardness per level and cap, infestation interval / multiplier / cap / cooldown, raiders following you down, indestructible shafts.

## Layout

- `Source/YuudongDeepMining/`: shaft content — `Building_MineShaft` (vanilla `MapPortal`), `Building_MineShaftExit` (`PocketMapExit`), `MapComponent_DeepLayer` (depth, infestations), `RaidDescent`, `GenSteps`, `Patches` (hardness, stray shots, colonist button).
- `Source/YuudongDeepMining/Levels/`: the "one colony across levels" core — `GhostUtility` (scoped evaluation of vanilla code as if a pawn/bench stood on another level), `LayerUtility` (chain, portals, travel cost), `LayerTravel` (home level, travel orders, work/need decisions), `HaulAcross`, `BillSupply`, `CarryPawns`, `Animals`, `ColonyAggregate` (alerts), `CountAggregate` (resources, bills, wealth), `RemoteControl` (level panel, remote orders).
- `Defs/`: buildings, underground map generator, damage / duty / job / work giver / key binding defs, think tree insertions (`Humanlike_PreMain`, `Humanlike_PostMain`).
- Build: run `dotnet build -c Release` in `Source/YuudongDeepMining`, which writes `Assemblies/YuudongDeepMining.dll`.

## Design rules (checked by the test harness)

1. Never replace vanilla: no prefix that skips the original, except one narrow projectile hook (stray shots pass over the shaft). UI hooks only consume the input event. Decisions come from calling vanilla code.
2. Map/position swaps ("ghost evaluation") live only in `GhostUtility`, last one call, are always restored in `finally`, also restore the RNG state and bill ingredient-search timers, and an exception only drops that one evaluation.
3. No parallel job queue: travel is vanilla `EnterPortal`; the mod only remembers "which level a colonist is heading to".
4. Hook common points (think tree nodes, map pawn lists, resource / wealth counters), not individual features.
5. Rendering, weather, roofs and pathfinding are untouched; every level stays a normal vanilla map.

## Notes

- Every underground level is a vanilla pocket map, so each one runs as its own map. More levels cost more performance.
- Removing the mod from a save with underground levels breaks the save.
- Underground levels have thick overhead mountain everywhere. Mining large areas can collapse the roof just like on the surface, so leave pillars.
