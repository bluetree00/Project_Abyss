# Quest System Dev Guide

## 개요

EVE-TERRACIDE 퀘스트 아키텍처 기반으로 이식. 핵심 원칙:
- **ScriptableObject 전략 패턴**: 퀘스트/태스크/보상/조건 모두 SO로 조합
- **QuestEvents 정적 버스**: 게임 코드가 QuestManager를 직접 몰라도 됨
- **CSV 자동화**: 퀘스트 데이터는 CSV → 에디터 툴 → SO 생성 파이프라인

---

## 파일 구조

```
Assets/Abyss/Systems/Quest/
├── Scripts/
│   ├── Quest.cs                  # 핵심 SO — 퀘스트 정의
│   ├── QuestManager.cs           # 순수 C# 클래스, Managers.Quest로 접근
│   ├── QuestEvents.cs            # 정적 이벤트 버스
│   ├── QuestSaveData.cs
│   ├── Achievement/
│   │   └── Achievement.cs        # Quest 상속, 달성형 (취소 없음)
│   ├── Task/
│   │   ├── Task.cs               # 단일 목표 단위
│   │   ├── TaskGroup.cs          # Task 묶음
│   │   ├── Action/               # SimpleCount, PositiveCount, NegativeCount …
│   │   ├── Target/               # StringTarget, GameObjectTarget
│   │   └── InitialSuccessValue/  # IntialSuccessValue
│   ├── Reward/
│   │   ├── Reward.cs             # 추상 베이스
│   │   ├── GoldReward.cs         # GameRunSession.AddGold 호출
│   │   └── ItemReward.cs         # RunItemInventory.AddItem 호출
│   └── Condition/
│       ├── Condition.cs
│       └── IsQuestComplete.cs
├── Data/
│   ├── QuestDefinition.csv       # 퀘스트 원본 데이터
│   └── AchievementDefinition.csv # 업적 원본 데이터
└── Generated/                    # 에디터 툴이 자동 생성 (git 불필요 시 .gitignore 가능)
    ├── Quests/
    ├── Achievements/
    ├── Tasks/
    ├── Categories/
    ├── Targets/
    └── Rewards/

Assets/Abyss/Editor/Quest/
├── QuestSOGenerator.cs           # Tools > Quest > Generate From CSV
└── QuestDatabaseAutoLinker.cs    # Tools > Quest > Rebuild Database
```

---

## 퀘스트 제작 파이프라인

### 1. CSV 작성

`Assets/Abyss/Systems/Quest/Data/QuestDefinition.csv`:

```csv
codeName,displayName,description,category,target,needCount,rewardType,rewardAmount,isSavable,chapter
kill_slime_10,슬라임 사냥꾼,슬라임 10마리를 처치하라,Kill,Slime,10,Gold,500,true,1
clear_room_3,방 청소부,방 3개를 클리어하라,Room,Normal,3,Gold,300,true,1
collect_sword,검의 수집가,검 아이템을 획득하라,Item,sword_basic,1,Gold,200,true,1
```

`Assets/Abyss/Systems/Quest/Data/AchievementDefinition.csv`:

```csv
codeName,displayName,description,category,target,needCount,rewardType,rewardAmount,isSavable,chapter
kill_100_monsters,백인 학살자,몬스터 100마리 처치,Kill,*,100,Gold,1000,true,0
clear_10_rooms,방 탐험가,방 10개 클리어,Room,Normal,10,Gold,500,true,0
```

**CSV 컬럼 설명:**

| 컬럼 | 설명 | 예시 |
|------|------|------|
| codeName | 내부 고유 키 (영어, 소문자_언더스코어) | `kill_slime_10` |
| displayName | 화면 표시 이름 | `슬라임 사냥꾼` |
| description | 퀘스트 설명 | `슬라임 10마리를 처치하라` |
| category | QuestEvents 라우팅 키 (`Kill`/`Room`/`Item`/`Gold`) | `Kill` |
| target | 추적 대상 (`*` = 전체 포함) | `Slime` |
| needCount | 달성 필요 횟수 | `10` |
| rewardType | `Gold` (현재 지원) | `Gold` |
| rewardAmount | 보상 수량 | `500` |
| isSavable | 저장 여부 | `true` |
| chapter | 챕터 번호 (0 = 전 챕터 공통) | `1` |

### 2. SO 일괄 생성

```
Unity 에디터 → Tools → Quest → Generate From CSV
```

CSV 파일을 선택하면 자동 생성:
- `{codeName}.asset` (Quest SO)
- `{codeName}_Task.asset` (TaskGroup SO)
- `{category}.asset` (Category SO — 중복 방지)
- `{target}.asset` (StringTarget SO — 중복 방지)
- `{codeName}_Reward.asset` (GoldReward/ItemReward SO)

### 3. Database 자동 연결

```
Unity 에디터 → Tools → Quest → Rebuild Database
```

- `Assets/Abyss/Systems/Quest/Generated/QuestDatabase.asset` 자동 갱신
- `Assets/Abyss/Systems/Quest/Generated/AchievementDatabase.asset` 자동 갱신

### 4. Addressables 등록

Addressables 창 (Window → Asset Management → Addressables → Groups):

| Asset | Address 키 |
|-------|-----------|
| `QuestDatabase.asset` | `QuestDatabase` |
| `AchievementDatabase.asset` | `AchievementDatabase` |

AppBootstrapper가 시작 시 이 키로 자동 로드 → `QuestManager.Initialize()` 호출.

---

## 게임 코드에서 이벤트 발행

게임 시스템은 `QuestEvents` 정적 버스만 호출. QuestManager를 직접 참조하지 않는다.

```csharp
// 몬스터 처치 시 (MonsterBase.RaiseDied)
QuestEvents.ReportKill(monsterCodeName);

// 방 클리어 시 (RoomWaveController.ClearRoomAsync)
QuestEvents.ReportRoomClear("Normal");   // Normal / Boss / Elite

// 아이템 획득 시 (RunItemInventory.AddItem)
QuestEvents.ReportItemCollect(itemId);

// 골드 획득 시 (GameRunSession.AddGold)
QuestEvents.ReportGold(amount);
```

**현재 연동된 호출 지점:**

| 위치 | 이벤트 | 키 |
|------|--------|-----|
| `MonsterBase.RaiseDied()` | `ReportKill` | `monsterConfig.monsterName` |
| `RoomWaveController.ClearRoomAsync()` | `ReportRoomClear` | `"Normal"` |
| `RunItemInventory.AddItem()` | `ReportItemCollect` | `item.itemId` |
| `GameRunSession.AddGold()` | `ReportGold` | `amount (int)` |

---

## 새 이벤트 카테고리 추가 방법

예시: 보스 처치 이벤트 추가

**1. QuestEvents.cs에 이벤트 추가:**
```csharp
public static event Action<string> OnBossKilled;
public static void ReportBossKill(string codeName) => OnBossKilled?.Invoke(codeName);
```

**2. QuestManager.SubscribeQuestEvents()에 구독 추가:**
```csharp
QuestEvents.OnBossKilled += codeName => ReceiveReport("Boss", codeName, 1);
```

**3. 게임 코드에서 발행:**
```csharp
QuestEvents.ReportBossKill(_config?.monsterName ?? "Unknown");
```

**4. CSV에 카테고리 `Boss`로 퀘스트 작성 후 Generate.**

---

## 완료 알림 UI (미구현 — 향후 작업)

`QuestManager` 이벤트를 구독해 HUD 알림 표시:

```csharp
Managers.Quest.onQuestCompleted   += OnQuestCompleted;
Managers.Quest.onAchievementCompleted += OnAchievementCompleted;
```

- Provider → Presenter → View 3단 구조 준수
- UniTask 기반 큐 처리로 알림 순차 표시
- `UI_QuestNotifier` 컴포넌트 (미구현)

---

## 런 수명과 퀘스트

| 시점 | 처리 | 현황 |
|------|------|------|
| 앱 시작 | Achievement 전체 자동 등록 | ✅ (`Initialize()` 내부) |
| 런 시작 | 챕터 퀘스트 자동 등록 | ⬜ 미구현 (`GameRunSession.OnRunStarted` 훅 필요) |
| 런 종료 | 진행 중 퀘스트 저장 | ⬜ 미구현 (`QuestManager.Save()` 호출 필요) |
| 씬 전환 | 퀘스트 상태 유지 (DDOL) | ✅ (QuestManager는 Managers DDOL 하위) |
