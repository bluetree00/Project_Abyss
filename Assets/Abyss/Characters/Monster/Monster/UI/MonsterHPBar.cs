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
public class MonsterHPBar : MonoBehaviour
{
    [Header("UI 참조")]
    [SerializeField] private Slider _slider;

    [Header("위치 설정")]
    [Tooltip("Head 본 위쪽 추가 오프셋 (m). 본이 없으면 콜라이더 상단 기준.")]
    [SerializeField] private float _headOffset = 0.1f;

    // ── 런타임 ─────────────────────────────────────────────
    private MonoBehaviour _monster;
    private Transform       _headBone;      // 우선 사용
    private Collider        _collider;      // 폴백용
    private Transform       _camTransform;

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 외부 API
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    /// <summary>몬스터와 연결하고 HP 바를 활성화한다.</summary>
    /// <param name="headBone">Head 본 Transform. null이면 콜라이더 상단 기준으로 폴백.</param>
    public void Link(MonoBehaviour monster, int currentHp, int maxHp, Transform headBone, float headOffset = 0.1f)
    {
        _monster      = monster;
        _headBone     = headBone;
        _collider     = headBone == null ? monster.GetComponentInChildren<Collider>() : null;
        _camTransform = Camera.main != null ? Camera.main.transform : null;
        _headOffset   = headOffset;

        UpdateHP(currentHp, maxHp);
        gameObject.SetActive(true);
    }

    /// <summary>몬스터 참조를 끊고 HP 바를 비활성화한다 (풀 반환 전 호출).</summary>
    public void Unlink()
    {
        _monster  = null;
        _headBone = null;
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

        // 위치 계산: Head 본이 있으면 본 위치 기준, 없으면 콜라이더 상단 기준
        Vector3 basePos = _headBone != null
            ? _headBone.position
            : _monster.transform.position + Vector3.up * (_collider != null ? _collider.bounds.size.y : 2f);

        transform.position = basePos + Vector3.up * _headOffset;

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
