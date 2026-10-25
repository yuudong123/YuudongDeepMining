using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace YuudongDeepMining
{
    // 지하 층의 위로 가는 사다리를 지도 가운데쯤에 놓고 주변을 비운다 (바닐라 동굴 출구 단계와 같은 방식).
    public class GenStep_DeepLayerExit : GenStep
    {
        public const float ClearRadius = 4.5f;

        public override int SeedPart => 734812771;

        public override void Generate(Map map, GenStepParams parms)
        {
            ThingDef exitDef = PocketMapUtility.currentlyGeneratingPortal?.def.portal?.exitDef ?? YDMDefOf.YDM_MineShaftExit;
            IntVec3 center = map.Center;
            if (!CellFinder.TryFindRandomCellNear(center, map, Mathf.Max(5, map.Size.x / 6), c => c.DistanceToEdge(map) > 8, out IntVec3 spot))
            {
                spot = center;
            }

            foreach (IntVec3 cell in GenRadial.RadialCellsAround(spot, ClearRadius, true))
            {
                if (!cell.InBounds(map))
                {
                    continue;
                }

                foreach (Thing thing in cell.GetThingList(map).ToList())
                {
                    if (thing.def.destroyable)
                    {
                        thing.Destroy();
                    }
                }

                if (!cell.GetTerrain(map).affordances.Contains(TerrainAffordanceDefOf.Heavy))
                {
                    map.terrainGrid.SetTerrain(cell, TerrainDefOf.Gravel);
                }
            }

            GenSpawn.Spawn(ThingMaker.MakeThing(exitDef), spot, map);
            MapGenerator.PlayerStartSpot = spot;
        }
    }

    // 깊이에 따라 광맥을 더 뿌린다. 깊을수록 광맥이 많고 값진 광석의 비중이 커진다.
    public class GenStep_DeepOres : GenStep_ScatterLumpsMineable
    {
        public float basePer10k = 4f;

        public float perDepthPer10k = 2f;

        public float maxPer10k = 14f;

        // 시장가치 valueForFullBonus 이상인 광석은 한 층마다 가중치에 valueBonusPerDepth를 더한다.
        public float valueBonusPerDepth = 0.35f;

        public float valueForFullBonus = 6f;

        public override int SeedPart => 482991733;

        private int depth;

        public override void Generate(Map map, GenStepParams parms)
        {
            depth = Mathf.Max(1, LayerUtility.generatingDepth > 0 ? LayerUtility.generatingDepth : LayerUtility.DepthOf(map));
            float per10k = Mathf.Min(maxPer10k, basePer10k + perDepthPer10k * depth);
            countPer10kCellsRange = new FloatRange(per10k * 0.8f, per10k * 1.2f);
            base.Generate(map, parms);
        }

        protected override ThingDef ChooseThingDef()
        {
            return DefDatabase<ThingDef>.AllDefs.RandomElementByWeightWithFallback(delegate(ThingDef d)
            {
                if (d.building == null || d.building.mineableScatterCommonality <= 0f || d.building.mineableThing == null)
                {
                    return 0f;
                }

                float value = d.building.mineableThing.BaseMarketValue;
                return d.building.mineableScatterCommonality * (1f + depth * valueBonusPerDepth * Mathf.Clamp01(value / valueForFullBonus));
            });
        }
    }
}
