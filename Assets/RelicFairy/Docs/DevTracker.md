# Abyss Development Status

> 현재 진행 상황 및 공통 오류 관리 문서
> Last Updated: 2026-05-19

---

## 팀 구성

| 이름 | 브랜치 | 담당 |
|------|--------|------|
| KBG  | `dev/KBG-D` (현재) | 아키텍처, UI, 전반 시스템, 플레이어 |
| Lee  | `dev/Lee-Grid` (현재) | 몬스터 AI, 그리드 블록 시너지 시스템 |

> 브랜치 머지는 항상 `develop` 경유, main 직접 푸시 금지

---

## 1. 현재 작업 중 (Now Working On)

### KBG — `dev/KBG-D`
- 전투 시스템/HUD 연동 완료
- **진행 중**: 런 진행 루프 (방 진입/클리어/런 종료), StagePoint 선택 UI

### Lee — `dev/Lee-Grid`
- 몬스터 AI 0.3ver 완성 → 머지 완료
- **진행 중**: 그리드 블록 시너지 시스템 (`UI/PuzzleGrid/Scripts/`)

---

## 2. KBG 작업 히스토리

### dev/KBG-N (2026-03-16 머지)
**게임 준비 패널 3-스테이트 + 무기 선택 + UI 기반 작업**

| 파일 | 내용 |
|------|------|
| `UI/Scene/Scripts/UI_PrepPanel.cs` | 메인/캐릭터선택/무기선택 3-스테이트 구조로 전면 재설계 |
| `Scripts/ScriptableObject/PlayerData/WeaponRoster.cs` | 준비화면 무기 목록 ScriptableObject (신규) |
| `Scripts/RunGame/PlayerLoadout.cs` | 로비 선택 결과(캐릭터+무기)를 InGame으로 전달하는 클래스 (신규) |
| `UI/SubItem/UI_WeaponSelectItem.cs` | 무기 선택 카드 UI 컴포넌트 (신규) |
| `Scripts/Bootstrapper/AppBootstrapper.cs` | `PlayerLoadout` 프로퍼티 추가 (DDOL 보관) |
| `Scripts/Bootstrapper/GameRunBootstrapper.cs` | 게임 시작 시 Loadout 무기 자동 장착 |
| `Characters/Player/PlayerController.cs` | **버그 수정**: UI 위 클릭 시 공격 입력 차단 (`IsPointerOverGameObject`) |
| `UI/Scene/ScenePrefabs/LobbyRoot.prefab` | PrepPanel 3-스테이트 레이아웃 완성 |
| `UI/RootUI/@UIRoot.prefab` | 구버전 HUD 텍스트 제거, 스트립 오버라이드 정리 |
| `Editor/FixPrepPanelRefs.cs` | 에디터 툴: PrepPanel 레이아웃 빌더 + 전체 UI 폰트 일괄 적용 |
| UI 전체 프리팹 | NotoSansKR-VariableFont_wght SDF 폰트 일괄 적용 |

### dev/KBG-D (이전 머지)
**전투 시스템 개선 + HUD 연동**

- HUD 전투 패널 플레이어 스탯/장비 연동
- 장비 선택(인벤토리) 기능 및 버리기 기능 추가
- 전투 시스템 개선: `AbilityExecution`, 회피(Dodge), 입력 차단 구조
- UI 레이아웃 수정

### dev/KBG (초기 브랜치)
**핵심 시스템 구축**

- Composition Root 완성 (AppBootstrapper, GameRunBootstrapper, UIRootBootstrapper)
- UIManager Addressable 기반 루트 구조 (@UIRoot DDOL 4-루트 체계)
- GameFlow FSM (Title → Lobby → InGame → Result)
- HUD 자동화 구조 (HudPresenter MVP, HUDIds Mode/Section)
- LogoScenario 제거 → UIRoot 기반 전환
- 로비 캐릭터 선택 기능 (`UI_PrepPanel` v1)
- 플레이어 인게임 구조 개선 (Layered FSM, InputBuffer)
- 플레이어 무기 판정 구조 개선 (SO 런타임 오염 제거)
- 플레이어 점프 공격 후 착지 공격 불가 버그 수정

---

## 3. Lee 작업 히스토리

> 여기서는 머지 현황만 기록.

| 날짜 | 내용 |
|------|------|
| 2026-03-16 | Monster 0.3ver (몬스터 AI) — develop 머지 |
| 이전 | Puzzle 0.5ver (그리드 퍼즐 시스템) |
| 이전 | Grid Addressable 주소 변경 |
| 이전 | Monster 0.1~0.2ver |

---

## 4. 공통 버그 / 이슈

| # | 증상 | 원인 | 상태 |
|---|------|------|------|
| 01 | UI 팝업 위 클릭 시 플레이어 공격 발사 | `PlayerController.Attack.started` 콜백에 UI 체크 없음 | **해결** (IsPointerOverGameObject 추가) |
| 02 | "캐릭터 리스트 참조 누락" 런타임 경고 | EditPrefabContentsScope 첫 실행 시 SerializedObject ref 미저장 | **해결** (에디터 툴 재실행으로 확인) |

---

## 5. 막힌 부분 / 조사 필요 (Blocked / Research Needed)

5.1 Quest 시스템 — 전체 주석 처리 상태. Phase 2에서 재설계 필요
5.2 GameEventManager — [제거됨 2026-06-27: 死 스크립트(외부참조 0) 정리]
5.3 Steam 로그인 — 뒤끝 Federation 500 에러 (Steam 앱 `unavailable` 상태). 앱 출시 예정 전환 후 재시도
5.4 ResourceManager — ✅ 해결됨 (2026-05-11): ResourceManager 삭제, 의존 코드 인라인 전환 완료

---

## 6. 다음 작업 순서 (Next Steps)

### KBG
6.1 런 진행 루프: 방 진입 → Combat → 클리어 → 다음 방 선택 흐름 완성
6.2 StagePoint 선택 UI (맵 화면) 구현
6.3 런 종료 → Result 씬 전환 플로우 연결
6.4 인게임 HUD CombatPanel 세부 위젯 바인딩 (스킬 쿨다운 등)

### Lee
6.1 그리드/블록 SO 리팩토링 완성
6.2 시너지 감지 로직 구현 (채워진 패턴 → 시너지 발동)
6.3 시너지 이벤트 연동 준비 (`event Action<SynergyDataSO>`)

---

## 전체 로드맵

```
Phase 0  ████████████████████  기반 아키텍처 & 코어 시스템     ✅ 완료
Phase 1  ████████░░░░░░░░░░░░  인게임 루프 완성                🔄 진행중
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
- [x] 그리드 블록 시너지 시스템 기본 구조 `[Lee]`
- [x] 몬스터 AI 0.3ver `[Lee]`
- [x] 플레이어 전투 시스템 (AbilityExecution, Layered FSM, InputBuffer) `[KBG]`
- [x] 인벤토리 장비 선택/버리기 기능 `[KBG]`
- [x] HUD 전투 패널 플레이어 스탯/장비 연동 `[KBG]`
- [x] **게임 준비 패널 3-스테이트 (캐릭터+무기 선택 → 게임 시작)** `[KBG]`
- [ ] 그리드/블록 SO 리팩토링 `[Lee]`
- [ ] 시너지 감지 로직 `[Lee]`
- [ ] 런 진행 루프 완성 (방 진입/클리어/런 종료) `[KBG]`
- [ ] StagePoint 선택 UI `[KBG]`
- [ ] 몬스터 추가 구현 (Cave Spider, Dark Mage, Fire Dragon) `[Lee]`
- [ ] 런 종료 → Result 씬 전환 `[KBG]`

### Phase 2 — 컨텐츠 확장 📋
- [ ] 방 레이아웃 추가 (각 카테고리 최소 3종)
- [ ] 이벤트 방 / 상점 UI
- [ ] 아이템/장비 ScriptableObject 확장
- [ ] 인게임 레벨업 & 스킬 트리
- [ ] Quest 시스템 재설계

### Phase 3 — 폴리싱 📋
- [ ] 카메라 연출 / VFX / SFX 연결
- [ ] 밸런스 시트 작성
- [ ] UI 애니메이션

### Phase 4 — 출시 준비 📋
- [ ] Steam 로그인 & 뒤끝 계정 연동 (현재 보류)
- [ ] 리더보드, 설정 화면, QA

---

## 기타 세부 사항

- 브랜치 머지는 항상 `develop` 경유 (main 직접 푸시 금지)
- `.meta` 파일은 에셋 삭제 시 반드시 함께 삭제
- 씬 파일은 작업자 개인 테스트 씬(`leeTestRunGameScene`, `TestRunGameScene`) 별도 유지
- UI 폰트 기준: **NotoSansKR-VariableFont_wght SDF** (에디터 툴 `Tools/Fix All UI Fonts`로 일괄 적용)
- Canvas 기준 해상도: 1920×1080, Scale With Screen Size, Match 0.5