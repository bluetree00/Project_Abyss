
namespace Abyss.Monster
{
/// <summary>
/// 외눈슬라임 몬스터.
/// 특수 상태: 일정 주기마다 이동을 멈추고 HP를 회복한다 (SlimeRegenState).
/// </summary>

public class SlimeMonster : MonsterBase
{
    public const string PrefabAddress = "Slime/Slime";
    protected override string ConfigAddress    => "Slime/SlimeConfig";
    protected override string DataAddress      => "Slime/SlimeData";
    protected override float  HPBarHeadOffset  => 0.7f;

    private float _regenCooldown;

    protected override void OnEnable()
    {
        base.OnEnable();
        // 여러 마리가 동시에 발동하지 않도록 초기 쿨다운을 30~100% 범위에서 랜덤 분산
        _regenCooldown = GetRegenInterval() * UnityEngine.Random.Range(0.3f, 1.0f);
    }

    public override IMonsterState TryGetSpecialState(MonsterContext ctx)
    {
        var state = GetSpecialState(0);
        if (state == null) return null;

        // 배회 상태일 때만 회복 진입 허용
        if (_fsm?.CurrentType != typeof(PatrolState)) return null;

        _regenCooldown -= UnityEngine.Time.deltaTime;
        if (_regenCooldown <= 0f)
        {
            _regenCooldown = GetRegenInterval();
            return state;
        }
        return null;
    }

    private float GetRegenInterval()
        => _config?.specialState0 is SlimeRegenData d ? d.interval : 20f;
}
}
