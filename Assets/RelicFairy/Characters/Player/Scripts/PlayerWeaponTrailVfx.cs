using UnityEngine;
using INab.Common;

/// <summary>
/// 검 공격/대시 시 칼날 트레일(INab Weapon Trail) 시각 연출 구동기.
///
/// PlayerController가 런타임에 자동 부착한다(DodgePresentation과 동일 패턴 — 프리팹/씬 수동 배선 불가 환경 대응).
/// 트레일 프리팹 참조는 데이터에서 읽는다:
///   - 공격: 현재 무기의 <see cref="WeaponData.trailVfxPrefab"/> (무기 종류별 색/모양)
///   - 대시: <see cref="CharacterData.dashTrailVfxPrefab"/>
/// 미할당 시 해당 상황 트레일만 무동작(참조 0 → NRE 0 → 현행 전투 회귀 0).
///
/// 구조: WeaponTrailEffect 단일 인스턴스 + 프리팹/앵커 스왑. 공격과 대시는 동시 발생하지 않으므로 충돌 없음.
/// 앵커:
///   - 공격: 무기 프리팹의 <see cref="WeaponInstance.tipPoint"/>/<see cref="WeaponInstance.rootPoint"/>(칼날 — 히트 판정과 공유, 위치만 사용해 무해)
///   - 대시: 플레이어 루트 자식 몸 앵커(상/하단). 칼날이 아닌 캐릭터 몸을 따라간다. 오프셋은 CharacterData에서 튜닝.
///
/// 타이밍 신호:
///   - 공격: 애니메이션 이벤트 AE_BeginTrail/AE_EndTrail → PlayerAnimationEventReceiver.OnBeginTrail/OnEndTrail
///   - 대시: PlayerController.OnDodgeStart/OnDodgeEnd
/// </summary>
[DisallowMultipleComponent]
public class PlayerWeaponTrailVfx : MonoBehaviour
{
    // ── SerializeField (튜닝값 — 런타임 자동 부착이라 기본값으로 동작) ──────────
    [Header("공격 트레일")]
    [SerializeField] private float attackFadeIn = 0.05f;
    [SerializeField] private float attackFadeOut = 0.15f;
    [SerializeField] private float attackTrailLength = 0.35f;

    [Header("대시 트레일")]
    [SerializeField] private float dashFadeIn = 0.04f;
    [SerializeField] private float dashFadeOut = 0.12f;
    [SerializeField] private float dashTrailLength = 0.35f;

    // ── Private (refs, Awake 캐싱) ─────────────────────────────────
    private PlayerController _controller;
    private CharacterData _data;
    private PlayerWeaponManager _weaponMgr;
    private PlayerAnimationEventReceiver _receiver;
    private WeaponTrailEffect _trail;

    // ── Private (현재 컨텍스트) ───────────────────────────────────
    private GameObject _weaponTrailPrefab;   // 현재 무기의 공격 트레일 프리팹
    private Transform _tip;                   // 현재 무기 칼끝(공격 앵커)
    private Transform _root;                  // 현재 무기 칼밑(공격 앵커)
    private Transform _dashTip;               // 몸 상단(대시 앵커)
    private Transform _dashRoot;              // 몸 하단(대시 앵커)
    private GameObject _loadedPrefab;         // 현재 인스턴스화돼 있는 프리팹(스왑 판정용)
    private bool _subscribed;

    // ── Lifecycle ─────────────────────────────────────────────────
    private void Awake()
    {
        _controller = GetComponent<PlayerController>();
        _data = _controller != null ? _controller.CharacterData : null;

        if (!TryGetComponent(out _trail))
            _trail = gameObject.AddComponent<WeaponTrailEffect>();

        // 수동 API 모드 — 에디터 프리뷰/애니메이터 구동 비활성.
        _trail.trailUsageType = WeaponTrailEffect.TrailUsageType.Manual;
        _trail.useEvents = false;
        _trail.enableGizmos = false;

        CreateDashAnchors();
    }

    /// <summary>대시 트레일용 몸 앵커(상/하단)를 플레이어 루트 자식으로 생성한다(칼날이 아닌 몸을 따라감).</summary>
    private void CreateDashAnchors()
    {
        Vector3 topOffset = _data != null ? _data.dashTrailTopOffset : new Vector3(0f, 1.3f, 0f);
        Vector3 bottomOffset = _data != null ? _data.dashTrailBottomOffset : new Vector3(0f, 0.05f, 0f);

        _dashTip = new GameObject("~DashTrailTop").transform;
        _dashTip.SetParent(transform, false);
        _dashTip.localPosition = topOffset;

        _dashRoot = new GameObject("~DashTrailBottom").transform;
        _dashRoot.SetParent(transform, false);
        _dashRoot.localPosition = bottomOffset;
    }

    private void OnEnable()
    {
        if (_controller == null) return;

        _weaponMgr = _controller.WeaponManager;
        _receiver = _controller.EventReceiver;

        if (_subscribed) return;

        _controller.OnDodgeStart += HandleDodgeStart;
        _controller.OnDodgeEnd += HandleDodgeEnd;

        if (_weaponMgr != null)
            _weaponMgr.OnWeaponChanged += HandleWeaponChanged;
        if (_receiver != null)
        {
            _receiver.OnBeginTrail += HandleBeginAttackTrail;
            _receiver.OnEndTrail += HandleEndAttackTrail;
        }

        _subscribed = true;

        // 부착 시점에 이미 무기가 장착돼 있으면 초기 동기화.
        if (_weaponMgr != null && _weaponMgr.HasWeapon)
            HandleWeaponChanged(_weaponMgr.CurrentWeaponData, _weaponMgr.CurrentWeaponInstance);
    }

    private void OnDisable()
    {
        if (_controller != null)
        {
            _controller.OnDodgeStart -= HandleDodgeStart;
            _controller.OnDodgeEnd -= HandleDodgeEnd;
        }
        if (_weaponMgr != null)
            _weaponMgr.OnWeaponChanged -= HandleWeaponChanged;
        if (_receiver != null)
        {
            _receiver.OnBeginTrail -= HandleBeginAttackTrail;
            _receiver.OnEndTrail -= HandleEndAttackTrail;
        }
        _subscribed = false;

        // 씬 언로드/오브젝트 파괴 중 OnDisable이면 StopTrail의 페이드 코루틴을 띄울 수 없다
        // (INab StopTrail은 항상 StartCoroutine 경로 → 파괴 중 오브젝트에서 "inactive" 경고).
        // 씬 언로드 중에는 activeInHierarchy가 true라 isActiveAndEnabled로는 못 거른다 →
        // scene.isLoaded로 판정한다(언로드 중이면 false). 그 경우 트레일은 어차피 사라지므로 스킵.
        // 실제 게임 중 컴포넌트만 비활성화될 때만(scene 로드됨) 정상 페이드 종료.
        if (_trail != null && _trail.isActiveAndEnabled && gameObject.scene.isLoaded)
            _trail.StopTrail(0.001f);
    }

    // ── Private Methods ───────────────────────────────────────────
    /// <summary>
    /// 프리팹을 인스턴스화하고 현재 무기 앵커에 바인딩한다. 준비되면 true.
    /// ⚠️ 반드시 "안전한 컨텍스트"(무기 교체/대시 시작·종료)에서만 호출.
    /// 애니메이션 이벤트 콜백 중에는 호출 금지 — INab _InstantiateTrailPrefab이
    /// 에디터 플레이모드에서 즉시 파괴(Undo.DestroyObjectImmediate)를 시도해 예외 발생.
    /// </summary>
    private bool LoadPrefab(GameObject prefab, Transform tip, Transform root)
    {
        if (_trail == null || prefab == null) return false;
        if (tip == null || root == null) return false;   // 앵커 없음 → 스킵

        _trail.lineTipTransform = tip;
        _trail.lineBottomTransform = root;
        _trail.SetNewTrailPrefab(prefab);   // 인스턴스화 + 앵커 바인딩
        _loadedPrefab = prefab;
        return true;
    }

    // ── Event Handlers ────────────────────────────────────────────
    private void HandleWeaponChanged(WeaponData data, GameObject instance)
    {
        _weaponTrailPrefab = data != null ? data.trailVfxPrefab : null;

        WeaponInstance wi = instance != null ? instance.GetComponent<WeaponInstance>() : null;
        _tip = wi != null ? wi.tipPoint : null;
        _root = wi != null ? wi.rootPoint : null;

        // 안전 컨텍스트 — 공격 트레일 프리팹을 무기 앵커에 미리 인스턴스화/바인딩.
        // (애니 이벤트 시점엔 인스턴스화 불가하므로 여기서 선로딩)
        if (!LoadPrefab(_weaponTrailPrefab, _tip, _root))
        {
            _loadedPrefab = null;
            if (_trail != null) _trail.StopTrail(0.001f);
        }
    }

    private void HandleBeginAttackTrail()
    {
        // 애니 이벤트 — 인스턴스화 금지. 무기 프리팹이 선로딩돼 있을 때만 시작.
        if (_trail == null || _weaponTrailPrefab == null) return;
        if (_loadedPrefab != _weaponTrailPrefab) return;
        _trail.StartTrailWithLength(attackFadeIn, attackTrailLength);
    }

    private void HandleEndAttackTrail()
    {
        if (_trail != null) _trail.StopTrail(attackFadeOut);
    }

    private void HandleDodgeStart()
    {
        // 대시 시작은 상태머신(Update) 경로 — 안전 컨텍스트라 프리팹 스왑 가능.
        // 칼날이 아닌 몸 앵커(상/하단)에 바인딩해 캐릭터를 따라가게 한다.
        GameObject dashPrefab = _data != null ? _data.dashTrailVfxPrefab : null;
        if (LoadPrefab(dashPrefab, _dashTip, _dashRoot))
            _trail.StartTrailWithLength(dashFadeIn, dashTrailLength);
    }

    private void HandleDodgeEnd()
    {
        if (_trail != null) _trail.StopTrail(dashFadeOut);

        // 안전 컨텍스트에서 공격용(무기 앵커) 프리팹으로 복원 —
        // 대시는 몸 앵커를 쓰므로 종료 시 무기 앵커/프리팹으로 되돌려 다음 공격 선로딩 상태를 만든다.
        LoadPrefab(_weaponTrailPrefab, _tip, _root);
    }
}
