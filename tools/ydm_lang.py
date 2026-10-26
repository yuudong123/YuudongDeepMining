import io
import os
from xml.sax.saxutils import escape

MOD = "D:/Study/rimworld/YuudongDeepMining/YuudongDeepMining/"

K = {
    "YDM.SettingsCategory": ("Yuudong 심층 채굴", "Yuudong Deep Mining"),
    "YDM.Settings.MaxDepth": ("최대 깊이: 지하 {0}층", "Maximum depth: level {0}"),
    "YDM.Settings.MapSize": ("새 지하 층 크기: {0} x {0} (이미 생긴 층은 그대로)", "New level size: {0} x {0} (existing levels keep their size)"),
    "YDM.Settings.HardnessPerLayer": ("한 층마다 바위·광석 단단함 증가: +{0}", "Rock and ore hardness per level: +{0}"),
    "YDM.Settings.HardnessCap": ("단단함은 지하 {0}층부터 더 늘지 않음", "Hardness stops increasing from level {0}"),
    "YDM.Settings.HardnessExample": ("  → 지하 1층 {0}, 가장 단단한 층 {1} (채굴·파괴에 드는 시간 배율)", "  → Level 1: {0}, hardest level: {1} (time needed to mine or break rock)"),
    "YDM.Settings.Infestations": ("지하 곤충군락", "Underground infestations"),
    "YDM.Settings.InfestationBase": ("지하 1층 곤충군락 평균 간격: {0}일", "Level 1 infestation interval: {0} days on average"),
    "YDM.Settings.InfestationFactor": ("한 층 내려갈 때마다 간격 배율: {0}", "Interval multiplier per level: {0}"),
    "YDM.Settings.InfestationCap": ("곤충군락 빈도는 지하 {0}층부터 더 늘지 않음", "Infestation frequency stops increasing from level {0}"),
    "YDM.Settings.InfestationCooldown": ("곤충군락 사이 최소 간격: {0}일", "Minimum days between infestations: {0}"),
    "YDM.Settings.InfestationExample": ("  → 지하 1층 평균 {0}일, 지하 {1}층부터 평균 {2}일마다 (정착민이 그 층에 있을 때만)", "  → Level 1: every {0} days, level {1} and deeper: every {2} days (only while colonists are on that level)"),
    "YDM.Settings.SmartRaiders": ("공병·돌파 습격대가 갱도로 내려옴", "Sapper and breacher raids follow you down the shaft"),
    "YDM.Settings.SmartRaidersDesc": ("지상에 활동 중인 정착민이 없고 지하에만 있을 때, 공병·돌파 전략을 쓰는 습격대는 갱도까지 길이 열리면 내려와 공격합니다. 다른 습격대는 지하를 알아채지 못합니다.", "When no active colonists are on the surface but some are underground, raiders using the sapper or breacher strategy climb down the shaft once they can reach it. Other raiders never notice the underground levels."),
    "YDM.Settings.ShaftInvulnerable": ("갱도가 부서지지 않음", "Mine shafts are indestructible"),
    "YDM.Settings.ShaftInvulnerableDesc": ("켜면 갱도가 피해를 받지 않습니다. 갱도가 파괴되면 아래층이 모두 무너지므로, 습격에 갱도를 잃고 싶지 않을 때 켜세요. 폐쇄는 그대로 할 수 있습니다.", "Mine shafts take no damage. A destroyed shaft collapses every level below it, so turn this on if you don't want to risk losing them in a raid. You can still seal them."),

    "YDM.Settings.DebugLog": ("층 판단 디버그 로그", "Level decision debug log"),
    "YDM.Settings.DebugLogDesc": ("켜면 정착민이 다른 층의 일·욕구를 판단한 결과와 운반 판단 결과를 로그에 남깁니다. 문제를 찾을 때만 켜세요.", "Logs what colonists found when checking other levels for work and needs, and hauling decisions. Turn on only when tracking down a problem."),

    "YDM.RescueToLevel": ("{0}을(를) {1} 침대로 구조", "Rescue {0} to a bed on {1}"),
    "YDM.CaptureToLevel": ("{0}을(를) {1} 감옥으로 포획", "Capture {0} to a prison on {1}"),

    "YDM.RemoteNoOptions": ("다른 층에서 선택한 림이 여기서 할 수 있는 명령이 없습니다", "The colonists selected on another level have nothing they can do here"),

    "YDM.Settings.Profiler": ("성능 측정 표시", "Show performance measurement"),
    "YDM.Settings.ProfilerDesc": ("켜면 층 패널 위에 이 모드 코드가 최근 1초 동안 범주별로 쓴 시간(ms)과 호출 수를 보여 주고, 10초마다 로그에 남깁니다. 겹친 범주는 자기 몫만 셉니다.", "Shows, above the level panel, how much time (ms) and how many calls this mod used per category over the last second, and logs it every 10 seconds. Nested categories count only their own share."),
    "YDM.Perf.Title": ("심층 채굴 연산: {0}ms/초", "Deep Mining: {0} ms per second"),
    "YDM.Perf.Cat.WorkProbe": ("다른 층 일 판단", "work on other levels"),
    "YDM.Perf.Cat.NeedProbe": ("다른 층 욕구 판단", "needs on other levels"),
    "YDM.Perf.Cat.HaulScan": ("층 넘는 운반 판단", "cross-level hauling"),
    "YDM.Perf.Cat.BillSupply": ("다른 층 재료 판단", "ingredients from other levels"),
    "YDM.Perf.Cat.RescueScan": ("다른 층 구조 판단", "rescue to other levels"),
    "YDM.Perf.Cat.AnimalFollow": ("동물 따라가기", "animals following"),
    "YDM.Perf.Cat.Travel": ("층 이동 진행", "level travel"),
    "YDM.Perf.Cat.CountResources": ("자원 수 합산", "resource counts"),
    "YDM.Perf.Cat.CountBills": ("작업 지시서 개수 합산", "bill product counts"),
    "YDM.Perf.Cat.CountWealth": ("정착지 부 합산", "colony wealth"),
    "YDM.Perf.Cat.AlertPawns": ("알림 정착민 목록 합산", "alert pawn lists"),
    "YDM.Perf.Cat.Raids": ("습격대 하강", "raid descent"),
    "YDM.Perf.Cat.Infestation": ("곤충군락", "infestations"),
    "YDM.Perf.Cat.LevelPanel": ("층 패널", "level panel"),
    "YDM.Perf.Cat.Chain": ("갱도 줄 찾기", "shaft chain lookup"),

    "YDM.Surface": ("지상", "surface"),
    "YDM.LayerN": ("지하 {0}층", "underground level {0}"),
    "YDM.GoDown": ("{0}(으)로 내려가기", "Go down to {0}"),
    "YDM.GoingDown": ("{0}(으)로 내려가는 중", "going down to {0}"),
    "YDM.GoUp": ("{0}(으)로 올라가기", "Go up to {0}"),
    "YDM.GoingUp": ("{0}(으)로 올라가는 중", "going up to {0}"),
    "YDM.LeadsTo": ("연결: {0}", "Leads to: {0}"),
    "YDM.NotDugYet": ("(아직 아무도 내려가지 않음)", "(no one has gone down yet)"),
    "YDM.InspectDepth": ("현재 위치: {0}", "Current level: {0}"),
    "YDM.InspectHardness": ("바위·광석 단단함: {0}", "Rock and ore hardness: {0}"),
    "YDM.InspectInfestation": ("곤충군락 평균 간격: {0}일", "Average days between infestations: {0}"),

    "YDM.Collapse": ("갱도 폐쇄", "Seal shaft"),
    "YDM.CollapseDesc": ("갱도를 무너뜨려 막습니다. 아래층은 모두 사라지고, 그 안의 물건은 잃으며, 그 안에 있는 사람과 동물은 갱도에 깔려 죽습니다.", "Collapse the shaft and seal it. Every level below disappears: everything there is lost and every person and animal still down there is crushed."),
    "YDM.CollapseConfirm": ("정말 갱도를 폐쇄할까요? {0}와(과) 그 아래층이 모두 무너지며 되돌릴 수 없습니다.", "Really seal this shaft? {0} and every level below it will collapse. This cannot be undone."),
    "YDM.CollapseConfirmPawns": ("아직 아래에 정착민 {0}명, 다른 생물 {1}마리가 있습니다. 모두 갱도에 깔려 죽습니다.", "{0} colonists and {1} other creatures are still down there. They will all be crushed."),
    "YDM.CollapsedLetterLabel": ("갱도 붕괴", "Shaft collapsed"),
    "YDM.CollapsedLetterText": ("갱도가 무너져 {0}와(과) 그 아래층이 사라졌습니다.", "The mine shaft caved in. {0} and every level below it are gone."),
    "YDM.CollapsedLetterLost": ("무너진 갱도에 깔려 죽음:", "Buried in the collapse:"),
    "YDM.CollapsedLetterItems": ("물건 {0}개가 함께 묻혔습니다.", "{0} items were buried with it."),


    "YDM.RaidersDescending": ("{0}의 습격대가 갱도를 타고 지하로 내려오고 있습니다!", "Raiders from {0} are climbing down the mine shaft!"),
    "YDM.MaxDepthReached": ("더 내려갈 수 없습니다 (최대 깊이: 지하 {0}층)", "Can't dig any deeper (maximum depth: level {0})"),
    "YDM.OneShaftPerMap": ("이 지도에는 이미 갱도가 있습니다", "This map already has a mine shaft"),

    "YDM.AssignFree": ("자유", "free"),
    "YDM.AssignLabel": ("주로 머무는 층: {0}", "Home level: {0}"),
    "YDM.AssignDesc": ("이 정착민이 주로 머무는 층을 정합니다. 평소에는 그 층에 머물다가, 그 층에 할 일이 없고 갱도로 이어진 다른 층에 할 일이 있으면 사다리를 타고 가서 일하고, 한가해지면 돌아옵니다. 배고프거나 졸린데 그 층에 먹을 것이나 침대가 없으면 위로 올라갑니다.\\n\\n자유: 머무는 층 없이, 일을 따라 옮긴 층에 그대로 머뭅니다.", "Choose the level this colonist mainly stays on. They stay there, but when there is nothing to do on it and there is work on another level connected by the shaft, they climb the ladders to go and do it, then come back once idle. If they get hungry or tired with no food or bed on that level, they go up.\\n\\nFree: no home level; they stay wherever their work took them."),
    "YDM.LayerPanelTitle": ("갱도 층", "Mine levels"),
    "YDM.LayerPanelTip": ("눌러서 그 층을 봅니다. 선택한 림은 원격 선택으로 남아, 이 층을 우클릭해 명령할 수 있습니다. 림은 사다리를 타고 와서 명령을 수행합니다.\\n\\n단축키: 위층 {0}, 아래층 {1}", "Click to view that level. Selected colonists stay remote-selected, so you can right-click on this level to give them orders; they climb the ladders and carry them out on arrival.\\n\\nHotkeys: level above {0}, level below {1}"),
    "YDM.RemoteSelected": ("다른 층에서 선택됨: {0}", "Selected on other levels: {0}"),
    "YDM.RemoteMoveHere": ("사다리 타고 와서 여기로 이동", "Come up/down the ladders and go here"),
    "YDM.RemoteMenuTitle": ("{0} (다른 층: 도착하면 실행)", "{0} (other level: carried out on arrival)"),
    "YDM.RemoteMenuTitleMulti": ("{0}명 (다른 층: 도착하면 실행)", "{0} colonists (other level: carried out on arrival)"),
    "YDM.RemoteOrderFailed": ("{0}이(가) 도착했지만 지금은 할 수 없습니다: {1}", "{0} arrived but can't do this now: {1}"),
}

DEF_KO = {
    "ThingDef": [
        ("YDM_MineShaft.label", "갱도"),
        ("YDM_MineShaft.description", "암반을 곧장 파 내려간 나무 버팀목 갱도입니다. 바위와 광맥으로 가득한 새 지하 층으로 이어지므로, 정착지 주변 산을 파헤치지 않아도 됩니다. 지하 층에 다시 지으면 한 층 더 내려갑니다.\\n\\n지도마다 갱도는 하나만 지을 수 있습니다. 갱도를 폐쇄하거나 갱도가 파괴되면 그 아래층이 모두 무너집니다. 안의 물건은 모두 잃고, 아래에 있던 생물은 모두 깔려 죽습니다."),
        ("YDM_SealedShaft.label", "폐쇄된 갱도"),
        ("YDM_SealedShaft.description", "흙으로 메우고 판자로 막아 버린 갱도입니다. 그 아래에 있던 것은 영영 사라졌습니다.\\n\\n해체하면 자리를 비울 수 있습니다."),
        ("YDM_MineShaftExit.label", "갱도 사다리"),
        ("YDM_MineShaftExit.description", "갱도를 따라 위로 올라가는 사다리입니다."),
    ],
    "DamageDef": [
        ("YDM_CaveIn.label", "갱도 붕괴"),
        ("YDM_CaveIn.deathMessage", "{0}이(가) 무너진 갱도에 깔려 생매장되었습니다."),
    ],
    "MapGeneratorDef": [
        ("YDM_Underground.label", "지하 층"),
    ],
    "JobDef": [
        ("YDM_RescueAcrossLayers.reportString", "TargetA을(를) 다른 층 침대로 옮기는 중."),
        ("YDM_CaptureAcrossLayers.reportString", "TargetA을(를) 다른 층 감옥으로 옮기는 중."),
        ("YDM_HaulAcrossLayers.reportString", "TargetA을(를) 다른 층으로 나르는 중."),
        ("YDM_CarryThroughPortal.reportString", "물건을 들고 TargetA을(를) 지나는 중."),
        ("YDM_PlaceCarried.reportString", "들고 온 물건을 보관하는 중."),
    ],
    "WorkGiverDef": [
        ("YDM_RescueAcrossLayers.label", "다른 층 침대로 쓰러진 아군 구조"),
        ("YDM_RescueAcrossLayers.verb", "구조"),
        ("YDM_RescueAcrossLayers.gerund", "구조하는 중"),
        ("YDM_SupplyBillsAcrossLayers.label", "다른 층 작업대로 재료 가져가기"),
        ("YDM_SupplyBillsAcrossLayers.verb", "재료 운반"),
        ("YDM_SupplyBillsAcrossLayers.gerund", "재료를 가져가는 중"),
        ("YDM_HaulAcrossLayers.label", "다른 층으로 운반"),
        ("YDM_HaulAcrossLayers.verb", "운반"),
        ("YDM_HaulAcrossLayers.gerund", "운반하는 중"),
    ],
    "KeyBindingDef": [
        ("YDM_LayerUp.label", "심층 채굴: 위층 보기"),
        ("YDM_LayerDown.label", "심층 채굴: 아래층 보기"),
    ],
}


def keyed(path, idx):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    lines = ['<?xml version="1.0" encoding="utf-8"?>', "<LanguageData>", ""]
    for k, v in K.items():
        lines.append("  <%s>%s</%s>" % (k, escape(v[idx]).replace("\n", "\\n"), k))
    lines += ["", "</LanguageData>", ""]
    io.open(path, "w", encoding="utf-8", newline="\n").write("\n".join(lines))


keyed(MOD + "Languages/Korean (한국어)/Keyed/YDM_Keys.xml", 0)
keyed(MOD + "Languages/English/Keyed/YDM_Keys.xml", 1)

for folder, entries in DEF_KO.items():
    path = MOD + "Languages/Korean (한국어)/DefInjected/%s/YDM_%s.xml" % (folder, folder)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    lines = ['<?xml version="1.0" encoding="utf-8"?>', "<LanguageData>", ""]
    for k, v in entries:
        lines.append("  <%s>%s</%s>" % (k, escape(v), k))
    lines += ["", "</LanguageData>", ""]
    io.open(path, "w", encoding="utf-8", newline="\n").write("\n".join(lines))
print("ok", len(K))
