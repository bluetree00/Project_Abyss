
namespace Abyss.Monster
{
/// <summary>
/// 달팽이 몬스터.
/// 특수 상태: 전투 중 일정 주기마다 이동 멈추고 받는 데미지 50% 감소 (SnailShellState).
/// </summary>

public class SnailMonster : MonsterBase
{
    public const string PrefabAddress = "Snail/Snail";
    protected override string ConfigAddress    => "Snail/SnailConfig";
    protected override string DataAddress      => "Snail/SnailData";
    protected override float  HPBarHeadOffset  => 0.7f;

    private float _shellCooldown;

    protected override void OnEnable()
    {
        base.OnEnable();
        _shellCooldown = GetShellInterval() * UnityEngine.Random.Range(0.3f, 1.0f);
    }

    public override IMonsterState TryGetSpecialState(MonsterContext ctx)
    {
        var state = GetSpecialState(0);
        if (state == null) return null;

        // 전투 상태(추격/공격준비/공격)일 때만 방어 진입 허용
        var t = _fsm?.CurrentType;
        if (t != typeof(ChaseState) && t != typeof(AttackReadyState) && t != typeof(AttackState)) return null;

        _shellCooldown -= UnityEngine.Time.deltaTime;
        if (_shellCooldown <= 0f)
        {
            _shellCooldown = GetShellInterval();
            return state;
        }
        return null;
    }

    private float GetShellInterval()
        => _config?.specialState0 is SnailShellData d ? d.interval : 15f;
}
}
