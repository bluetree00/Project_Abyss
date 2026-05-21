namespace RelicFairy.Monster
{
/// <summary>
/// BlackKnight addressable asset wrapper.
/// 현재는 보스 구현체가 아니라 일반 MonsterBase 파생 몬스터로만 사용된다.
/// </summary>
public class BlackKnightMonster : MonsterBase
{
    public const string PrefabAddress = "BlackKnight/BlackKnight";

    protected override string ConfigAddress => "BlackKnight/BlackKnightConfig";
    protected override string DataAddress => string.Empty;
    protected override string HeadBoneName => null;
    protected override float HPBarHeadOffset => 0.18f;
}
}
