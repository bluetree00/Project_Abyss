# Project Abyss - Claude 지침

## 세션 시작 시 자동 실행
- 대화 시작 시 `git fetch origin`으로 원격 패치를 확인한다
- 업데이트가 있으면 `git pull --ff-only`로 자동 풀 받는다
- 현재 브랜치: `dev/KBG-D`, 메인 브랜치: `main`, 통합 브랜치: `develop`

## 커밋 컨벤션
- `feat:` 새 기능
- `fix:` 버그 수정
- `docs:` 문서 변경
- `refactor:` 리팩토링
- `chore:` 기타 작업

## 프로젝트 아키텍처 요약

### 부트 플로우
```
AppBootstrapper (DDOL) → Managers (서비스 로케이터, DDOL) → GameFlow (상태 머신)
Logo → Login → Lobby → StageMap → GameScene → Result
```

### 핵심 시스템 위치
- 부트스트래퍼: `Assets/Abyss/Systems/Bootstrapper/Scripts/` (AppBootstrapper, GameRunBootstrapper, StageMapBootstrapper, HudBootstrapper, UIRootBootstrapper)
- 매니저: `Assets/Abyss/Systems/Managers/Scripts/` (Managers, UIManager, AddressableManager, InputManager 등)
- 게임 세션: `Assets/Abyss/Systems/Stage/RunGame/` (GameRunSession, PlayerLoadout, PlayerRunState)
- 스테이지: `Assets/Abyss/Systems/Stage/Stage/` (RoomManager, StagePointManager)
- 게임 플로우: `Assets/Abyss/Systems/Stage/GameFlow/GameFlow.cs`
- 플레이어: `Assets/Abyss/Characters/Player/Scripts/PlayerController.cs`
- 씬: `Assets/Abyss/Scenes/` (Logo, Login, Lobby, StageMap, GameScene, Result)

### 주요 패턴
- 서비스 로케이터: `Managers.Instance` → 하위 매니저 접근
- DDOL 싱글톤: AppBootstrapper, Managers, @UIRoot
- 비동기: UniTask 기반
- 세션 캡슐화: GameRunSession이 런 전체 상태 보유
- MVP: HudPresenter ↔ HudView ↔ CombatPanelView

## 코드 컨벤션 (필수 준수)
- **비동기**: `UniTask` 사용 (코루틴 사용 금지), `CancellationToken` 전달 및 `OperationCanceledException` catch 필수
- **리소스 로드**: `AddressableManager` 경유 (Resources.Load 사용 금지)
- **이벤트**: C# `event Action` 기반 (UnityEvent 지양), `OnEnable`에서 구독 / `OnDisable`에서 해제
- **UI 데이터 흐름**: `Provider → Presenter → View` 3단 구조 준수
- **ScriptableObject**: 정적 데이터(설정값)만 사용, 런타임 상태 저장 금지, `[CreateAssetMenu]` 필수
- **Canvas 기준**: 1920×1080, Scale With Screen Size, Match 0.5
- **씬 조작**: MCP HTTP 호출만 사용 (에디터 스크립트로 씬 수정 금지)

## Unity C# 규칙 (IMPORTANT)
- **직렬화**: `[SerializeField] private` only — Inspector용 public 필드 금지, 외부 접근은 public read-only 프로퍼티
- **컴포넌트 캐싱**: `Awake()`/`Start()`에서 캐싱 — `Update`/`FixedUpdate`/`LateUpdate`에서 `GetComponent`/`FindObjectOfType`/`GameObject.Find` 절대 금지
- **Null 안전**: 컴포넌트 존재 불확실 시 `TryGetComponent<T>()` 사용
- **할당 금지**: Update 루프에서 `new` 힙 할당, 문자열 접합(`+`) 금지
- **.meta 파일**: 직접 생성/수정/삭제 절대 금지 — Unity가 자동 관리
- **스레딩**: 백그라운드 스레드에서 Unity API 호출 금지
- **클래스 멤버 순서**: `Constants` → `Static` → `[SerializeField]` → `Private` → `Properties` → `Lifecycle`(Awake→OnEnable→Start→Update→OnDestroy) → `Public Methods` → `Private Methods` → `Event Handlers`
- **Animator**: 새 상태 추가 시 `writeDefaultValues = false` 필수

## 컴파일 검증 규칙 (필수)
- 모든 .cs 파일 수정 작업이 끝난 후, 반드시 `refresh_unity` → `read_console`로 컴파일 에러 확인
- 에러가 있으면 즉시 수정 후 다시 `refresh_unity` → `read_console` 확인
- 에러가 0이 될 때까지 반복한다
- 매 파일 수정마다가 아니라, 한 질문(작업 단위)이 끝난 시점에 1회 수행한다

## 현재 개발 상태
- **Phase 1 (인게임 루프)** 진행 중
- **참고 문서**: `Assets/Abyss/Docs/` (Core Architecture, BG_Abyss_Worklog, Lee_Abyss)
- **참고**: `DevTracker.md` 2026-05-11 업데이트 완료. ResourceManager 제거, Addressables 전환 반영됨

## 에이전트 팀 구조
역할 정의 파일은 `.claude/agents/`에 위치:
- `lead.md` — 팀장 (기획/조율): 작업 분해, 배분, 결과 평가
- `client.md` — 클라이언트 개발: 게임 로직, 무기/스킬, 네트워크
- `uiux.md` — UIUX 개발: HUD, Canvas, UI 프리팹

### 팀 워크플로우
1. 사용자 → 팀장에게 목표 전달
2. 팀장 → 작업 분해 후 클라이언트/UIUX에게 배분
3. 클라이언트/UIUX → 작업 완료 후 팀장에게 결과 보고
4. 팀장 → 기획 의도 기준으로 평가, 부적절 시 피드백 반환
5. 팀장 → 모든 결과 적절 시 사용자에게 최종 보고

### 파일 소유권
- 클라이언트: `Assets/Abyss/Shared/Characters/`, `Assets/Abyss/Systems/Network/`
- UIUX: `Assets/Abyss/UI/`
- 공유(수정 시 팀장 조율): `Assets/Abyss/Systems/Bootstrapper/`, `Assets/Abyss/Systems/Managers/`
- 서로의 영역을 직접 수정하지 않는다
