namespace RelicFairy.Monster
{
/// <summary>
/// 쥐 암살자. 최고 이동속도, 낮은 체력의 고속 근접 몬스터.
/// </summary>
public class RatAssassinMonster : MonsterBase
{
    public const string PrefabAddress = "RatAssassin/RatAssassin";
    protected override string ConfigAddress => "RatAssassin/RatAssassinConfig";
    protected override string DataAddress   => string.Empty;
}
}
