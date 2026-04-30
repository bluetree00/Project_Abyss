namespace Abyss.Monster
{
/// <summary>
/// BlackKnight 보스 몬스터.
/// </summary>
public class BlackKnightMonster : MonsterBase, IBoss
{
    public const string PrefabAddress = "BlackKnight/BlackKnight";

    protected override string ConfigAddress => "BlackKnight/BlackKnightConfig";
    protected override string DataAddress   => string.Empty;
    protected override string HeadBoneName  => null;
    protected override float  HPBarHeadOffset => 0.18f;
    protected override bool   UseWorldHPBar   => false;

    private readonly BossAttackBlackboard _blackboard = new();

    public float               HpRatio   => (_config != null && EffectiveMaxHp > 0) ? (float)CurrentHp / EffectiveMaxHp : 1f;
    public BossAttackBlackboard Blackboard => _blackboard;

    protected override void OnInitialized()
    {
        BindBossHud();
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        BindBossHud();
    }

    protected override void OnDisable()
    {
        UnbindBossHudIfBound();
        base.OnDisable();
    }
}
}
