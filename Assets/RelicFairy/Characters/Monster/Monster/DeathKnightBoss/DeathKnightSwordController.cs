using System.Threading;
using Cysharp.Threading.Tasks;
using INab.Common;
using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// DeathKnight 보스 검 출현/소멸 컨트롤러.
///
/// 패턴 시작 → ShowSword()  : 디졸브로 등장
/// 패턴 종료 → HideSword()  : 디졸브로 소멸 후 비활성화
///
/// 검은 기본적으로 오른손 Bone_Sword에 붙어있다.
/// 인스펙터 미할당 시 Awake에서 "SM_DarkKnight2_Sword" 이름으로 자동 탐색.
///
/// 검 트레일은 INab Weapon Trail(VFX Graph) 사용 — 흰색 검=성속성, 검은색 검=암속성 프리팹을
/// <see cref="SetSwordColor"/>에서 스왑한다(플레이어 측 PlayerWeaponTrailVfx와 동일 패턴).
/// </summary>
public class DeathKnightSwordController : MonoBehaviour
{
    // ── Inspector ─────────────────────────────────────
    [Header("검 오브젝트")]
    [Tooltip("SM_DarkKnight2_Sword GameObject. 미할당 시 자동 탐색.")]
    [SerializeField] private GameObject _swordGO;

    [Header("디졸브 시간")]
    [Tooltip("등장 디졸브 지속 시간 (초)")]
    [SerializeField] private float _appearDuration  = 0.3f;
    [Tooltip("소멸 디졸브 지속 시간 (초)")]
    [SerializeField] private float _disappearDuration = 0.25f;

    [Header("검 색상 머티리얼")]
    [Tooltip("흰색 검 머티리얼")]
    [SerializeField] private Material _whiteMaterial;
    [Tooltip("검은색 검 머티리얼")]
    [SerializeField] private Material _blackMaterial;

    [Header("검 트레일 (INab Weapon Trail VFX)")]
    [Tooltip("흰색 검 트레일 프리팹 (성속성, 예: Holy 1/2)")]
    [SerializeField] private GameObject _holyTrailPrefab;
    [Tooltip("검은색 검 트레일 프리팹 (암속성, 예: Dark 1)")]
    [SerializeField] private GameObject _darkTrailPrefab;
    [Tooltip("칼끝 앵커 로컬 오프셋(검 메시 기준). 메시에 따라 미세 조정 필요.")]
    [SerializeField] private Vector3 _trailTipOffset = new Vector3(0f, -1.2f, 0f);
    [Tooltip("칼밑(손잡이) 앵커 로컬 오프셋(검 메시 기준). 메시에 따라 미세 조정 필요.")]
    [SerializeField] private Vector3 _trailRootOffset = new Vector3(0f, -0.05f, 0f);
    [Tooltip("트레일 페이드 인 시간 (초)")]
    [SerializeField] private float _trailFadeIn = 0.05f;
    [Tooltip("트레일 페이드 아웃 시간 (초)")]
    [SerializeField] private float _trailFadeOut = 0.15f;
    [Tooltip("트레일 길이(VFX Lifetime/Length)")]
    [SerializeField] private float _trailLength = 0.35f;

    // ── 상태 ──────────────────────────────────────────
    private bool _isVisible;
    private CancellationToken _destroyCt;
    private Renderer _swordRenderer;
    private WeaponTrailEffect _trail;
    private Transform _tipAnchor;
    private Transform _rootAnchor;
    private GameObject _loadedTrailPrefab;

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // Lifecycle
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private void Awake()
    {
        _destroyCt = this.GetCancellationTokenOnDestroy();

        if (_swordGO == null)
            _swordGO = FindSwordInHierarchy();

        if (_swordGO == null)
        {
            Debug.LogWarning("[DKSword] SM_DarkKnight2_Sword를 찾을 수 없습니다.", this);
            return;
        }

        _swordRenderer = _swordGO.GetComponentInChildren<Renderer>();

        SetupTrail();

        // 시작 시 검 및 트레일 숨김
        _swordGO.SetActive(false);
        _isVisible = false;
    }

    /// <summary>검 메시 기준 칼끝/칼밑 앵커를 생성하고 WeaponTrailEffect를 수동 모드로 구성한다.</summary>
    private void SetupTrail()
    {
        if (!TryGetComponent(out _trail))
            _trail = gameObject.AddComponent<WeaponTrailEffect>();

        _trail.trailUsageType = WeaponTrailEffect.TrailUsageType.Manual;
        _trail.useEvents = false;
        _trail.enableGizmos = false;

        _tipAnchor = new GameObject("~SwordTrailTip").transform;
        _tipAnchor.SetParent(_swordGO.transform, false);
        _tipAnchor.localPosition = _trailTipOffset;

        _rootAnchor = new GameObject("~SwordTrailRoot").transform;
        _rootAnchor.SetParent(_swordGO.transform, false);
        _rootAnchor.localPosition = _trailRootOffset;

        _trail.lineTipTransform = _tipAnchor;
        _trail.lineBottomTransform = _rootAnchor;
    }

    private void OnEnable()
    {
        // 풀 재사용 시 검 및 트레일 초기화
        if (_swordGO != null)
        {
            _swordGO.SetActive(false);
            _isVisible = false;
        }
        if (_trail != null && _trail.isActiveAndEnabled) _trail.StopTrail(0.001f);
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 공개 API
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    /// <summary>
    /// 디졸브 효과와 함께 검을 등장시킨다.
    /// 이미 보이는 경우 무시.
    /// </summary>
    public void ShowSword()
    {
        if (_swordGO == null || _isVisible) return;
        _isVisible = true;
        _swordGO.SetActive(true);
        DissolveEffect.PlayAppear(_swordGO, _appearDuration, activationToken: _destroyCt);
        if (_trail != null) _trail.StartTrailWithLength(_trailFadeIn, _trailLength);
    }

    /// <summary>검 Transform을 반환한다 (VFX 부착 등에 사용).</summary>
    public Transform SwordTransform => _swordGO != null ? _swordGO.transform : null;

    /// <summary>
    /// 검 머티리얼을 지정된 색상으로 변경한다.
    /// </summary>
    public void SetSwordColor(DKSwordColor color)
    {
        if (_swordRenderer != null)
        {
            Material mat = color == DKSwordColor.White ? _whiteMaterial : _blackMaterial;
            if (mat != null)
                _swordRenderer.material = mat;
        }

        if (_trail != null)
        {
            GameObject trailPrefab = color == DKSwordColor.White ? _holyTrailPrefab : _darkTrailPrefab;
            // 색이 실제로 바뀔 때만 재생성 — 매 패턴 시작마다 재생성하면 궤적이 누적될 시간 없이 한 프레임짜리 반짝임만 남는다.
            if (trailPrefab != null && trailPrefab != _loadedTrailPrefab)
            {
                _trail.SetNewTrailPrefab(trailPrefab);
                _loadedTrailPrefab = trailPrefab;
            }
        }
    }

    /// <summary>
    /// 디졸브 효과와 함께 검을 소멸시킨다.
    /// 디졸브 완료 후 SetActive(false) 처리.
    /// </summary>
    public void HideSword()
    {
        if (_swordGO == null || !_isVisible) return;
        _isVisible = false;
        if (_trail != null) _trail.StopTrail(_trailFadeOut);
        DissolveEffect.PlayDisappear(
            _swordGO,
            _disappearDuration,
            onComplete: () =>
            {
                if (_swordGO != null)
                    _swordGO.SetActive(false);
            });
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 내부
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private GameObject FindSwordInHierarchy()
    {
        var t = FindDeep(transform, "SM_DarkKnight2_Sword");
        return t != null ? t.gameObject : null;
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
