# 일반 몹 공격 텔레그래프 / UX 분석 및 개편안

> 범위: 일반 몹 **27종** (보스 6종 제외). 폐기 확정된 Slime/Snail/Cactus 제외.
> 작성 관점: CLI 분석 한계상 **로직·타이밍·범위 정합성** 중심. 이펙트의 "시각적 룩" 정성 평가는 플레이 영상/스크린샷 필요 (체크리스트로 대체).
> 작성일: 2026-06-05 / **정정: 2026-06-05 (아래 0-A 참조)**

---

## 0-A. 🔴 중요 정정 — 두 가지 공격 경로

초판은 **기본 AttackState 경로만** 보고 27종 전체를 일반화했으나, 실제로는 **17종이 `PatternAttackOverride`(패턴 시스템)** 로 공격한다. `stateOverrides`에 연결되어 ChaseState/AttackReadyState/AttackState를 패턴 버전으로 교체한다.

| 경로 | 몹 | 공격 타이밍 출처 | 바닥 경고 |
|---|---|---|---|
| **A. 패턴 시스템 (17종)** | BattleBee, Beholder, BishopKnight, BlackKnight, CrabMonster, EvilMage, Fishman, Golem, LizardWarrior, NagaWizard, Orc, RatAssassin, Salamander, Skeleton, Specter, Spider, Werewolf | **패턴별 `damageDelay`** (Config의 damageApplyDelay 아님) | `MonsterGroundWarning` **지원** (패턴별 `disableWarning`로 on/off). 근접 패턴은 대부분 `disableWarning:1` |
| **B. 기본 AttackState (약 10종)** | ChestMonster, Cyclops, FairyBat, FlyingDemon, MonsterPlant, MushroomAngry, MushroomSmile, StingRay, WormMonster 등 | Config `damageApplyDelay` / `attackDelay` | 미사용 (애니 텔레그래프만) |

**정정 영향:**
- 3장 타이밍 표의 `attackDelay`/`damageApplyDelay` 수치는 **B 경로(기본 몹)에만 유효**. A 경로 몹은 패턴 에셋의 `damageDelay`가 실제값.
- P2("일반 몹은 바닥 경고를 전혀 안 쓴다")는 **부정확**: 시스템은 존재하며 A 경로에서 선택적으로 사용. 근접 패턴이 `disableWarning:1`로 끈 것은 **설계 선택**(애니 의존).
- "Beholder/Salamander/BishopKnight를 원거리로 전환" 제안은 **불필요**: 이미 패턴/특수상태(레이저·화염숨·돌진콘 등) 보유.

**A 경로 17종 패턴 damageDelay 전수 검증 (2026-06-05):**

기준: <0.25 = 🔴 반응 불가 / 0.25 = 🟡 경계(유지) / ≥0.3 = 🟢. 전 패턴 `disableWarning:1`(근접 애니 의존).

| 몹 | 패턴(dmgDelay) | 판정/조치 |
|---|---|---|
| BattleBee | Sting 0.18→**0.3**, DashSting 0.22→**0.3** | 🔴→🟢 수정 완료 |
| RatAssassin | Slash 0.2→**0.3**, Lunge 0.25 | 🔴→🟢 / 🟡 유지 |
| Specter | Bite 0.22→**0.3**, Lunge 0.42 | 🔴→🟢 수정 완료 |
| CrabMonster | Claw 0.25, Stab 0.4, Spin 0.65 | 🟡 경계 유지 / 🟢 |
| Skeleton | Slash 0.3, Thrust 0.55 | 🟢 양호 (초판 "이상치"는 죽은 Config값 오독) |
| Beholder | Laser1_Line3 0.35, Laser2_Burst8 0.45 | 🟢 (레이저 패턴 보유) |
| BishopKnight | Slash 0.45, Thrust 0.5 | 🟢 |
| BlackKnight | Slash 0.4, Thrust 0.5, Overhead 0.65 | 🟢 강공격 windup 차등 양호 |
| EvilMage | Bolt 0.45, Burst 0.8 | 🟢 |
| Fishman | SpearJab 0.4, BubbleShot 0.5 | 🟢 |
| Golem | Smash 0.35, Slam 0.95 | 🟢 강타 windup 우수 |
| LizardWarrior | Slash 0.4, Thrust 0.45 | 🟢 |
| NagaWizard | MagicBolt 0.45, HeavyBolt 0.82 | 🟢 |
| Orc | Slash 0.45, Spin 0.75 | 🟢 |
| Salamander | Bite 0.32, ChargeBite 0.5 | 🟢 (화염숨 특수상태 별도) |
| Spider | Bite 0.3, BodyBash 0.5, WebShot 0.48 | 🟢 |
| Werewolf | Claw 0.35, HeavySwipe 0.5 | 🟢 |

**결론**: A경로 17종은 대체로 **잘 튜닝**됨. 강공격일수록 windup이 길어지는 문헌 원칙(예: Golem Slam 0.95, EvilMage Burst 0.8)이 잘 지켜짐. 반응 불가(sub-250ms)는 BattleBee·RatAssassin·Specter 3건뿐이었고 모두 0.3으로 수정 완료. 경계값(0.25: RatAssassin Lunge, CrabMonster Claw)은 정확히 반응 하한이라 유지.

남은 공통 특성: **17종 전 근접 패턴이 `disableWarning:1`** → 바닥경고 없이 애니 텔레그래프에만 의존. 가독성 강화 여지는 있으나 설계 선택(P2 결정 사항).

---

## 0. 외부 UX 문헌 기준 (개편 판단 근거)

| 기준 | 수치/원칙 | 출처 |
|---|---|---|
| 단순 시각 반응시간 | **200~300ms (~0.25s, 60fps 기준 ~15프레임)** | retrogamedeconstructionzone, gdkeys |
| 텔레그래프(예고) 길이 공식 | **예고 = 플레이어 반응시간(0.25s) + 행동 실행시간 + 난이도 버퍼** | gdkeys "Anatomy of an Attack" |
| 공격 3단 구조 | **예고(Anticipation) → 타격(Strike) → 후딜(Recovery)** | gdkeys, rivalslib |
| 강공격 원칙 | 강한 공격일수록 **예고 포즈를 더 크고 길게** (물리감 + 공정성) | escapeindustries, gdkeys |
| 가독성 | 적마다 **고유 실루엣 + 고유 예고 애니메이션**, 강조 VFX(무기 트레일), 색/사운드 큐(Sekiro 危) | leveldesignbook, gamedeveloper |
| 입력 반응성 | 캐릭터 행동 입력 랙 **< 100ms** | gdkeys |
| 공정성 임계 | 어떤 행동이든 **20프레임(약 0.33s) 초과 반응요구**는 불공정 위험 | retrogamedeconstructionzone |

**핵심 판단 기준**: 일반 몹의 "공격 애니 시작 → 타격" 사이 시간이 **최소 0.25s 이상**이어야 회피 가능. 0.2s 이하는 반응 불가 구간.

---

## 1. 현 텔레그래프 아키텍처 (코드 기준)

### 1.1 일반 몹 공격 흐름
```
ChaseState → AttackReadyState (attackDelay 대기, attackReady 애니) → AttackState (attack 애니 + damageApplyDelay 후 타격)
```
- `attackDelay` ([MonsterData.cs](../Assets/RelicFairy/Characters/Monster/Monster/Core/SO/MonsterData.cs)) — AttackReady 포즈 유지 시간(1차 예고)
- `damageApplyDelay` — Attack 애니 시작 → 실제 데미지 판정까지의 타이머(2차 예고 = 실질 반응창)
- 데미지는 [MonsterBase.DealDamageToPlayer()](../Assets/RelicFairy/Characters/Monster/Monster/Core/MonsterBase.cs#L447)에서 **타격 순간 거리 재검사**로 게이트 → 회피 시 무피해 (공정성 OK)

### 1.2 텔레그래프 수단 (현재)
| 수단 | 일반 몹 적용 여부 |
|---|---|
| 공격 애니메이션 | ✅ 유일한 상시 예고 수단 |
| `attackDelay` 정지 포즈 | ✅ (단, **첫 공격은 스킵** — 1.3 참조) |
| 바닥 가이드라인(`MonsterGroundWarning`) | ❌ **일반 몹 미사용** — 보스/특수패턴(`PatternAttackOverrideSO`)·DragonBoss 전용 ([grep 확인](../Assets/RelicFairy/Characters/Monster/Monster/Core/MonsterGroundWarning.cs)) |
| 콘 공격 경고 VFX | ⚠️ **데미지와 동시 발생** ([MonsterConeAttackSO.cs:31-35](../Assets/RelicFairy/Characters/Monster/Monster/Core/SO/MonsterConeAttackSO.cs#L31)) — 사전 예고 아님 |
| 원거리 발사체 | ✅ 비행시간이 곧 반응창 ([MonsterRangedAttackSO.cs](../Assets/RelicFairy/Characters/Monster/Monster/Core/SO/MonsterRangedAttackSO.cs)) |

---

## 2. 시스템 레벨 문제 (다수 몹에 동시 영향 — 우선순위 높음)

### 🔴 P1. 첫 공격 예고 스킵 (전 몹 공통)
[AttackReadyState.cs:19](../Assets/RelicFairy/Characters/Monster/Monster/States/AttackReadyState.cs#L19)
```csharp
ctx.Runtime.StateTimer = ctx.Runtime.IsFirstAttack ? 0f : ctx.Stat.attackDelay;
```
- 첫 조우 시 `attackDelay`(1차 예고)를 **0으로 스킵** → 첫 타격 예고창 = `damageApplyDelay`뿐.
- 빠른 저딜레이 몹(BattleBee/RatAssassin, damageApplyDelay 0.2s)은 **첫 타가 사실상 반응 불가**.
- 개편: 첫 공격에도 최소 예고(예: `attackDelay * 0.5` 또는 0.3s 하한) 보장.

### ✅ P2. 근접 판정 바닥경고 — **구현 완료 (2026-06-05)**
- **A경로 17종**: 37개 패턴 전부 `disableWarning: 0` → 공격 시작 시 판정 경고, 데미지는 `damageDelay` 후(경고 선행).
- **B경로 기본 AttackState**: [AttackState.SpawnAttackWarning](../Assets/RelicFairy/Characters/Monster/Monster/States/AttackState.cs) 추가 → 원형 경고(반경=GetCombatHitDistance), `damageApplyDelay`만큼 선행. 투사체(MonsterRangedAttackSO)는 제외.
- 결과: 활성 일반 몹 전 근접이 "공격 전 암시 판정"을 표시. 원거리는 투사체 비행이 예고.
- 아래는 구현 전 원분석 기록:

### (원분석) P2. 바닥 가이드라인 부재 (일반 몹 전체)
- 일반 몹은 AoE/돌진/광역 타격에도 바닥 경고가 없음. 모던 액션게임(Hades/Sekiro/Diablo) 표준 대비 가독성 미달.
- 콘 공격은 경고 VFX가 **타격과 동시** ([MonsterConeAttackSO.cs:33](../Assets/RelicFairy/Characters/Monster/Monster/Core/SO/MonsterConeAttackSO.cs#L33)) → 예고 역할 못 함.
- 개편: 광역/콘 공격 몹은 `MonsterGroundWarning`을 **예고 단계(AttackReady)** 에 띄우고, 경고 지속 = 반응창 확보 후 타격.

### 🟡 P3. damageApplyDelay = 애니 비동기 블라인드 타이머
- [AttackState.cs:50-57](../Assets/RelicFairy/Characters/Monster/Monster/States/AttackState.cs#L50) — 데미지는 애니 히트프레임이 아니라 **고정 타이머**로 발사.
- AnimEvent 미사용 몹은 애니 시각 타격과 데미지 순간이 어긋날 수 있음(팬텀히트).
- 개편 방향: AnimEvent(`OnAttackHit`) 기반으로 통일하거나, 각 몹 `damageApplyDelay`를 애니 히트프레임에 맞춰 캘리브레이션.

---

## 3. 몬스터별 타이밍 분석 (27종)

판정: 🟢 양호 / 🟡 경계(0.3s, 보완 권장) / 🔴 위험(<0.25s 또는 모순)

| 몬스터 | 등급/유형 | range | radius | rate | attackDelay | **damageApplyDelay** | 판정 | 비고 |
|---|---|---|---|---|---|---|---|---|
| BattleBee | C 근접 | 1.5 | 1.5 | 1.2 | 0.2 | **0.2** | 🔴 | 반응창<250ms + 고속(5.5) |
| RatAssassin | C 근접 | 1.5 | 1.5 | 1.5 | 0.2 | **0.2** | 🔴 | 최고속(5.0)+연타(1.5)+0.2s = 회피 난해 |
| Spider | C 근접 | 2 | 1.3 | 1.2 | 0.3 | 0.3 | 🟡 | 고속(4.5), 경계 |
| FairyBat | C 근접 | 2 | 1 | 1 | 0.2 | 0.4 | 🟢 | attackDelay 0.2 짧으나 hit 0.4 |
| WormMonster | C 근접 | 2 | 2 | 0.35 | 0.8 | 0.4 | 🟢 | 느린 몹, 양호 |
| Skeleton | C 근접 | 1 | 1 | **2** | **1.0** | **1.0** | 🔴 | 예고 1s인데 rate 2/s — 모순. hit 1.0s 비정상 장기 |
| Orc | R 근접 | 1 | 1 | 0.8 | 0.5 | 0.5 | 🟢 | 표준 |
| Golem | R 근접 | 1 | 1 | 1 | 0.5 | 0.4 | 🟢 | 표준 |
| Fishman | R 원거리 | 6 | 0.5 | 0.8 | 0.5 | 0.5 | 🟢 | attackShape(원거리) |
| MonsterPlant | R 원거리 | 1.8 | 1.8 | 0.8 | 0.4 | 0.4 | 🟢 | range 1.8은 원거리 치고 짧음(확인) |
| StingRay | R 원거리 | 1.5 | 1.5 | 1 | 0.3 | 0.3 | 🟡 | Discharge 특수상태 별도, range 1.5는 근접급(확인) |
| Cyclops | E 근접 | **10** | 0.5 | 0.4 | 0.7 | 0.4 | 🟡 | attackShape 사용. range 10 = 돌진/빔? 형태 확인 필요 |
| LizardWarrior | E 근접 | 2 | 2 | 0.7 | 0.4 | 0.4 | 🟢 | 표준 |
| BlackKnight | E 근접 | 1 | 1 | 0.7 | 0.45 | 0.4 | 🟢 | 표준 |
| CrabMonster | E 근접 | 1 | 1 | 0.6 | 0.5 | 0.5 | 🟢 | 표준 |
| FlyingDemon | E 근접 | 1.8 | 1.8 | 1 | 0.3 | 0.3 | 🟡 | 고속(4.5), 경계 |
| Werewolf | E 근접 | 1.8 | 1.8 | 0.9 | 0.4 | 0.4 | 🟢 | 표준 |
| Specter | E 근접 | 1.8 | 1.8 | 0.8 | 0.3 | 0.3 | 🟡 | 고속(4.0), 경계 |
| MushroomAngry | C 근접 | 2 | 2 | 0.8 | 0.4 | 0.4 | 🟢 | 표준 |
| MushroomSmile | C 근접 | 2 | 2 | 0.8 | 0.4 | 0.4 | 🟢 | 표준 |
| Salamander | E 원거리? | 2 | 2 | 0.7 | 0.4 | 0.4 | 🟡 | CSV는 Ranged인데 range 2 + shape 없음 → 근접 판정 중. 불일치 |
| Beholder | E 원거리 | 3.2 | 1.6 | 0.65 | 0.25 | 0.4 | 🔴 | **원거리인데 발사체/shape 없음** → 3.2m 히트스캔식 구체. 텔레그래프 부재 |
| BishopKnight | E 원거리 | 2 | 1 | 0.7 | 0.5 | 0.5 | 🟡 | CSV Ranged인데 range 2 + shape 없음 → 근접화. 불일치 |
| EvilMage | E 원거리 | 8 | 0.5 | 0.5 | 0.6 | 0.6 | 🟢 | attackShape(원거리), 양호 |
| NagaWizard | E 원거리 | 8 | 0.5 | 0.5 | 0.6 | 0.6 | 🟢 | attackShape(원거리), 양호 |
| ChestMonster | E 근접 | 2 | 2 | 0.6 | 0.5 | 0.5 | 🟢 | 표준 (미믹) |

### 3.1 개별 주요 플래그
- **🔴 BattleBee / RatAssassin**: `damageApplyDelay` 0.2s → 0.3~0.35s로 상향, 또는 명확한 돌진 예고 모션 추가. 특히 RatAssassin은 고속+연타라 우선 조정.
- **🔴 Skeleton**: `attackDelay 1.0 / damageApplyDelay 1.0 / attackRate 2`의 조합이 모순. 의도(중공격? 설정 오류?) 확인 후 정상화 필요. 1.0s hit delay는 27종 중 유일한 이상치.
- **🔴 Beholder**: 등급/원형상 원거리(부유 안구)인데 발사체 SO 미할당 → 3.2m 거리에서 즉시 구체 데미지. 발사체(`MonsterRangedAttackSO`) 부여로 비행시간 예고 확보 권장.
- **🟡 데이터 불일치(CSV `attack_type` Ranged ↔ 실제 근접화)**: Salamander, BishopKnight (range 짧고 shape 없음). 원거리 의도면 발사체 부여, 근접 의도면 CSV 수정.
- **🟡 range 의미 확인**: Cyclops range 10(돌진/빔?), StingRay·MonsterPlant range가 유형과 안 맞음 → attackShape/특수상태 동작 재확인.
- **🟡 0.3s 그룹(Spider/FlyingDemon/Specter/StingRay)**: 모두 고속 이동 몹. 반응창 0.3s는 공정성 하한선(20프레임)이라 단독으론 OK지만, 고속 접근과 겹치면 체감 난이도 급증 → 접근속도 또는 예고 모션 보강 검토.

---

## 4. 개편안 (우선순위順)

### 4.1 즉시 효과 큰 시스템 개선
1. **첫 공격 예고 보장** — [AttackReadyState.cs:19](../Assets/RelicFairy/Characters/Monster/Monster/States/AttackReadyState.cs#L19) 첫 공격 `StateTimer`에 하한(예: `Mathf.Max(0.3f, attackDelay*0.5f)`).
2. **저딜레이 몹 반응창 정상화** — BattleBee/RatAssassin `damageApplyDelay` 0.2→0.3+, Skeleton 이상치 정상화.
3. **Beholder/원거리 불일치 몹** — 발사체 SO 부여 or CSV `attack_type` 정합.

### 4.2 텔레그래프 가독성 강화 (P2 대응)
4. **광역/콘 몹 바닥 가이드라인 도입** — `MonsterGroundWarning`을 AttackReady 단계에 노출(예고→타격 순서). 콘 공격은 경고를 데미지 이전으로 분리.
5. **무기 트레일 VFX** — 근접 몹 공격 클립에 트레일 추가(문헌 권장). *시각 작업 → 프리팹/애니 단계, CLI 외.*
6. **강공격 차별화** — Elite 강타류(Cyclops/CrabMonster 등)는 예고 포즈/경고를 더 크고 길게.

### 4.3 일관성/정합
7. **damageApplyDelay → AnimEvent 통일** 또는 몹별 히트프레임 캘리브레이션(P3).
8. **CSV attack_type ↔ 실제 공격형태 정합** 점검(Salamander/BishopKnight/StingRay/MonsterPlant).

---

## 5. 구현 시 영향 범위 (참고)
- 1~3번: 코드 1곳(`AttackReadyState`) + Config 에셋 수치 → 저위험.
- 4번: `MonsterGroundWarning` 호출 지점 신설(AttackReady/Pattern), 몹별 형태 데이터 추가 → 중간.
- 5·6번: 프리팹/애니/VFX 작업 → CLI 단독 불가, 비주얼 세션 필요.
- 7번: 전 몹 데미지 타이밍 회귀 테스트 필요 → 중간 이상.

---

## 6. 출처 (외부 문헌)
- [Keys to Combat Design: Anatomy of an Attack — GDKeys](https://gdkeys.com/keys-to-combat-design-1-anatomy-of-an-attack/)
- [Reaction Time and Game Design — Retro Game Deconstruction Zone](https://www.retrogamedeconstructionzone.com/2020/05/reaction-time-and-game-design.html)
- [Enemy Attacks and Telegraphing — Game Developer](https://www.gamedeveloper.com/design/enemy-attacks-and-telegraphing)
- [Enemy design — The Level Design Book](https://book.leveldesignbook.com/process/combat/enemy)
- [Elements of a Good Boss Fight, Part 1: Anticipation — Escape Industries](https://www.escapeindustries.net/elements-good-boss-fight-1-anticipation/)
- [Anticipation, Action, Recovery — Rivals Workshop Library](https://www.rivalslib.com/workshop_guide/art/anticipation_action_recovery.html)
