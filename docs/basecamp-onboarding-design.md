# BaseCamp 초회 온보딩 강제 시퀀스 — 설계

> 상태: **기획/설계 (미구현)**. 구현은 사용자 명시 지시 후.
> 작성: 2026-06-30

## 1. 목표

BaseCamp 첫 진입(초회)에만 동작하는 **강제 온보딩 시퀀스**. 플레이어가 컨텐츠를 순서대로 완료해야 다음으로 진행되는 구조.

**강제 순서**: `유물 선택 → 장비 선택 → 서약 선택 → 다음 맵 진입`

**연출/가이드** (전부 **초회만**):
- 가이드 **화살표** + **퀘스트** + **가이드라인 대사** 로 다음 목표 지시·강제
- **순차 잠금**: 이전 단계 완료 전까지 다음 제단/게이트 비활성·통과 차단
- **카메라 연출 1 — 단계 진입**: 시퀀스 전환 시 연출(초회만)
- **카메라 연출 2 — 구역 도달**: 유물/장비 구역에 도달하면 **주변 공간을 보여주며 분위기 전달**

2회차부터: 시퀀스/잠금/화살표/연출 전부 **스킵**, 자유 이용.

## 2. 기존 자산 (재활용 — 신규 최소화)

| 단계 | 기존 스크립트 | 퀘스트 보고 | 비고 |
|---|---|---|---|
| 유물 | `RelicAltar` (F 선택) | ✅ `Report("Relic", 이름)` | 이미 배선됨 |
| 장비 | `WeaponForgeAltar` (모루) | ✅ `Report("Equip", 무기)` | 이미 배선됨 |
| 서약 | `CovenantPickup` / `WorldCovenantPickup` | ❌ **없음** | `Report("Covenant", …)` 추가 필요 |
| 다음 맵 | `BaseCampDungeonGate` | (게이트는 `Report("Gate")` — StartRoomGate) | 현재 **유물+무기** 준비 시 활성. 서약 조건은 미포함 |

- **퀘스트**: `QuestManager`(Managers.Quest) — `RegisterQuest(codeName)`로 시작, `QuestEvents.Report` 단일 채널 구독, `onQuestCompleted` 이벤트, `completedQuests` **PlayerPrefs 영속**(="questSystem").
- **카메라**: `GameCameraController.PlayStartRoomTourAsync(center, ct)` — 둘러보기 패닝. BaseCamp **시작** 호출은 제거됐지만 **구역 도달 "주변 보여주기"로 재활용**(챕터/대기방 진입엔 여전히 사용).
- **대사**: 프로젝트 TextAsset `DIALOGUE_DATA.csv` + `UI_DialoguePopup`(BlocksGameplay=시간정지·입력잠금).
- **연출 보조**: `DissolveEffect`(제단 등장), `ScreenFade`.
- **허브 진입점**: `BaseCampBootstrapper`(싱글톤, 플레이어 스폰/카메라).

## 3. 신규 필요 구성

### 3-1. `BaseCampOnboardingDirector` (신규 — 핵심 오케스트레이터)
시퀀스 상태머신. BaseCamp 씬에 1개 배치(또는 BaseCampBootstrapper가 생성).
- **Step enum**: `Relic → Equip → Covenant → Gate → Done`
- 단계별로: ① 다음 대상 **잠금 해제**(이전 비활성) ② **화살표 타겟** 지정 ③ **가이드 대사** 출력 ④ **퀘스트 등록**
- `QuestEvents.OnReported`(또는 `QuestManager.onQuestCompleted`) 구독 → 해당 카테고리 보고 시 다음 Step 전이
- **초회 판정**: 온보딩 완료 퀘스트가 `completedQuests`에 있으면 **즉시 Done**(전체 스킵)

### 3-2. 가이드 화살표 `GuideArrow` (신규)
현재 목표를 가리키는 포인터. **현재 프로젝트에 네비 화살표 없음** → 신규.
- 옵션 A: **월드 3D 화살표**(목표 위 부유 + 바운스) — 직관적, 구현 단순
- 옵션 B: **화면 가장자리 인디케이터**(오프스크린 시) — 정교하나 복잡
- → 초회 짧은 동선이라 **옵션 A 권장**

### 3-3. 순차 잠금
- 제단/게이트에 `SetLocked(bool)` 추가(또는 GO 비활성 + 물리 배리어). Director가 단계별 토글.
- `BaseCampDungeonGate`: 현재 유물+무기 → **온보딩 중엔 Director가 제어**(서약까지 완료해야 활성). 2회차는 기존 조건.

### 3-4. 서약 픽업 퀘스트 배선
- `CovenantPickup`/`WorldCovenantPickup` 선택 시 `QuestEvents.Report("Covenant", 이름)` 추가(유물/장비와 동일 패턴).

### 3-5. 구역 도달 카메라 연출
- 유물/장비 구역에 **ZoneArrivalTrigger**(트리거 볼륨) → 최초 진입 시 `PlayStartRoomTourAsync(zoneCenter)` 1회 + 가이드 대사. Director가 초회 여부로 게이팅.

### 3-6. 온보딩 퀘스트 체인 SO
- `QuestSOGenerator`(CSV) 형식으로 3단계 퀘스트 저작: category `Relic`/`Equip`/`Covenant`, target `*`.
- 또는 단일 다태스크 퀘스트 + 완료 시 온보딩 achievement 완료(초회 플래그).

## 4. 상태 흐름

```
[BaseCamp 진입]
   └ Director.Start: 온보딩 완료 기록 있나?
        ├ 있음 → Done (자유 이용)
        └ 없음 → Step=Relic
             각 Step:
               1) 대상 잠금해제 / 그 외 잠금
               2) 화살표 = 대상, 가이드 대사
               3) (구역 도달 시) 주변 카메라 연출 1회
               4) Report(category) 수신 → 다음 Step
             Gate 완료 → 온보딩 achievement 완료(영속) → 던전 입장
```

## 5. 초회 판정 (영속)
- `QuestManager.completedQuests`(PlayerPrefs "questSystem")에 **온보딩 완료 퀘스트/업적** 존재 여부로 판정. 별도 플래그 불필요(기존 영속 재사용).
- 세이브 초기화/신규 계정 = 다시 초회.

## 6. 설계 대안 비교 (구조 리뷰)

| 안 | 장점 | 단점 | 채택 |
|---|---|---|---|
| **A. 전용 Director 상태머신** | 흐름 한 곳 집중, 잠금/화살표/연출 일원화, 디버그 쉬움 | 신규 클래스 1개 | **권장** |
| B. 퀘스트 afterQuest 체인 + 각 픽업 자체 잠금판단 | 신규 최소 | 흐름이 SO/픽업에 분산 → 파악·수정 어려움, 화살표/연출 주체 불명확 | ✕ |
| C. Timeline/하드코딩 연출 | 연출 정밀 | 잠금/분기 로직과 결합도↑, 데이터주도성↓ | ✕ |

→ **A 채택**: Director가 시퀀스를 소유하고, 기존 픽업/퀘스트/카메라는 **그대로 재활용**(이벤트 구독). 픽업 스크립트엔 `SetLocked` + 서약 Report만 추가(최소 침습).

## 7. 구현 분할 (제안 PR)
1. **PR1 — 배선 기반**: 서약 `Report("Covenant")` + 제단/게이트 `SetLocked` API + 온보딩 퀘스트 SO 저작
2. **PR2 — Director 상태머신**: Step 전이·잠금·초회 판정(화살표/연출 없이 로직만 검증)
3. **PR3 — 가이드 화살표 + 대사**: GuideArrow(월드 3D) + 단계별 가이드라인 대사
4. **PR4 — 카메라 연출**: 구역 도달 ZoneArrivalTrigger + PlayStartRoomTourAsync(초회 게이팅)

## 8. 결정 사항 (2026-06-30 확정)
1. **화살표**: **3D 월드 화살표 + 화면 가장자리 UI 인디케이터 둘 다** (화면 안=3D, 밖=UI)
2. **잠금**: **투명 돔 배리어 오브젝트** (구역별 돔, 단계 완료 시 제거)
3. **대사**: **임시 문구** 우선(추후 정식 대사 시퀀스로 교체)
4. **구역 도달 연출**: **둘러보기 + 자동 대사 넘김**
5. **게이트**: **서약까지** 완료 필수 (유물+무기+서약 예약)

## 9. 구현 상태 (PR1 — 게이팅 코어, 완료)
- 신규: `Systems/Stage/StartRoom/Onboarding/`
  - `BaseCampOnboardingDirector.cs` — 상태머신(유물→장비→서약→게이트), 퀘스트 보고 구독, 돔 배리어 토글, 초회 판정(PlayerPrefs `basecamp_onboarding_done_v1`), 화살표 타겟, 구역 도달 둘러보기 연출+임시대사
  - `OnboardingGuideArrow.cs` — 3D 월드 화살표(부유·바운스·빌보드) + 화면 가장자리 UI 인디케이터
  - `OnboardingZoneTrigger.cs` — 구역 도달 트리거(1회 통지)
- 수정: `BaseCampDungeonGate.cs` — 서약 예약 조건 추가
- 서약 퀘스트 보고는 `CovenantPickup`에 **이미 배선돼 있었음**(추가 불필요)

## 10. 구현 상태 (PR2/PR3 — 완료)
- **PR2 (연출)**: `OnboardingBarrierDome.cs`(콜라이더 즉시 해제 + 스케일/알파 디졸브) + Director가 단계 완료 시 `dome.Unlock()` 호출 + **둘러보기 중 입력 잠금**(`PlayerController.SetInputEnabled`)
- **PR3 (zero-config 화살표)**: `OnboardingGuideArrow`가 worldArrow/screenIndicator 미할당 시 **TMP 글리프(▼/➤) 자동 생성** — 수동 메시/UI 제작 불필요(핑크 위험 없음)

**남은 작업 = 씬 배선(에디터 수작업 권장)**:
- `@OnboardingDirector` GO: `BaseCampOnboardingDirector` + `OnboardingGuideArrow` 추가, guideArrow 연결
- steps[0] 유물(target=RelicAltar, unlockBarrier=장비존 돔), steps[1] 장비(WeaponForgeAltar, 서약존 돔), steps[2] 서약(CovenantPickup, 게이트 돔), gateTarget=던전 게이트
- 투명 돔 배리어 3개(투명 머티리얼 + 솔리드 콜라이더 + `OnboardingBarrierDome`) — 장비/서약/게이트 구역
- `OnboardingZoneTrigger`(트리거 콜라이더) 각 구역 배치(stepIndex + director)

**후속(선택)**: 임시 대사(HUD 노티스) → 정식 대사 시퀀스 + 자동 넘김
