# 영웅 유물(캐릭터) v1 — 가웨인·랜슬롯 설계서

> 작성일: 2026-06-12 / 근거: "가웨인 유물.pdf", "렌슬롯 유물.pdf"
> 상태: **설계 단계 — 코드 구현 전.** 본 문서 합의 후 착수.
> 전제(메모리 [[project_relic_class_system]]): 몸=CombatGirl 고정, 유물이 **패시브+고유스킬+외형(+스탯 가산)** 부여. 무기는 별도.

---

## 0. TL;DR
- **레거시 보존**: 기존 `GawainRelic`(SolarTimer)/`GalahadRelic`/`Characters/Player/Gawain·Galahad`는 **레거시로 두고 건드리지 않는다.** 차세대 유물은 새 프레임워크 위에 **신규**로 만든다(in-place 재작성 ✗).
- 기존 유물 골격의 **재사용 가능 조각**(`IRelicBehavior`·패시브 등록·`ISkillRuntime`·RuntimeStats 유물 레이어)은 살리되, **확장성 중심으로 표준화**한다.
- **확장 골자 = "리소스 + 효과 프리미티브 + 모듈 레시피"**:
  - 모든 차세대 유물은 **고유 리소스 1종**(시간형 게이지/적중형 스택 등)을 갖고, 공통 `IRelicResource` 계약으로 HUD·세이브·게이팅·변형아이템과 연결된다.
  - 패시브/스킬/디버프 효과는 **재사용 가능한 효과 프리미티브**(조건부 스탯버프·1회 출력펄스·적 낙인/DoT)로 조합한다.
  - 유물 추가 = SO + RelicBehavior + Resource 구현 + 레지스트리 1줄 (보일러플레이트 최소).
- 1차 대상: **가웨인(Zenith 게이지)·랜슬롯(Madness 스택)**. 변형 아이템 6종은 아이템 시스템 연동 → **후속**.

---

## 1. 대상 유물 스펙 (PDF 요약)

### 1-A. 가웨인 — 정오의 맹세 (버스트/타이밍)
**기본 스탯**: HP 950 · ATK 140 · DEF 32 · MoveSpd 108 · Crit 8% · CritDmg 100% (전부 4종 중 중간)

**고유 메커닉 — 정오 게이지(Zenith)**: 시간 자동 충전. 45초 1사이클 = **충전 20초 → 정오 10초(스킬 가능) → 쿨다운 15초**.
- 정오 구간 효과: 공속 +30%, 치명타 확률 +10%, 치명타 피해 +20%, 모든 피해 +20%. **스킬은 정오 구간에서만 발동.**

| 요소 | 내용 |
|---|---|
| 패시브1 태양의 각인 | 게이지 ≥80% → ATK+10%, MoveSpd+10% 선행. 정오 진입 시 각인 소모 → **첫 타격 +50%**. |
| 패시브2 태양의 잔열 | 정오 중 부여한 태양 화상이 쿨다운 15초 동안 지속(피해 50%로 감소). 황혼(쿨다운) 중 처치 시 다음 게이지 충전 +20% 가속. |
| 고유 스킬 태양 강림 | 정오 전용·1회/구간·수동. 전방 부채꼴 광역 **ATK×350%** + 태양 화상 6초(ATK×15%/s). 각인 첫타와 겹치면 525%. |
| 변형 아이템 ×3 | 영원한 정오(구간20/쿨30/스킬2회), 번개 같은 여명(충전10/구간5/쿨8), 태양의 서약(정오 미사용 축적→다음 정오 ATK+20%·스킬+50%, 최대3회). |

### 1-B. 랜슬롯 — 찢긴 서약의 검 (하이리스크 스택)
**기본 스탯**: HP 650(최저) · ATK 180(최고) · DEF 18(최저) · MoveSpd 105 · Crit 12% · CritDmg 100%

**고유 메커닉 — 광기(Madness)**: 적중마다 스택 +1(MAX 50). 스택↑ → ATK↑ + 받는 피해↑. 2초 미공격 시 초당 -1. **MAX 50 → 고유 스킬 자동 발동.**

| 스택 | ATK | 받는 피해 | 비고 |
|---|---|---|---|
| 0 | 기본 | 기본 | |
| 1–15 | +최대15% | -최대5% | 안전 |
| 16–30 | +최대25% | -최대15% | 균형 |
| 31–49 | +최대35% | -최대20% | 위험 |
| 50 | +40% | -30% | 스킬 발동 |

| 요소 | 내용 |
|---|---|
| 패시브1 기사의 긍지 | 기본 받는 피해 -10%, 스택 누적으로 희석(0–15:-10% / 16–30:-6% / 31–49:-2% / 50:0%). |
| 패시브2 배신의 대가 | 스킬 후 4초 빈틈(받는피해+50%, 이속-20%). 빈틈 중 처치 시 즉시 해제. 빈틈 종료 후 스택 10부터 재시작. |
| 고유 스킬 심판의 일격 | 스택50 자동(49에서 멈춰 유지 가능)·수동 불가. 전방 직선 관통 **ATK×(200%+스택×8%)**(50시 600%). 심판 낙인 4초(받는피해+20%), 낙인 만료 시 ATK×100% 추가 폭발. |
| 변형 아이템 ×3 | 절제의 족쇄(MAX30), 광기의 심연(스택25유지/빈틈7초), 기네비어의 눈물(빈틈제거/감소 2초→1초·-1→-2). |

---

## 2. 기존 유물 아키텍처 (재사용)

| 자산 | 역할 |
|---|---|
| `IRelicBehavior` | OnAttach(패시브 등록·컴포넌트 추가·구독)/OnDetach/`CreateSkillRuntime`/`GetSkillCooldown`/`ModifyIncomingDamage` |
| `RelicClassSO` | id·이름·로어·초상·**passives(PassiveSO[])**·qSkillClipKey·**stats(StatModifier[])**(공통 베이스 위 가산)·오라 |
| `RelicRegistry` | RelicId → IRelicBehavior 팩토리 |
| `RelicId` enum | 유물 식별자 (Lancelot 추가 필요) |
| 리소스 컴포넌트 선례 | `SolarTimer`(가웨인 기존) — Initialize(RuntimeStats), OnPhaseChanged, IsEmpowered |
| 패시브 시스템 | `owner.RegisterRelicPassive(PassiveBase)` + `PassiveTrigger`(OnAttackHit/OnSkillUse/OnTakeDamage…) |
| 스킬 런타임 | `ISkillRuntime`(Q 슬롯) — 가웨인 SolarStrikeSkillRuntime, 갈라하드 HolyShieldSkillRuntime |
| 스탯 레이어 | `PlayerRuntimeStats`에 **유물 전용 레이어**(_relicAttackSpeed 등) + AllDamagePercent/DamageReduction/MoveSpeedMultiplier/AttackSpeedMultiplier |
| 선택/적용 | RelicAltar(베이스캠프) → `Loadout.SetRelic` → PlayerController.ApplyRelic → OnAttach |
| 각성 | `RelicAwakeningDataManager`/Entry (서버 데이터 경로 존재) |

**결론: 프레임워크 교체 아님. 리소스 컴포넌트 2종 신규 + 스탯/스킬/디버그 확장.**

---

## 3. 갭 분석 (기존 → 필요)

| 필요 | 기존 | 신규 |
|---|---|---|
| 정오 게이지(시간 자동, 3구간) | SolarTimer(2구간) 유사 | `ZenithGauge` 컴포넌트(충전/정오/쿨다운 + 이벤트) |
| 광기 스택(적중 누적·감쇠·자동발동) | 선례 없음 | `MadnessStack` 컴포넌트(적중+1/2초후 감쇠/50자동) |
| 정오 구간 스탯 버프(공속/모든피해/이속) | RuntimeStats 유물 레이어 O | 적용 토글만 |
| **치명타 확률/피해 버프** | RuntimeStats에 **없음**(crit 필드 부재) | RuntimeStats에 crit 보너스 필드 신규 |
| 스택 스케일 ATK(+최대%) | RuntimeStats 유물 ATK 레이어 O | MadnessStack이 매 스택 갱신 |
| **받는 피해 증가(스택)/감소(긍지)** | DamageReduction O(감소) | 증가(음수 reduction) 적용 + 희석 곡선 |
| 스킬 게이팅(정오한정 / 자동발동) | GetSkillCooldown O, 가용성 게이팅 ✗ | 스킬 사용 가능 조건 훅 + 자동 발동 트리거 |
| 첫타·각인 1회 출력 +50% | 유물 출력 훅 ✗(Incoming만 있음) | 1회용 출력 버프(AllDamagePercent 펄스) 또는 신규 OutgoingHook |
| 적 디버그: 태양 화상 DoT | 존재(GawainSolarSwordPassive) | 재사용 |
| 적 디버그: 심판 낙인(받는피해+20%) | 없음 | 적 "받는 피해 증폭" 디버프 시스템 |
| 변형 아이템(6종) | 아이템 시스템 구축 중 | **후속(아이템/시너지 완성 후, 서약 스텁과 동일 정책)** |

---

## 4. 확장 프레임워크 설계 (핵심)

설계 목표: **유물 추가가 "데이터 + 얇은 로직"으로 끝나도록.** 변동성이 큰 부분(리소스 메커닉)만 유물별 구현, 나머지(HUD·게이팅·스탯·디버프·세이브)는 공통 계약으로 흡수.

### 4-0. 차세대 유물 모듈 = 4조각
```
RelicDefinitionSO (데이터)  ─┐
RelicBehavior   (IRelicBehavior, 조립 로직) ─┤→ PlayerController.ApplyRelic가 조립
IRelicResource  (고유 메커닉: 게이지/스택 …) ─┤
Passive×2 + SkillRuntime (효과 프리미티브 조합) ─┘
```
- **유물 추가 레시피**: ① `RelicDefinitionSO` 에셋, ② `XxxRelic : IRelicBehavior`(OnAttach에서 리소스 컴포넌트 추가 + 패시브 등록 + 스킬런타임 지정), ③ `XxxResource : IRelicResource` 구현, ④ `RelicId` + `RelicRegistry` 1줄. HUD·게이팅·세이브·변형아이템은 **공통 계약이 자동 처리** → 보일러플레이트 최소.

### 4-1. 공통 리소스 계약 — `IRelicResource` (확장성 핵심)
모든 유물 메커닉(시간형 게이지·적중형 스택·미래의 다른 형태)이 구현하는 단일 계약. HUD/게이팅/세이브/변형아이템이 **유물 종류를 몰라도** 연동.
```
interface IRelicResource {
    float  Fill { get; }            // 0~1 정규화 (HUD 바 공통)
    string Label { get; }           // HUD 표시("정오까지", "광기 32")
    int    Phase { get; }           // 구간/상태 인덱스(HUD 색·라벨)
    bool   IsSkillReady { get; }     // 스킬 게이팅(정오 중 / 스택 MAX 등)
    event Action OnChanged;         // HUD 갱신
    // 라이프사이클 — PlayerController가 라우팅
    void Tick(float dt);
    void OnAttackLanded(GameObject target);
    void OnKill(GameObject target);
    // 세이브/변형아이템
    RelicResourceState Capture();   void Restore(RelicResourceState s);
    void ApplyConfig(RelicResourceConfig cfg); // 변형 아이템이 수치 덮어씀
}
```
- 구현체: `ZenithGauge`(시간형: Charging/Noon/Cooldown 3구간, Fill=게이지%, IsSkillReady=정오), `MadnessStack`(적중형: Fill=스택/Max, Phase=구간, IsSkillReady=MAX, OnMaxReached로 자동발동).
- **HUD 단일 프리젠터**: `RelicResourceHud`가 `IRelicResource`만 바인딩 → 게이지/스택 모두 한 위젯으로 표시. (유물별 HUD 코드 불필요)
- 리소스가 변화 시 RuntimeStats 유물 레이어 갱신(SolarTimer.RefreshDefenseBonus 패턴 일반화).

### 4-1b. 효과 프리미티브 (유물 간 재사용 빌딩블록)
유물 패시브/스킬이 **조합**해서 쓰는 공통 효과 — 새 유물도 이걸 재사용.
| 프리미티브 | 용도 예 |
|---|---|
| 조건부 스탯버프(window/tier) | 가웨인 정오 구간 버프, 랜슬롯 스택 곡선 |
| 1회 출력 펄스(다음 1타 +x%) | 가웨인 각인 첫타 +50% |
| 적 낙인/DoT(`EnemyDebuff`) | 태양 화상(DoT), 심판 낙인(받는피해 증폭+만료 폭발) |
| 자동 스킬 트리거 | 랜슬롯 MAX 자동발동 |
| 처치 보상 훅 | 가웨인 황혼 처치→충전 가속, 랜슬롯 빈틈 처치→해제 |

### 4-2. 스탯 버프 — RuntimeStats 유물 레이어 확장
- 기존 레이어(AttackSpeed/MoveSpeed/AllDamage/DamageReduction) 재사용.
- **신규 필드**: `CritChanceBonus`, `CritDamageBonus`(유물 레이어). 가웨인 정오(+10%/+20%), 랜슬롯(필요 시).
- 받는 피해 **증가**는 DamageReduction을 음수로 가산(또는 별도 `DamageTakenAmp`). 랜슬롯 스택/빈틈에서 사용.

### 4-3. 스킬 게이팅·자동 발동
- **가웨인(정오 한정)**: 스킬 입력 처리 전 `relic.CanUseSkill(slot)` 질의 → 정오 아니면 무시(쿨/대기). ActSkillStateBase/PlayerController 스킬 진입에 훅 1개 추가.
- **랜슬롯(자동·수동 불가)**: `MadnessStack.OnMaxReached` → 유물이 스킬 런타임을 직접 발동. 수동 입력은 차단. 49 유지는 자동 발동 임계를 50으로 두고 감쇠로 자연 유지.

### 4-4. 첫타·각인 1회 출력 버프
- 가웨인 정오 진입 시 "각인 소모 → 첫 타격 +50%": **1회용 출력 펄스**. OnAttackHit 패시브가 다음 1타에 한해 AllDamagePercent를 일시 가산하거나, 출력 계산에 1회 플래그. (서약 ModifyOutgoing 배선과 유사하게 출력 훅 재사용 검토.)

### 4-5. 적 디버프 시스템
- **태양 화상 DoT**: 기존 GawainSolarSwordPassive/화상 재사용(태양 강림이 6초 부여).
- **심판 낙인(받는 피해 +20%)**: 적에 "받는 피해 증폭" 디버프 부착 + 만료 시 추가 폭발. 몬스터 측 피해 수신부에 증폭 계수 필요 → 신규 소형 시스템(낙인 컴포넌트/태그). [[project_hit_feedback_system]] 피해 경로 참고.

### 4-6. 변형 아이템 (6종) — 후속
- 메커닉 수치(사이클 길이·MAX 스택·빈틈 등)를 변형 → 리소스 컴포넌트의 데이터 값을 아이템이 덮어쓰는 구조. **아이템/시너지 시스템 완성 후** 구현(서약 스텁과 동일 정책). 1차는 컴포넌트가 기본값으로 동작.

---

## 5. 데이터 아키텍처 (서버 권위 — 확정)

서약과 동일 철학: **CSV → 서버(CDN) 권위 → 로컬캐시/Addressables/SO 폴백.** 출시 전 stat_version=1, 캐시 수동 정리 [[project_stat_version_policy]]. 매니저는 `CovenantDataManager` 패턴 복제.

### 5-1. 유물별 베이스 스탯 — **기존 `CHARACTER_DATA` 재사용** (확정·구현됨)
> 발견: `PlayerController.ApplyRelic`이 이미 **유물 char_id 행(CHARACTER_DATA)으로 베이스 스탯 전체 교체**("유물이 곧 캐릭터"). 별도 RELIC_BASE_STAT는 **중복 → 폐기**.
- 유물 베이스 스탯 = `CHARACTER_DATA.csv` 행(char_id = relic_id). `PlayerDataManager`(서버 권위) → `RuntimeStats.InitializeFromServer`.
- **크릿 추가(구현됨)**: `CHARACTER_DATA`에 `crit_chance`/`crit_damage` 컬럼 신설 → `PlayerStatEntry`/`PlayerDataManager.ParseRow`/`InitializeFromServer`가 RuntimeStats 유물 크릿 레이어로 적용.
- 적용 행: gawain(950/140/32, crit 8) Zenith로 갱신 · lancelot(650/180/18, crit 12) 추가.

### 5-2. 메커닉 수치 — `RELIC_STAT_DATA` (확정)
사이클/스택/곡선/스킬 배율 등 모든 튜닝 수치. **슬롯 기반**(서약 `CovenantStatEntry`와 동형) — 슬롯 인덱스는 각 RelicBehavior의 `V_XXX` 상수와 1:1.
```
csv: index, relic_id, slot, description, value, stat_version
예) gawain 슬롯: 0충전20 1정오10 2쿨다운15 3정오공속0.3 4정오크릿0.1 5정오크댐0.2
    6정오모든피해0.2 7각인임계0.8 8각인ATK0.1 9각인이속0.1 10첫타0.5 11스킬배율3.5
    12화상지속6 13화상틱0.15 14잔열감소0.5 15황혼가속0.2
예) lancelot 슬롯: 0MAX50 1감쇠유예2 2감쇠속도1 3스택ATK상한0.4 4스택받피상한0.3
    5긍지기본0.1 6빈틈시간4 7빈틈받피0.5 8빈틈이속0.2 9빈틈재시작10 10스킬기본2.0
    11스킬스택계수0.08 12낙인시간4 13낙인받피0.2 14낙인폭발1.0
```
- 신규 `RelicStatDataManager`(서버 권위) + RelicBehavior가 `V(slot)`/`VI(slot)`로만 접근(매직넘버 금지, 폴백만 코드 상수). 서약 `V()/VI()` 패턴 그대로.
- **변형 아이템(보류)**은 향후 이 슬롯 값을 런타임 오버라이드(`IRelicResource.ApplyConfig`) — 데이터 위치만 잡아둠.

### 5-3. 텍스트(이름/로어/패시브·스킬 설명)
- `RelicDefinitionSO`(선택 UI에 이미 연결) + UI. 서버화 불필요(텍스트는 SO 권위).

---

## 5b. 디자인 패턴 (확정)
| 패턴 | 적용 |
|---|---|
| **Strategy** | `IRelicBehavior` — 유물 로직을 교체 가능한 전략 객체로(상속 ✗ 합성 ✓) |
| **Component** | `IRelicResource` 구현(MonoBehaviour) — 고유 메커닉을 컴포넌트로 부착 |
| **Registry/Factory** | `RelicRegistry`(RelicId→Behavior), `RelicResource` 생성은 Behavior.OnAttach |
| **Data-driven (서버 권위)** | `RelicBaseStatManager`/`RelicStatDataManager` — 수치 외부화, 코드는 슬롯 접근만 |
| **효과 프리미티브(조합)** | 패시브/스킬이 공통 빌딩블록(스탯버프·출력펄스·EnemyDebuff)을 조합 |
| **Observer** | `IRelicResource.OnChanged` → HUD/스탯 갱신 (SolarTimer.OnPhaseChanged 일반화) |
- **단일 책임**: 데이터(SO/CSV) ↔ 메커닉(Resource) ↔ 효과(Primitive) ↔ 표시(HUD) ↔ 조립(Behavior) 분리.

---

## 6. 구현 페이즈
- **P0 기반**: RuntimeStats crit 필드 + 받는피해 증폭 + 스킬 게이팅 훅 + (필요 시)출력 1회 펄스.
- **P1 가웨인 Zenith**: ZenithGauge + 정오 버프 + 각인/잔열 패시브 + 태양 강림 스킬(부채꼴 광역+화상) + HUD 게이지. 기존 GawainRelic 재작성.
- **P2 랜슬롯 Madness**: MadnessStack + 스택 곡선(ATK/받는피해) + 긍지/배신 패시브 + 심판의 일격 자동 스킬 + 심판 낙인 디버프 + HUD 스택.
- **P3 변형 아이템 6종**: 아이템 시스템 연동(후속).
- 각 페이즈: 컴파일(refresh_unity→read_console) + 전투 시뮬 리뷰(서약과 동일 절차).

---

## 7. 레거시 처리 (in-place 재작성 ✗)
- 기존 `GawainRelic`/`SolarTimer`/`GalahadRelic`/`Characters/Player/Gawain·Galahad`는 **레거시로 보존** — 수정하지 않는다. 차세대 가웨인은 새 프레임워크 위 **신규** `GawainZenithRelic`(가칭)로 만든다.
- 레거시 정리(레지스트리에서 구 가웨인 제외/파일 삭제)는 신규 완성·검증 후 **사용자 확인 후** 진행(서약 구버전 8종 정리와 동일 정책). 그 전까지 병존 가능.
- `RelicId`에 신규 항목 추가(Lancelot, 신규 Gawain), `RelicRegistry`에 등록, 신규 `RelicDefinitionSO`/RelicAltar(베이스캠프) 추가.
- 메모리 [[project_gawain_concept]]는 "레거시"로 표기 + 신규 Zenith 설계 메모 별도 추가(합의 후).

---

## 8. 대안 검토 (구조 결정 포인트)

### 리소스: 공통 계약 vs 유물별 완전 전용
| | 공통 `IRelicResource` 계약 + 유물별 구현(채택) | 유물별 완전 전용(계약 없음) |
|---|---|---|
| 장점 | **HUD·게이팅·세이브·변형아이템 공통 처리** → 유물 추가 저비용(확장성↑) | 초기 코드 최소 |
| 단점 | 계약 1개 설계 비용 | 유물마다 HUD/세이브/게이팅 중복 → 확장 시 비용 폭증 |
**결정: 공통 `IRelicResource` 계약 채택**(§4-1). 사용자 요구(확장성)에 부합 — 시간형/적중형 차이는 구현체가 흡수하고, 바깥(HUD/게이팅/세이브)은 계약만 본다. (서약의 `CovenantBase` 공통 골격과 같은 철학.)

### 기본 스탯: StatModifier 가산 vs 유물별 베이스
- 현행 SO는 "공통 베이스 + 가산"이라 가산으로 표현 가능하나, 유물 간 스탯 격차가 커(HP 650~950) 가산값이 직관적이지 않음. → **유물별 베이스 스탯 테이블(CSV)** 도입이 더 명확(§5). 결정 필요.

---

## 9. 결정 현황
**확정**
- ✅ 메커닉 수치 = **서버(CDN) 권위** (`RELIC_STAT_DATA`, 매니저는 Covenant 패턴 복제).
- ✅ 기본 스탯 = **유물별 베이스 스탯**(`RELIC_BASE_STAT`, CombatGirl 공통 베이스 대체).
- ✅ 변형 아이템 6종 = **보류**(아이템/시너지 시스템 완성 후. 데이터 슬롯/`ApplyConfig` 자리만 확보).
- ✅ 기존 갈라하드/가웨인 = **레거시 보존**, 신규는 별도 신설.

**잔여(착수 전 확인)**
1. **적 디버프(심판 낙인=받는피해 증폭)** — 몬스터 피해 수신부에 증폭 계수 추가 필요. 별도 소형 시스템(`EnemyDebuff`)으로 신설 OK? (태양 화상 DoT는 기존 재사용)
2. **로스터/우선순위** — 가웨인 먼저 vs 랜슬롯 먼저, 그리고 갈라하드 신규화 여부.

### 부록 — 관련 파일
- 유물 코어: `Assets/RelicFairy/Systems/Relic/Scripts/`(IRelicBehavior/RelicClassSO/RelicRegistry/RelicId/RelicAltar)
- 기존 구현: `GawainRelic`/`GalahadRelic`, `Characters/Player/Galahad|Gawain/`
- 스탯: `Characters/Player/Scripts/PlayerRuntimeStats.cs`(유물 레이어)
- 적용: PlayerController.ApplyRelic, `Loadout.SetRelic`, CharacterDisplayStand/RelicAltar
