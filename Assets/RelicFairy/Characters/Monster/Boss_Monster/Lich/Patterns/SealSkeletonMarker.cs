using System;
using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// 결계 해골(옛 이름 Seal Skeleton)에 부착하는 마커.
///
/// MonsterBase.OnDied를 구독해 해당 해골이 죽으면 OnKilled를 발행하고
/// 바닥 마커 디스크(청록)를 제거한다.
/// LichSealBreakerPatternSO의 상태가 이 이벤트로 남은 봉인 수를 추적한다.
/// </summary>
public class SealSkeletonMarker : MonoBehaviour
{
    private const float SafeFlashSeconds = 0.4f;   // 끊긴 봉인 — 초록 바닥 원이 이만큼 보인 뒤 지워진다

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
        _disc          = PatternGuideHelper.Disc(transform.position, 0.7f, PatternGuideHelper.Breakable);   // 청록 = 공격해서 끊을 수 있음
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
        // 몸은 공용 사망 디졸브(DieState)가 거둔다 — 예전엔 여기서 통째로 지워 디졸브 없이 사라졌다(10-12 디졸브 원칙). 표식만 뗀다
        if (_disc != null) { Destroy(_disc, SafeFlashSeconds); _disc = null; _discTransform = null; }
        Destroy(this);
    }
}
}
