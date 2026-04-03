namespace Abyss.Monster
{
/// <summary>
/// 일반 몬스터 버전 블랙나이트.
/// 기존 보스 임시 에셋 주소를 재사용하되 MonsterBase 기반으로 동작한다.
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
