# RelicFairy — 출시까지 작업 체크리스트

> 작성: 2026-06-27 / 작성 근거: 코드 정밀 조사로 검증된 5건 정정 + 메모리 노트(point-in-time, 일부 stale 가능) 종합.
> 표기 규칙: 각 항목에 **상태 / 근거**를 달았다. 코드로 직접 검증한 항목은 `[검증됨]`, 메모리 노트 기반 추정은 `[노트]`, 단정 불가는 **확인 필요**로 표시.
> ⚠️ 이 문서는 코드 변경 없이 작성됨(체크리스트만). 실제 작업 시 각 항목 재검증 권장.

---

## 0. 출시 블로커 우선순위 요약 (P0/P1/P2)

### P0 — 출시 불가 블로커
- [ ] **Steam Cloud 동기화 미착수** — `ISteamRemoteStorage`/`FileWrite`/`FileRead` 호출 0줄 `[검증됨: grep 0 hit]`. 파트너 대시보드 Auto-Cloud 경로 등록 필요. → §3
- [ ] **회사/제품명 `DefaultCompany/Project_A` 확정** — Cloud 경로·빌드 식별자에 박힘 `[노트: project_save_system]`. → §3
- [ ] **임시 `bossThreshold=0` → 출시값 복원** — 보스 등장 임계 임시 0으로 내려둠(테스트용), 출시 전 8 복원 `[노트: project_boss_custom_arena, project_ch1_complete_demo]`. → §7
- [ ] **`ChapterRegistry._finalChapter=1` 게이팅 해제** — 현재 Ch1 클리어=게임 승리(데모용). 전 챕터(Ch1~4) 연결로 전환 필요 `[노트: project_custom_room_exit_and_chapter_transition, project_ch1_complete_demo]`. **확인 필요**(데모 의도면 유지). → §1
- [ ] **보스 Ch2~4 완성 여부** — 동업자 LEE(dev/lee) 담당, 진척 **확인 필요** `[노트: project_collab_lee_kbg, project_shop_custom_per_chapter]`. → §7

### P1 — 출시 품질 필수
- [ ] 세이브 무결성 "거부모드" 전환 결정(현재 변조 시 경고만, 거부 안 함) `[검증됨]`. → §3
- [ ] 멀티기기 세이브 충돌해결(세대비교) 미구현 `[노트]`. → §3
- [ ] 방 빌드 프레임 분산 미구현(MapBuilder 전부 동기 for) `[검증됨: async 구문 0]`. → §5
- [ ] 서약 P1 근사 다운사이드 미적용(arthur 과강 등) + 스텁 3종(leodegrance/nimue/guinevere) 구현 `[노트: project_covenant_v2_impl]`. → §1
- [ ] Event 룸 템플릿 0개(마일스톤 강제는 있으나 콘텐츠 없음) `[노트: project_designed_run_structure]`. → §2
- [ ] 한글 기본폰트 Regular TTF 재생성(근본해결, 현재 머티리얼 보정만) `[노트: project_korean_font_thin]`. → §4
- [ ] 퀘스트/업적: 목록패널·추적위젯·세이브 통합 `[노트: project_quest_system_v2]`. → §1
- [ ] HUD Q스킬 소스 수정 미구현 `[노트: project_weapon_forge_system]`. → §1
- [ ] 적 가시성 Render Objects 등록(수동 작업 잔여) `[노트: project_enemy_visibility_render]` **확인 필요**. → §4
- [ ] 오디오 믹서 자산 수동 authoring + UI 슬라이더 `[노트: project_audio_mixer_impl]`. → §4

### P2 — 출시 후 가능하나 권장
- [ ] 카메라 오클루전 LayerMask=Nothing(현재 inert, 가림방지 미동작) `[노트: project_camera_occlusion_inert]` **확인 필요**. → §4
- [ ] 아이템 데이터 마감: CSV 변경 시 CDN 재업로드 + 폴백 JSON 재생성 동기화 `[노트: project_item_data_load_path]`. → §2
- [ ] 원격 LFS 회수(filter-repo + GitHub purge) 미완 `[노트: project_thirdparty_lfs_cleanup]`. → §6
- [ ] BaseCamp 진열광장 마감(깔때기·전시 재배치) `[노트: project_basecamp_display_plaza]`. → §4

---

## 1. 기능 미완 (게임 로직/시스템)

- [ ] **챕터 연결(_finalChapter 해제)** — 상태: Ch1만 승리 처리 / 근거: `[노트]` project_custom_room_exit_and_chapter_transition / 난이도: 중(보스 Ch2~4 의존). **확인 필요**(데모 게이팅 의도 여부).
- [ ] **서약 P1 근사 정리** — 상태: arthur 범위-50% 다운사이드 미적용→과강, bedivere/tristan/isolde 형태변환 미완 / 근거: `[검증됨/노트]` project_covenant_v2_impl / 난이도: 중.
- [ ] **서약 스텁 3종 구현** — 상태: leodegrance(보급)·nimue(그리드)·guinevere(재선택) 무동작 TODO. 아이템/룬/시너지 완성 후 구현 예정 / 근거: `[노트]` / 난이도: 중~상.
- [ ] **퀘스트/업적 UI 마감** — 상태: 이벤트채널·Generator·완료토스트 구현됨, 목록패널·추적위젯·세이브통합 남음 / 근거: `[노트]` project_quest_system_v2 / 난이도: 중.
- [ ] **HUD Q스킬 소스 수정** — 상태: Q=유물 모델 정정됐으나 HUD 표시 소스 미수정 / 근거: `[노트]` project_weapon_forge_system / 난이도: 하.
- [ ] **R스킬 처리 결정** — 상태: 부분 활성(입력→ActState.RSkill FSM + 카타나 RSkill_01 데이터 잔존). 정식 복원 vs 완전 제거 결정 필요 / 근거: `[검증됨]` PlayerController.cs:1083/1129/1164, KatanaAnimation.asset:117 / 난이도: 하(제거) ~ 상(복원). → §콘텐츠 결정 선행.

---

## 2. 데이터 마감

- [ ] **Event 룸 템플릿 작성** — 상태: 마일스톤 강제 배선만 있고 템플릿 0개 / 근거: `[노트]` project_designed_run_structure / 난이도: 중.
- [ ] **런 구조 CSV(RUN_STRUCTURE) CDN 업로드** — 상태: 미업로드면 SO 폴백 / 근거: `[노트]` project_run_structure_csv / 난이도: 하.
- [ ] **아이템 데이터 CDN/폴백 동기화** — 상태: CSV 변경 시 CDN 재업로드 + Resources/ITEM_DATA.json 둘 다 갱신 필요 / 근거: `[노트]` project_item_data_load_path / 난이도: 하(절차).
- [ ] **stat_version 정책 유지** — 상태: 출시 전 모든 차트 CSV stat_version=1 고정, 캐시 수동 정리 / 근거: `[노트]` project_stat_version_policy / 난이도: 하.
- [ ] **대사 CSV 소스 동기화** — 상태: 진짜 소스=프로젝트 TextAsset(데스크탑 CSV 아님), 편집 후 프로젝트 복사 필요 / 근거: `[노트]` project_dialogue_data_source / 난이도: 하.

---

## 3. 세이브 — 클라우드 / 무결성 (정정 반영)

> **핵심 정정(2026-06-27):** "PR1 완료"는 로컬 이어하기 한정. 클라우드/변조거부/멀티기기는 미완. (project_save_system 정정 이력 참조)

- [ ] **Steam Cloud 동기화(P0)** — 상태: 미착수. `ISteamRemoteStorage`/`FileWrite`/`FileRead`/`RemoteStorage` 코드 0줄 / 근거: `[검증됨: grep 0 hit]` / 작업: Steam 파트너 대시보드 Auto-Cloud 경로 등록(코드 아님) / 난이도: 하(설정)~중(검증).
- [ ] **회사/제품명 확정(P0)** — 상태: `DefaultCompany/Project_A` 임시값, Cloud 경로에 사용 / 근거: `[노트]` / 난이도: 하.
- [ ] **무결성 거부모드 결정(P1)** — 상태: HMAC-SHA256 .sig 사이드카 + SaveSanitizer 클램프 구현됨. 단 서명 불일치 시 **거부 아닌 경고만**(주석에 전환지점만 표시) / 근거: `[검증됨]` / 난이도: 하(전환) — 단 레거시 호환 정책 결정 선행.
- [ ] **멀티기기 충돌해결(P1)** — 상태: 세대비교 미구현 / 근거: `[노트]` / 난이도: 중.
- [ ] **PR5 텔레메트리 분리** — 상태: 완전 fire-and-forget 분리 미적용(로컬+서버 병행 기록) / 근거: `[노트]` / 난이도: 하.
- [ ] **런타임 검증** — 룬 Shape 재구성 시각/재집기, 복원 시 방 빌드 순서 / 근거: `[노트]` / 난이도: 중.

---

## 4. 아트 / UX 마감

- [ ] **한글 폰트 Regular TTF 재생성(P1)** — 상태: NotoSansKR Thin 마스터로 구워져 얇음/깨짐, 현재 FaceDilate/Outline 보정만 / 근거: `[노트]` project_korean_font_thin / 난이도: 중.
- [ ] **오디오 믹서 마감(P1)** — 상태: P1 코드 구현(폴백 동작), 믹서 자산 수동 authoring·UI 슬라이더(P2)·더킹(P3) 대기 / 근거: `[노트]` project_audio_mixer_impl / 난이도: 중.
- [ ] **적 가시성 Render Objects 등록(P1)** — 상태: 3-tier Forward+SSAO 코드 준비, Render Objects 등록은 수동 / 근거: `[노트]` project_enemy_visibility_render / 난이도: 하. **확인 필요**(등록 완료 여부).
- [ ] **카메라 오클루전(P2)** — 상태: Collider+OcclusionFader 둘 다 LayerMask=Nothing이라 inert(가림방지 미동작) / 근거: `[노트]` project_camera_occlusion_inert / 난이도: 하. **확인 필요**(의도적 비활성 여부).
- [ ] **BaseCamp 진열광장 마감(P2)** — 상태: 바닥 확장·받침대/아치 배치 완료, 깔때기·전시 재배치 미완 / 근거: `[노트]` project_basecamp_display_plaza / 난이도: 하.
- [ ] **챕터 배경 마감** — 상태: Ch1 완성, Ch2~4 RenderSettings 테마 확인 필요 / 근거: `[노트]` project_chapter_scene_backgrounds / 난이도: 중. **확인 필요**.

---

## 5. 최적화 (정정 반영)

> **핵심 정정(2026-06-27):** "최적화 PR1~9"는 실체 없음. 실제=세이브 PR1~6 + 성능 커밋 1건(672dcf81b). (project_optimization_naming 참조)

- [ ] **방 빌드 프레임 분산(P1)** — 상태: **미구현**. 커밋 메시지엔 "방 빌드 분산"이라 적혀 있으나 `MapBuilder.cs`에 async/await/UniTask/yield 0건, 빌드 루프 전부 동기 for → 빌드 시 동기 스파이크 잔존 / 근거: `[검증됨: grep 0 hit]` / 난이도: 중.
- [ ] **이미 적용된 것(참고)** — RFLog 로그 스트립, PooledOneShotVfx 풀, Addressable/ObjectPooler 워밍, Boss/MonsterSpawner 정리(커밋 672dcf81b) / 근거: `[검증됨: git show]`.
- [ ] **빌드 광원 티어** — 상태: Performant URP 티어 추가라이트 Disabled 원인 수정 완료 / 근거: `[노트]` project_build_lighting_quality_tier / 난이도: 완료(검증만).

---

## 6. 레거시 정리 (전수 점검 분류)

> 분류 기준: 1순위=死 확정(런타임 비참여 확인), 2순위=구버전 잔재(폐쇄섬 외부참조0), 3순위=콘텐츠 결정 선행, 보류=확인 필요, 삭제금지=동적사용.
> 출처: 사용자 정밀조사 전수 점검 결과(검증됨). 착수 전 표본 재확인 권장.

### 1순위 — 死 확정(안전 제거 대상)

**묘비(전체 주석 파일) 5** — 파일 전체가 주석 처리됨:
- [x] `MonsterInitializer.cs` / `StageEffectInitializer.cs` / `ObjectPoolEffectInitializer.cs` / `UIChapterMapData.cs` / `UIAddressableLoader.cs` — 근거: `[검증됨]` / 난이도: 하. ✅제거됨(2026-06-27).

**死 스크립트(외부참조 0) 18** — 어디서도 참조 안 됨:
- [x] `GameEventManager` / `UIDataManager` / `GoogleSheetManager` / `BackendManager` / `MainPanel` / `UI_Title` / `LoopBgm` / `AnimatorExtensions` / `AugmentSelector` / `ShapeBoundsUtility` / `TestRewardSpawner` / `RingMeshWarning` / `MonsterGroundMarker` / `MonsterStateType`(enum) / `ElementEffectEntry` / `ChapterTransitionOverlay` / `NavMeshSurfaceRuntimeLoader` / `UILobbyData` — 근거: `[검증됨]` / 난이도: 하. ✅제거됨(2026-06-27), CLAUDE.md 갱신 완료.
  > ⚠️ 주의: `GameEventManager`/`UIDataManager`는 CLAUDE.md 매니저 목록에 등재돼 있으나 실제 외부참조 0(死). 제거 시 CLAUDE.md도 갱신 필요.

**死 주석 1:**
- [x] `BlockSynergyBridge.cs:734` — GridEditView 언급 주석. ⚠️파일 자체는 보호(클래스 `MerlinRuneBridge` 현용) — 주석만 제거 / 근거: `[검증됨]` / 난이도: 하. ✅주석 정정됨(2026-06-27).

**서약 v1 에디터 스텁(死 잔재):**
- [x] `CovenantDataGenerator.cs` v1 8종 블록(prometheus/solomon/mordred/morrigan/cuchulainn/lugh/balor/hecate) + `CovenantPickup.cs:16` "morrigan" 툴팁. 활성 12종 클래스/SO/테이블은 삭제 완료, 이것만 잔존(런타임 비참여) / 근거: `[검증됨]` / 난이도: 하. ✅v1 8종 Create 엔트리 제거(nimue/arthur/morgana/galahad 4종 유지) + 툴팁 morrigan→nimue 정정(2026-06-27).

### 2순위 — 구버전 잔재(폐쇄섬, 외부참조 0)

- [x] **어빌리티-SO 섬** — `HeavyAttackAbilitySO`/`LightAttackAbilitySO`/`IHeavyAttackAbility`/`ILightAttackAbility`/`PlayerAbilitySetSO` + 에셋 Sword/HeavyAttack·LightAttack, Bow/BowHeavy·BowLightAttack. 무기 주력=EQUIPMENT_DATA CDN로 대체됨 / 근거: `[검증됨]` project_damage_pipeline / 난이도: 중(섬 일괄). ✅제거됨(2026-06-27). 4개 .asset은 이미 missing-script 고아였음.
- [x] **퀘스트 v1 섬 5** — `QuestSystem`/`QuestReporter`/`IsQuestComplete`/`ItemReward`/`GameObjectTarget`. v2(QuestEvents.Report)로 대체 / 근거: `[검증됨]` project_quest_system_v2 / 난이도: 하. ✅제거됨(2026-06-27). 참고: IsQuestComplete/ItemReward/GameObjectTarget는 v2 Condition/Reward/TaskTarget 프레임워크의 미사용(인스턴스 0) 노드였음.
- [x] **신구 1:1 대체쌍 3** — `SolarStrikeSkillRuntime`(신=SolarDescentSkillRuntime) / `SwordAttackInputPolicy`(신=SwordAttackPolicy/BowAttackPolicy) / `WeaponDisplayStand`(신=WeaponForgeAltar). 구버전 제거 / 근거: `[검증됨]` project_weapon_forge_system / 난이도: 하. ✅제거됨(2026-06-27).
- [ ] **R스킬 카타나 데이터 + 입력/FSM 배선** — KatanaAnimation.asset RSkill_01(actionType:5) + PlayerController 입력/ActState.RSkill. ⚠️3순위 R스킬 결정과 묶임 / 근거: `[검증됨]` / 난이도: 하(제거).
- [ ] **세이브 레거시 단일파일 마이그레이션 경로** — run_save.json→3슬롯 이전(MigrateIfNeeded). 출시 후 일정 경과 시 제거 가능 / 근거: `[노트]` project_save_system / 난이도: 하(후순위).

### 3순위 — 콘텐츠 결정 선행(결정 후 제거/유지)

- [ ] **DeathKnightBoss / DragonBoss 미사용 세트** — 프리팹 11개(DK 6 + Dragon 5) + SO 다수(DK_Combo1~4, DBIce*/DBThunder*PatternSO, DragonRoarData 등). 스폰테이블 참조 0 → **컷/WIP 보스 결정 필요** / 근거: `[검증됨]` / 난이도: 결정(보존이면 콘텐츠 작업).
- [ ] **Bamao UI 팩** — `Prefabs/UI/Bamao/` 프리팹 119개 + `TextColorSwitcher.cs`(서드파티 키트, 참조 0). 사용 안 할 거면 일괄 제거 / 근거: `[검증됨]` / 난이도: 하(결정 후).
- [ ] **방 버프 vs 서약 공존 결정** — 방 버프 **활성**(BuffTileInteraction→BuffHandler.AddBuff 실호출). 유지/정리 결정 / 근거: `[검증됨]` project_roombuff_deprecated(정정) / 난이도: 중(결정).
- [ ] **R스킬 복원 vs 제거 결정** — 2순위 R스킬과 동일 사안. 복원=콘텐츠, 제거=1순위 강등 / 근거: `[검증됨]` / 난이도: 결정.

### 보류 — 확인 필요(추가 조사 후 분류)

- [ ] **스카이박스 머티리얼** — `Skybox_Ch1_EnchantedDepths.mat`, `Skybox_CosmicVoid.mat`. 현재 어떤 씬 RenderSettings도 미참조 → 씬서 빠졌거나 다른 .mat 사용 가능 / **확인 필요**.
- [ ] **몬스터 가시성 머티리얼** — `Mat_MonsterRim`/`Mat_MonsterSilhouette`/`Mat_MonsterGroundShadow`. Render Objects 수동 배선 대기 가능(§4 P1과 연동) / **확인 필요**.
- [ ] **무기/스킬 SO** — `Sword_QSkill`/`Sword_ESkill`/`BowCollider`, `PhantomSlash`/`SenkoSlash`. Addressable/CSV 동적연결 여지 / **확인 필요**.
- [ ] **로스터 SO** — `WeaponRoster.asset`, `Roster.asset`. CSV/CDN 대체된 구 SO 추정 / **확인 필요**.
- [ ] **몬스터 패턴/조건 SO ~23개** — Beholder*/Golem*/*HpCond*/*ThrustCone, `MT_AndConditionSO`/`MT_DistanceConditionSO`. 패턴시스템 동적참조 여지 / **확인 필요**.
- [ ] **휴면/기타** — `EventStageData`/`MSTData` + .asset(Event 템플릿 0개와 연동 휴면, §2 연계), `NightVolumeProfile`, `ElementPalette_Default`, `wolfpack-Regular SDF`, Dialogue SO 2종, 맵/테스트 머티리얼 다수, 백업파일(`*.prefab.bak`, `_Recovery/*.unity`) / **확인 필요**.

### ⛔ 삭제 금지 — 동적사용(死 아님, 오인 제거 방지)

- [ ] **토큰 핸들러 5** — `DecorationHandler`/`CharacterPickupHandler`/`CovenantAltarHandler`/`PitTriggerHandler`/`WeaponPickupHandler`. `[TokenHandler]` 리플렉션 자동등록 → 정적 외부참조 0으로 보여도 **사용 중**. 제거 금지 / 근거: `[검증됨]`.
- [ ] **Lobby 델리게이트 배선** — `TopPanelViewer`/`PopupUpateProfileViewer`. Lobby 씬 델리게이트로 연결 → 제거 금지 / 근거: `[검증됨]`.
- [ ] **에디터 도구 ~50종** — `[MenuItem]` 호출형. 메뉴에서 호출 → 정적참조 0이라도 제거 금지 / 근거: `[검증됨]`.

---

## 7. 보스

- [ ] **임시 bossThreshold 복원(P0)** — 0(테스트)→8(출시) / 근거: `[노트]` project_boss_custom_arena / 난이도: 하.
- [x] **Ch1 보스 이중 등장 버그 — 해결됨(dev/KBG-D 기준)** — ForestGuardian 프리팹 비활성 배치(m_IsActive:0) + BossSpawner 1회만 활성화로 즉시등장·디졸브 재등장 두 경로 모두 차단. dev/lee와 보스 스폰 코드 동일 / 근거: `[검증됨]` 확인필요 4건 #4 / 상태: 완료.
- [ ] **Ch2~4 보스 완성(P0)** — 동업자 LEE 담당, KBG는 건드리지 말 것. 진척 / 근거: `[노트]` project_collab_lee_kbg, project_shop_custom_per_chapter / 난이도: 상. **확인 필요**.
- [ ] **보스 아레나 Ch2~4** — 동업자 담당(상점 커스텀과 별개) / 근거: `[노트]` project_shop_custom_per_chapter / **확인 필요**.

---

## 부록 A — 이번 정정 5건 요약 (코드 검증)

| 영역 | 기존 메모 | 정정(검증) | 근거 |
|---|---|---|---|
| 서약 | 구버전 8종 삭제 대기 | 활성 12종=클래스 12개(1:1). 구버전 8종 클래스/SO/테이블 삭제 완료, Editor 생성 스텁 + Pickup 툴팁만 잔존 | CovenantDataGenerator.cs:30~80, CovenantPickup.cs:16("morrigan") |
| 세이브 | PR1 완료(=클라우드 완성처럼 읽힘) | 로컬 한정. SteamCloud 미착수(RemoteStorage 0줄), 무결성=경고만 | grep RemoteStorage 0 hit |
| 최적화 | PR1~9 / 방 빌드 분산 | PR1~9 실체 없음. 방 빌드 분산 미구현(MapBuilder 동기 for) | git show 672dcf81b, MapBuilder async 0 |
| 방 버프 | 폐기/서약으로 교체 | 실제 활성(AddBuff 실호출), 서약과 공존 | BuffTileInteraction.cs:125 |
| R스킬 | 전면 폐기 | 부분 활성·도달 가능, 정책=신규 미작성뿐 | PlayerController.cs:1164, KatanaAnimation.asset:117 |

## 부록 B — 한계/주의

- 이 체크리스트는 **검증된 5건 정정 + 확인필요 4건 + 레거시 전수 점검(검증됨) + 메모리 노트** 기반이다. `[노트]` 표시 항목은 작성 시점 메모리이며 일부 stale 가능 → 착수 전 코드 재확인 권장.
- **확인필요 4건·레거시 전수 점검 전체 목록 반영 완료(2026-06-27 갱신).** §6에 4분류(死확정/구버전잔재/콘텐츠결정선행/확인필요) + **삭제금지(동적사용)** 추가. §6 "확인 필요" 항목은 검증 미완으로 의도적 보류.
- ⚠️ `[검증됨]` 항목이라도 死/구버전 일괄 제거 전 표본 빌드/컴파일 검증 1회 권장(리플렉션·델리게이트·MenuItem 호출형은 정적참조 0으로도 사용 중 — §6 삭제금지 참조).
- 관련 메모리: project_save_system / project_covenant_v2_impl / project_optimization_naming / project_roombuff_deprecated / project_rskill_dropped / project_release_checklist.
