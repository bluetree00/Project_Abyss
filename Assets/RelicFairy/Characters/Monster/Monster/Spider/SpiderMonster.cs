namespace RelicFairy.Monster
{
/// <summary>
/// 거미. 빠른 이동, 낮은 체력의 표준 근접 몬스터.
/// </summary>
public class SpiderMonster : MonsterBase
{
    public const string PrefabAddress = "Spider/Spider";
    protected override string ConfigAddress => "Spider/SpiderConfig";
    protected override string DataAddress   => string.Empty;
}
}
