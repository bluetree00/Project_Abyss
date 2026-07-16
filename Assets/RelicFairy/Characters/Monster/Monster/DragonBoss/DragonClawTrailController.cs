using INab.Common;
using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// 드래곤 보스 발톱 할퀴기(ClawAttackL/R) 애니메이션 구간 동안 좌우 발톱에 WeaponTrailEffect를 재생한다.
/// 패턴 코드(DragonClawSlashState)에서 StartTrail/StopTrail을 직접 호출한다 (Manual 모드).
/// </summary>
public class DragonClawTrailController : MonoBehaviour
{
    // ── Inspector ─────────────────────────────────────
    [Header("트레일 프리팹")]
    [Tooltip("좌우 발톱 공용 트레일 VFX 프리팹 (예: Claws Fire 1)")]
    [SerializeField] private GameObject _trailPrefab;

    [Header("발톱 앵커 (미할당 시 본 이름으로 자동 탐색)")]
    [Tooltip("왼쪽 발톱 끝 본 (기본: L Finger11)")]
    [SerializeField] private Transform _leftClawTip;
    [Tooltip("왼쪽 손바닥 본 (기본: L Hand)")]
    [SerializeField] private Transform _leftClawRoot;
    [Tooltip("오른쪽 발톱 끝 본 (기본: R Finger11)")]
    [SerializeField] private Transform _rightClawTip;
    [Tooltip("오른쪽 손바닥 본 (기본: R Hand)")]
    [SerializeField] private Transform _rightClawRoot;

    [Header("트레일 파라미터")]
    [SerializeField] private float _trailFadeIn  = 0.05f;
    [SerializeField] private float _trailFadeOut = 0.15f;
    [SerializeField] private float _trailLength  = 0.4f;

    // ── 런타임 ──────────────────────────────────────────
    private WeaponTrailEffect _leftTrail;
    private WeaponTrailEffect _rightTrail;
    private bool _leftPrefabLoaded;
    private bool _rightPrefabLoaded;

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // Lifecycle
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private void Awake()
    {
        if (_leftClawTip   == null) _leftClawTip   = FindDeep(transform, "L Finger11");
        if (_leftClawRoot  == null) _leftClawRoot  = FindDeep(transform, "L Hand");
        if (_rightClawTip  == null) _rightClawTip  = FindDeep(transform, "R Finger11");
        if (_rightClawRoot == null) _rightClawRoot = FindDeep(transform, "R Hand");

        _leftTrail  = CreateTrailGO("~LeftClawTrail",  _leftClawTip,  _leftClawRoot);
        _rightTrail = CreateTrailGO("~RightClawTrail", _rightClawTip, _rightClawRoot);
    }

    private void OnEnable()
    {
        _leftPrefabLoaded  = false;
        _rightPrefabLoaded = false;
        if (_leftTrail  != null && _leftTrail.isActiveAndEnabled)  _leftTrail.StopTrail(0.001f);
        if (_rightTrail != null && _rightTrail.isActiveAndEnabled) _rightTrail.StopTrail(0.001f);
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 공개 API
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    public void StartTrail(bool isLeft)
    {
        if (isLeft) PlayTrail(_leftTrail,  ref _leftPrefabLoaded);
        else        PlayTrail(_rightTrail, ref _rightPrefabLoaded);
    }

    public void StopTrail(bool isLeft)
    {
        if (isLeft) _leftTrail?.StopTrail(_trailFadeOut);
        else        _rightTrail?.StopTrail(_trailFadeOut);
    }

    public void StopAll()
    {
        _leftTrail?.StopTrail(_trailFadeOut);
        _rightTrail?.StopTrail(_trailFadeOut);
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 내부
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private void PlayTrail(WeaponTrailEffect trail, ref bool prefabLoaded)
    {
        if (trail == null) return;
        if (_trailPrefab != null && !prefabLoaded)
        {
            trail.SetNewTrailPrefab(_trailPrefab);
            prefabLoaded = true;
        }
        trail.StartTrailWithLength(_trailFadeIn, _trailLength);
    }

    private WeaponTrailEffect CreateTrailGO(string goName, Transform tip, Transform root)
    {
        var go = new GameObject(goName);
        go.transform.SetParent(transform, false);
        var trail = go.AddComponent<WeaponTrailEffect>();
        trail.trailUsageType      = WeaponTrailEffect.TrailUsageType.Manual;
        trail.useEvents           = false;
        trail.enableGizmos        = false;
        trail.lineTipTransform    = tip;
        trail.lineBottomTransform = root;
        return trail;
    }

    private static Transform FindDeep(Transform root, string name)
    {
        foreach (Transform child in root)
        {
            if (child.name == name) return child;
            var found = FindDeep(child, name);
            if (found != null) return found;
        }
        return null;
    }
}
}
