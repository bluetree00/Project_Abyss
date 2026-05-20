using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Abyss.Monster
{
/// <summary>
/// DeathKnight 보스 검 출현/소멸 컨트롤러.
///
/// 패턴 시작 → ShowSword()  : 디졸브로 등장
/// 패턴 종료 → HideSword()  : 디졸브로 소멸 후 비활성화
///
/// 검은 기본적으로 오른손 Bone_Sword에 붙어있다.
/// 인스펙터 미할당 시 Awake에서 "SM_DarkKnight2_Sword" 이름으로 자동 탐색.
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

    // ── 상태 ──────────────────────────────────────────
    private bool _isVisible;
    private CancellationToken _destroyCt;

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

        // 시작 시 검 숨김
        _swordGO.SetActive(false);
        _isVisible = false;
    }

    private void OnEnable()
    {
        // 풀 재사용 시 검 초기화
        if (_swordGO != null)
        {
            _swordGO.SetActive(false);
            _isVisible = false;
        }
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
    }

    /// <summary>
    /// 디졸브 효과와 함께 검을 소멸시킨다.
    /// 디졸브 완료 후 SetActive(false) 처리.
    /// </summary>
    public void HideSword()
    {
        if (_swordGO == null || !_isVisible) return;
        _isVisible = false;
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
