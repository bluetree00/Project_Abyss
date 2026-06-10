using System;
using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// 봉인 해골(Seal Skeleton)에 부착하는 마커.
///
/// MonsterBase.OnDied를 구독해 해당 해골이 죽으면 OnKilled를 발행하고
/// 바닥 마커 디스크(파란색)를 제거한다.
/// LichSealBreakerPatternSO의 상태가 이 이벤트로 남은 봉인 수를 추적한다.
/// </summary>
public class SealSkeletonMarker : MonoBehaviour
{
    /// <summary>해당 해골 사망 시 발행. LichSealBreakerState가 구독한다.</summary>
    public event Action OnKilled;

    private MonsterBase  _monster;
    private GameObject   _disc;
    private Transform    _discTransform;

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 초기화
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private void Start()
    {
        _monster = GetComponentInParent<MonsterBase>();
        if (_monster == null)
        {
            Destroy(this);
            return;
        }

        _monster.OnDied += HandleDied;

        // 바닥 마커 디스크 (봉인 색상)
        _disc          = PatternGuideHelper.Disc(transform.position, 0.7f, PatternGuideHelper.Seal);
        _discTransform = _disc.transform;
    }

    private void Update()
    {
        if (_discTransform == null) return;
        Vector3 p = _discTransform.position;
        p.x = transform.position.x;
        p.z = transform.position.z;
        _discTransform.position = p;
    }

    private void OnDestroy()
    {
        if (_monster != null)
            _monster.OnDied -= HandleDied;
        if (_disc != null)
            Destroy(_disc);
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 이벤트 핸들러
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private void HandleDied(MonsterBase _)
    {
        // 짧은 초록 플래시로 봉인 해제를 시각적으로 표시
        if (_disc != null)
            PatternGuideHelper.SetColor(_disc, PatternGuideHelper.Safe);

        OnKilled?.Invoke();
        Destroy(gameObject);
    }
}
}
