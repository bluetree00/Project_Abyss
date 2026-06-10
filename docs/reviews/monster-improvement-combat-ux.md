# RelicFairy — 몬스터 개선 체크리스트 + 전투 UX 분석

작성일: 2026-06-03
참고: GDC "Juice it or lose it", Vlambeer "The art of screenshake", Game Maker's Toolkit, 사례 Hades/Dead Cells/Souls/DMC/몬스터헌터.

## Part A. 몬스터 개선 체크리스트
방향: "확률·커브 중심"(등급필터+웨이브+eliteChance+difficultyByVisit+보스전용패턴) → "데이터(SO) 조합 중심".

| # | 항목 | 현재 | 개선안 | 우선순위 | 재활용 |
|---|---|---|---|---|---|
| 1 | 적 역할/Encounter 조합 | 등급필터+웨이브 | MonsterRole enum + EncounterTemplateSO | ★★ | MonsterConfigSO role 필드 |
| 2 | 엘리트/어픽스 | eliteChance만 | MonsterModifierSO + 공정성 레일 | ★★ | StatModifier 패턴 |
| 3 | 텔레그래프/가독성 | 보스만 패턴 | windup/active/recovery 표준 + 검사기 | ★★★ | 보스 패턴 SO |
| 4 | AI 의사결정 | 타입키 FSM | FSM 유지 + 결정 지점만 유틸리티 스코어링 | ★★ | MonsterFSM/Context |
| 5 | 그룹 조율 | 개별 독립(클럼핑) | 공격 토큰 + 간격 패널티(중앙 플래너 없이) | ★★ | AttackReady |
| 6 | 절차 스폰/페이싱 | difficultyByVisit+웨이브 | 스폰 크레딧 예산 + 인텐시티 디렉터 | ★★ | difficultyByVisit 커브 |
| 7 | 보스 패턴 일반화 | 보스 전용 | 코어 추출→엘리트 재사용(잡몹 전체는 과함) | ★ | BossPatternRunner |
| 8 | MonsterBase(~933L) 분해 | 초기화+전투 혼재 | 초기화/로딩 ↔ 전투 컴포넌트 분리 | ★★(토대) | 메서드 추출 |

종합 우선순위: 텔레그래프 표준+검사기 → MonsterBase 분해 → EncounterTemplate/Role → 공격 토큰.
과설계 경계: GOAP/HTN 전면, 풀 boids, 중앙 스쿼드 플래너, 모든 잡몹 다패턴, 유틸리티 전면 이행.

## Part B. 전투 UX 시스템 분석
진단: 게임필 엔진(히트스톱·넉백·슈퍼아머·입력버퍼)은 강한데, 플레이어에게 보이는 출력단이 얇다.

- B-1 타격 피드백(★★★): 히트스톱 양측 표준화, 머티리얼 플래시, 풀링 데미지 넘버. EffectManager 훅 재활용.
- B-2 가독성(★★★): A-#3 텔레그래프 연동(windup 표식), 슈퍼아머/경직 외형 신호, 처치 임박 신호.
- B-3 플레이어 상태(★★): 피격 방향 인디케이터+임팩트 비네트, 저HP 비네트(URP Volume), 닷지 i-frame 잔상.
- B-4 타겟팅 보조(★★): 선택적 소프트 락온, 근접 소프트 에임, 어그로 표식. Cinemachine 타겟 그룹.
- B-5 접근성(★★, 가성비 큼): 쉐이크/슬로우 강도 토글(현재 항상 켜짐=Xbox 가이드라인 위반), 색약 팔레트, 텔레그래프 오디오 대체. 전역 강도 계수 1개로 광범위 적용.
- B-6 게임필(★★): 입력버퍼 있음, 애님캔슬 윈도/코요테타임 데이터화, 피드백 동기화.

## 확인 필요 8항목
1. 데미지 넘버 표시 2. 잡몹 체력바 3. 피격 플래시 4. 방향표시/저HP 비네트 5. 닷지 i-frame 값 6. 락온/소프트에임 7. 코요테타임/애님캔슬 8. 피드백 동기화

## 통합 권장 순서
①텔레그래프 표준+검사기(몬스터+UX 동시) →②타격 피드백 출력단 →③MonsterBase 분해 →④접근성 토글 →⑤Encounter/공격토큰.
