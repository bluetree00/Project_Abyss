# 아이템 T3/T4 효과 구현 설계 (55종) — 나중에 착수용

> 전제: T1/T2 100종 + 동적 스탯 기반·조건부·실드·다음공격 버퍼는 구현 완료(커밋 `ec2cd0ad7`).
> 이 문서는 **남은 T3/T4 효과 클래스** 구현 명세. 데이터(CSV)는 효과 클래스와 **한 묶음으로만** 추가(미등록 effect_type은 GenericStatEffect가 value를 AllDamageFlat에 가산해 스탯 오염).

## 0. 공용 베이스 (시작점 생성됨)
`Assets/RelicFairy/Systems/Item/Effects/Tier3/ItemCombatEffectBase.cs` — `IsActive=true` 고정(이벤트 상시 수신) + `EffAtk(ctx)`(유효 공격력) + `Self(ctx)`. T3/T4 전투 효과는 이걸 상속.

## 1. 재사용 인프라 (전부 구현됨)
| 용도 | API |
|---|---|
| 광역/체인 질의 | `CombatQuery.GetNearbyEnemies(center,radius,exclude,max,buf)` / `GetEnemiesInCone(...)` |
| 방어우회 즉발 피해 | `CombatQuery.DealSynergyDamage(target, amount, instigator, defenseIgnore=1, isCrit=false)` (OnPostDealDamage 미재귀) |
| 받는피해 증폭 | `MonsterBase.ApplyDamageTakenAmp(amp, duration, statusId)` |
| 상태이상(CC/Slow/DoT/마크) | `MonsterBase.Status` = `MonsterStatusReceiver` (`ApplyCc/ApplySlow/ApplyDot/HasDot/HasCc`) |
| 스턴 | `MonsterBase.ApplyStun(duration)` |
| 다음공격 강화(1타) | `EffectManager.QueueNextAttackBonus(bonus)` (0.4=+40%, 1.5=+150%) |
| 동적 스탯(틱 합산) | override `ContributeDynamicStats(ctx, ref ItemDynamicStats)` |
| 회복 | `ctx.Player.Heal(int)` |
| 처치 훅 | override `OnKill(ctx, target)` (배선됨) |
| 유효 공격력 | `EffAtk(ctx)` |

## 2. 트리거/등록 규약
- 효과 클래스 → `ItemEffectRegistry.RegisterAll`에 `Register("Key", s => new XxxEffect(s));`
- CSV: `effect_type`=Key, `trigger`=`OnHit`(IsActive=true라 OnPostDealDamage 발화) 또는 해당 이벤트.
- shape_id: T3 1×3=4, L형=8, T형=13 / T4 I형=11, O형=10, T형=13, J형=18. grade: T3=Epic, T4=Legendary.
- 파라미터: value/value2/value3/max_stack/duration에 매핑(아래 각 항목).

---

## 3. 잔첨형 (T3 10종) — effect 훅 위주, 자기완결

| # | 아이템(블록) | Key | 훅 | 구현 | params |
|---|---|---|---|---|---|
|1|누적의 인장(1×3)|`StackDamagePerTarget`|OnPostDealDamage|같은 대상이면 stack++(cap max_stack), 다른 대상이면 리셋. 보너스=`DamageDealt×stack×value` DealSynergyDamage|value=0.02, max_stack=10|
|2|연쇄의 잔영(L)|`EchoStrike`|OnPostDealDamage+OnTick|value 확률로 (target,damage,delay) 지연큐 적재 → OnTick에서 만료 시 DealSynergyDamage. **DarkAfterimage 패턴 복제**|value=0.30(확률), value2=1.0(피해비), duration=0.3(지연)|
|3|집요한 흔적(T)|`TargetVulnStack`|OnPostDealDamage|같은 대상 연속마다 vulnStack++, `ApplyDamageTakenAmp(stack×value, duration,"vulnerable")`. cap +30%|value=0.03, max_stack=10, duration=3|
|4|쌍타의 검(1×3)|`SplitStrike`|OnPostDealDamage|hit 카운터, value2회마다 추가타. "2회 분할 각70%"≈순증 `DamageDealt×(2×value-1)` DealSynergyDamage(또는 2회 70% 직접)|value=0.70, value2=5|
|5|메아리 화살(L)|`EchoArrow`|OnPostDealDamage|원거리(`ctx.WeaponType` 원거리)만, value 확률로 인접/동일대상에 `DamageDealt×value2`. ⚠️실제 투사체 스폰은 후속(근사: 즉발)|value=0.25, value2=0.50|
|6|독니의 자국(T)|`MarkExplode`|OnPostDealDamage|per-target 마크 Dict. max_stack 도달 시 `mark×EffAtk×value` 광역(GetNearbyEnemies) 후 리셋|value=0.10, max_stack=5|
|7|흡수의 자국(1×3)|`LifestealStack`|OnPostDealDamage|`Heal(DamageDealt×(value+min(bonusP,value3)))`, 적중마다 bonusP+=value2, 갭2s 리셋|value=0.05, value2=0.01, value3=0.05|
|8|끝나지 않는 일격(L)|`RepeatChance`|OnPostDealDamage|while(rand<chance){ DealSynergyDamage(DamageDealt); 재롤 } 반복 상한(안전)|value=0.05|
|9|잔재의 칼날(T)|`CritMomentum`|OnPostDealDamage+ContributeDynamicStats|crit이면 bonus+=value(cap value2), 비crit이면 -value. `dyn.critChance+=bonus`|value=0.10, value2=0.30|
|10|낙인의 사슬(1×3)|`BrandChain`|OnPostDealDamage+OnKill|per-target 낙인 Dict(cap max_stack, 무소멸). OnKill 시 낙인==max면 주변에 절반 전파|max_stack=8|

> per-target Dict는 풀 재사용 시 stale 가능 → 갭 타이머로 정리하거나 OnKill에서 remove(낙인은 처리). 미세하므로 후속 정리.

## 4. 광폭형 (T3 10종) — 다음공격 버퍼 + 타이머/누적

| # | 아이템 | Key | 훅 | 구현 | params |
|---|---|---|---|---|---|
|11|폭주의 코어(1×3)|`TimedEmpowerNext`|OnTick|타이머≥value 시 `QueueNextAttackBonus(value2-1)` 후 리셋|value=30(초), value2=2.5|
|12|심판의 파편(L)|`HpThresholdAoE`|OnPostTakeDamage|HP비율이 value 밑으로 내려가는 순간 1회 주변 `EffAtk×value2` 광역. 방 진입 시 재무장|value=0.25, value2=2.0|
|13|각성의 인장(T)|`SkillReadyEmpower`|OnTick|모든 스킬 IsReady로 전환되는 순간 다음 일반공격 `QueueNextAttackBonus(value-1)` 1회|value=1.8|
|14|폭발하는 분노(1×3)|`DamageAccumEmpower`|OnPostTakeDamage|누적피해≥MaxHp×value 시 `QueueNextAttackBonus(value2-1)`+누적 리셋|value=0.5, value2=2.2|
|15|최후의 숨결(L)|`LastBreath`|OnPostTakeDamage+ContributeDynamicStats|HP≤value 시 duration초 윈도 무장(방 재충전). 윈도 중 `dyn.allDamage+=value2`(받피 증가는 별도/생략)|value=0.10, value2=0.60, duration=5|
|16|균열의 일격(T)|`GuaranteedCrit`|OnPostDealDamage|비치명 카운터, value회 연속 비치명 시 다음타 강제 치명 `QueueNextAttackBonus(value2-1)`(치명 근사)|value=3, value2=1.9|
|17|침묵의 폭발(1×3)|`SkillIdleEmpower`|OnSkillUse+OnTick|마지막 스킬 후 value초 경과 시 다음 스킬 강화 플래그 → 스킬 데미지에 반영(스킬피해 동적 or 버퍼). ⚠️스킬 단발 강화 경로 필요|value=15, value2=3.0|
|18|무게의 추(L)|`ChargeWhileIdle`|OnTick|정지 value초 충전 시 `QueueNextAttackBonus(value2-1)` 1회(이동 리셋). FirstHitBonus 변형|value=3, value2=2.8|
|19|광기의 파동(T)|`DamageAccumPenetrate`|OnPostTakeDamage|누적피해≥MaxHp×value 시 다음타 방어무시 플래그 → DealSynergyDamage(defenseIgnore=1) 보강 or 별도. ⚠️일반공격 방무 경로 필요|value=0.30|
|20|최종 결의(1×3)|`CounterShockwave`|OnPostTakeDamage|단일 피해≥MaxHp×value 시 주변 `EffAtk×value2` 광역(쿨 60s)|value=0.40, value2=2.0, duration=60(쿨)|

> "다음 일반공격 강화"는 `QueueNextAttackBonus`로 충분. "다음 **스킬** 강화"(17)는 스킬 단발 강화 경로가 없어 후속(스킬 데미지에 일시 배율 필요).

## 5. 타이밍형 (T3 10종) — 일부 하드(적 windup 필요)

| # | 아이템 | Key | 구현 | 난이도 |
|---|---|---|---|---|
|21|완벽한 순간(1×3)|`JustGuard`|피격직전 0.2초 내 공격 +50% → **적 공격 windup(예고) 타이밍 노출 필요**|★하드(후속)|
|22|마지막 기회(L)|`SkillCdReset`|OnPostDealDamage: 임의 스킬 잔여쿨≤value면 `CooldownTracker.ResetCooldown` (1회/10초)|중|
|23|교차하는 칼날(T)|`DoubleHitTiming`|0.1초내 2적중 감지 → +value. **다중 콜라이더 동시적중 타이밍**|★하드|
|24|전환의 틈(1×3)|`RoomEntryWindow`|OnRoomEnter 후 value2초 윈도, OnPreDealDamage에 +value. (또는 FirstHitBonus 변형)|쉬움|
|25|숨 고르기(L)|`NoHitThenCrit`|무피격 value초 후 다음 공격 확정크릿 `QueueNextAttackBonus`|중|
|26|섬광의 순간(T)|`AttackInterrupt`|적 공격시작 0.3초내 적중 시 적 공격 취소 → **적 windup + 캔슬 API 필요**|★하드(후속)|
|27|끝맺음의 검(1×3)|`ExecuteBonus`|OnPreDealDamage: 대상 HP≤예상피해면 +value(확정처치). 대상 HP 노출 필요(IDamageable)|중|
|28|여명의 일격(L)|`CombatStartWindow`|OnRoomEnter 후 value2초간 `dyn.critChance+=value`|쉬움|
|29|기다림의 미학(T)|`StationaryRangeBuff`|정지 5초 같은자리 → 범위+30%. **공격 판정 사거리 수정 필요**|★하드|
|30|순간의 균열(1×3)|`SkillCastGuard`|스킬 시전중 피격 시 시전 유지 + 스킬피해+25%. **스킬 시전 상태/캔슬 가드 API 필요**|★하드|

## 6. 형태변형 (T3 6종) — 무기/투사체 훅

| # | 아이템 | 처리 |
|---|---|---|
|31|분산의 화살통(1×3)|`ProjectileCount`(기존) value=1 → **데이터만**|
|33|관통하는 창(T)|`ProjectilePierce`(기존) value=1 → **데이터만**(피해70% 감쇠는 관통 로직 확인)|
|32|이중 타격의 장갑(L)|`MeleeMultiHit` — 근접 타격판정 1회 추가(80%). **무기 히트박스 재트리거 훅 필요**|★|
|34|확장된 칼끝(1×3)|`MeleeRangeExtend` — 근접 사거리+20%. **공격 콜라이더 스케일 훅 필요**|★|
|35|두 줄기 빛(L)|`SkillProjectileCount` — 스킬 투사체+1. **스킬 투사체 스폰 훅 필요**|★|
|36|원형 충격파(T)|`MeleeShapeCircle` — 부채꼴→원형(80%). **공격 판정 형태 변경 훅 필요**|★|

## 7. T4 (19종) — 대부분 T3 진화형(클래스 재사용 + 파라미터)

| 계열 | T4 아이템 | 재사용 클래스 | 비고 |
|---|---|---|---|
|잔첨|무한의 사슬(I)|`StackDamagePerTarget`|max_stack=25, 무제한 유지(대상바뀌어도 리셋X 옵션)|
|잔첨|끝없는 메아리(O)|`EchoStrike`|연쇄 재발동(잔영도 재롤)|
|잔첨|심연의 표식(T)|`TargetVulnStack`+`MarkExplode`|max 12, 12중첩 폭발+절반 전파|
|잔첨|피의 순환(J)|`LifestealStack`|회복4%/+1%p/cap10%p|
|잔첨|천 번의 칼날(L)|`RepeatChance`|8% 재발동, 재롤마다 -1%p|
|광폭|종말의 코어(I)|`TimedEmpowerNext`|20s ×4.0|
|광폭|최후의 숨결—진화(O)|`LastBreath`|HP15% 8s +80%/받피+40%|
|광폭|파국의 인장(T)|`DamageAccumEmpower`|40% ×2.8, 누적 절반만 차감|
|광폭|억눌린 격노(J)|`SkillIdleEmpower`|10s ×3.5, 사용 시 5s로 재시작|
|광폭|심판의 시간(L)|`ChargeWhileIdle`|3s ×3.5 +범위50%|
|타이밍|완전한 순간—진화(I)|`JustGuard`|+80% +적 다음공격 1회 무효 ★|
|타이밍|이중 교차(O)|`DoubleHitTiming`|×100% +약점표식 ★|
|타이밍|영원한 여명(T)|`CombatStartWindow`|첫8초 확정크릿→이후 치확+20%|
|타이밍|확정의 끝맺음(J)|`ExecuteBonus`|+50% +처치 시 0.5초 모든피해+30%|
|타이밍|균열의 시간(L)|`SkillCastGuard`|시전유지+스킬피해40%+받피-25% ★|
|형태|산탄의 비(I)|`ProjectileCount` value=2|데이터(부채꼴30도)|
|형태|전방위 칼날(O)|`MeleeShapeCircle` 360°|★|
|형태|관통하는 빛살(T)|`ProjectilePierce` value=2|데이터(스킬 투사체도)|
|형태|메아리 치는 일격(J)|`MeleeMultiHit` 2회 추가|★|

> ⚠️ 표지=24종이나 상세표=19종(5/5/5/4). **누락 5종 확인 필요.**

## 8. 후속 인프라 (★ 항목 선결)
이 인프라가 없으면 ★ 항목 구현 불가:
1. **공격 판정 형태/사거리 수정 훅** — 근접 콜라이더 스케일·형태(부채꼴↔원형↔360°), 다단 히트 재트리거. (이중타격·확장된칼끝·원형충격파·전방위칼날·메아리치는일격)
2. **스킬 투사체 스폰 훅** — 스킬 발사체 개수/형태. (두줄기빛, 산탄의비 스킬분)
3. **적 공격 windup(예고) 타이밍 + 캔슬 API** — 저스트가드/적공격취소. (완벽한순간·섬광의순간·완전한순간)
4. **스킬 시전 상태/캔슬 가드** — 시전중 피격 유지. (순간의균열·균열의시간)
5. **스킬 단발 강화 경로** — 다음 스킬에 일시 배율. (침묵의폭발·억눌린격노)
6. **다중 콜라이더 동시적중 타이밍** — 0.1초내 2적중. (교차하는칼날·이중교차)

→ ★ 외 항목(잔첨 10 + 광폭 대부분 + 타이밍 쉬움/중 + 형태 데이터 2종)은 **현 인프라로 즉시 구현 가능**. 권장 순서: 잔첨형 → 광폭형 → 타이밍(쉬움/중) → ★인프라 → 나머지.

## 9. CSV 작성 시
- 데스크톱 `사용중 테이블/ITEM_DATA.csv`에 T3/T4 행 추가(현재 T1/T2 100종). **반드시 UTF-8 BOM 유지**(Excel 한글). PowerShell `[System.IO.File]::WriteAllText(path, content, (New-Object System.Text.UTF8Encoding($true)))`.
- 효과 클래스 등록 **후에만** 해당 행 추가(미등록 시 GenericStatEffect 오염).
- 컬럼: `passive_id,item_name,slot,effect_type,trigger,value,value2,value3,max_stack,duration,description,stat_version,shape_id,grade`.
- 게임은 뒤끝 CDN 로드 → 작성 후 **사용자가 뒤끝 업로드**.
