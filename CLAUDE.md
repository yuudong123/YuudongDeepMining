# Yuudong Deep Mining

RimWorld 1.7(Waylith .NET 10) 개인용 모드. 모드 본체는 `YuudongDeepMining/`, 설명은 `YuudongDeepMining/README.md`.

- 빌드: `YuudongDeepMining/Source/YuudongDeepMining`에서 `dotnet build -c Release`
- 검사: `tools/ydmtest`에서 `dotnet build -c Release` 후 `dotnet bin/Release/net10.0/ydmtest.dll` (패치 적용, 정의·클래스 참조, 설계 원칙 검사. failed=0이어야 함)
- 번역: `python tools/ydm_lang.py` (영어·한국어 Keyed와 한국어 DefInjected를 생성. 번역 문구는 이 파일에서 고친다)
- 설계 원칙: `YuudongDeepMining/README.md`의 Design rules. 원본을 막는 Prefix 금지, 지도·위치 바꿔치기는 `Levels/GhostUtility.cs`에서만.
- 배포: 게임 `D:\Games\Steam\steamapps\common\RimWorld\Mods\YuudongDeepMining`에 모드 폴더 복사 (obj·bin 제외). 요청이 있을 때만.

## 공통 작업 절차
이 프로젝트는 Agent Harness Toolkit을 따른다.
- Toolkit: 로컬 `D:/study/harness`, 클라우드 `/home/user/harness` (GitHub `yuudong123/harness`, Private)
- 클라우드 세션에 Toolkit이 없으면 `yuudong123/harness`를 세션에 추가하고 `/home/user/harness`에 clone한다.
- 작업 전 Toolkit의 `CLAUDE.md`를 읽고 그 절차를 따른다. Toolkit은 수정 대상이 아니다.
