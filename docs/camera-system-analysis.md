# Project RelicFairy — 카메라 시스템 진단 & 해결책 리포트

> 작성일: 2026-06-06 · 대상: Cinemachine 2.10.5 기반 3D 액션 로그라이크
> 범위: 코드 근거 진단 → 외부 리서치(출처 명시) → 기술 해결책 + UX 제안 + 우선순위 백로그

---

## 핵심 요약 (먼저 읽기)

**증상**: 벽과의 충돌/막힘, 시야 가림, 좁은 복도/방 클리핑, 전투 중 흔들림.

**근본 원인 (코드/씬 근거로 확정된 것)**:

1. **벽 가림 방지 장치 2개가 모두 "켜져 있지만 작동 안 함"** — 카메라 GameObject에 `CinemachineCollider`(공식 디오클루전)와 커스텀 `CameraOcclusionFader`(가림 오브젝트 반투명)가 **둘 다 붙어 있으나, 둘 다 LayerMask가 `Nothing`(`m_Bits: 0`)으로 직렬화**되어 있어 아무 벽도 감지하지 못한다. → **현재 벽 가림을 막는 코드가 사실상 0개.** (`GameScene_Ch1.unity` 830·848행)
2. **가림 방지 장치가 Ch1·LichTest 씬에만 존재** — `CinemachineCollider`/`CameraOcclusionFader`/`GameCameraController`/`CinemachineFreeLook` 모두 `GameScene_Ch1.unity`와 `GameScene_LichTest.unity`에만 authoring 돼 있고 **Ch2/Ch3/Ch4/BaseCamp/Tutorial 씬에는 없음**. (절차생성 피벗 이후 Ch1을 베이스 런 씬으로 쓰는지 **확인 필요** — 만약 Ch2~4가 실제로 쓰인다면 거기엔 가림 방지가 전무.)
3. **카메라 경계(Confiner) 부재** — 절차생성 방(`MapBuilder`)에 카메라를 방 안으로 가두는 장치가 없다. 좁은 방/복도에서 카메라가 벽 밖/천장을 보거나 클리핑할 수 있음.
4. **수동 카메라 점유 구간에서는 Collider가 아예 안 돈다** — `GameCameraController`가 인트로/존 패닝/블렌드 동안 `Brain`을 꺼고 `transform`을 직접 보간한다. 이 구간엔 Cinemachine 확장(Collider)이 적용되지 않아 벽을 통과할 수 있음.
5. **스크린 쉐이크가 Cinemachine과 충돌** — `HitFeelService.CameraShake()`가 `Camera.main.transform.localPosition`을 직접 흔드는데, 같은 GameObject의 `CinemachineBrain`이 매 `LateUpdate`마다 `transform`을 덮어쓴다. 둘이 경쟁 → 흔들림이 약하거나 튀는 원인.

**가장 빠른 효과 (P0)**: 두 LayerMask를 `Wall`(+`Ground`)로 채우기만 해도 가림 문제 대부분이 즉시 완화된다. 코드 수정 없이 인스펙터 값만 바꾸면 됨. 단, 절차생성 벽 콜라이더가 실제로 `Wall` 레이어인지 **확인 필요**.

---

## 1단계: 현재 카메라 구현 정밀 진단 (파일:심볼 근거)

### 1.1 카메라 리그 구조

- **단일 GameObject에 전부 적층**: `GameScene_Ch1.unity`의 "Main Camera"(fileID 330585543)에 `Camera` + `CinemachineBrain`(330585549) + `CinemachineFreeLook`(메인 vcam) + `CinemachineCollider`(330585552) + `GameCameraController`(330585550) + `CameraOcclusionFader`(330585551)가 **한 오브젝트에 모두** 올라가 있다.
- **추적 방식 = 3인칭 오빗(FreeLook)**:
  - `PlayerController.SetupCamera()` (`PlayerController.cs:1175`)가 `CinemachineFreeLook.Follow/LookAt = player.transform`로 바인딩.
  - **오빗 3링이 모두 동일**: Top/Middle/Bottom = `Height 5 / Radius 2` (`GameScene_Ch1.unity:718`). 즉 수직 오빗 변화가 없는 **고정 시점** — 사실상 "5m 위, 2m 뒤"의 가파른(≈68° 내려보는) 근접 3인칭/유사 탑다운.
  - `m_BindingMode: 4` = SimpleFollowWithWorldUp(이동 방향 뒤에서 추적), `m_Heading.m_Definition: 3` = Velocity(속도 기준 헤딩), Recentering 비활성.
  - `Brain.m_DefaultBlend` = EaseInOut **2초** — vcam 전환 블렌드가 길다.

### 1.2 카메라 전환/점유 (`GameCameraController.cs`)

- 이 컨트롤러는 연출 구간마다 **`Brain`과 vcam을 꺼고 `transform`을 직접 보간**한 뒤 Cinemachine에 제어권을 돌려주는 패턴:
  - `ActivateForStartRoom()` / `BlendToActiveCameraAsync()` (285행): 둘러보기 포즈 → Wisp/플레이어 추적 시점 수동 보간.
  - `PlayStartRoomTourAsync()` (354행): 방 둘러보기 호(arc) 패닝.
  - `PanToZoneAndReturnAsync()` (413행): 존/보스 줌인 후 복귀.
  - `SnapToTarget()` (159행): 절차 방 전환 직후 `PreviousStateIsValid = false`로 즉시 스냅(댐핑 패닝 방지).
- **함의**: 이 모든 수동 구간에서 `CinemachineCollider`/`CinemachineBrain`이 꺼져 있으므로 **디오클루전이 동작하지 않는다.** 패닝/줌인이 벽을 통과해도 막아줄 장치가 없음.

### 1.3 벽 충돌/오클루전 처리 현황 — **둘 다 inert**

**(A) `CinemachineCollider` (공식 디오클루전 확장) — 작동 안 함**
`GameScene_Ch1.unity:848`
```
m_CollideAgainst:  m_Bits: 0      # ← Nothing! 충돌 검사 대상 레이어 없음
m_IgnoreTag: Player
m_TransparentLayers: m_Bits: 0    # ← Nothing
m_MinimumDistanceFromTarget: 0.5
m_AvoidObstacles: 1               # 켜져 있으나 검사 레이어가 없어 무의미
m_Strategy: 1                     # PreserveCameraHeight
m_CameraRadius: 0.3
m_SmoothingTime: 0.2
m_Damping: 0.5
m_DampingWhenOccluded: 0.1
m_MinimumOcclusionTime: 0
```
→ `Avoid Obstacles`가 켜져 있어도 **`Collide Against`가 비어 있으면 어떤 장애물도 인식하지 못한다** (Cinemachine 공식 문서: "Objects not in these layers are ignored"). 사실상 비활성.

**(B) 커스텀 `CameraOcclusionFader` — 작동 안 함**
- 설계는 양호: `SphereCastNonAlloc` + 고정 버퍼(GC 0) + `MaterialPropertyBlock` 재사용 + 렌더러 캐시로, 카메라~플레이어 사이 오브젝트를 `hiddenAlpha 0.15`로 반투명화. (`CameraOcclusionFader.cs:113` `DetectOccluders`)
- **그러나 씬 직렬화 값이 `occlusionMask.m_Bits: 0`(Nothing)** (`GameScene_Ch1.unity:830`). 코드 기본값은 `~0`(Everything)이지만 **씬에 저장된 값이 우선**하므로 SphereCast가 어떤 레이어도 안 맞춤 → 페이드 전무.
- `SetTarget()`은 외부에서 호출되지 않지만 `Start()`의 `SubscribePlayer()`가 자동으로 플레이어를 잡으므로 타겟팅 자체는 OK. **막는 건 오직 빈 마스크.**

**(C) 중복**: (A)와 (B)는 오클루전을 **서로 다른 방식**(카메라 당기기 vs 오브젝트 반투명)으로 해결하는 중복 장치다. 하나를 1차로 정하고 나머지를 보조로 쓰는 정리가 필요.

### 1.4 절차생성 방에서 벽 막힘이 생기는 구조적 원인

- **경계 부재**: `MapBuilder`가 방/복도를 동적으로 짓지만 카메라를 방 bounds 안으로 가두는 `CinemachineConfiner`가 없다. 좁은 방에서 radius 2 오빗 + SimpleFollow 헤딩이 벽 쪽으로 회전하면 카메라가 벽/천장에 박히거나 방 밖 빈 공간을 비춘다.
- **레이어 일관성 미확인**: 절차 벽 콜라이더가 `Wall` 레이어인지 코드로는 단정 불가(`MapGen/*.cs`에 `gameObject.layer`/`NameToLayer` 할당 없음 → 프리팹/블록에서 에디터 지정). **확인 필요.**

### 1.5 전투 흔들림(역경직 = 카메라 쉐이크)과의 상호작용

- `HitFeelService.CameraShake(amplitude, duration)` (`HitFeelService.cs:30`)가 `Camera.main.transform.localPosition`을 **직접** 흔든다(`ShakeRoutine` 73행).
- 같은 오브젝트의 `CinemachineBrain`이 매 `LateUpdate`에 `transform.position`을 vcam 해석값으로 덮어쓴다 → **쉐이크 오프셋이 매 프레임 지워짐**. 흔들림이 약하거나 1프레임만 튀는 식으로 보일 가능성(실측 **확인 필요**).
- 강도: `Light 0.04 / Heavy 0.12 / Crit 0.18 m`. Crit 0.18은 근접 시점 기준 꽤 큰 폭 → 멀미/가독성 리스크.
- 별개로 `UltimateCinematicService`는 **의도적으로 회전 없는 오프셋 + Cinemachine smooth blend만 사용**(`UltimateCinematicService.cs:12` 주석 "어지러움 방지") — 멀미 방지 의식이 일부 코드에 이미 반영돼 있다(좋은 패턴).

### 1.6 코드로 단정 못 하는 항목 (확인 필요)

- 절차생성 벽/복도 콜라이더의 실제 레이어(`Wall`?).
- Ch2/Ch3/Ch4/BaseCamp 씬이 현재 런에서 실제로 로드되는지(절차생성 피벗 이후 Ch1이 베이스 런 씬인지).
- 런타임에 `CinemachineCollider`/`Fader`의 마스크가 코드로 덮여 쓰이는지(현재 그런 코드는 발견되지 않음 → 씬 값 그대로 사용으로 추정).
- 쉐이크 vs Brain 경쟁의 실제 체감 정도(플레이 테스트 필요).

---

## 2단계: 외부 리서치 (출처 명시)

### 2.1 카메라 충돌·오클루전 처리 기법

- **레이/볼륨 캐스트**: 가장 단순·일반적인 방법은 카메라→타겟으로 광선을 쏘는 것. 단일 레이는 한계가 있어, 캡슐/실린더 **볼륨 투영**(스피어/캡슐 캐스트)이 더 안정적. 두께가 있는 스피어캐스트로 근평면을 감싸는 방식이 권장됨. (Haigh-Hutchinson, *Real-Time Cameras: Navigation and Occlusion*) — 본 프로젝트의 `CameraOcclusionFader`가 이미 `SphereCast`를 쓰는 것은 정석에 부합.
- **가림 시 대응(우선순위)**: ① 장애물 따라 부드럽게 슬라이드/당기기, ② 가리는 오브젝트 **반투명/페이드**, ③ 줌인. **급격한 점프 금지** — Haigh-Hutchinson은 "좁은 통로(문 등)를 지날 때 부드러운 전환"을 핵심 제약으로 강조.
- **"플레이어는 절대 가리지 않는다" 규칙의 예외**: 카메라가 벽 사이에서 튀어 멀미를 유발하느니 **차라리 가리는 오브젝트를 투명 처리**하는 게 낫다는 실무 합의. *Hitman: Absolution*은 장애물이 시야에 들어오기 전에 카메라를 미리 슬라이드시키는 방식으로 호평. (커뮤니티/실무 사례)

### 2.2 Cinemachine 공식 해법 (본 프로젝트 2.10.5에 즉시 적용 가능)

- **`CinemachineCollider`** (2.x): `Collide Against` 레이어를 지정해야 동작. `Avoid Obstacles` 켜면 3가지 전략 —
  - **Pull Camera Forward**: Z축으로 타겟 쪽으로 당겨 가림 해소.
  - **Preserve Camera Height**: 높이 유지하며 재배치(현재 설정).
  - **Preserve Camera Distance**: 거리 유지하며 재배치.
  - 튜닝: `Camera Radius`(장애물과 유지 거리), `Minimum Occlusion Time`(과민 반응 억제), `Smoothing Time`(최근접 위치 유지로 떨림 감소), `Damping`/`Damping When Occluded`(복귀/회피 속도), `Transparent Layers`(시야 막지 않을 레이어). (Cinemachine 2.8 공식 문서)
  - 3.x에선 같은 기능이 **`CinemachineDeoccluder`**(시야 보존 당기기)와 **`CinemachineDecollider`**(최종 위치를 콜라이더 밖으로 밀기)로 분리됨 — 향후 3.x 업그레이드 시 참고.
- **`CinemachineConfiner` / `Confiner2D/3D`**: 카메라를 정해진 볼륨/경계 안에 가둠. 절차생성 방마다 bounds를 주면 카메라가 방 밖으로 못 나감.
- **스크린 쉐이크 = `CinemachineImpulse`**: 폭발/타격 같은 순간 충격은 `Noise`(연속 흔들림)가 아니라 **Impulse**(소스가 신호 발생 → vcam의 Impulse Listener가 수신)를 쓰라는 것이 Unity 공식 권장. Brain과 **합성**되므로 transform 직접 조작처럼 충돌하지 않음. (Cinemachine Impulse 공식 문서)

### 2.3 카메라 UX·게임필·접근성

- **멀미 방지**: 시각-전정 불일치가 멀미 원인. 핵심 게임플레이가 아닌 **반복적 상하/좌우 화면 움직임(카메라 바빙·과한 쉐이크)을 피하고**, 플레이어가 카메라 움직임/감도를 **커스터마이즈**할 수 있게 하라. (Microsoft Xbox Accessibility Guideline 117)
- **쉐이크 절제 + 토글**: 쉐이크 강도 슬라이더/온오프 옵션 제공이 표준. (Feel/MoreMountains 문서, MS AXG 117)
- **가독성**: 전투 중 적·텔레그래프가 보여야 함 → 가리는 지형은 페이드, 적은 절대 페이드하지 않도록 마스크 분리.

### 2.4 로그라이크/액션 사례

- **Hades(탑다운/아이소메트릭, 약간 기울인 시점)**: 사방에서 오는 적을 빠르게 파악하도록 **고정에 가까운 부감 시점 + 명확한 레이아웃 가독성** 우선. 카메라가 거의 안 흔들리고 회전이 적다 → 본 프로젝트의 "오빗 고정(5/2)"은 이 방향과 일치하므로, **회전형 추적을 더 줄이고 가독성/경계 쪽을 강화**하는 게 장르 정합적.
- **Soulslike(3인칭 락온)/DMC**: 락온 시 카메라 거리·각도를 동적 조정, 벽 근접 시 당기기 + 캐릭터/오브젝트 페이드 병행.

---

## 3단계: 종합 리포트 — 해결책

### 3.1 기술적 해결책 (우선순위순)

각 항목: **[기법 → 현재와의 갭 → 적용법(Unity/Cinemachine 구체) → 난이도/효과]**

---

#### ★ P0-1. 두 오클루전 장치의 LayerMask 채우기 (가장 큰 효과, 거의 무비용)

- **기법**: 디오클루전·페이드는 검사 대상 레이어가 있어야 동작.
- **현재 갭**: `CinemachineCollider.m_CollideAgainst = 0`, `CameraOcclusionFader.occlusionMask = 0` → 둘 다 Nothing.
- **적용법**:
  1. (사전) 절차생성 방/복도 벽 콜라이더가 `Wall` 레이어인지 확인. 아니면 블록 프리팹에서 `Wall`로 지정.
  2. `CameraOcclusionFader.occlusionMask` = `Wall`(+필요시 `Ground`/`ShapeBlock`)로 설정.
  3. `CinemachineCollider.Collide Against` = `Wall`로, `Transparent Layers` = (페이드로 처리할 레이어, 예: 데코)로 설정. `Ignore Tag`는 이미 `Player`라 양호.
  4. **둘 중 1차 선택**: 근접 부감 시점에선 카메라를 당기면 더 답답해지므로 **`CameraOcclusionFader`(페이드)를 1차**, `CinemachineCollider`는 보조(혹은 큰 지형 클리핑 방지용 `Pull Camera Forward`로만)로 운용 권장.
- **난이도**: 매우 낮음(인스펙터 값). **효과**: 매우 큼 — 벽 가림 증상 대부분 즉시 해소.

#### ★ P0-2. 가림 장치를 모든 런 씬에 일관 적용

- **기법**: 카메라 리그를 프리팹화하거나 부트스트래퍼에서 런타임 부착.
- **현재 갭**: `CinemachineCollider`/`CameraOcclusionFader`/FreeLook이 Ch1·LichTest 씬에만 존재. (Ch2~4 사용 여부 **확인 필요**.)
- **적용법**: ① Ch1을 유일 베이스 런 씬으로 확정했다면 문서화. ② 아니라면 카메라 리그를 **프리팹**으로 묶어 모든 게임 씬에 배치하거나, `GameRunBootstrapper.EnsureCameraController()`(2304행) 옆에서 `CameraOcclusionFader`/마스크를 코드로 보장. 후자는 씬별 누락을 원천 차단.
- **난이도**: 낮음~중. **효과**: 큼(씬별 회귀 방지).

#### ★ P0-3. 스크린 쉐이크를 Cinemachine Impulse로 이관

- **기법**: `CinemachineImpulseSource` + vcam에 `CinemachineImpulseListener`.
- **현재 갭**: `HitFeelService`가 `transform.localPosition` 직접 조작 → Brain이 매 프레임 덮어써 충돌.
- **적용법**: 카메라(FreeLook)에 Impulse Listener 1개 추가, `HitFeelService.CameraShake`를 Impulse 신호 발생으로 교체(`Light/Heavy/Crit` 강도를 Impulse 크기로 매핑). HitStop(timeScale) 로직은 그대로 두되 Impulse는 `IgnoreTimeScale` 옵션 검토.
- **난이도**: 중. **효과**: 큼(흔들림 정상 동작 + Brain과 합성 + 멀미 토글과 연동 쉬움).

#### P1-1. 절차생성 방 경계 — Confiner 도입

- **기법**: `CinemachineConfiner3D`(또는 2D) + 방 bounds 볼륨.
- **현재 갭**: 카메라 경계 전무 → 좁은 방/복도에서 방 밖/천장 노출·클리핑.
- **적용법**: `MapBuilder`가 방을 빌드할 때 방 외곽 bounds(BoxCollider/PolygonCollider) 1개를 함께 생성해 Confiner의 Bounding Volume으로 주입. 방 전환 시 Confiner의 bound를 교체하고 `InvalidatePathCache()`/스냅. (현 `SnapToTarget()` 흐름과 자연스럽게 결합 가능.)
- **난이도**: 중. **효과**: 큼(구조적 클리핑/이탈 차단, Hades식 "방에 갇힌" 안정적 프레이밍).

#### P1-2. Collider 튜닝 (페이드를 1차로 쓰되 보조 Collider를 둘 경우)

- **기법**: 떨림·과민 반응 억제 파라미터.
- **현재 갭**: `MinimumOcclusionTime: 0`(즉시 반응 → 잦은 흔들림 가능), 전략 PreserveCameraHeight.
- **적용법**: `Minimum Occlusion Time` 0.1~0.2, `Smoothing Time` 유지(0.2), `Damping When Occluded` 약간↑. 큰 지형만 막고 싶으면 `Pull Camera Forward`로 단순화.
- **난이도**: 낮음. **효과**: 중(흔들림/튐 감소).

#### P1-3. 수동 점유 구간의 벽 통과 방지

- **기법**: 수동 보간 경로를 Confiner bounds로 클램프, 또는 패닝 목표를 벽 안쪽으로 제한.
- **현재 갭**: `PanToZoneAndReturnAsync`/`BlendToActiveCameraAsync`가 Brain off 상태라 Collider 미적용.
- **적용법**: 보간 목표 위치 계산 후 방 bounds로 `ClosestPoint` 클램프, 또는 짧은 SphereCast로 벽 앞까지만 이동.
- **난이도**: 중. **효과**: 중(연출 중 클리핑 제거).

#### P2-1. 페이드 품질 개선 (디더/실루엣)

- **기법**: 반투명 대신 디더(스티플) 페이드나 가림 시 캐릭터 실루엣 표시.
- **현재 갭**: `hiddenAlpha 0.15` 알파 블렌딩(반투명) — 정렬/렌더큐 이슈 가능. `_BaseColor` 없는 셰이더는 스킵 처리(이미 방어 코드 있음).
- **적용법**: URP 디더링 셰이더 변형 또는 가림 시 외곽선 셰이더. 선택적.
- **난이도**: 중~높. **효과**: 중(가독성·미관).

---

### 3.2 UX 제안 (멀미·가독성·접근성)

1. **쉐이크 강도 옵션 + 토글** (MS AXG 117): 0~150% 슬라이더 + Off. `HitFeelService`의 `amplitude`에 전역 배율 적용. Crit 0.18 기본값은 100% 기준으로 다소 큼 → 0.12 정도로 하향 검토.
2. **카메라 회전/추적 절제**: SimpleFollow + Velocity 헤딩은 이동 시 카메라가 따라 도는데, 근접 부감에선 멀미 유발 가능. Hades식으로 **헤딩을 더 고정**(Recentering 비활성 유지 + 헤딩 영향 축소)하거나 데드존을 넓혀 미세 이동에 카메라가 반응하지 않게.
3. **데드존/소프트존 추가**: 현재 오빗 radius 2 고정 + 데드존 설정 미확인. FreeLook 각 rig의 Composer에 Dead Zone을 줘 작은 움직임에 카메라가 흔들리지 않게(가독성↑, 멀미↓).
4. **가독성 마스크 분리**: 페이드 대상은 지형/데코만, **적·투사체·텔레그래프는 절대 페이드하지 않도록** occlusionMask에서 제외(이미 Player는 SphereCast에서 제외 처리됨 — `CameraOcclusionFader.cs:130`).
5. **FOV/거리 접근성 옵션**: 멀미에 약한 유저용으로 약간의 FOV/거리 조절 옵션(선택).
6. **이미 잘 된 점**: 궁극기 연출이 회전 없이 오프셋+blend만 사용(`UltimateCinematicService`) — 이 원칙을 일반 연출/패닝에도 확장 권장.

---

### 3.3 우선순위 백로그

| 우선 | 항목 | 분류 | 난이도 | 효과 |
|---|---|---|---|---|
| **P0** | 두 오클루전 장치 LayerMask = `Wall` 채우기 (+벽 레이어 확인) | 출시 전 필수 | 매우 낮음 | 매우 큼 |
| **P0** | 가림 장치를 모든 런 씬에 일관 적용(프리팹/부트스트래퍼) | 출시 전 필수 | 낮음~중 | 큼 |
| **P0** | 쉐이크 → Cinemachine Impulse 이관 (+강도 토글) | 출시 전 필수 | 중 | 큼 |
| **P1** | 절차 방 Confiner(경계) 도입 | 출시 전 권장 | 중 | 큼 |
| **P1** | 수동 패닝/블렌드 구간 벽 클램프 | 출시 전 권장 | 중 | 중 |
| **P1** | Collider 튜닝(MinOcclusionTime 등) — 보조로 쓸 경우 | 개선 | 낮음 | 중 |
| **P1** | 데드존/소프트존 + 헤딩 절제(멀미·가독성) | 개선 | 낮음~중 | 중 |
| **P2** | 페이드 디더/실루엣 품질 개선 | 개선 | 중~높 | 중 |
| **P2** | FOV/거리 접근성 옵션 | 개선 | 낮음 | 소~중 |

**출시 전 반드시**: P0 3종(특히 LayerMask는 당장) + 가능하면 P1 Confiner.
**개선(출시 후 가능)**: 나머지 P1~P2.

---

## 근거 (파일:심볼 / 출처)

### 코드·씬 근거
- `Assets/RelicFairy/Systems/Camera/GameCameraController.cs` — 수동 점유/블렌드/패닝(`BlendToActiveCameraAsync` 285, `PanToZoneAndReturnAsync` 413, `SnapToTarget` 159, `ActivateForStartRoom` 224)
- `Assets/RelicFairy/Systems/Camera/CameraOcclusionFader.cs` — SphereCast 페이드(`DetectOccluders` 113), `occlusionMask` 기본 `~0`
- `Assets/RelicFairy/Systems/Camera/Ultimate/UltimateCinematicService.cs:12,170` — 회전 없는 연출(멀미 방지) + Transposer/Composer damping
- `Assets/RelicFairy/Characters/Player/Scripts/PlayerController.cs:1175` — `SetupCamera` FreeLook 바인딩
- `Assets/RelicFairy/Systems/Combat/HitFeelService.cs:30,73` — `CameraShake` transform 직접 조작(Brain과 충돌)
- `Assets/RelicFairy/Scenes/GameScenes/GameScene_Ch1.unity` — FreeLook 오빗 5/2(718), `CameraOcclusionFader.occlusionMask m_Bits:0`(830), `CinemachineCollider m_CollideAgainst m_Bits:0`(848), `Brain.DefaultBlend 2s`(748)
- 가림 장치 존재 씬: `GameScene_Ch1.unity`, `GameScene_LichTest.unity`만 (Ch2~4/BaseCamp/Tutorial 없음)
- `ProjectSettings/TagManager.asset` — `Wall`/`Ground` 레이어 존재

### 외부 출처
- Cinemachine Collider (2.8 공식): https://docs.unity3d.com/Packages/com.unity.cinemachine@2.8/manual/CinemachineCollider.html
- Cinemachine Deoccluder / Decollider (3.1): https://docs.unity3d.com/Packages/com.unity.cinemachine@3.1/manual/CinemachineDeoccluder.html · https://docs.unity3d.com/Packages/com.unity.cinemachine@3.1/manual/CinemachineDecollider.html
- Cinemachine Impulse (공식): https://docs.unity3d.com/Packages/com.unity.cinemachine@2.3/manual/CinemachineImpulse.html
- Cinemachine Noise vs Impulse: https://docs.unity3d.com/Packages/com.unity.cinemachine@3.1/manual/setup-apply-noise.html
- Mark Haigh-Hutchinson, *Real-Time Cameras: Navigation and Occlusion* (Game Developer): https://www.gamedeveloper.com/design/real-time-cameras---navigation-and-occlusion
- Haigh-Hutchinson, *Fundamentals of Real-Time Camera Design* (GDC 2005): https://media.gdcvault.com/gdc05/slides/GD_Haigh-Hutchinson_FundamentalsReal-TimeCameraDesign2.pdf
- Microsoft Xbox Accessibility Guideline 117 (멀미/카메라 옵션): https://learn.microsoft.com/en-us/gaming/accessibility/xbox-accessibility-guidelines/117
- Feel(MoreMountains) Screen Shakes: https://feel-docs.moremountains.com/screen-shakes.html
- 3인칭 카메라 충돌/페이드 실무: https://ndotl.wordpress.com/2014/10/18/third-person-camera/ · https://alfredbaudisch.com/blog/gamedev/unreal-engine-ue/unreal-engine-actors-transparent-block-camera-occlusion-see-through/
- Game Camera Setups 개요: https://pixune.com/blog/game-camera-setups/
