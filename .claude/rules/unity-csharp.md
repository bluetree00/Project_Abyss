---
paths: ["**/*.cs"]
description: Unity C# 코드 작성/수정 시 자동 적용되는 필수 규칙
---

# Unity C# 코드 체크리스트 (MUST CHECK)

코드를 작성하거나 수정하기 전에 아래 항목을 반드시 확인한다.
위반 시 런타임 버그, 성능 저하, 에디터 오류가 발생한다.

## 직렬화
- `[SerializeField] private` only — public 필드로 Inspector 노출 금지
- 외부 접근은 public read-only 프로퍼티로 제공
- `[Header("카테고리")]`로 Inspector 정리

## 퍼포먼스
- `Awake()`/`Start()`에서 컴포넌트 참조 캐싱
- `Update`/`FixedUpdate`/`LateUpdate`에서 `GetComponent`, `FindObjectOfType`, `GameObject.Find` 절대 금지
- Update 루프에서 `new` 힙 할당, 문자열 `+` 접합 금지
- `TryGetComponent<T>()` 사용 (null 가능 시)

## 비동기
- `UniTask` 사용 (코루틴 금지)
- `CancellationToken` 항상 전달
- `OperationCanceledException` catch 필수
- 토큰을 오브젝트 수명/씬 수명에 연결

## 이벤트
- C# `event Action` 기반 (UnityEvent 지양)
- `OnEnable`에서 구독, `OnDisable`에서 해제
- `OnDestroy`에서 정리 (tween, pool 반환 등)

## 클래스 구조
```
Constants → Static → [SerializeField] → Private fields → Properties
→ Lifecycle (Awake → OnEnable → Start → Update → FixedUpdate → LateUpdate → OnDisable → OnDestroy)
→ Public Methods → Private Methods → Event Handlers
```

## Unity 고유
- `.meta` 파일 직접 생성/수정/삭제 절대 금지
- 백그라운드 스레드에서 Unity API 호출 금지
- ScriptableObject에 `[CreateAssetMenu]` 필수, 런타임 상태 저장 금지
- Animator 새 상태: `writeDefaultValues = false` 필수
- 리소스 로드: `AddressableManager` 경유 (Resources.Load 금지)

## 이 프로젝트 전용
- UI 데이터: `Provider → Presenter → View` 3단 구조
- 서비스 접근: `Managers.Instance`
- 씬/오브젝트 조작: MCP HTTP 호출만 사용
