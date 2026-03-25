using Abyss.Monster;
using UnityEngine;

/// <summary>
/// 애니메이션 이벤트를 MonsterBase로 전달하는 수신기.
///
/// 사용 방법:
///  1) 몬스터 프리팹의 Animator 오브젝트(또는 루트)에 이 컴포넌트 추가.
///  2) 공격 히트 프레임에 Animation Event → 함수명 "OnAttackHit" 등록.
///  3) MonsterCombatSO.damageApplyDelay = 0 으로 설정하면
///     타이머 방식 대신 이벤트 방식만 사용.
/// </summary>
public class MonsterAnimEventReceiver : MonoBehaviour
{
    private MonsterBase _monster;

    private void Awake()
    {
        _monster = GetComponent<MonsterBase>()
                   ?? GetComponentInParent<MonsterBase>();

        if (_monster == null)
            Debug.LogWarning("[MonsterAnimEventReceiver] MonsterBase를 찾을 수 없습니다.", this);
    }

    // ── Animation Event 콜백 ──────────────────────────────

    /// <summary>공격 모션 시작 프레임에 호출 (필요 시 파생 클래스에서 활용).</summary>
    public void OnAttackStart() { }

    /// <summary>공격 모션 종료 프레임에 호출 (필요 시 파생 클래스에서 활용).</summary>
    public void OnAttackEnd() { }

    /// <summary>공격 히트 판정 프레임에 호출.</summary>
    public void OnAttackHit()
    {
        _monster?.OnAnimAttackHit();
    }

    /// <summary>피격 애니메이션 종료 시 호출 (필요 시 활용).</summary>
    public void OnGetHitEnd() { }

    /// <summary>사망 애니메이션 종료 시 호출 (필요 시 활용).</summary>
    public void OnDieEnd() { }
}
