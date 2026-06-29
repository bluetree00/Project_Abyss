using UnityEngine;
using INab.Common;

/// <summary>
/// 검 공격 시 칼날 트레일(INab Weapon Trail) 시각 연출 구동기.
///
/// PlayerController가 런타임에 자동 부착한다(DodgePresentation과 동일 패턴 — 프리팹/씬 수동 배선 불가 환경 대응).
/// 트레일 프리팹은 현재 무기의 <see cref="WeaponData.trailVfxPrefab"/>에서 읽는다(무기 종류별 색/모양).
/// 미할당 시 무동작(참조 0 → NRE 0 → 현행 전투 회귀 0).
///
/// 구조: WeaponTrailEffect 단일 인스턴스. 칼끝/칼밑 앵커는 무기 프리팹의
/// <see cref="WeaponInstance.tipPoint"/>/<see cref="WeaponInstance.rootPoint"/>를 재사용(히트 판정과 공유 — 위치만 사용해 무해).
/// 타이밍: 애니메이션 이벤트 AE_BeginTrail/AE_EndTrail → PlayerAnimationEventReceiver.OnBeginTrail/OnEndTrail.
///
/// 인스턴스화(SetNewTrailPrefab)는 무기 교체(OnWeaponChanged) 안전 컨텍스트에서만 — 애니 이벤트 콜백 중엔
/// INab _InstantiateTrailPrefab의 즉시 파괴가 금지되므로 호출 금지. 공격 애니 이벤트에선 선로딩분에 Start/Stop만.
///
/// 참고: 대시(구르기) 트레일은 이 컴포넌트가 아니라 DodgePresentation의 TrailRenderer가 담당한다(직선 모션에 최적).
/// </summary>
[DisallowMultipleComponent]
public class PlayerWeaponTrailVfx : MonoBehaviour
{
    // ── SerializeField (튜닝값 — 런타임 자동 부착이라 기본값으로 동작) ──────────
    [Header("공격 트레일")]
    [SerializeField] private float attackFadeIn = 0.05f;
    [SerializeField] private float attackFadeOut = 0.15f;
    [SerializeField] private float attackTrailLength = 0.35f;

    // ── Private (refs, Awake 캐싱) ─────────────────────────────────
    private PlayerController _controller;
    private PlayerWeaponManager _weaponMgr;
    private PlayerAnimationEventReceiver _receiver;
    private WeaponTrailEffect _trail;

    // ── Private (현재 컨텍스트) ───────────────────────────────────
    private GameObject _weaponTrailPrefab;   // 현재 무기의 공격 트레일 프리팹
    private Transform _tip;                   // 현재 무기 칼끝
    private Transform _root;                  // 현재 무기 칼밑
    private GameObject _loadedPrefab;         // 현재 인스턴스화돼 있는 프리팹
    private bool _subscribed;

    // ── Lifecycle ─────────────────────────────────────────────────
    private void Awake()
    {
        _controller = GetComponent<PlayerController>();

        if (!TryGetComponent(out _trail))
            _trail = gameObject.AddComponent<WeaponTrailEffect>();

        // 수동 API 모드 — 에디터 프리뷰/애니메이터 구동 비활성.
        _trail.trailUsageType = WeaponTrailEffect.TrailUsageType.Manual;
        _trail.useEvents = false;
        _trail.enableGizmos = false;
    }

    private void OnEnable()
    {
        if (_controller == null) return;

        _weaponMgr = _controller.WeaponManager;
        _receiver = _controller.EventReceiver;

        if (_subscribed) return;

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
        if (_weaponMgr != null)
            _weaponMgr.OnWeaponChanged -= HandleWeaponChanged;
        if (_receiver != null)
        {
            _receiver.OnBeginTrail -= HandleBeginAttackTrail;
            _receiver.OnEndTrail -= HandleEndAttackTrail;
        }
        _subscribed = false;

        // 비활성/파괴 중에는 StopTrail(코루틴)을 쓰면 'inactive GameObject' 경고 발생.
        // 코루틴 없이 VFX를 즉시 끈다(트레일 인스턴스는 오브젝트와 함께 사라짐).
        if (_trail != null)
        {
            _trail.SetProperty_EffectActive(false);
            _trail.SetProperty_EffectAlive(0f);
        }
    }

    // ── Private Methods ───────────────────────────────────────────
    /// <summary>
    /// 무기 트레일 프리팹을 칼날 앵커에 인스턴스화/바인딩한다. 준비되면 true.
    /// ⚠️ 무기 교체(안전 컨텍스트)에서만 호출 — 애니 이벤트 중 호출 시 에디터 즉시파괴 예외.
    /// </summary>
    private bool LoadPrefab(GameObject prefab)
    {
        if (_trail == null || prefab == null) return false;
        if (_tip == null || _root == null) return false;   // 무기/앵커 없음 → 스킵

        _trail.lineTipTransform = _tip;
        _trail.lineBottomTransform = _root;
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

        // 안전 컨텍스트 — 공격 트레일 프리팹을 미리 인스턴스화/바인딩(애니 이벤트 시점엔 인스턴스화 불가).
        if (!LoadPrefab(_weaponTrailPrefab))
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
}
