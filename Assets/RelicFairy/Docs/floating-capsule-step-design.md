# 플로팅 캡슐(스프링) 이동 + Foot IK 설계 — 계단/접지 리워크

상태: **설계 검증 단계** (구현 전 승인 필요). 작성 2026-06-24.

## 1. 목표 / 현재 문제
- 계단/단차/슬로프에서 반복된 끼임·고정·순간이동·진동. 원인은 예측형 MovePosition 스텝업(물리와 싸움)과 binary 접지+스냅 부재.
- 요구사항(사용자): **물리 피드백**(힘/속도) 사용, **위치 강제 업데이트 없음**, **진행방향(실제 이동) 대응**.
- 결론: 임시 패치 누적보다 **플로팅 캡슐(hover spring)** 로 접지 모델을 교체하는 게 근본 해결 (실무 표준).

## 2. 현재 물리 셋업 (코드/프리팹 검증된 실측치)
- Rigidbody: **mass 50**, linearDamping 1, constraints=80(회전 X·Z 고정, Y 자유), 런타임 `useGravity=false`(DefaultJumpAbility가 커스텀 중력), Interpolate 강제.
- CapsuleCollider: radius **0.27**, height **1.15**, center.y **0.576** → **캡슐 바닥 ≈ y0 = 발/원점**. transform.position = 발 높이.
- 접지/중력/점프: `DefaultJumpAbility` — `UpdateGroundCheck`(아래 레이, groundCheckDistance **0.3**), `ApplyGravity`(접지 시 return, 공중 시 커스텀 중력+fallMultiplier), `Jump`(vy AddForce). FixedUpdate 순서: GroundCheck → ApplyGravity → FreezeRotation → ApplyFacing → StepClimb.
- 공중/착지: `LocoAirState`(IsGrounded/IsJumping 기반), 낙하높이 게이트·착지 셰이크(최근 작업) 존재.
- ⚠️ groundCheckDistance(0.3) < StepMaxHeight(0.35) — 현 스텝 처리의 마찰 원인.

## 3. 설계 — 플로팅 캡슐 (스프링-댐퍼)
### 3-1. 콜라이더 재배치 (프리팹, 인게임 확인 필수)
- 캡슐 **바닥을 발(원점)에서 `rideHeight`만큼 띄운다**. 예: rideHeight 0.4 → 콜라이더 바닥 y=0.4, height≈0.75, center.y≈0.775.
- 이유: 스프링이 콜라이더 바닥↔지면 간격을 rideHeight로 유지하면 **발(원점)이 지면에 안착**. rideHeight 이하 단차는 콜라이더가 지면에 안 닿아(클리어) **충돌 잼 없음**.
- 모델은 원점에 발이 있으므로 추가 오프셋 불필요. (rideHeight ≥ 최대 스텝높이여야 단차 흡수)

### 3-2. 스프링 힘 (PD 컨트롤러)
- 매 FixedUpdate, 콜라이더 바닥에서 아래로 캐스트(거리 `rideHeight + probe`).
- 지면 적중 시 수직 힘:
  `Fy = (rideHeight - hitDistance) * k_spring  -  vY * k_damp  +  mass * g`
  - 검증(리서치): `k_spring = 2 * k_damp` 이면 임계감쇠(진동 0). mass 50 기준 튜닝.
  - `mass*g` 항으로 중력 상쇄 → hover 유지. (힘 기반 = 물리 피드백, **위치 강제 없음**)
- 적중 없음(공중) → 스프링 0, 일반 중력(낙하).

### 3-3. 접지/중력 재정의
- `IsGrounded` = 스프링 캐스트가 `rideHeight + tolerance` 내 지면 적중. → 계단/슬로프에서도 항상 접지(공중상태 오전이 없음).
- 중력: hover 범위 밖(점프/낙하)만 적용. hover 범위 내는 스프링이 수직 담당.

### 3-4. 점프 연동
- 점프: 상향 vy 부여 + **스프링 일시 비활성**(상승 동안) → 스프링이 끌어내리지 않음.
- 재활성: 하강하여 hover 범위 재진입 시(또는 grace 타이머 후). 착지 = 스프링이 부드럽게 흡수(바운스 없음, 댐핑).

### 3-5. 진행방향 대응
- 수평 이동은 기존 속도 기반 그대로. 스프링은 수직만. 진행방향(실제 속도)으로 전진하다 단차를 만나면 스프링이 자동 흡수 → 별도 스텝 코드 불필요.

### 3-6. StepClimb 제거
- 현 `DefaultMoveAbility.StepClimb` 및 호출부 제거(스프링이 대체). `IsStepClimbing` 억제 로직도 정리.

## 4. 통합 지점 (변경 파일)
| 파일 | 변경 |
|---|---|
| `DefaultJumpAbility` | UpdateGroundCheck→스프링 캐스트+접지판정, ApplyGravity→hover밖만, Jump→스프링 일시 비활성 |
| `DefaultMoveAbility` | StepClimb 제거(수평 이동 유지) |
| `PlayerController.FixedUpdate` | StepClimb 호출 제거, 스프링은 JumpAbility 내부 |
| `LocoAirState` / Loco*State | IsGrounded 의미 변경에 맞춰 공중/착지 전이 재검증(낙하높이 게이트·착지셰이크 재통합) |
| `CharacterData` | rideHeight·k_spring·k_damp·probe 파라미터 추가 |
| PlayerCharacter.prefab | CapsuleCollider center/height 재배치(인게임 확인) |

## 5. Foot IK (이동 확정 후 별도 단계)
- Humanoid(CombatGirl) → `OnAnimatorIK`에서 발별 다운레이 → `SetIKPosition/Rotation` + 골반(pelvis) 드롭 + weight 블렌딩. Animator 레이어 **IK Pass** 필요.
- 플로팅으로 생기는 부유감/경사 접지를 발 IK로 가림. 전용 컴포넌트, 별도 튜닝.

## 6. 리스크 & 완화
- **점프/낙하/착지 회귀**(최근 튜닝분): 스프링 비활성 타이밍·hover tolerance로 재통합, 단계별 검증.
- **콜라이더 재배치 → 히트박스 변화**: 피격 판정·룬/아이템 콜라이더 영향 확인.
- **넉백/외력**: 힘 기반이라 자연 호환(기존 AddForce 넉백 유지). hover 복귀 확인.
- **mass 50 스프링 튜닝**: 인게임 반복 필요(제가 화면 못 봄 → 사용자 협업).
- **완화책**: `CharacterData`에 `useFloatingController` 토글 → 켜고/끄고 비교, 문제 시 즉시 구(舊) 접지로 롤백.

## 7. 검증/테스트 계획 (수용 기준)
1. 평지: 지터 0, 높이 일정(발 안착).
2. 슬로프: 매끄럽게 오르내림.
3. 계단/단차(≤rideHeight): 잼/순간이동/진동 없이 흡수.
4. 벽: 그냥 멈춤(고정 없음).
5. 점프: 깔끔한 상승·아치, 착지 바운스 없음, 낙하높이 게이트/착지셰이크 동작.
6. 낙사/높은 낙하: 추락 애니 + 착지 연출.
7. 넉백: 정상 + hover 복귀.

## 8. 롤백
- `useFloatingController=false`면 기존 `DefaultJumpAbility`+`StepClimb` 경로 유지(코드 보존). 토글로 즉시 복귀.
- 또는 별도 브랜치에서 작업.

## 9. 대안 (기각)
- CharacterController.stepOffset: 키네마틱 → Rigidbody 물리(넉백) 상실. 기각.
- Rigidbody Source식 스텝업/CapsuleCast 보정: 위치 강제(요구 위반) + 끼임 잔존. 기각(현 방식).
