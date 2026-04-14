# 아이템 시스템 개발 가이드

## 아키텍처 요약

```
CSV 테이블 (ITEM_DATA)
  → ItemDataManager (CDN/Resources 로드)
    → RuntimeItemData (런타임 데이터)
      → RunItemInventory (보유 아이템)
        → ItemEffectManager (효과 관리 허브)
          → IItemEffect 인스턴스들 (각 효과 클래스)
            → PlayerRuntimeStats (스탯 반영)
            → 게임 이벤트 처리 (피격, 공격, 구르기 등)
```

## 아이템 데이터 구조

```
ItemSO (에디터 SO)  ──→  RuntimeItemData  ←──  ItemEntry (서버 CSV)
     ↓ FromSO()                                   ↓ FromServer()
                       RuntimeItemData
                       ├── itemId, displayName
                       ├── rarity (Common/Rare/Epic)
                       ├── category (Ring/Necklace/Boots/Gloves/Belt/Charm/Active)
                       ├── shapeId (퍼즐 블록 연결)
                       ├── cooldown (Active 아이템)
                       └── effects: List<ItemEffectSlot>
                           ├── effectType ("MeleeDamage", "Lifesteal" 등)
                           ├── trigger ("Always", "WithFireWeapon", "OnHit" 등)
                           └── value, value2, value3, maxStack, duration
```

## 플레이어-장비-아이템 연계 구조

```
PlayerController
├── RuntimeStats (PlayerRuntimeStats)
│   ├── Base Layer      ← CharacterData (캐릭터 기본 스탯)
│   ├── Passive Layer   ← PassiveSO (캐릭터 패시브)
│   ├── Weapon Layer    ← WeaponData.baseAttack/baseDefense
│   ├── Item Layer      ← ItemEffectManager.GetAccumulatedStats()
│   ├── RoomBuff Layer  ← RoomBuffHandler (버프 타일)
│   ├── Synergy Layer   ← BlockSynergyBridge (퍼즐 시너지)
│   └── Conditional     ← ConditionalSynergy (OnHit/OnLowHp 조건부)
│
├── WeaponManager (PlayerWeaponManager)
│   ├── CurrentWeaponData
│   │   ├── weaponType (Katana/Greatsword/Bow/Crossbow/Staff)
│   │   ├── element (None/Water/Fire/Grass/Earth/Lightning) ← 아이템 원소 조건 판정용
│   │   ├── baseAttack, baseDefense, attackSpeed, attackRange
│   │   └── abilitySet → WeaponAbilitySO → AbilityStep (공격 스텝)
│   └── slots[0], slots[1] (2슬롯)
│
├── WeaponEffectHandler
│   ├── PlayEffect() → AbilityStep 순회
│   │   ├── EffectStep → VFX/이펙트 스폰
│   │   ├── ColliderStep → 히트박스 스폰 (Trail/Spawned)
│   │   └── BasicArrow → 투사체 발사
│   │       ├── BonusProjectile → SpawnFanShots (고정 부채꼴)
│   │       └── ExtraShotCount → SpawnExtraShots (스킬 버프, 랜덤)
│   └── DamageFormula.Calculate(baseDamage, AttackPower)
│
└── EffectManager (ItemEffectManager) ← GameRunSession 소유
    ├── Rebuild() → 아이템 변경/무기 변경 시 전체 재생성
    ├── GetAccumulatedStats() → 패시브 스탯 합산
    ├── OnPreDealDamage() → ColliderInstance/BasicArrow에서 호출
    ├── OnPostDealDamage() → 흡혈, 독, 빙결 등
    ├── OnPreTakeDamage() → 피해 무효화, 감소
    ├── OnPostTakeDamage() → 반사, 방버프
    ├── OnNearDeath() → 부활, 사망무효
    ├── OnRollEnd/Land() → 구르기 후 버프, 착지 AoE
    ├── OnRoomClear() → 방 클리어 회복
    ├── OnBossClear() → 보스 드랍, 버프 갱신
    ├── OnRecipeComplete() → 시너지 완성 효과
    ├── OnSkillUse() → 원소 스킬 추가 효과
    └── ModifyHeal() → 치유량 증가
```

## 장비(무기) 원소 시스템

```
Water → Fire → Grass → Earth → Lightning → Water (순환 상성)

WeaponSO.element / WeaponData.element (WeaponElement enum)
  → ItemEffectContext.WeaponElement로 전달
  → ItemEffectBase.IsActive()에서 trigger 조건 판정
    WithFireWeapon → element == Fire?
    WithWaterWeapon → element == Water?
    ...

무기 변경 시:
  → GameRunSession.RebuildItemEffects()
  → EffectManager.RefreshContext() (원소 갱신)
  → EffectManager.Rebuild() (조건 재판정)
  → PlayerRuntimeStats.RefreshItemBonuses() (스탯 재계산)
```

## 투사체 시스템 (아이템 연계)

```
기본 발사 (1발):     ↑
BonusProjectile=1:   ↗  ↖      (아이템 패시브, 고정 15도 간격)
BonusProjectile=2:   ↗  ↑  ↖
ExtraShotCount=2:    ? ?  ?     (스킬 버프, 랜덤 spread, 80ms 딜레이)

WeaponEffectHandler.PlayEffect()
  → BasicArrow 감지 시
    → SpawnFanShots(BonusProjectile) ← 아이템 패시브
    → SpawnExtraShots(ExtraShotCount) ← 스킬 버프
```

## 스탯 계산 흐름 (Recalculate)

```
dmgMul  = 1 + AllDamagePercent + AllStatsPercent  (공격력 배율)
defMul  = 1 + AllStatsPercent                      (방어력 배율)
luckMul = 1 + AllStatsPercent                      (행운 배율)

MeleeAttack  = (Base + Passive + Weapon + Item + RoomBuff + Synergy + Conditional) × dmgMul
RangedAttack = (Base + Passive + Weapon + Item + RoomBuff + Synergy + Conditional) × dmgMul
Defense      = (Base + Passive + Weapon + Item + RoomBuff + Synergy) × defMul
Luck         = (Base + Passive + Item + Synergy) × luckMul

AttackSpeedMultiplier = 1 + bonus + synergy + roomBuff + itemAttackSpeed + conditional
MoveSpeedMultiplier   = 1 + roomBuff + itemMoveSpeed
```

## 서버 테이블 (뒤끝 CDN)

| 차트명 | 용도 | 데이터 수 |
|--------|------|----------|
| ITEM_DATA | 아이템 효과 | 56개 (76행) |
| BUFF_DATA | 버프/디버프 티어 | 29행 |
| PLAYER_DATA | 캐릭터 기본 스탯 | 3명 |
| PASSIVE_DATA | 캐릭터 패시브 | 9행 |
| EQUIPMENT_DATA | 무기 데이터 | 4개 |
| STAGE_DATA | 맵 그리드 | 9개 방 |
| BLOCK_SHAPE_DATA | 퍼즐 블록 | 20종 |
| BLOCK_SYNERGY_DATA | 시너지 그리드 | 18행 |

ChartLoader.Load("차트명", row => ...) 공통 유틸 사용.
CDN 1회 로드 → 캐시 → 모든 DataManager 공유.

## 새 아이템 추가 방법

1. CSV에 행 추가 (passive_id, slot, effect_type, trigger, value...)
2. 뒤끝 차트에 업로드
3. Resources/ITEM_DATA.json에도 동기화
4. 끝 (기존 effectType이면 코드 수정 없음)

## 새 effectType 추가 방법

1. Effect 클래스 생성 (ItemEffectBase 상속)
2. ItemEffectRegistry.RegisterAll()에 한 줄 등록
3. 끝

---

## effectType 전체 사전

### 패시브 스탯 (ModifyStats)

| effectType | 의도 | 적용 대상 | 상태 |
|---|---|---|---|
| MeleeDamage | 근거리 공격력 Flat 가산 | MeleeAttack | 동작 |
| RangedDamage | 원거리 공격력 Flat 가산 | RangedAttack | 동작 |
| AllDamage | 정수=Flat 가산, 소수=% 배율 | Melee+Ranged | 동작 |
| AttackDamage | HP 조건부 공격력 % | AllDamagePercent | 동작 |
| Defense | 방어력 Flat 가산 | Defense | 동작 |
| MaxHP | 최대 체력 가산 (음수=감소) | MaxHp | 동작 |
| Luck | 행운력 Flat 가산 | Luck | 동작 |
| MoveSpeed | 이동속도 m/s 가산 | MoveSpeedMultiplier → DefaultMoveAbility | 동작 |
| AttackSpeed | 공격속도 가산 (음수=감소) | AttackSpeedMultiplier → ActAttackState Animator.speed | 동작 |
| RollCooldown | 구르기 쿨타임 비율 (음수=감소) | RollCooldownBonus → LocoDodgeState.Exit | 동작 |
| RollDistance | 구르기 거리 m 가산 | RollDistanceBonus → LocoDodgeState.Enter duration 연장 | 동작 |
| RangedRange | 원거리 사거리 m 가산 | RangedRangeBonus → BasicArrow lifetime 연장 | 동작 |
| SkillCooldownReduction | 스킬 쿨타임 비율 감소 | SkillCooldownReduction | 동작 |
| ActiveItemCooldownReduction | 액티브 아이템 쿨타임 감소 | ActiveItemCooldownReduction | 동작 |
| HealingReceived | 받는 치유량 % 증가 | ModifyHeal에서 amount 곱연산 | 동작 |
| DamageReduction | 받는 피해 % 감소 | OnPreTakeDamage에서 FinalDamage 곱연산 | 동작 |
| AllStats | 모든 스탯 % 증가 (공격+방어+행운) | dmgMul, defMul, luckMul | 동작 |
| DebuffResistance | 디버프 무효화 확률 | 디버프 시스템 구현 시 사용 | 스탯만 |
| AllElementBonus | 원소 효과 % 증가 | 원소 데미지 시스템 구현 시 사용 | 스탯만 |
| SpecialRoomChance | 특수방 등장 확률 가산 | 맵 생성 시 반영 | 스탯만 |
| HighGradeItemChance | 고등급 아이템 드랍 확률 가산 | 아이템 드랍 시 반영 | 스탯만 |
| ConsumableSlot | 소모품 슬롯 효과 배율 | 소모품 시스템 구현 시 사용 | 스탯만 |
| DebuffDuration | 디버프 지속시간 방 수 가산 | 디버프 시스템 구현 시 사용 | 스탯만 |

### 공격 적중 (OnPostDealDamage)

| effectType | 의도 | 상태 |
|---|---|---|
| Lifesteal | 가한 피해의 N% HP 회복 | 동작 |
| HPRegenOnHit | 공격마다 고정 HP 회복 | 동작 |
| PoisonOnHit | 확률로 독 (초당 피해, N초) | TODO: 상태이상 시스템 |
| Freeze | 확률로 빙결 (이동/공격 정지, N초) | TODO: 상태이상 시스템 |
| ExtraAttack | 확률로 동일 타격 1회 추가 | TODO: 타격 재발동 |
| TeleportSwap | 확률로 플레이어↔적 위치 교체 | 동작 |

### 피격 (OnPreTakeDamage / OnPostTakeDamage)

| effectType | 의도 | 상태 |
|---|---|---|
| DamageNegate | 확률로 피해 완전 무효화 | 동작 |
| DamageReflect | 받은 피해 N%를 공격자에게 반사 | TODO: IDamageable 연결 |
| DefenseOnHit | 피격 시 임시 방어력 버프 (쿨다운) | 동작 |

### 처치 (OnKill)

| effectType | 의도 | 상태 |
|---|---|---|
| GoldOnKill | 확률로 골드 추가 | 동작 |
| FullHealOnKill | 확률로 전체 HP 회복 | 동작 |

### 방/보스 클리어 (OnRoomClear / OnBossClear)

| effectType | 의도 | 상태 |
|---|---|---|
| HPRegenOnClear | 방 클리어 시 고정 HP 회복 | 동작 |
| BossDropItem | 보스 처치 시 추가 아이템 드랍 | TODO: 드랍 로직 |
| BuffRefreshOnBoss | 보스 처치 시 버프 리셋+1티어 승급 | 동작 |

### 시너지 완성 (OnRecipeComplete)

| effectType | 의도 | 상태 |
|---|---|---|
| HPRegenOnRecipe | 시너지 완성 시 HP 회복 | 동작 |
| AllDamageOnRecipe | 시너지 완성마다 공격력% 누적 (최대N) | 동작 (PersistentStack) |
| RecipeSynergyNextAttack | 시너지 완성 시 다음 1회 공격에 원소 효과 | TODO: 플래그 시스템 |

### 구르기 (OnRollEnd / OnRollLand)

| effectType | 의도 | 상태 |
|---|---|---|
| FirstAttackAfterRoll | 구르기 후 첫 공격 데미지% 증가 | 동작 |
| RollLandingDamage | 착지 시 범위 피해 | TODO: OverlapSphere |

### 사망 직전 (OnNearDeath)

| effectType | 의도 | 상태 |
|---|---|---|
| ReviveHeal | HP≤0 → HP N% 회복 (런당 횟수제한) | 동작 |
| DeathNegate | HP≤0 → 사망무효 + N초 무적 (런당 횟수제한) | 동작 (무적 TODO) |

### 스킬 (OnSkillUse)

| effectType | 의도 | 상태 |
|---|---|---|
| FireExplosionOnSkill | 불무기+스킬 → 화염 폭발 범위 데미지 | TODO: VFX+AoE |
| LightningOnSkill | 번개무기+스킬 → 번개 추가 데미지 | TODO: VFX+데미지 |

### 특수

| effectType | 의도 | 상태 |
|---|---|---|
| PoisonApple | 획득 시 확률로 체력 감소 (1회, 부작용) | 동작 (TryTriggerOnce) |
| RandomElement | 무속성 공격→랜덤 원소 변환 | 코드 있음, 원소 시스템 필요 |
| ItemGradeUp | 아이템 획득 시 확률로 등급 상승 | 동작 |
| Heal | 액티브 아이템 HP 회복 | 액티브 사용 시스템 필요 |

---

## trigger 조건 사전

| trigger | 조건 | IsActive 판정 |
|---|---|---|
| Always | 항상 활성 | true |
| WithFireWeapon | 장착 무기 = 불 | ctx.WeaponElement == Fire |
| WithWaterWeapon | 장착 무기 = 물 | ctx.WeaponElement == Water |
| WithGrassWeapon | 장착 무기 = 풀 | ctx.WeaponElement == Grass |
| WithMagicWeapon | 장착 무기 = 땅 | ctx.WeaponElement == Earth |
| WithLightningWeapon | 장착 무기 = 번개 | ctx.WeaponElement == Lightning |
| WithShield | 방패 장착 | ctx.HasShield |
| HPBelow50 | HP ≤ 50% | ctx.HpRatio <= 0.5 |
| HPBelow30 | HP ≤ 30% | ctx.HpRatio <= 0.3 |
| OnHit | 이벤트: 공격 적중 / 피격 시 | 이벤트 발생 시 |
| OnKill | 이벤트: 적 처치 시 | 이벤트 발생 시 |
| OnRoomClear | 이벤트: 방 클리어 시 | 이벤트 발생 시 |
| OnBossClear | 이벤트: 보스 처치 시 | 이벤트 발생 시 |
| OnRecipeComplete | 이벤트: 시너지 완성 시 | 이벤트 발생 시 |
| OnRollEnd | 이벤트: 구르기 종료 시 | 이벤트 발생 시 |
| OnRollLand | 이벤트: 구르기 착지 시 (지상만) | 이벤트 발생 시 |
| OnNearDeath | 이벤트: HP≤0 도달 시 | 이벤트 발생 시 |
| OnPickup | 이벤트: 아이템 획득 시 (1회) | OnActivate 시 |
| OnItemPickup | 이벤트: 다른 아이템 획득 시 | 이벤트 발생 시 |

---

## 원소 시스템

```
Water → Fire → Grass → Earth → Lightning → Water (순환 상성)
```

| WeaponElement | trigger 매핑 |
|---|---|
| None | 무속성 |
| Water | WithWaterWeapon |
| Fire | WithFireWeapon |
| Grass | WithGrassWeapon |
| Earth | WithMagicWeapon |
| Lightning | WithLightningWeapon |

---

## 버프 시스템

- BUFF_DATA 테이블: buff_type × tier (1/2/3) × 버프/디버프
- BuffRoller: 버프60%/디버프40%, 티어 60%/30%/10%
- ActiveRoomBuff: Modifier + Tier + BuffType + RoomsRemaining
- BuffRefreshOnBoss: 보스 처치 시 전체 버프 tier+1 승급 + 지속시간 리셋

---

## 파일 구조

```
Assets/Abyss/Systems/Item/
├── Core/
│   ├── IItemEffect.cs
│   ├── ItemEffectBase.cs
│   ├── ItemEffectContext.cs
│   ├── ItemEffectManager.cs
│   ├── ItemEffectRegistry.cs
│   ├── DamagePacket.cs
│   └── GenericStatEffect.cs
├── Effects/
│   ├── Passive/PassiveStatEffects.cs (23개)
│   ├── Passive/SpecialEffects.cs (4개)
│   ├── OnHit/OnHitEffects.cs (6개)
│   ├── OnTakeDamage/OnTakeDamageEffects.cs (3개)
│   ├── OnKill/OnKillEffects.cs (2개)
│   ├── OnClear/OnClearEffects.cs (6개)
│   ├── OnRoll/OnRollEffects.cs (2개)
│   ├── OnDeath/OnDeathEffects.cs (2개)
│   └── OnSkill/OnSkillEffects.cs (2개)
├── AccumulatedStats.cs
└── WeaponElement.cs
```

## Hook 포인트

| 위치 | 호출 |
|---|---|
| PlayerController.TakeDamage | OnPreTakeDamage, OnNearDeath, OnPostTakeDamage |
| PlayerController.Heal | ModifyHeal |
| ColliderInstance.ApplyDamage | OnPreDealDamage, OnPostDealDamage |
| BasicArrow.OnTriggerEnter | OnPreDealDamage, OnPostDealDamage |
| LocoDodgeState.Exit | OnRollEnd, OnRollLand |
| GameRunSession.EnterStandby | OnRoomClear |
| GameRunSession.EnterChapterClear | OnBossClear |

## TODO (미구현)

- [ ] RollLandingDamage: OverlapSphere 범위 피해
- [ ] DamageReflect: IDamageable.TakeDamage 연결
- [ ] BossDropItem: WorldItemDisplay.SpawnFromData 호출
- [ ] PoisonOnHit: 상태이상 DoT 시스템
- [ ] Freeze: 상태이상 빙결 시스템
- [ ] ExtraAttack: 타격 판정 재발동
- [ ] FireExplosionOnSkill: VFX + AoE 데미지
- [ ] LightningOnSkill: VFX + 추가 데미지
- [ ] RecipeSynergyNextAttack: 다음 공격 원소 플래그
- [ ] RandomElement: 원소 데미지 상성 배율
- [ ] DeathNegate 무적 상태 부여
