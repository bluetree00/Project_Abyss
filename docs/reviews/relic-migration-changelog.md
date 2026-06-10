# RelicFairy — 유물 마이그레이션 작업 변경 정리

작업일: 2026-06-03
범위: Tier 1 레거시 정리 + 유물 마이그레이션 P0~P6 (+오라 훅, 패시브 경로)
컴파일: 에러 0 / 신규 경고 0. 회귀 0(현재도 파생 Galahad/Gawain이 라이브, 신규 유물 경로는 휴면).

## 배경
"유물=별개 캐릭터" → "CombatGirl 단일 몸 + 유물(RelicClassSO)로 클래스 결정"으로 마이그레이션. 기획 결정: ①Knight/Mage/Berserker 폐기 ②유물에 스탯 추가 ③외형은 현재 오라, 추후 모델 ④무기=E/R, Q=유물 전용.

## 변경 단계
- Tier1: 죽은 zone-layout 전투 분기 제거(StartRoomGate/GameRunBootstrapper/RoomClearGate/ClearRewardTrigger). UseProcGen=true 불변으로 도달 불가 코드.
- P0: base PlayerController.InitLayerFSMs/RouteInputsToLayers 본문을 RegisterDefaultFSMs/DefaultRouteInputsToLayers로 채움. IsInAttackOrSkillState에 Charge/HeavyAttack. (파생 override라 하위호환)
- P1: RelicClassSO에 StatModifier[] stats. PlayerRuntimeStats에 _relic* 레이어 + ApplyRelicStats(reset 재적용=멱등). PlayerController.SetRelicAndApply + _relicApplied 가드. PlayerLoadout.Relic/SetRelic. GameRunBootstrapper 스폰 경로에서 SetRelicAndApply(Loadout.Relic).
- P4: CharacterDisplayStand에 relicClass 필드 + ConfirmSelect에서 SetRelic + 팝업 유물 우선 표시(폴백 유지).
- P5: HolyShield/SolarStrike SkillRuntime이 RelicClass.QSkillCinematic ?? CharacterData.QSkillCinematic.
- 오라 훅: ApplyRelic에서 auraVfxKey 있으면 SpawnRelicAuraAsync(멱등, OnDestroy 정리).
- 패시브 경로: RelicClassSO.Passives의 dead 루프 제거, Stats+Passives[].baseModifiers를 ApplyRelicStats로 가산.
- P6 연기(의도적): HolyShieldSkillRuntime의 _galahad 폴백을 라이브 파생 Galahad가 의존 → 제거 시 파생 방패 깨짐 → P7 파생 삭제와 함께.

## 현재 상태
- 회귀 0: 현재 스폰은 파생 Galahad/Gawain. Loadout.Relic=null → SetRelicAndApply no-op. 유물 경로는 휴면(코드 완비).
- 유물 경로: 선택대→Loadout→스폰 주입→ApplyRelic까지 끝-끝 연결. prefab에 relicClass 에셋만 꽂으면 켜짐.

## 남은 작업
- P2(에셋, 에디터): CombatGirl 프리팹+Addressables 등록, 공통 CharacterData, RelicClass_Galahad/Gawain.asset 작성, CharDisplay_*.prefab의 relicClass 할당, characterPickupPrefabs/CSV CP* 토큰 점검.
- P7(폐기): HolyShield _galahad 폴백 제거 + Galahad/Gawain/Knight/Mage/Berserker .cs·프리팹 삭제 + Addressables Characters.asset 정리.
- 별건: Tier1 orphan(DirectlyEnterFirstNextZoneAsync/SpawnRemainingWorldZonesAsync)은 Tier2 World/ 제거와 함께.

## git diff 요약(.cs)
12 files changed, +238/-70. 변경 11개 + RelicClassSO.cs(untracked 폴더라 diff 미표시). BossSpawner.cs/DefaultMoveAbility.cs는 세션 전부터 modified. 커밋 안 함.
