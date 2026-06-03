# 유물 클래스 시스템 (Relic Class System) — 설계 문서

상태: 설계 확정 / 구현 진행 (2026-06-02)

## 1. 개념

기존 "캐릭터 선택"(Knight/Galahad/Gawain/Mage/Berserker 각 별도 몸)을 폐기하고,
**몸은 CombatGirl 하나로 통일**한다. 플레이어는 **유물 클래스(Relic Class)** 1개를
선택해 그 힘을 입는다. 확정된 로어("영웅 몸 = 유물", "멀린의 영혼 복구 + 기사 잔상 선택")와 일치.

- 첫 연출: 영혼 → CombatGirl 빙의 (인트로, 후순위)
- 유물 1개를 **시작방에서 선택 → 런(사망) 종료까지 고정** (교체 없음)
- 유물이 부여: **패시브 + 고유 스킬(Q) + 외형(초기엔 오라만)**. **스탯은 주지 않음.**
- **무기는 별도 선택**(줍기/장착) — 유물과 독립
- **서약(Oath)** = 별개의 추가 효과 레이어 (이 설계 범위 밖)
- 시작 유물: **Galahad, Gawain** 2종. Knight/Mage/Berserker 폐기.

## 2. 데이터 모델 — `RelicClassSO`

```
RelicClassSO : ScriptableObject   [CreateAssetMenu]
├─ id            : RelicId (enum)            // 로직 팩토리 매핑 키
├─ displayName   : string
├─ loreDesc      : string
├─ portrait      : Sprite                    // 선택 UI
├─ rosterIllust  : Sprite                    // 선택 UI 전신
├─ passives      : PassiveSO[]               // 순수 데이터 패시브
├─ qSkillClipKey : string                    // 고유스킬 애니 (Addressables)
├─ qSkillCinematic : UltimateCinematicConfig // Q 연출 (nullable)
└─ auraVfxKey    : string  + auraSocket : string  // 외형 오라 VFX + 부착 소켓
```

기존 `CharacterData`의 passive / QSkillClipKey / QSkillCinematic / portrait / rosterIllust 가
**그대로 RelicClassSO로 이주**된다. (스탯 컬럼은 유물에서 제외 — 베이스 스탯은 CombatGirl 공통)

## 3. 런타임 구조

- 몸 프리팹: **CombatGirl 단일** (`Player`로 일반화). 파생 PlayerController 클래스 제거.
- `PlayerController` init 시 **선택된 RelicClassSO 적용**:
  1. `passives[]` → RegisterPassive (PassiveSO 기반)
  2. `id` → **RelicSkillFactory** 레지스트리에서 고유스킬 ISkillRuntime 생성 (코드 필요한 스킬)
  3. 코드형 패시브(흡혈 스택 등)도 `id` → **RelicPassiveFactory** 레지스트리로 등록
  4. auraVfx 스폰 (소켓에 부착, 런 동안 유지)

### 로직 처리 = 하이브리드 (확정)

순수 데이터 패시브는 `PassiveSO`로 직접 참조. 코드가 필요한 패시브/스킬은
`RelicId → 팩토리` 레지스트리로 매핑한다. 기존 파생클래스(Galahad/Gawain)의
`InitPassives()` / `CreateCharacterSkillRuntime()` 로직을 이 팩토리로 이주만 하면 되어
재작성을 최소화한다.

## 4. 기존 자산 이주

| 기존 캐릭터 | → 유물 클래스 |
|---|---|
| Galahad (빛 반사 패시브 + 성스러운 방패 Q) | RelicClass_Galahad |
| Gawain (태양검/화상 + 불 장판 Q) | RelicClass_Gawain |
| Knight / Mage / Berserker | **폐기** |

프리팹·모델·파생클래스는 폐기, 데이터(SO) + 로직 팩토리만 보존.

## 5. 선택 흐름

- 베이스캠프 `CharacterDisplayStand → RelicDisplayStand` 교체
- 시작방에서 유물 선택 → 확정 시 CombatGirl 몸에 적용 + GameRunSession/Loadout에 저장 → 런 동안 고정
- 무기 선택대/줍기는 그대로 (유물과 독립)

## 6. 구현 단계

- **Phase 1 (데이터)**: `RelicClassSO` + `RelicId` enum + 팩토리 레지스트리. RelicClass_Galahad/Gawain 데이터 작성, 파생클래스 로직 팩토리 이주.
- **Phase 2 (런타임)**: CombatGirl 단일 PlayerController + RelicApplier(패시브/스킬/오라 적용). 파생클래스 제거.
- **Phase 3 (선택)**: RelicDisplayStand(시작방) + 런 저장.
- **Phase 4 (연출, 후순위)**: 영혼 → CombatGirl 빙의 인트로.
- **Cleanup**: Knight/Mage/Berserker 폐기 (삭제 전 확인).

## 7. 범위 밖

- 무기 선택 시스템 (변경 없음)
- 서약(Oath) 레이어
- 스탯 보정 (유물은 스탯 미부여)
