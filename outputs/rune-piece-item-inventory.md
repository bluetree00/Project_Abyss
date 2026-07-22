# 룬 조각 풀 설계용 — 아이템 데이터 현役/레거시 판별 보고서

작성일: 2026-07-19 · 브랜치 `dev/KBG-D` · **조사 전용(코드/에셋/CSV 무수정)**

---

## 0. 결론 요약 (먼저 읽을 것)

**아이템 효과 시스템은 "구현은 다 됐고, 데이터가 연결이 안 된" 상태다.**

- 효과 로직 **110종**이 전부 구현·등록돼 있고, 트리거 훅 13/14개가 발생지·소비지 양쪽 다 배선돼 있다. (= 자산으로서 살아 있음)
- 그러나 **런타임에 획득되는 아이템의 효과 목록은 항상 비어 있다.** 실제 게임에서 아이템 효과는 **단 하나도 발동하지 않는다.**
- 원인은 버그 하나가 아니라 **두 개의 독립된 단절**이다 (§3).

설계 관점의 함의: **"192아이템 중 뭘 살릴까"는 잘못된 질문이다.** 192종 CSV는 애초에 게임에 도달한 적이 없다. 살아 있는 자산은 아이템 *목록*이 아니라 **효과 실행 프레임워크(110종 로직 + 훅)** 이고, 이건 새 조각 풀에 거의 그대로 재사용 가능하다. 아이템 목록·모양·등급 쪽은 사실상 백지에서 다시 짜는 게 맞다.

---

## 1. 데이터 소스 지도

### 1.1 실제 파일

| 소스 | 경로 | 규모 | 상태 |
|---|---|---|---|
| CSV 원본(작업용) | `C:\Users\u\Desktop\사용중 테이블\ITEM_DATA.csv` | 23KB / 192행 / 153 고유 id | 오프라인 편집본 |
| JSON 폴백 | `Assets/RelicFairy/Resources/ITEM_DATA.json` | 74KB / 192엔트리 / 153 고유 id | 런타임 폴백 |
| 뒤끝 CDN | 차트명 `ITEM_DATA` | — | 우선 소스 |
| ItemSO 에셋 | `Assets/RelicFairy/Shared/Item/SOdata/<등급>/<분류>/*.asset` | **56개** | 실제 획득 경로가 쓰는 것 |
| SO 인덱스 | `.../SOdata/ItemSODatabase.asset` | 81 guid 참조 | 56개 실존 + 참조 불일치 추정 |
| 모양 테이블 | `Assets/RelicFairy/Resources/BLOCK_SHAPE_DATA.json` | 20종(id 1~20) | 로드됨 |
| 모양 원본 | `Assets/RelicFairy/Docs/MERLIN_RUNE_PIECE_DATA.csv` | 20행 | `cell_size` 40 vs JSON 90 불일치 |

CSV 헤더(14열):
`passive_id, item_name, slot, effect_type, trigger, value, value2, value3, max_stack, duration, description, stat_version, shape_id, grade`

### 1.2 연결 관계 (실선 = 실제 흐름, 점선 = 끊김)

```
[뒤끝 CDN "ITEM_DATA"] ──┐
[Resources/ITEM_DATA.json]├─→ ItemDataManager ──→ 메모리 153종 ┄┄╳┄┄→ (소비처 없음)
[Addressable "ITEM_DATA"] ┘   :26-61, :122-148                  └→ DebugStageRunPanel:117 (디버그 전용)

[ItemSO 56개] ──→ RuntimeItemData.FromSO ──→ 인벤토리 ──→ ItemEffectManager ──→ [효과 110종]
                   :34-76                                    :48-60
                        │
                   modifiers[] 가 전부 비어 있음 → effects 항상 빈 리스트 → 효과 0개 발동
```

**핵심 파일 위치**
- 로더: `Assets/RelicFairy/Systems/Managers/Scripts/DataManagers/ItemDataManager.cs`
- 행 모델: `.../DataManagers/ItemEntry.cs`
- CDN 계층: `Assets/RelicFairy/Systems/Managers/Scripts/ChartLoader.cs:81-140`
- 런타임 변환: `Assets/RelicFairy/Shared/Item/RuntimeItemData.cs`
- SO 정의: `Assets/RelicFairy/Shared/Item/ItemSO.cs`
- 효과 레지스트리: `Assets/RelicFairy/Systems/Item/Core/ItemEffectRegistry.cs:54-198`
- 효과 매니저: `Assets/RelicFairy/Systems/Item/Core/ItemEffectManager.cs`

---

## 2. "실제 사용 가능" 판정 기준

각 항목을 아래 4관문으로 판정했다. **네 개를 모두 통과해야 現役**이다.

| 관문 | 정의 | 검증 방법 |
|---|---|---|
| **(a) 로직** | 효과 동작이 C#으로 구현돼 있는가 | `ItemEffectRegistry` 등록 + 효과 클래스 실체 확인 |
| **(b) 배선** | 아이템 → 효과 인스턴스 생성 경로가 런타임에 실재하는가 | `RuntimeItemData.effects` 가 채워지는 경로 추적 |
| **(c) 데이터** | 항목이 파이프라인을 타고 로드되어 **소비**되는가 | 로드만이 아니라 소비처(caller) 존재 확인 |
| **(d) 획득** | 플레이 중 실제로 얻을 수 있는가 | 드랍/상점/보상 경로 추적 |

> 판정 시 (c)를 "로드되는가"가 아니라 **"소비되는가"** 로 잡은 것이 이번 조사의 분기점이다. ITEM_DATA는 (c-로드)는 통과하지만 (c-소비)에서 탈락한다.

---

## 3. 두 개의 단절 (직접 검증함)

### 단절 1 — 획득 경로가 CSV가 아니라 ItemSO를 쓴다

실제 획득은 전부 `RuntimeItemData.FromSO()` 를 경유한다:

| 획득 경로 | 위치 |
|---|---|
| 방 클리어 드랍 | `Systems/Stage/RunGame/RoomClearGate.cs:166` |
| 상점(차트) | `Systems/Stage/Shop/ShopRoomController.cs:593` |
| 상점(카탈로그 폴백) | `Systems/Stage/Shop/ShopRoomController.cs:668` |
| 월드 배치 픽업 | `Shared/Item/WorldItemDisplay.cs:47` |

`FromSO` 는 효과를 **`so.modifiers` 에서만** 만든다 — `RuntimeItemData.cs:48-57`:
```csharp
foreach (var mod in so.modifiers)
    data.effects.Add(new ItemEffectSlot { effectType = mod.Type.ToString(), ... });
```
그런데 **ItemSO 56개 전부 `modifiers: []`** 다. (직접 검증: 비어있지 않은 에셋 **0개**)
→ `data.effects` 는 항상 빈 리스트 → `ItemEffectManager.Rebuild()`(`:48-60`)가 아무것도 등록 못 함.

CSV 경로 `FromServer()` 의 호출자는 **`DebugStageRunPanel.cs:117` 단 하나**(디버그 패널). 직접 확인함.
`GameRunBootstrapper.cs:402-407` 은 ItemData를 **초기화만** 하고 읽지 않는다.

### 단절 2 — CSV id와 ItemSO id의 교집합이 0

직접 검증한 결과:
```
ItemSO id 56개      : item_adventure_map, item_aladdin_lamp, item_beauty_beast_rose ...  (동화 모티프)
ITEM_DATA id 153개  : item_t1_attack_flow, item_t1_battle_awaken, item_t1_calm_blade ... (티어 접두)
교집합              : 0
```
→ `FromSO` 안의 shapeId 구제 로직(`RuntimeItemData.cs:60-66`)도 **영원히 매치 실패**.
→ 결국 해시 폴백(`:71`)으로 떨어진다.

**두 단절은 독립적이다.** 하나만 고쳐도 효과는 살아나지 않는다.

---

## 4. 現役 표 — 실제로 살아 있는 것

### 4.1 現役 자산 ①: 효과 실행 프레임워크 (최대 재사용 가치)

`ItemEffectRegistry.cs:54-198`, 등록 110종. **효과 로직 자체는 전부 구현체가 실존**한다.

| 그룹 | 라인 | 개수 | (a)로직 | (b)배선 | 훅 상태 |
|---|---|---|---|---|---|
| 패시브 스탯 | 57-82 | 26 | ✅ | ⛔ | 상시 적용, 훅 불필요 |
| 정적 % 스탯 | 85-88 | 4 | ✅ | ⛔ | 상시 |
| 조건부/시간제 | 91-100 | 10 | ✅ | ⛔ | OnTick ✅ |
| OnHit | 103-109 | 7 | ✅ | ⛔ | OnPostDealDamage ✅ |
| 피격시 | 112-116 | 5 | ✅ | ⛔ | OnPre/PostTakeDamage ✅ |
| 처치시 | 119-120 | 2 | ✅ | ⛔ | OnKill ✅ |
| 방/보스 클리어 | 123-125 | 3 | ✅ | ⛔ | OnRoomClear/OnBossClear ✅ |
| 구르기 | 128-129 | 2 | ✅ | ⛔ | OnRollEnd/Land ✅ |
| 사망/빈사 | 132-133 | 2 | ✅ | ⛔ | OnNearDeath ✅ |
| **레시피** | **136-140** | **5** | ✅ | ⛔ | **⛔ 발생지 없음 (§5)** |
| 방/보스 입장 | 143-144 | 2 | ✅ | ⛔ | OnRoomEnter/OnBossEnter ✅ |
| 스킬 | 147-148 | 2 | ✅ | ⛔ | OnSkillUse ✅ |
| 특수 | 151-154 | 4 | ✅ | ⛔ | OnItemPickup ✅ |
| T3/T4 잔류 | 157-165 | 9 | ✅ | ⛔ | OnPostDealDamage ✅ |
| T3/T4 광폭 | 168-177 | 10 | ✅ | ⛔ | 피격/스킬 ✅ |
| T3/T4 타이밍 | 180-189 | 10 | ✅ | ⛔ | Pre/PostDealDamage ✅ |
| T3/T4 형태 | 192-195 | 4 | ✅ | ⛔ | ItemCombatMods ✅ |

> **(b)배선이 전 항목 ⛔인 이유는 효과 쪽 결함이 아니라 §3의 데이터 단절 때문이다.** 데이터만 연결하면 110종이 한꺼번에 살아난다 — 이게 이 프로젝트에서 가장 값어치 있는 재사용 자산이다.

**트리거 훅 (발생지↔소비지 양쪽 확인됨) — 13/14 정상**

| 훅 | 발생지 | 상태 |
|---|---|---|
| OnTick | `PlayerController.cs:967` | ✅ |
| OnPreDealDamage | `Shared/Combat/CombatDamage.cs:158` | ✅ |
| OnPostDealDamage | `Shared/Combat/CombatDamage.cs:246` | ✅ |
| OnPreTakeDamage | `PlayerController.cs:118` | ✅ |
| OnPostTakeDamage | `PlayerController.cs:212` | ✅ |
| OnNearDeath | `PlayerController.cs:151` | ✅ |
| OnKill | `MonsterBase.cs:818, :888` | ✅ |
| OnRollEnd / OnRollLand | `LocoDodgeState.cs:183 / :187` | ✅ |
| OnJumpLand | `LocoAirState.cs:146` | ✅ |
| OnSkillUse | `ActSkillStateBase.cs:69` | ✅ |
| ModifyHeal | `PlayerController.cs:220` | ✅ |
| OnRoomEnter / OnBossEnter | `GameRunSession.cs:477 / :474` | ✅ |
| OnRoomClear / OnBossClear | `GameRunSession.cs:489 / :512` | ✅ |
| OnItemPickup | `ClearRewardTrigger.cs:180`, `WorldItemDisplay.cs:156` | ✅ |
| **OnRecipeComplete** | **없음** | **⛔** |

**보조 인프라 (現役, 재사용 가능)**
- `ItemDynamicStats.cs:13-35` — 9개 가산 채널(공% 방% 공속 이속 치확 치피 스킬뎀 전체뎀 최대HP%)
- `ItemCombatModifiers.cs:16-44` — 공격 판정 변형 8종. 소비처 `Weapon/Scripts/ColliderInstance.cs:68`, `Shared/Combat/CombatDamage.cs:181`
- 효과 표시 레이어 — `Shared/Effect/EffectDescriptionFormatter.cs`, `EffectMetaRegistry.cs`

### 4.2 現役 자산 ②: 아이템 개체 56종 (껍데기)

| 항목 | 값 |
|---|---|
| 개수 | 56 (Common 20 / Rare 16 / Epic 12 / Legendary 8) |
| 보유 정보 | itemId, 표시명, 아이콘, 등급, 분류, VFX 키 |
| **효과** | **전부 없음** (`modifiers: []` × 56) |
| **모양** | **전부 `shapeId: 0`** → 해시 폴백으로 임의 배정 |
| 획득 | ✅ 가능 (드랍/상점/월드픽업) |

→ **이름·아이콘·등급 에셋으로서는 現役**, 게임 메커니즘으로서는 빈 껍데기.

### 4.3 現役 자산 ③: 획득 경로

| 경로 | 위치 | 판정 |
|---|---|---|
| 방 클리어 드랍 | `RoomClearGate.cs:140-175` | ✅ 動 (단 `luckTable` 미할당 시 `:142-146`에서 스킵) |
| 상점 — 레거시 카탈로그 폴백 | `ShopRoomController.cs:376-383, 657-688` | ✅ **실질적으로 유일하게 동작하는 상점 경로** |
| 월드 배치 픽업 | `WorldItemDisplay.cs:47,153` | ✅ 動 |
| 세이브 복원 | `GameRunSession.cs:298` | ✅ 動 |
| 상점 — 차트 경로 | `ShopRoomController.cs:586-611` | ⛔ `SHOP_PRICE_DATA.json`에 아이템 행 없음(`potion_hp` 1행뿐) |

### 4.4 現役 자산 ④: 룬 채널 (아이템과 별개, 완전 정상)

**아이템 채널과 룬 채널은 키 네임스페이스가 완전히 분리된 별도 시스템**이다.

| | 아이템 | 룬 |
|---|---|---|
| 팩토리 | `ItemEffectRegistry.Create` (딕셔너리) | `RuneEffectFactory.Create` (switch, `:15-55`) |
| 키 출처 | `ITEM_DATA.effect_type` | `MERLIN_RUNE_SYNERGY_DATA.effect_type` |
| 키 개수 | 110 | 24 (6속성×4단계) |
| 폴백 | `GenericStatEffect` (`:37`) | `new RuneEffect()` 무동작 (`:54`) |
| **키 교집합** | **0** | |
| 실동작 | ⛔ | **✅ 완전 동작** |

유일한 연결 간선 — `ItemEffectManager.cs:162-164`:
```csharp
// (Fix#2) 원거리는 HitFeedbackService.RaiseHit를 안 타므로 이 경로가 근/원 단일 통지점이다.
_ctx.Player?.RuneEffects?.NotifyHit(report);
```
이 호출이 **효과 순회 가드보다 앞**에 있어서, 아이템 효과가 0개여도 룬 채널은 정상 작동한다. (룬 시스템이 지금 멀쩡한 이유)

---

## 5. 레거시 / 폐기 후보 표

| # | 항목 | 위치 | 사유 | 판정 |
|---|---|---|---|---|
| L1 | **ITEM_DATA 192행 전체** | `Resources/ITEM_DATA.json`, 데스크탑 CSV | 소비처가 디버그 패널뿐. 게임에 도달한 적 없음 | **폐기 후보(최대 규모)** |
| L2 | 티어 접두 id 체계 `item_t1_*` | 위와 동일 | ItemSO id와 교집합 0 | **폐기** |
| L3 | `OnRecipeComplete` + 효과 5종 | `ItemEffectRegistry.cs:136-140`, `OnClearEffects.cs:49,67,92,103,123` | 발생지 없음. 데이터 문제와 무관하게 구조적 사망 | **폐기 또는 발생지 신설** |
| L4 | `ChallengeRewardTable` 인스턴스 API | `Challenge/ChallengeRewardTable.cs:32-46` | `.asset` 파일 부재(직접 확인), `For()` 호출자 0. `RoomClearGate.cs:86`은 static `DefaultReward` 사용 | **폐기 또는 에셋 생성** |
| L5 | `chapterFuelScale` 필드 | `ChallengeRewardTable.cs` | `DefaultChapterScale`(`:64-70`)에 가려짐 | **폐기** |
| L6 | `ItemChartId = "236201"` | `ItemDataManager.cs:18` | 선언만, 미사용. ChartLoader는 차트*명* 기준 | **죽은 상수** |
| L7 | 원석(`FuelKind.RuneOre`) 흐름 | `Challenge/FuelKind.cs:8` | 지급 코드 0줄 (§7) | **미구현** |
| L8 | `TryExtractPlacedRune` | `GameRunSession.cs:732` | 호출자 0 | **미배선** |
| L9 | 상점 차트 아이템 경로 | `ShopRoomController.cs:586-611` | `SHOP_PRICE_DATA.json` 아이템 행 0 | **데이터 부재** |
| L10 | `StatType` ↔ 레지스트리 키 불일치 | `StatModifier.cs:6-19` | 14개 중 6개 이름 불일치 (§8) | **지뢰 — 반드시 수정** |
| L11 | 모양 20종 중 12종 | `BLOCK_SHAPE_DATA.json` | CSV가 8종(1,2,4,8,10,11,13,18)만 참조 | **미사용(신설 시 활용 가능)** |
| L12 | `PASSIVE_DATA` 계열 | 메모리 기록 | 기존 레거시 표기 유지 | **폐기** |

---

## 6. 새 조각 풀 설계 — 즉시 사용 / 재작업 분류

### ✅ 그대로 가져다 쓸 수 있는 것

1. **효과 실행 프레임워크 110종** — `ItemEffectRegistry` + 효과 클래스 + 13개 훅. 조각 풀 효과를 여기 키로 정의하면 **로직 재작성 0**.
2. **`ItemDynamicStats` 9채널** — 조각 스탯 효과의 집계층 그대로 사용.
3. **`ItemCombatModifiers` 8종** — 공격 판정 변형(범위/추가타/원형/투사체수) 인프라 완비.
4. **모양 테이블 20종** — `BLOCK_SHAPE_DATA.json` 로드 정상. 조각 모양 어휘로 즉시 사용 가능(1~9칸).
5. **획득 경로 3종** — 방 클리어 드랍 / 상점 카탈로그 폴백 / 월드 픽업. 배관은 살아 있음.
6. **룬 채널 24효과** — 완전 동작. 손대지 말 것.
7. **효과 설명 표시 레이어** — `EffectDescriptionFormatter` / `EffectMetaRegistry`.
8. **세이브 왕복** — `RuntimeItemData` 직렬화 경로 동작.

### ⚠️ 재작업이 필요한 것

1. **아이템 목록 전체(192종)** — 재사용 불가. 새 조각 풀은 **백지 설계**가 맞다. 다만 CSV의 `effect_type` **어휘 54종**은 레지스트리와 100% 일치하므로 **효과 어휘로서는 재사용 가치가 있다**(항목이 아니라 어휘를).
2. **id 네임스페이스 통일** — 조각 데이터와 SO가 같은 id 공간을 쓰도록 최초에 확정할 것. 이번 사태의 근본 원인.
3. **데이터 → 효과 결선 방식 택1** —
   - (A) SO의 `modifiers` 를 authoring 하고 `FromSO` 유지, 또는
   - (B) id를 통일하고 획득 경로를 `FromServer` 로 전환.
   **(B) 권장** — 차트 기반이라 밸런싱 반복이 빠르고, 프로젝트의 CDN 정본화 방향(`RUN_STRUCTURE` 선례)과 일치. 단 CDN 재업로드 + 폴백 JSON 재생성이 매번 필요(기존 메모 사항).
4. **shapeId 실데이터 부여** — 56개 전부 0. 해시 폴백 제거 필요.
5. **모양·칸수 정책 재정의** — "8모양/4칸 상한"은 코드에 **강제 로직이 아예 없다**. 그리드 총량 20칸만 존재(`UI_GridPanel.cs:786, :1105`). 새 정책을 코드에 실제로 넣어야 함.
6. **원석 획득 경로 신설** — 지급 코드가 0줄. 조각 경제의 전제.
7. **`StatType` 이름 정합** — 6개 불일치 수정(§8).
8. **`OnRecipeComplete` 처리** — 발생지 신설 or 효과 5종 폐기.

---

## 7. 각주 — 기존 결함이 이 추출에 미친 영향

**① 원석 획득 경로 전무**
`FuelBank.Add` 전수 조사 결과, `FuelKind.RuneOre` 지급은 `GameRunSession.cs:732` 한 곳뿐이고 그마저 **환불**이다. 그 함수 `TryExtractPlacedRune` 는 **호출자가 0개**. 상점 룬 제거는 골드만 받고 끝난다 — `ShopServiceRunner.cs:97-99`:
```csharp
run.RuneExtractCount++;
// TODO(연결): 상점 UI가 룬판을 '제거 모드'로 열고, 선택된 룬을 run.TryExtractPlacedRune(item)로 제거.
```
→ **영향**: 조각 풀을 원석 경제 위에 설계하면 재화 소스가 없어 설계 전체가 공중에 뜬다. **조각 풀보다 원석 소스 설계가 선행되어야 한다.** HUD 원석 표시(`HudView.cs:133,280`)는 영원히 0.

**② `ChallengeRewardTable.asset` 부재**
직접 확인 — `Assets/` 전체에 `.asset` 없음(`.cs`/`.cs.meta`만). `RoomClearGate.cs:86`이 static `DefaultReward` 만 쓰므로 **하드코딩 폴백이 항상 경로**다. 그리고 `Make()`(`:90-94`)는 전 등급이 `fuelKind = EnhanceMaterial` 고정.
→ **영향**: 조각/원석을 챌린지 보상으로 주려면 이 테이블부터 실체화해야 한다. 현재는 등급별 보상 차등이 authoring 불가.

**③ ItemSO shapeId 전부 0**
직접 확인 — 56/56이 `shapeId: 0`. 생성기 `Assets/Editor/GenerateItemSOFromCSV.cs:111` 은 `newSO.shapeId = meta.shape_id` 를 넣게 돼 있으므로, 현 에셋들은 **생성기 이전 산물이거나 생성기를 우회**한 것.
→ **영향**: 기존 56종에서 "현재 모양·크기"를 **추출할 수 없다.** 요청하신 표의 모양 필드가 전부 미상인 이유. 새 풀은 모양을 처음부터 배정해야 한다.

**④ CSV 헤더 열수 불일치**
JSON은 `item_id` + `passive_id` 를 둘 다 갖고, CSV는 `passive_id` 만 갖는다. `ItemDataManager.ParseRow:178-179` 가 `passive_id → item_id` 로 back-compat 매핑해 흡수 중이라 **로드는 성공**한다.
→ **영향**: 로드 단계에서는 무해. 다만 **정본 id 컬럼이 뭔지 모호**하다는 뜻이므로, 새 스키마에서는 id 컬럼을 하나로 확정할 것. 부수 발견으로 `cell_size` 가 JSON 90 / CSV 40 으로 불일치(모양 테이블) — 새 풀에서 UI 스케일 결정 시 확인 필요.

**⑤ 해시 폴백 `%10+1`**
`RuntimeItemData.cs:69-73`:
```csharp
data.shapeId = (UnityEngine.Mathf.Abs(data.itemId.GetHashCode()) % 10) + 1;
```
③+단절2 때문에 **현재 게임의 모든 아이템이 이 줄을 탄다.**
→ **영향**: (1) 모양 20종 중 1~10만 나와 11~20은 영구 미사용. (2) `String.GetHashCode()`는 .NET 런타임/플랫폼 간 안정성이 보장되지 않아 **빌드마다 모양이 달라질 수 있다** — 세이브 호환성 리스크. 새 풀 도입 시 이 폴백은 제거 대상이며, 남긴다면 결정적 해시(FNV 등)로 교체할 것.

---

## 8. 추가 발견 — 아직 터지지 않은 지뢰

**`StatType` 열거형과 레지스트리 키 이름 불일치** (`Shared/.../StatModifier.cs:6-19`)

`FromSO` 는 `mod.Type.ToString()` 을 효과 키로 쓴다. 그런데 레지스트리는 **대소문자 구분 딕셔너리**다.

| StatType | 레지스트리 키 | 결과 |
|---|---|---|
| `AttackPower` | `AttackDamage` | ❌ 불일치 |
| `MaxHp` | `MaxHP` | ❌ 대소문자 |
| `MeleeAttack` | (없음) | ❌ |
| `RangedAttack` | (없음) | ❌ |
| `Projectile` | `ProjectileCount` | ❌ |
| `InstantDamage` | (없음) | ❌ |
| `Defense`, `MoveSpeed`, `AttackSpeed`, `SkillCooldownReduction`, `Luck`, `ActiveItemCooldownReduction`, `CritChance`, `CritDamage` | 동일 | ✅ |

**14개 중 6개가 불일치.** 불일치분은 `GenericStatEffect` 로 폴백되는데, 이건 `GenericStatEffect.cs:10-13` 에서 **값을 전부 `AllDamageFlat` 에 더한다** — 즉 "공격력 +10"이 "전체 피해 +10"으로 조용히 변질된다. 경고 로그는 남지만(`ItemEffectRegistry.cs:35-37`) 의미는 손실된다.

→ **지금은 `modifiers` 가 비어 있어 아무도 안 밟는다.** 하지만 §6-⚠️-3에서 (A)안(SO authoring)을 택하는 순간 **즉시 터진다.** (B)안을 권하는 부수적 이유이기도 하다.

---

## 9. 조사 신뢰도

**직접 검증함 (원문 확인)**
- ItemSO 56개 `modifiers` 비어 있음 / `shapeId: 0` — 전수 grep
- CSV id ↔ SO id 교집합 0 — 정렬 후 `comm` 비교
- `FromServer` 호출자 = `DebugStageRunPanel.cs:117` 단 하나
- `OnRecipeComplete` 발생지 부재
- `ChallengeRewardTable.asset` 부재
- `RuntimeItemData.cs:34-76` 전문
- `GameRunBootstrapper.cs:402-407` 이 ItemData를 초기화만 함

**에이전트 보고 기반 (라인 참조는 제시됐으나 전 항목 원문 대조는 안 함) — 추정 포함**
- 효과 110종의 개별 구현 품질(등록 사실은 확인, 각 클래스 내부 동작은 미검증)
- 훅 소비지 목록의 완전성
- `StatType` 불일치 6건 (열거형·레지스트리 양쪽 라인은 제시됨, 교차 대조는 미수행)

**미확인 (확인 필요)**
- `ITEM_DATA` 가 Addressables 그룹에 실제 등록됐는지 — `ItemDataManager.cs:36` 이 Addressable 키를 쓰는데 파일은 `Resources/` 에 있다. 미등록이면 오프라인 폴백이 **조용히 0건**을 반환한다.
- `ItemSODatabase.asset` 의 81 guid 참조 vs 실존 56개의 차이 — 25개 깨진 참조 **추정**.
- 뒤끝 CDN의 `ITEM_DATA` 차트 실제 내용(로컬 JSON과 동일한지)

---

*본 문서는 조사 결과만 담으며, 코드·에셋·CSV를 일절 수정하지 않았다.*
