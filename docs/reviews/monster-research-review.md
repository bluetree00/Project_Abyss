# RelicFairy 몬스터 시스템 — 외부 리서치 + 비교 코드리뷰

작성일: 2026-06-03
사례: Hades, Dead Cells, Hollow Knight, Enter the Gungeon, Risk of Rain 2, Binding of Isaac, Soulslike, DOOM(2016), F.E.A.R., Left 4 Dead, Halo.

## 0. 핵심 결론
진짜 격차는 "개별 적 AI의 정교함"이 아니라, 적을 묶고·곱하고·조율하는 상위 시스템 층이 얇다는 점. RelicFairy 뼈대(타입키 FSM, DI 컨텍스트, stateless 상태 + 런타임 데이터 집중, BossPatternRunner, 데이터 주도 3단 폴백)는 업계 베스트프랙티스와 구조적으로 정합 → 대부분 재작성이 아니라 데이터 모델 추가 + 기존 자산 재활용으로 가능.

## 1. 리서치 핵심 통찰 5가지
1. 재미는 똑똑한 적이 아니라 "역할이 직교적인 적들의 비동기 조합"에서 나온다.
2. 그룹 AI 목표는 협동 연출이 아니라 클럼핑·체크메이트 방지. 공격 토큰+간격 패널티+바크면 충분(F.E.A.R.).
3. 엘리트/어픽스는 가장 값싼 콘텐츠 곱셈기(N×M). 단 공정성 레일(비용 가중·조합 제한·가독) 동반(RoR2/D3/PoE).
4. 텔레그래프(예비→공격→후딜)는 폴리시가 아니라 공정성 요구. 보스→엘리트→몹 일반화.
5. 절차 페이싱엔 두 엔진 — 크레딧/예산(얼마나) + 인텐시티 디렉터(언제, L4D식).

## 2. 항목별 비교 (요지)
- 적 역할/Encounter: 현재 등급필터+웨이브 → MonsterRole + EncounterTemplateSO.
- 엘리트/어픽스: eliteChance만 → MonsterModifierSO + 공정성 레일.
- 텔레그래프: 보스만 → windup/active/recovery 표준 + 검사기.
- AI: 타입키 FSM 유지 + 결정 지점만 유틸리티 스코어링.
- 그룹: 공격 토큰 + 간격 패널티(중앙 플래너 없이).
- 절차 스폰: difficultyByVisit → 크레딧 예산 + 인텐시티 디렉터.
- 보스 패턴: 코어 추출해 엘리트 재사용(잡몹 전체는 과함).

## 3. 우선순위 백로그
- P0: MonsterRole + EncounterTemplateSO / 엘리트·어픽스 모디파이어.
- P1: 텔레그래프 표준+검사기 / MonsterBase 분해 / FSM 위 유틸리티 결정 레이어.
- P2: 스폰 크레딧 예산+인텐시티 / 공격 토큰+공간 유틸리티 / BossPatternRunner 일반화.
- P3: 전투 바크 / 적 풀 바이옴 스코프.

## 4. 보존할 강점
타입키 FSM + stateless + MonsterRuntimeData, MonsterContext DI, SO 인스턴스화+stat_version 오버라이드, BossPatternRunner/Blackboard/ICondition, 3단 폴백.

## 5. 과설계 경계
GOAP/HTN 전면, 풀 boids, 중앙 스쿼드 플래너, 모든 잡몹 다패턴, 유틸리티 전면 이행.
