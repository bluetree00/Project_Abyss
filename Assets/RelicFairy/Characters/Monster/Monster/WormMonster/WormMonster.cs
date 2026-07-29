namespace RelicFairy.Monster
{
/// <summary>
/// 웜 몬스터. 제자리 매복형 몬스터 (Walk/Run 애니메이션 없음 — patrol/chase 모두 IdleNormal).
/// </summary>
public class WormMonster : MonsterBase
{
    public const string PrefabAddress = "WormMonster/WormMonster";
    protected override string ConfigAddress => "WormMonster/WormMonsterConfig";
    protected override string DataAddress   => string.Empty;
}
}
