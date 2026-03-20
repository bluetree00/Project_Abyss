using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 몬스터 머리 위 WorldSpace HP 바.
///
/// ━━━ 사용 흐름 ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
///  1) LeeMonsterHPBarManager.RequestHPBar(monster) 로 꺼냄
///  2) Link(monster) 호출 → 몬스터 참조 연결, 위치 추적 시작
///  3) UpdateHP(current, max) 로 슬라이더 값 갱신
///  4) 몬스터 사망/반환 시 Unlink() → 매니저가 풀에 반환
/// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
/// </summary>
public class LeeMonsterHPBar : MonoBehaviour
{
    [Header("UI 참조")]
    [SerializeField] private Slider _slider;

    [Header("위치 설정")]
    [Tooltip("콜라이더 상단으로부터 추가 오프셋 (m)")]
    [SerializeField] private float _headOffset = 0.3f;

    // ── 런타임 ─────────────────────────────────────────────
    private LeeMonsterBase _monster;
    private Collider        _collider;
    private Transform       _camTransform;

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 외부 API
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    /// <summary>몬스터와 연결하고 HP 바를 활성화한다.</summary>
    public void Link(LeeMonsterBase monster, int currentHp, int maxHp, float headOffset = 0.3f)
    {
        _monster      = monster;
        _collider     = monster.GetComponentInChildren<Collider>();
        _camTransform = Camera.main != null ? Camera.main.transform : null;
        _headOffset   = headOffset;

        UpdateHP(currentHp, maxHp);
        gameObject.SetActive(true);
    }

    /// <summary>몬스터 참조를 끊고 HP 바를 비활성화한다 (풀 반환 전 호출).</summary>
    public void Unlink()
    {
        _monster  = null;
        _collider = null;
        gameObject.SetActive(false);
    }

    /// <summary>슬라이더 값을 현재 HP 비율로 갱신한다.</summary>
    public void UpdateHP(int currentHp, int maxHp)
    {
        if (_slider == null) return;
        _slider.value = maxHp > 0 ? (float)currentHp / maxHp : 0f;
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 매 프레임 : 위치 + 빌보드
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private void Update()
    {
        if (_monster == null) return;

        // 머리 위 위치 계산 (콜라이더 높이 + 추가 오프셋)
        float height = _collider != null
            ? _collider.bounds.size.y + _headOffset
            : 2f + _headOffset;

        transform.position = _monster.transform.position + Vector3.up * height;

        // 카메라를 향해 빌보드 회전
        if (_camTransform != null)
            transform.rotation = _camTransform.rotation;
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 초기화
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private void Awake()
    {
        // Slider를 자동으로 찾아 캐시 (Inspector 미할당 시 대비)
        if (_slider == null)
            _slider = GetComponentInChildren<Slider>();

        gameObject.SetActive(false);
    }
}
