# RelicFairy — 코드 리뷰 (아키텍처 · 디자인 패턴 · 개선 백로그)

작성일: 2026-06-03
대상: Assets/RelicFairy 중심. 근거: 직접 확인한 파일:심볼 기준(전 저장소 자동 스캔은 대용량 디렉터리에서 행이 걸려 검증된 코드 토대로 작성).

## 0. 총평
데이터 주도 + 패턴 기반 확장성이 잘 잡힌 중대형 로그라이크. 비동기/리소스/이벤트 규약 일관, FSM·팩토리·전략·토큰 레지스트리가 콘텐츠 추가를 쉽게 함. 최대 부채는 소수 거대 클래스 + 전환기 레거시 공존 + 세이브 부재. 구조 재설계는 불필요, 관심사 분리 리팩터링으로 충분.

## 1. 아키텍처
강점: 명확한 부트 순서·수명 관리, 상태/런타임 데이터 분리, 견고한 3단 폴백, 일관된 UniTask/Addressables, 이벤트 주도 UI.
약점: 서비스 로케이터(Managers.Instance) 전역 결합으로 의존이 코드에 안 드러나고 테스트 어려움 / DDOL 싱글톤 다수의 초기화 순서 암묵 의존 / UI Presenter가 도메인 변화 집결지로 비대 / 전환기 이중 경로 인지 부하 / 세이브·이어하기 부재.

## 2. 디자인 패턴
타입키 FSM, DI 컨텍스트, 팩토리(RelicRegistry/특수상태), Reflection 토큰 레지스트리(개방-폐쇄 우수), 전략(Ability/Passive), Provider→Presenter→View, 레이어드 스탯 — 적절·일관. 개선: RelicClassSO.Passives 인터페이스 불일치(수정됨), Q 시네마틱 출처 불일치(수정됨) — 패턴 인터페이스 불일치가 만든 조용한 버그 사례.

## 3. 코드 품질
- 거대 클래스: PlayerController(~1357L), MonsterBase(~933L), HudPresenter(~470L), MerlinRuneBridge(~670L) — 책임 분할.
- 에러 처리: DataManager가 로깅 후 진행 — 3단 폴백 모두 실패 시 UX 미정의.
- 매직넘버 상수화, Update 경로 점검(WispController OverlapSphere 등), Presenter 구독 누수 위험.
- 테스트 가능성: RunSequencer·ZoneClusterCalculator·PlayerRuntimeStats 등 순수 로직부터 테스트 작성 좋음.

## 4. 리팩터링 백로그
- 지금: 레거시 정리 마무리 / 순수 로직 단위 테스트 / 유물 마이그레이션 완료.
- 다음: PlayerController·HudPresenter·MonsterBase 책임 분할.
- 나중: 세이브 시스템 / 방버프 완전 이관 / MerlinRune 레이아웃 외부화 / (선택)서비스 로케이터 의존 명시화.

## 5. 몬스터(요지)
타입키 FSM + DI 컨텍스트 + 데이터 오버라이드 토대 견고. 개선은 별도 문서(monster-research-review.md, monster-improvement-combat-ux.md) 참조.
