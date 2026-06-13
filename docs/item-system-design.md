# 아이템 시스템 v5 (160종) 구현 설계

> 전제: **기존 아이템 구조(CSV → RuntimeItemData → ItemEffectManager → Registry → IItemEffect)를 그대로 재사용**하고
> 로스터만 신규 160종으로 교체한다. 기존 동화 아이템 76종은 **데이터 보존 + 드롭/활성 풀에서 제외**(비파괴).

관련 코드:
- 데이터: `Assets/RelicFairy/Shared/Item/RuntimeItemData.cs` (`ItemEffectSlot`), CSV `ITEM_DATA.csv`
- 레지스트리: `Assets/RelicFairy/Systems/Item/Core/ItemEffectRegistry.cs`
- 효과 베이스/훅: `Assets/RelicFairy/Systems/Item/Core/ItemEffectBase.cs`
- 오케스트레이터: `Assets/RelicFairy/Systems/Item/Core/ItemEffectManager.cs`
- 블록 형태: `Assets/RelicFairy/Resources/BLOCK_SHAPE_DATA.json`

---

## 0. 확정/가정 (정정 환영)

| 항목 | 결정 |
|---|---|
| 테마 | 신규 160종으로 교체. 동화 76종은 비활성(보존, 미삭제) |
| 추가 형태 | 기존 CSV 포맷(`passive_id, slot, effect_type, trigger, value, value2, value3, max_stack, duration, description, stat_version, shape_id`) 행 추가 |
| 티어→rarity | T1=Common, T2=Rare, T3=Epic, T4=Legendary (가정) |
| T4 종수 | 기획 표지=24, 상세표=19(5/5/5/4). **19로 진행 + 누락 5종 확인 대기** |
| 블록 shape_id | 아래 §1 매핑 |

---

## 1. 블록 shape_id 매핑 (기존 카탈로그 재사용)

| 기획 블록 | shape_id | 이름 |
|---|---|---|
| 1×1 | 1 | 점 |
| 1×2 | 2 / 3 | 가로2 / 세로2 |
| 1×3 | 4 / 5 | 가로3 / 세로3 |
| L형 | 6 / 7 / 8 / 9 | ㄱ·역ㄱ·L·역L |
| T형 | 13 / 14 | T·역T |
| I형(4) | 11 / 12 | 가로4 / 세로4 |
| O형(4) | 10 | 정사각형(2×2) |
| J형(4) | 18 | 큰L자 |

→ **신규 블록 작업 불필요.**

---

## 2. effectType 매핑 — 3분류

### 🟢 분류 A: 순수 데이터 (기존 effectType 재사용, ~90종)
T1/T2 플랫·퍼센트 대부분. CSV 행만 추가하면 끝.

| 기획 효과 | effect_type | 단위 확인 |
|---|---|---|
| 공격력 +N (고정) | `AttackDamage` | flat |
| 방어력 +N | `Defense` | flat |
| 최대 HP +N | `MaxHP` | flat |
| 공격력 +N% | `AllDamage` (또는 Melee/Ranged) | 배율 |
| 방어력/HP +N% | `Defense`/`MaxHP` | ⚠️ %지원 여부 확인 |
| 이동속도 +N% | `MoveSpeed` | ⚠️ 현재 flat(m/s) — %처리 확인 |
| 공격속도 +N% | `AttackSpeed` | 배율 |
| 스킬 쿨타임 -N% | `SkillCooldownReduction` | 배율 |
| 스킬 피해 +N% | `SkillDamage` | 배율 |
| 복합(공+방 등) | 다중 slot 행 | — |

> **선행 체크리스트**: 각 effectType의 `value` 단위(flat/배율/%)를 `PassiveStatEffects`에서 확인해 기획 수치와 정합. 특히 MoveSpeed/방어%/HP%.

### 🟡 분류 B: effectType 1~2개 신설 (그 후 데이터)
| 기획 효과 | 신규 effect_type | 비고 |
|---|---|---|
| 치명타 확률 +N% | `CritChance` | `PlayerRuntimeStats`에 아이템 크릿 레이어 추가 |
| 치명타 피해 +N% | `CritDamage` | 동상 |

### 🟠 분류 C: 새 효과 클래스 = "효과를 바꾼다" (T3/T4 + 타임드 조건부)
대응 effectType이 없어 신규 `IItemEffect` 작성. **단 전부 기존 Registry/훅 위에서 동작** → 구조 변경 없음.
상세는 §4.

---

## 3. 선결 배선 (구조 변경 아님, 작은 연결 3+1)

| # | 작업 | 위치 | 필요 이유 |
|---|---|---|---|
| W1 | `EffectManager.OnTick(dt)` 펌프 | `PlayerController.Update`(이미 `RuneEffects.Tick` 구동) | 타이머 효과(광폭형 "30초마다"), 타임드 조건부 |
| W2 | `EffectManager.OnKill(target)` 라우팅 | `MonsterBase` 사망부(`CovenantHandler.OnKill` 옆) | 처치 효과(`GoldOnKill` 등 + 확정처치 보너스) |
| W3 | `HasShield` 실연결 | `ItemEffectContext.Update` | "보호막 존재 시" 조건 (현재 항상 false) |
| W4 | 아이템 동적 스탯 레이어 `SetItemDynamic*` | `PlayerRuntimeStats` | 타임드/조건부 버프 라이브 반영(룬 `SetSynergyDynamic*` 패턴 차용) |

> 현재 `RefreshItemBonuses`는 그리드 배치 변경 시에만 호출(정적) → 조건부/타임드는 W4 동적 레이어 + W1 틱으로 처리. 무조건 스탯은 기존 `ModifyStats` 그대로.

---

## 4. 신규 효과 클래스 목록 (T3/T4 실제 코드 작업)

표기: ♻ = 룬/기존 인프라 재사용, ★ = 난이도 높음

### 잔첨형
| 아이템 | 클래스 | 훅 | 재사용 |
|---|---|---|---|
| 누적의 인장 | `StackDamagePerTarget` | OnPostDealDamage | per-target Dict |
| 연쇄의 잔영 / 끝없는 메아리 | `EchoStrike` | OnPostDealDamage+OnTick | ♻ 지연타(DarkAfterimage) |
| 집요한 흔적 / 심연의 표식 | `TargetVulnStack` | OnPostDealDamage | ♻ MonsterStatusReceiver(받피증폭) |
| 쌍타의 검 | `SplitStrike` | OnPostDealDamage | per-counter |
| 메아리 화살 | `EchoArrow` | OnPostDealDamage | 투사체 스폰 |
| 독니의 자국 | `MarkExplode` | OnPostDealDamage | ♻ MonsterStatusReceiver(마크)+CombatQuery |
| 흡수의 자국 / 피의 순환 | `LifestealStack` | OnPostDealDamage | 기존 Lifesteal 확장 |
| 끝나지 않는 일격 / 천 번의 칼날 | `RepeatChance` | OnPostDealDamage | 확률 재발동 |
| 잔재의 칼날 | `CritMomentum` | OnPostDealDamage | ♻ 동적 크릿 레이어(W4) |
| 낙인의 사슬 | `BrandPropagate` | OnPostDealDamage+OnKill | ♻ 마크+W2 |

### 광폭형 ("다음 공격 강화" 버퍼 공통)
| 아이템 | 클래스 | 트리거 |
|---|---|---|
| 폭주의 코어 / 종말의 코어 | `TimedEmpowerNext` | OnTick 타이머 |
| 심판의 파편 | `HpThresholdAoE` | OnPostTakeDamage ♻CombatQuery |
| 각성의 인장 | `SkillReadyEmpower` | OnTick(쿨 충전 감지) |
| 폭발하는 분노 / 파국의 인장 | `DamageAccumEmpower` | OnPostTakeDamage 누적 |
| 최후의 숨결(+진화) | `LastBreath` | OnPostTakeDamage, 방 재충전 |
| 균열의 일격 | `GuaranteedCrit` | OnPostDealDamage 카운터 |
| 침묵의 폭발 / 억눌린 격노 | `SkillIdleEmpower` | OnTick(미사용 타이머) |
| 무게의 추 / 심판의 시간 | `ChargeWhileIdle` | OnTick(정지 충전) |
| 광기의 파동 | `DamageAccumPenetrate` | OnPostTakeDamage |
| 최종 결의 | `CounterShockwave` | OnPostTakeDamage ♻CombatQuery |

### 타이밍형
| 아이템 | 클래스 | 비고 |
|---|---|---|
| 완벽한 순간(+진화) | `JustGuard` ★ | 적 공격 windup 타이밍 필요 |
| 섬광의 순간 | `AttackInterrupt` ★ | 적 공격 캔슬 — windup 필요 |
| 마지막 기회 | `SkillCdReset` | OnPostDealDamage |
| 교차하는 칼날 / 이중 교차 | `DoubleHitTiming` | 0.1초 내 2적중 |
| 전환의 틈 | `RoomEntryWindow` | OnRoomEnter+OnTick |
| 숨 고르기 | `NoHitThenCrit` | OnTick 무피격 타이머 |
| 끝맺음의 검 / 확정의 끝맺음 | `ExecuteBonus` | OnPreDealDamage(치사 예측) |
| 여명의 일격 / 영원한 여명 | `CombatStartWindow` | OnRoomEnter+OnTick |
| 기다림의 미학 | `StationaryRangeBuff` | OnTick 위치 |
| 순간의 균열 / 균열의 시간 | `SkillCastGuard` | 스킬 시전 피격 |

### 형태변형
| 아이템 | 클래스 | 재사용 |
|---|---|---|
| 분산의 화살통 / 산탄의 비 | — | ♻ `ProjectileCount`(데이터) |
| 관통하는 창 / 관통하는 빛살 | — | ♻ `ProjectilePierce`(데이터) |
| 이중 타격의 장갑 / 메아리 치는 일격 | `MeleeMultiHit` | 근접 다단히트 |
| 확장된 칼끝 | `MeleeRangeExtend` ★ | 무기 판정 사거리 |
| 두 줄기 빛 | `SkillProjectileCount` | 스킬 투사체 |
| 원형 충격파 / 전방위 칼날 | `MeleeShapeCircle` ★ | 공격 판정 형태 변경 |

> **T4는 대부분 T3 클래스의 진화형(수치↑/효과 추가)** → 같은 클래스 + 파라미터로 흡수. 신규 고유 클래스는 위 목록 기준 **약 25~28종**.
> 형태변형 2종(★)·타이밍 2종(★)이 무기 코드 훅을 요구해 최난이도 — 후순위.

---

## 5. CSV 스키마

기존 컬럼 유지. 신규 효과는 `value/value2/value3/max_stack/duration`에 파라미터 매핑.
선택: 가독성용 `tier`, `category`(잔첨/광폭/타이밍/형태변형) 컬럼 추가 가능(로직 미사용, 에디터/표시용).

`passive_id` 네이밍: `item_t{tier}_{slug}` (예: `item_t3_accumulation_seal`).

---

## 6. 단계 (Phase)

| Phase | 범위 | 산출 |
|---|---|---|
| **P0** | 선결 배선 W1~W4 + 크릿 effectType(B) | 코드 소 |
| **P1** | 분류 A 데이터(~90종) + 단위 정합 | CSV |
| **P2** | 잔첨형·광폭형 클래스(♻ 룬 인프라) + 데이터 | 코드+CSV |
| **P3** | 타이밍·형태변형(★ 포함) + 데이터 | 코드+CSV |
| **P4** | 밸런스/VFX | — |

---

## 7. 미해결 (확인 필요)
1. T4 19 vs 24 (누락 5종)
2. 각 기존 effectType의 value 단위(특히 MoveSpeed/방어%/HP%)
3. 동화 76종 비활성 방식(드롭 테이블에서 제외 지점 = 어디서 필터?)
4. `JustGuard`/`AttackInterrupt`가 요구하는 **적 공격 windup(예고) 타이밍** 노출 여부 — 없으면 해당 4종 재설계
