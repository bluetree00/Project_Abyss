namespace RelicFairy.Monster
{
/// <summary>
/// 가오리 몬스터 (비행형).
/// 특수 상태: 피격 3회 누적 시 발동 — 즉시 범위 방전 공격 (StingRayDischargeState).
/// OnDamageTaken override로 피격 횟수를 누적하여 임계값 도달 시 특수 상태를 트리거한다.
/// </summary>
public class StingRayMonster : MonsterBase
{
    public const string PrefabAddress = "StingRay/StingRay";
    protected override string ConfigAddress   => "StingRay/StingRayConfig";
    protected override string DataAddress     => string.Empty;
    protected override string HeadBoneName    => null;
    protected override float  HPBarHeadOffset => 0.5f;

    private int _hitCount;

    protected override void OnDamageTaken()
    {
        base.OnDamageTaken();
        if (++_hitCount >= 3 && _fsm?.CurrentConstraints == SpecialStateConstraint.None)
        {
            _hitCount = 0;
            var s = GetSpecialState(0);
            if (s != null) _fsm.ChangeState(s);
        }
    }

    protected override void OnEnable()
    {
        _hitCount = 0;
        base.OnEnable();
    }
}
}
