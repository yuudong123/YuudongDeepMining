using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace YuudongDeepMining
{
    // 아래층으로 내려가는 갱도 입구. 바닐라 지도 포털(구덩이 문)을 그대로 쓴다.
    // 폐쇄하거나 파괴되면 아래층 전부가 무너지고, 안에 있던 생물은 깔려 죽는다.
    [StaticConstructorOnStartup]
    public class Building_MineShaft : MapPortal
    {
        private static readonly Texture2D CollapseTex = ContentFinder<Texture2D>.Get("UI/Designators/Cancel");

        private bool collapsing;

        // 플레이어가 폐쇄했는가. 폐쇄하면 그 자리에 '폐쇄된 갱도'가 남고, 파괴되면 돌무더기만 남는다.
        private bool sealing;

        public int DepthBelow => LayerUtility.DepthOf(Map) + 1;

        public override string EnterString => "YDM.GoDown".Translate(LayerUtility.LayerLabel(DepthBelow));

        public override string EnteringString => "YDM.GoingDown".Translate(LayerUtility.LayerLabel(DepthBelow));

        protected override Map GeneratePocketMapInt()
        {
            int size = Mathf.Clamp(DeepMiningMod.Settings.mapSize, 50, 300);
            int depth = DepthBelow;
            LayerUtility.generatingDepth = depth;
            try
            {
                Map map = PocketMapUtility.GeneratePocketMap(new IntVec3(size, 1, size), YDMDefOf.YDM_Underground, GetExtraGenSteps(), Map);
                MapComponent_DeepLayer comp = LayerUtility.LayerComp(map);
                if (comp != null)
                {
                    comp.depth = depth;
                }

                return map;
            }
            finally
            {
                LayerUtility.generatingDepth = 0;
            }
        }

        public override void PreApplyDamage(ref DamageInfo dinfo, out bool absorbed)
        {
            if (DeepMiningMod.Settings.shaftInvulnerable || !IsDirectAttack(dinfo))
            {
                absorbed = true;
                return;
            }

            base.PreApplyDamage(ref dinfo, out absorbed);
        }

        // 이 갱도를 직접 노린 공격만 피해를 준다. 빗나간 탄·다른 곳을 노린 폭발·불길 등은 막는다.
        // 원거리·폭발은 피해 정보의 목표로, 근접은 (바닐라가 목표를 담지 않으므로) 공격자가 하는 일의 대상으로 판단한다.
        private bool IsDirectAttack(DamageInfo dinfo)
        {
            if (dinfo.IntendedTarget != null)
            {
                return dinfo.IntendedTarget == this;
            }

            return dinfo.Instigator is Pawn pawn && pawn.CurJob != null && pawn.CurJob.targetA.Thing == this;
        }

        public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
        {
            Map map = Map;
            IntVec3 pos = Position;
            bool spawned = Spawned;
            if (!collapsing && spawned && PocketMapExists)
            {
                CollapseBelow();
                SoundDefOf.Roof_Collapse.PlayOneShot(new TargetInfo(pos, map));
                if (!sealing)
                {
                    FilthMaker.TryMakeFilth(pos, map, ThingDefOf.Filth_RubbleRock, 6);
                }
            }

            base.Destroy(mode);
            if (sealing && spawned && map != null && !map.Disposed)
            {
                Thing sealedShaft = ThingMaker.MakeThing(YDMDefOf.YDM_SealedShaft);
                sealedShaft.SetFactionDirect(Faction.OfPlayer);
                GenSpawn.Spawn(sealedShaft, pos, map, WipeMode.Vanish);
            }
        }

        // 아래층들을 깊은 곳부터 무너뜨린다.
        public void CollapseBelow()
        {
            if (collapsing)
            {
                return;
            }

            collapsing = true;
            List<Pawn> lost = new List<Pawn>();
            int lostItems = 0;
            CollapseMapRecursive(PocketMap, lost, ref lostItems);
            pocketMap = null;
            exit = null;

            StringBuilder text = new StringBuilder();
            text.Append("YDM.CollapsedLetterText".Translate(LayerUtility.LayerLabel(DepthBelow)));
            if (lost.Count > 0)
            {
                text.AppendLine();
                text.AppendLine();
                text.AppendLine("YDM.CollapsedLetterLost".Translate());
                foreach (Pawn pawn in lost)
                {
                    text.AppendLine("  - " + pawn.LabelCap);
                }
            }

            if (lostItems > 0)
            {
                text.AppendLine();
                text.Append("YDM.CollapsedLetterItems".Translate(lostItems));
            }

            LetterDef letterDef = lost.Any(p => p.Faction == Faction.OfPlayer) ? LetterDefOf.NegativeEvent : LetterDefOf.NeutralEvent;
            Find.LetterStack.ReceiveLetter("YDM.CollapsedLetterLabel".Translate(), text.ToString().TrimEnd(), letterDef, new TargetInfo(Position, Map));
        }

        private static void CollapseMapRecursive(Map map, List<Pawn> lost, ref int lostItems)
        {
            if (map == null || map.Disposed)
            {
                return;
            }

            Building_MineShaft deeper = LayerUtility.DownShaft(map);
            if (deeper != null && deeper.PocketMapExists)
            {
                deeper.collapsing = true;
                CollapseMapRecursive(deeper.PocketMap, lost, ref lostItems);
            }

            if (Find.CurrentMap == map)
            {
                SoundDefOf.Roof_Collapse.PlayOneShotOnCamera();
                Find.CameraDriver.shaker.DoShake(1.5f);
            }

            DamageInfo dinfo = new DamageInfo(YDMDefOf.YDM_CaveIn, 99999f, 999f);
            List<Pawn> pawns = map.mapPawns.AllPawns.ToList();
            foreach (Pawn pawn in pawns)
            {
                if (pawn.Dead)
                {
                    continue;
                }

                if (pawn.RaceProps.Humanlike || pawn.Faction == Faction.OfPlayer)
                {
                    lost.Add(pawn);
                }

                pawn.Kill(dinfo);
            }

            foreach (Thing thing in map.listerThings.AllThings)
            {
                if (thing.def.EverHaulable && !(thing is Corpse) && !thing.def.IsWithinCategory(ThingCategoryDefOf.Chunks))
                {
                    lostItems += thing.stackCount;
                }
            }

            PocketMapUtility.DestroyPocketMap(map);
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos())
            {
                yield return gizmo;
            }

            if (Faction == Faction.OfPlayer)
            {
                Command_Action collapse = new Command_Action
                {
                    defaultLabel = "YDM.Collapse".Translate(),
                    defaultDesc = "YDM.CollapseDesc".Translate(),
                    icon = CollapseTex,
                    action = ConfirmCollapse,
                };
                yield return collapse;
            }
        }

        public void ConfirmCollapse()
        {
            int colonists = 0;
            int others = 0;
            if (PocketMap != null)
            {
                foreach (Map map in new[] { PocketMap }.Concat(LayerUtility.MapsBelow(PocketMap)))
                {
                    foreach (Pawn pawn in map.mapPawns.AllPawns)
                    {
                        if (pawn.Dead)
                        {
                            continue;
                        }

                        if (pawn.IsColonist)
                        {
                            colonists++;
                        }
                        else
                        {
                            others++;
                        }
                    }
                }
            }

            TaggedString text = "YDM.CollapseConfirm".Translate(LayerUtility.LayerLabel(DepthBelow));
            if (colonists > 0 || others > 0)
            {
                text += "\n\n" + "YDM.CollapseConfirmPawns".Translate(colonists, others);
            }

            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(text, delegate
            {
                sealing = true;
                Thing.allowDestroyNonDestroyable = true;
                try
                {
                    Destroy(DestroyMode.Vanish);
                }
                finally
                {
                    Thing.allowDestroyNonDestroyable = false;
                }
            }, destructive: true));
        }

        public override string GetInspectString()
        {
            StringBuilder sb = new StringBuilder(base.GetInspectString());
            if (sb.Length > 0)
            {
                sb.AppendLine();
            }

            sb.Append("YDM.LeadsTo".Translate(LayerUtility.LayerLabel(DepthBelow)));
            if (!PocketMapExists)
            {
                sb.Append(" " + "YDM.NotDugYet".Translate());
            }

            return sb.ToString();
        }

        public override void ExposeData()
        {
            base.ExposeData();
        }
    }
}
