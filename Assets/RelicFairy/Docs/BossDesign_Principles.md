# 보스 디자인 원칙 (Boss Design Principles)

> **용도:** Project Abyss 보스 설계 시 모든 팀원이 참고하는 기준 문서  
> **근거:** 아브렐슈드·카멘 레이드 분석 + 아래 학술/산업 문헌 종합  
> **업데이트:** 2026-05-21

---

## 이론적 기반 — 참고 문헌 요약

이 문서의 원칙들은 다음 문헌에서 도출되었다.

| 문헌 | 핵심 기여 |
|------|---------|
| Pichlmair & Johansen (2020) *Designing Game Feel. A Survey* — arXiv:2011.09201 | Game Feel 3영역(Physicality·Amplification·Support) 프레임워크 |
| Lin et al. (2022) *What Features Influence Impact Feel?* — arXiv:2208.06155 | 타격감을 결정하는 3요소: 히트스톱·사운드 일관성·카메라 제어 |
| Melhárt (2018) *Towards a Comprehensive Model of Mediating Frustration in Videogames* — Game Studies 18(1) | 플레이어 좌절 중재 모델: 공정성 귀인(Attribution)이 핵심 |
| Nacke & Lindley (2010) *Affective Ludology, Flow and Immersion* — arXiv:1004.0248 | 심박수로 측정한 Flow ↔ 좌절 ↔ 몰입 관계 |
| Jennett et al. (2008) *Measuring and defining the experience of immersion in games* — ScienceDirect | 6차원 몰입 모델 (활동·게임·환경·내러티브·캐릭터·커뮤니티) |
| Fromm (2021) *Boss Engineering: Methods and Tools for Game Development* — TU München 학사논문 | 보스 설계 언어: 페이즈·액션·가중치·위협 레벨 체계 |
| Jerry Zhang (2022) *Anatomy of an Enemy Attack in Dark Souls 3* — Game Developer | 공격 5단계 분해 (Kabuki 자세 이론 기반) |
| GDKeys (n.d.) *Keys to Combat Design: Anatomy of an Attack* | 공격 타이밍 3단계 실무 가이드 |
| Game Developer *Enemy Attacks and Telegraphing* | 텔레그래핑 공정성 원칙 |
| Game Developer *Boss Battle Design and Structure* | 긴장 곡선·에스컬레이션 구조 |

---

## 1. Game Feel 3영역 프레임워크

**(Pichlmair & Johansen, 2020)**

Game Feel은 순간순간 상호작용의 감정적 영향을 의도적으로 설계하는 것이다. 세 영역으로 나뉜다.

```
┌─────────────────────────────────────────────────────┐
│  Game Feel = Physicality + Amplification + Support  │
└─────────────────────────────────────────────────────┘
         ↓ 폴리싱 행위
    Tuning       Juicing       Streamlining
  (물리감 조율)  (피드백 증폭)  (의도 실현 지원)
```

| 영역 | 목표 | 보스 적용 |
|------|------|---------|
| **Physicality → Tuning** | 응집성·예측가능성 확보. 물리적 존재감 | 보스 몸무게감, 이동 관성, 히트리액션 일관성 |
| **Amplification → Juicing** | 피드백 명확화. 중요 이벤트 강조 | 타격 히트스톱·파티클·사운드로 강도 전달 |
| **Support → Streamlining** | 플레이어 의도가 실행되게 보조 | 공격 예비동작 길이 조율로 반응 여유 확보 |

**원칙:** 세 영역 중 하나라도 무너지면 전체 Game Feel이 무너진다.

---

## 2. 공격 5단 구조 — 다크소울 3 기반

**(Jerry Zhang, 2022 — Kabuki 자세 이론)**

단순한 3단계(예비·공격·회복)보다 더 세밀하게 5단계로 분해한다.  
다크소울 3는 일본 가부키 자세 전통에서 영향 받아 각 단계를 '포즈'로 정의한다.

```
① Opening Pose   ② Attack Signal   ③ Attack   ④ End Pose   ⑤ Return
  (공격 예고)      (히트박스 활성 전)  (피격 판정)  (과신전 자세)  (기본 자세 복귀)
      ↑                  ↑                ↑              ↑             ↑
   읽기 시작         최종 회피 기회      실제 피해       반격 창        다음 패턴
```

| 단계 | 역할 | 길이 기준 | 위반 시 결과 |
|------|------|---------|------------|
| **① Opening Pose** | 어떤 공격인지 알림. 독특한 실루엣 필수 | 약공격: 0.3~0.5s / 강공격: 0.6~1.2s | 읽기 불가 → 불공정 |
| **② Attack Signal** | 히트박스 미활성 상태의 동작. 최후 회피 가능 | 0.1~0.2s | 너무 짧으면 반응 불가 |
| **③ Attack** | 실제 피격 판정. 히트박스 = 보이는 것과 일치 | 공격 종류별 상이 | 히트박스 불일치 → 불공정 분노 |
| **④ End Pose** | 과신전(over-extended) 자세. 플레이어 반격 창 | 0.3~0.6s | 너무 짧으면 반격 박탈 |
| **⑤ Return** | 기본 자세 복귀. 다음 패턴 준비 | 0.2~0.5s | 연속 공격 체이닝 시 생략 가능 (단, 명확한 리듬 필요) |

### 5단계 적용 예시 — 리치 마법볼트

```
① Opening Pose (0.8s):   두 팔을 앞으로 모음 + 손에 빛 집중
② Attack Signal (0.15s): 빛이 최대로 밝아지며 조준선 확장
③ Attack (0.3s):         마법볼트 발사 + 히트박스 활성
④ End Pose (0.4s):       팔이 약간 뒤로 밀린 자세 (과신전)
⑤ Return (0.3s):         기본 호버링 자세 복귀
```

---

## 3. 타격감 (Impact Feel) 3요소

**(Lin et al., arXiv:2208.06155, 2022)**

이 논문은 2000년대 이후 액션 게임 타격감 데이터를 실험으로 분석했다.  
**히트스톱·사운드 일관성·카메라 제어** 세 가지 중 하나라도 빠지면 타격감이 붕괴한다.

```
타격감 = 히트스톱 × 사운드 일관성 × 카메라 제어
                        (세 요소의 곱. 하나가 0이면 전체 0)
```

### 히트스톱 (Hit Stop / Hitlag)

| 타격 강도 | 권장 히트스톱 |
|---------|------------|
| 약공격 | 0.04~0.06s |
| 중공격 | 0.08~0.12s |
| 강공격·필살기 | 0.15~0.25s |
| 전멸기·Phase 전환 | 0.3~0.5s (슬로모션 연계) |

- 히트스톱 중 타격 파티클이 정지 상태에서 최대 크기로 표현됨
- 히트스톱이 없으면 타격이 "통과"하는 느낌 — 무게감 소실

### 사운드 일관성

- 공격 사운드의 **주파수 대역**이 시각 크기와 비례해야 한다
- 약공격: 높은 주파수 + 짧은 지속 / 강공격: 낮은 주파수 + 긴 여운
- 타격음이 히트스톱 시작과 **정확히 동기화**되어야 함 (오프셋 ±30ms 이내 권장)
- 충전음(예비) → 발동음(공격) → 충격음(피격) 3단계 분리

### 카메라 제어

| 연출 | 권장 수치 | 용도 |
|------|---------|------|
| **화면 쉐이크** | 진폭 0.08~0.2 / 지속 0.2~0.4s | 일반 공격 피격 |
| **강한 쉐이크** | 진폭 0.3~0.5 / 지속 0.4~0.6s | 강공격·Phase 전환 |
| **줌인** | FOV -5~-10도 / 0.3s 보간 | 전멸기·보스 등장 |
| **크로마틱 어베레이션** | 강도 0.3~0.6 / 0.5s 후 소멸 | Phase 전환 순간 |
| **슬로모션** | 0.4~0.5× 속도 / 0.3~0.8s | 보스 사망·Phase 첫 등장 |
| **비네팅** | 가장자리 어둡기 0.4~0.6 | 플레이어 위기 (체력 20% 이하) |
| **화면 플래시** | 흰색 0.08s → 즉시 소멸 | 전멸기 폭발 |

---

## 4. 애니메이션 12원칙 — 보스 전용 해석

**(Jonathan Cooper, Game Anim, 2019 — Disney 원칙 게임 재적용)**

| 원칙 | 보스 적용 | 위반 시 결과 |
|------|---------|------------|
| **Squash & Stretch** | 타격 순간 무기/몸의 형태 변형으로 에너지 표현. 딱딱한 보스는 변형 최소화 → 무게감 | 고무처럼 보이거나 무게감 소실 |
| **Anticipation** | 강한 공격일수록 더 크고 길게. NPC 공격 예비는 길수록 공정 | 읽기 불가 → 불공정 |
| **Staging** | 실루엣만으로 공격 방향·종류 판별 가능해야 함 | 공격 구분 불가 |
| **Straight Ahead / Pose to Pose** | 보스: Pose to Pose (명확한 포즈 정의) + Straight Ahead (자연스러운 보간) | 어색한 동작 |
| **Follow Through / Overlapping Action** | 공격 후 관성으로 무기·로브·체인이 계속 진행. 망토는 몸보다 늦게 반응 | 중간에 딱 멈추면 어색 |
| **Slow In / Slow Out** | 시작(slow) → 최고속(fast) → 끝(slow). 가속/감속 표현 | 균일 속도 = 로봇처럼 보임 |
| **Arcs** | 모든 스윙은 호(弧) 궤적. 직선 스윙은 기계적으로 보임 | 자연스러움 소실 |
| **Secondary Action** | 로브·망토·머리카락·체인의 물리 반응 (TheReaper: PhysicBone DLL) | 정적인 외형 = 생동감 없음 |
| **Timing** | 프레임 수 = 무게감. 강한 공격은 예비동작 프레임 수 ↑ | 빠른 대형 공격 = 속임수 |
| **Exaggeration** | 현실보다 20~40% 크게. 읽기 위해 필요한 최소 과장 | 현실적 = 읽기 불가 |
| **Solid Drawing** | 3D에서도 각 포즈가 명확한 입체 실루엣 유지 | 포즈가 납작하게 보임 |
| **Appeal** | 보스도 '인상적인 형태'를 가져야 함. 카리스마 있는 포즈 | 기억에 남지 않음 |

---

## 5. 텔레그래핑 — 다층 신호 체계

**공정성 원칙 (Game Developer, Enemy Attacks and Telegraphing):**  
"플레이어 실패는 시스템의 한계가 아닌, 플레이어 자신의 판단 결과여야 한다."

공격 예고는 반드시 **2~3가지 채널을 동시에** 사용해야 한다. 단일 신호는 인지율이 낮다.

```
신호 강도 (약 → 강)
─────────────────────────────────────────────────
채널 1: 애니메이션 예비동작  (항상 필수)
채널 2: 사운드 충전음        (약공격 이상)
채널 3: VFX 파티클          (중공격 이상)
채널 4: 색상 코드           (강공격·전멸기)
채널 5: 카메라 연출          (전멸기·Phase 전환)
채널 6: 대사/보이스          (서사적 강조 패턴)
```

| 채널 | 구현 방법 | 주의 |
|------|---------|------|
| **애니메이션** | Opening Pose의 실루엣이 공격 종류마다 고유해야 함 | 두 공격이 같은 예비동작 공유 금지 |
| **사운드** | 충전음(예비) → 발동음(공격) 두 단계 분리 | 발동음과 히트스톱 동기화 필수 |
| **VFX** | 파티클이 공격 방향 또는 히트박스 중심으로 집중 | 파티클이 방향 오해를 유발하면 역효과 |
| **색상 코드** | 이 프로젝트 통일 기준 적용 (아래 표 참고) | 전체 게임 동일 코드 유지 필수 |
| **카메라** | 전멸기 직전 0.3s 줌인. 남용 금지 | 자주 쓰면 긴장감 소실 |
| **대사** | 짧고 강렬하게. 공격 의도가 담긴 대사 | 너무 길면 패턴이 끝나버림 |

### 색상 코드 — 프로젝트 통일 기준

| 색상 | 의미 | 예시 |
|------|------|------|
| **빨강 / 주홍** | 즉시 회피 필요한 위험 공격 | Phase 2 낫 패턴, 전멸기 |
| **보라 / 남색** | 마법·저주 계열 공격 | 마법볼트, 데스레이 |
| **흰색 / 밝은 노랑** | 안전 지대 또는 상호작용 가능 오브젝트 | 피해야 할 장판 바깥 |
| **파랑 / 청록** | 무력화(카운터) 가능 상태 | 보스 무력화 게이지 노출 구간 |
| **초록** | 버프·회복 아이템 | 마나 구슬, 회복 오브 |

---

## 6. 자연스러운 방향성 — 신체 역학 원칙

### 자연스러운 공격 순서

```
눈/머리  →  몸통  →  발  →  공격 발동
(먼저 목표를 봄) (방향 전환) (발 딛음) (공격 나감)
```

### 금지 행동 (즉시 수정 대상)

| 위반 유형 | 결과 | 수정 방법 |
|---------|------|---------|
| 공격 판정 중 방향 전환 | 히트박스 ≠ 보이는 방향 → 분노 | 공격 ③단계 동안 방향 고정 |
| 예비동작 없이 순간 발동 | 읽기 불가 → 불공정 | Opening Pose 0.3s 이상 필수 |
| 회복 없이 즉각 연속 공격 | 반격 창 박탈 → 피로감 | End Pose 최소 0.2s 확보 |
| 발 방향과 다른 공격 방향 | 예측 배반 → 불신 | 공격 방향으로 발 포지셔닝 선행 |
| 히트박스가 시각 범위보다 큼 | "보이지 않는 공격에 맞음" 분노 | 히트박스 ≤ 애니메이션 범위 |

### 지상 보스 vs 공중 부유 보스 비교

| 항목 | 지상 보스 | 공중 부유 보스 (리치) |
|------|---------|-----------------|
| 방향 예고 | 발 위치·체중 이동 | **몸 기울기·팔 위치** |
| 강도 표현 | 발 딛는 힘·무릎 굽힘 | **몸통 회전 각도** |
| 이동 예고 | 발이 먼저 이동 | **몸이 먼저 기울어짐** |
| 기본 자세 | 안정적 직립 | **미세 상하 호버링 (±0.15m, 주기 2s)** |
| 돌진 예비 | 발을 뒤로 빼는 킥백 | **몸 전체가 흘러가듯 뒤로 물러남** |
| Secondary Action | 망토, 허리띠 | 망토, 체인, 팔 소매 (PhysicBone) |

---

## 7. 보스 이동 (Locomotion) 원칙

| 이동 유형 | 자연스러운 표현 | 금지 사항 |
|----------|--------------|---------|
| **전진** | 발 착지마다 약한 카메라 진동 + 몸 약간 상하 반동 | 균일 속도 직선 이동 |
| **호버링 (부유)** | 미세 상하 진동 (±0.15m, 주기 2s). 정지 시도 없음 | 완전 정지 부유 |
| **돌진** | 킥백(뒤로 빼기) 0.3s → 급가속 → 충격 착지 | 예비 없는 순간 돌진 |
| **순간이동** | 출발지 잔상 VFX (0.5s) + 도착지 충격 파티클 | 설명 없는 순간 위치 변경 |
| **방향 전환** | 호 궤적으로 자연스럽게 선회 (최소 0.2s) | 즉각 90·180도 회전 |
| **피격 히트리액션** | 맞은 방향 반대로 0.1~0.2m 밀림 + 0.1s 경직 | 피격 리액션 없음 |

---

## 8. 플레이어 좌절과 공정성 — 심리학적 근거

**(Melhárt, 2018 — Game Studies 18(1))**

플레이어는 반복 사망 시 좌절감을 **귀인(Attribution)** 으로 처리한다.

```
사망 원인을 어디에 귀인하느냐에 따라 동기가 달라진다:

  내부 귀인: "내가 못 피했다"        → 다시 도전 욕구 (건강한 좌절)
  외부 귀인: "게임이 날 죽였다"       → 분노·이탈 (독성 좌절)
```

**좌절이 건강하게 유지되는 조건:**

| 조건 | 설계 기준 |
|------|---------|
| **공정한 텔레그래핑** | 모든 공격에 읽기 가능한 예비동작 |
| **히트박스 정직성** | 시각적 범위와 판정 범위 일치 |
| **의미 있는 반격 창** | End Pose에서 반격 가능 시간 확보 |
| **패턴 학습 가능성** | 반복할수록 예측 가능해지는 패턴 구조 |
| **진전 피드백** | 보스 체력이 눈에 보이게 감소함 |

**(Nacke & Lindley, 2010 — Flow 이론):**  
높은 심박수 = 심리적 긴장/좌절. 낮은 심박수 = Flow 상태 (긍정적 몰입).  
보스 설계의 목표는 **긴장과 Flow 사이의 진동**을 반복시키는 것이다.

```
Flow Channel:

난이도 ↑
  │    / ← 보스 Phase 2 (불안)
  │   /     ← Phase 1 (Flow)
  │  /           ← 연습 구간 (권태)
  └─────────────── 실력 ↑

보스는 Phase마다 플레이어 실력 곡선보다 약간 앞서야 한다.
```

---

## 9. 보스 구조 설계 언어

**(Fromm, 2021 — TU München Boss Engineering)**

### 보스를 구성하는 단위

```
Boss
├── Attribute (HP, 속도, 방어력, Phase 임계값 등)
├── Phase[]
│   ├── Phase 1
│   │   ├── Action[] (패턴 목록)
│   │   │   ├── Action: MagicBolt  (weight: 40, threat: 2)
│   │   │   ├── Action: TeleportStrike  (weight: 25, threat: 3)
│   │   │   └── Action: DeathRay  (weight: 15, threat: 4)
│   │   └── PacingZone (패턴 간격·리듬 설계)
│   └── Phase 2
│       ├── Action[] (교체된 패턴 목록)
│       └── PacingZone
└── Transition[] (Phase 전환 조건 + 연출)
```

### 액션 속성 정의

| 속성 | 설명 | 예시 |
|------|------|------|
| **Weight (가중치)** | 해당 패턴이 선택될 확률 비중 | MagicBolt: 40 / DeathRay: 15 |
| **Threat Level (위협도)** | 1~5. 피격 시 피해량·회피 난이도 | 일반: 1~2 / 전멸기: 5 |
| **Condition (발동 조건)** | HP%, 거리, 시간, Phase 등 | HP < 40% 시 Phase 2 전환 |
| **Transition (연결)** | 이전/다음 패턴과의 자연스러운 연결 | TeleportStrike → 일반 이동으로 복귀 |

### 페이싱 존 (Pacing Zone)

보스 전투는 **압박 → 숨통 → 압박** 리듬이 명확해야 한다.

```
[압박 구간]      [숨통 구간]    [압박 구간]    [전멸기]
MagicBolt ×2  →  이동/배회  →  TeleportStrike  →  DeathRay
    ↑                ↑                ↑               ↑
 Threat 2~3     Threat 1         Threat 3         Threat 5
```

---

## 10. 긴장 곡선 (Tension Curve) 설계

**(Game Developer, Boss Battle Design and Structure)**

보스 전투의 긴장감은 건물처럼 **쌓였다가 해소**되는 리듬을 가져야 한다.

```
긴장도
  │
5 │                              ████ Phase 2 Entry (전멸기)
4 │              ████ DeathRay  │    │
3 │   ████ Tel  │    │          │    │  ████
2 │  │    │     │    │  ██      │    │  │
1 │  │    │ ██  │    │  │  ██   │    │  │  ██(해골)
0 └──┴────┴─┴───┴────┴──┴──┴────┴────┴──┴──┴───→ 시간
   진입  볼트 이동 텔포 이동 볼트 이동 Phase2  낫  소환 ...
```

### 긴장 곡선 설계 원칙

| 원칙 | 내용 |
|------|------|
| **에스컬레이션** | Phase가 바뀔수록 패턴 속도·범위·복잡도 증가 |
| **숨통 확보** | 강한 패턴 후 반드시 저위협 구간 삽입 |
| **Peak 전 예고** | 전멸기 직전에 시각·청각 신호로 긴장 최고조 |
| **보상 후 해소** | 보스 사망·Phase 전환 후 짧은 안도 구간 제공 |
| **무작위 + 패턴** | 완전 무작위 = 전략 없음. 완전 고정 = 암기 게임. 가중치 기반 랜덤 권장 |

---

## 11. 페이즈 전환 (Phase Transition) 설계

### 권장 타임라인 템플릿

```
T+0.0s   HP 임계값 도달 → 보스 공격 중단·무적 상태 진입
T+0.2s   카메라 서서히 줌인 (FOV -8도, 0.3s 보간)
T+0.5s   보스 전환 예비 모션 시작 (대표 카리스마 자세)
T+0.8s   대사 출력 시작 (대화 팝업 재생)
T+1.5s   폭발적 VFX + 크로마틱 어베레이션 (0.5s)
T+1.8s   슬로모션 구간 (0.5× 속도, 0.6s)
T+2.4s   슬로모션 종료 + 폼 체인지 완료 (실루엣 변화)
T+2.6s   환경 변화 (바닥 붕괴 등)
T+3.0s   음악 전환
T+3.5s   Phase 2 전투 시작 (첫 패턴은 Threat 1~2로 학습 기회 제공)
```

### 체크리스트

- [ ] 전환 전 플레이어에게 1초 이상 안전 시간이 있는가
- [ ] 폼 변화가 실루엣으로 명확히 구별되는가
- [ ] 대사가 전환의 서사적 의미를 전달하는가
- [ ] 새 Phase의 첫 공격이 느리게 시작해 학습 기회를 주는가
- [ ] 음악이 Phase에 맞게 변하는가
- [ ] 환경이 Phase를 시각적으로 지원하는가 (바닥 색상, 조명 변화 등)

---

## 12. 공중 부유 보스 체크리스트 (리치 전용)

이 체크리스트는 공중 부유 존재인 리치 보스에만 적용한다.

**기본 자세:**
- [ ] 기본 자세에서 미세한 호버링 상하 운동이 있는가 (±0.15m, 주기 2s)
- [ ] 로브·체인이 PhysicBone DLL로 물리 반응하는가

**이동:**
- [ ] 이동 방향으로 몸이 먼저 기우는가
- [ ] 방향 전환 시 즉각 회전이 아닌 부드러운 선회인가 (최소 0.2s)
- [ ] 이동 시 로브가 반대 방향으로 지연 반응하는가

**공격 예비:**
- [ ] 공격마다 고유한 Opening Pose가 있는가
- [ ] 공격 예비 시 몸이 공격 방향 반대로 살짝 빠지는가 (kickback)
- [ ] 강한 공격일수록 Opening Pose가 더 크고 길게 표현되는가

**히트리액션:**
- [ ] 피격 시 맞은 방향 반대로 밀리는가
- [ ] 경직 프레임이 있는가 (최소 0.1s)

---

## 13. 레이드 사례 → 리치 보스 매핑

| 레이드 기믹 | 핵심 철학 | 리치 보스 적용 |
|-----------|---------|--------------|
| **아브렐슈드 4관문** 큐브 3형태 | 폼 체인지 = 패턴풀 완전 교체 | Phase 1 (마법서) → Phase 2 (낫): 패턴 전부 교체 |
| **아브렐슈드 5관문** 전멸기 기믹 회피 | 기믹 성공 = 살아남음의 성취 | Phase 2Entry 바닥 붕괴 — 가장자리로 피하면 생존 |
| **카멘 1관문** 유기체 수집 | 필드 자원 수집 → 강화 폭발 | 마나 구슬 획득 → 방어력 버프 or 반격 데미지 증가 |
| **카멘 2관문** 장판 갯수 관리 | 방치하면 전멸하는 환경 압박 | 방치된 해골이 어둠 장판 생성, 8개 시 전멸기 |
| **카멘 3관문** 지형 파괴 | 아레나가 시간 따라 축소 | Phase 2 바닥 붕괴로 전투 공간 축소 |
| **카멘 4-2관문** 분신 식별 | 진짜 찾기 — 개인 독해 능력 | 해골 소환 시 진짜 리치 분신 혼재, 진짜만 공격 유효 |
| **다크소울 계열** 전체 | 읽기 가능 공격, 죽어도 "내 탓" | 예비동작 + 히트박스 일치 철저 준수 |

---

## 14. 공정함 자가 검증 기준

보스 패턴을 완성한 후 아래 질문에 **모두 Yes** 여야 출시 가능하다.

```
공정성 검증:
□ 모든 공격에 Opening Pose가 있는가?
□ 히트박스가 애니메이션 범위를 벗어나지 않는가?
□ End Pose에서 반격 창이 0.2s 이상 열리는가?
□ 공격 중 방향 전환이 없는가?
□ 플레이어가 사망 후 "내가 못 피했다"고 느끼는가?
□ 텔레그래핑이 2채널 이상인가?
□ Phase 전환 첫 패턴이 학습 기회를 주는가?
□ 색상 코드가 전체 게임과 일치하는가?
```

**황금 법칙:**  
플레이어는 `읽기 → 반응 → 행동` 루프가 성립할 때만 몰입한다.  
이 루프가 한 번이라도 깨지면 분노로 전환되며, 그것이 외부 귀인으로 이어지면 이탈한다.

---

## 15. 참고 문헌 전체 목록

### 학술 논문
- Pichlmair, M. & Johansen, M. (2020). *Designing Game Feel. A Survey.* IEEE COG 2021. [arXiv:2011.09201](https://arxiv.org/abs/2011.09201)
- Lin, Z. et al. (2022). *What Features Influence Impact Feel? A Study of Impact Feedback in Action Games.* [arXiv:2208.06155](https://arxiv.org/abs/2208.06155)
- Melhárt, D. (2018). *Towards a Comprehensive Model of Mediating Frustration in Videogames.* Game Studies, 18(1). [gamestudies.org](https://gamestudies.org/1801/articles/david_melhart)
- Nacke, L. & Lindley, C. (2010). *Affective Ludology, Flow and Immersion in a First-Person Shooter.* [arXiv:1004.0248](https://arxiv.org/pdf/1004.0248)
- Jennett, C. et al. (2008). *Measuring and defining the experience of immersion in games.* International Journal of Human-Computer Studies. [ScienceDirect](https://www.sciencedirect.com/science/article/abs/pii/S1071581908000499)
- Fromm, C. (2021). *Boss Engineering: Methods and Tools for Game Development.* Bachelor Thesis, TU München. [PDF](https://collab.dvb.bayern/download/attachments/77832795/BT_Clemens_Fromm_Boss_Engineering.pdf)

### 산업 문헌
- Zhang, J. (2022). [Anatomy of an Enemy Attack in Dark Souls 3.](https://www.iamjerryzhang.com/2022/07/03/anatomy-of-an-enemy-attack-in-dark-souls-3/) Game Developer.
- Cooper, J. (2019). [The 12 Principles of Animation in Video Games.](https://www.gameanim.com/2019/05/15/the-12-principles-of-animation-in-video-games/) Game Anim.
- GDKeys. [Keys to Combat Design: Anatomy of an Attack.](https://gdkeys.com/keys-to-combat-design-1-anatomy-of-an-attack/)
- Game Developer. [Enemy Attacks and Telegraphing.](https://www.gamedeveloper.com/design/enemy-attacks-and-telegraphing)
- Game Developer. [Boss Battle Design and Structure.](https://www.gamedeveloper.com/design/boss-battle-design-and-structure)
- Game Developer. [Designing for Difficulty: Readability in ARPGs.](https://www.gamedeveloper.com/game-platforms/designing-for-difficulty-readability-in-arpgs)

---

*최종 업데이트: 2026-05-21*
