# Abyss Development Status

> 현재 진행 상황 및 공통 오류 관리 문서
> Last Updated: 2026-03-02

---

## 팀 구성

| 이름 | 브랜치 | 담당 |
|------|--------|------|
| KBG  | `dev/KBG-UI` | 아키텍처, UI, 전반 시스템 |
| Lee  | `dev/Lee-Grid` (추천) | 그리드 블록 시너지 시스템 |

> **Lee**: `develop` 기준으로 `dev/Lee-Grid` 브랜치 생성 후 작업 → PR → develop 머지

---

## 1. 현재 작업 중 (Now Working On)

### KBG
1.1 2026-03-02
- 작업 내용: dev/KBG-UI 브랜치 — 인게임 Combat/Explore/Boss UI 위젯 연결 작업

### Lee
1.2 2026-03-02
- 작업 내용: 그리드 블록 시너지 시스템 — 그리드/블록 SO 데이터 기반 리팩토링 진행 중
  - 그리드 기본 구조 완료 (`leeGrid.cs`, `leeGridSquare.cs`)
  - 블록 기본 구현 완료 (`leeShape.cs`, `leeGridManager.cs`)
  - 현재: 그리드 형태 및 블록 데이터를 ScriptableObject로 분리 중
- 참고 문서: `Lee_Abyss.md`

---

## 2. 현재 작업 목표 (Today Target)

### KBG
2.1 CombatPanel 위젯 바인딩 (HP바, 스킬 쿨다운)
2.2 ExplorePanel 위젯 바인딩 (미니맵, 골드, 층수)
2.3 BossPanel HP바 연결

### Lee
2.1 GridLayoutSO 클래스 작성 (그리드 형태 데이터 SO)
2.2 ShapeDataSO 클래스 작성 (블록 도형 데이터 SO)
2.3 leeGrid / leeShape SO 주입 방식으로 리팩토링

---

## 3. 최근 완료 작업 (Recently Done)

3.1 2026-03-02 — 불필요 파일 정리 (FSM 레거시 몬스터, Quest 주석 파일, BTMonster dead code 등 250개 파일 제거)
3.2 2026-03-02 — dev/KBG-UI 브랜치 생성
3.3 2026-02-24 — HUD 연동 구조 완성 (HudPresenter MVP, HUDIds Mode/Section 자동화)
3.4 2026-02-19 — AppBootstrapper Composition Root 구조 완성
3.5 그리드 블록 시너지 시스템 0.1 — 그리드/블록 기본 구조 구현 완료

---

## 4. 공통 오류 목록 (Shared Bugs)

4.1
- 증상: (없음)
- 원인 추정: -
- 해결 여부: -

---

## 5. 막힌 부분 / 조사 필요 (Blocked / Research Needed)

5.1 Quest 시스템 — 전체 주석 처리 상태. Phase 2에서 재설계 필요
5.2 GameEventManager — portal 이벤트만 존재, 활용 방향 미결정
5.3 ResourceManager — Addressables 전환 후 레거시 제거 여부 결정 필요

---

## 6. 다음 작업 순서 (Next Steps)

### KBG (UI 브랜치)
6.1 CombatPanel, ExplorePanel, BossPanel UI 위젯 연결
6.2 StagePoint 선택 UI (맵 화면) 구현
6.3 런 종료 → Result 씬 전환 플로우 연결

### Lee (그리드 브랜치)
6.1 SO 리팩토링 완성 (GridLayoutSO / ShapeDataSO 분리)
6.2 시너지 감지 로직 구현 (채워진 패턴 → 시너지 발동)
6.3 시너지 이벤트 연동 준비 (`event Action<SynergyDataSO>` 방식)

---

## 전체 로드맵

```
Phase 0  ██████████████████░░  기반 아키텍처 & 코어 시스템     ✅ 완료
Phase 1  ████░░░░░░░░░░░░░░░░  인게임 루프 완성                🔄 진행중
Phase 2  ░░░░░░░░░░░░░░░░░░░░  컨텐츠 (던전, 몬스터, 스킬)    📋 예정
Phase 3  ░░░░░░░░░░░░░░░░░░░░  폴리싱 & 밸런스                📋 예정
Phase 4  ░░░░░░░░░░░░░░░░░░░░  출시 준비                       📋 예정
```

### Phase 0 — 기반 아키텍처 ✅
- [x] Composition Root (AppBootstrapper, GameRunBootstrapper, UIRootBootstrapper)
- [x] Manager 허브 구조 (`Managers.Instance.*`)
- [x] UniTask 기반 비동기 초기화 패턴
- [x] Addressables 리소스 로드/해제 시스템
- [x] ObjectPooler
- [x] GameFlow 상태 머신 (None → Title → Lobby → InGame → Result)
- [x] Generic FSM (`StateMachine<T>`, `State<T>`)
- [x] PlayerRunState (HP/Gold 이벤트 기반)
- [x] HUD 자동화 구조 (HudPresenter MVP, HUDIds Mode/Section)
- [x] StagePoint 그래프 (Locked/Available/Visited/Cleared)
- [x] RoomManager (카테고리별 가중치 랜덤)
- [x] StageMapSpawner (Addressable 기반 맵 로드)

### Phase 1 — 인게임 루프 🔄
- [x] 그리드 블록 시너지 시스템 0.1 — 기본 그리드/블록 구조
- [ ] **그리드/블록 SO 리팩토링** `[Lee]` ← 현재 작업
- [ ] 시너지 감지 로직 `[Lee]`
- [ ] Combat/Explore/Boss UI 위젯 연결 `[KBG]` ← 현재 작업
- [ ] 플레이어 전투 판정 (히트박스, 데미지, 피격)
- [ ] 몬스터 FSM 신규 구현 (Cave Spider, Dark Mage, Fire Dragon)
- [ ] StagePoint 선택 UI
- [ ] 런 진행 루프 완성 (방 진입/클리어/런 종료)

### Phase 2 — 컨텐츠 확장 📋
- [ ] 방 레이아웃 추가 (각 카테고리 최소 3종)
- [ ] 이벤트 방 / 상점 UI
- [ ] 아이템/장비 ScriptableObject
- [ ] 인게임 레벨업 & 스킬 트리
- [ ] Quest 시스템 재설계

### Phase 3 — 폴리싱 📋
- [ ] 카메라 연출 / VFX / SFX 연결
- [ ] 밸런스 시트 작성
- [ ] UI 애니메이션

### Phase 4 — 출시 준비 📋
- [ ] 백엔드 로그인 & 계정 연동
- [ ] 리더보드, 설정 화면, QA

---

## 기타 세부 사항 정리

- 브랜치 머지는 항상 `develop` 경유 (main 직접 푸시 금지)
- `.meta` 파일은 에셋 삭제 시 반드시 함께 삭제
- 씬 파일은 작업자 개인 테스트 씬(`leeTestRunGameScene`, `TestRunGameScene`) 별도 유지