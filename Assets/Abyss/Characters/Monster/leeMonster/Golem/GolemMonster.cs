/// <summary>
/// 골렘 몬스터.
/// MonsterConfigSO 의 invincibleStateData 슬롯에 GolemRoarData.asset 을 할당하면
/// HP 임계값 이하 도달 시 포효 특수 상태가 한 번 발동된다.
/// </summary>
public class GolemMonster : LeeMonsterBase
{
    public const string PrefabAddress = "Golem/Golem";
    protected override string ConfigAddress   => "Golem/GolemConfig";
    protected override string DataAddress     => "Golem/GolemData";
    protected override float  HPBarHeadOffset => 1.0f;

    private bool _hasRoared;

    // ── 특수 상태 진입 훅 ──────────────────────────────────

    public override ILeeMonsterState TryGetSpecialState(LeeMonsterContext ctx)
    {
        if (SpecialInvincible == null || _hasRoared) return null;
        if (_config.invincibleStateData is not GolemRoarData roarData) return null;

        float hpRatio = (float)ctx.Runtime.CurrentHp / ctx.Config.stat.maxHp;
        if (hpRatio <= roarData.hpThreshold)
        {
            _hasRoared = true;
            return SpecialInvincible;
        }
        return null;
    }

    // ── 풀 재사용 시 플래그 리셋 ──────────────────────────

    protected override void OnEnable()
    {
        _hasRoared = false;
        base.OnEnable();
    }
}
