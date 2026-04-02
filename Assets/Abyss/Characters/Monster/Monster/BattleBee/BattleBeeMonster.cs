namespace Abyss.Monster
{
/// <summary>
/// 배틀비. 비행형 빠른 근접 몬스터 (FlyFWD/FlyFWDFast 이동).
/// </summary>
public class BattleBeeMonster : MonsterBase
{
    public const string PrefabAddress = "BattleBee/BattleBee";
    protected override string ConfigAddress => "BattleBee/BattleBeeConfig";
    protected override string DataAddress   => string.Empty;
    protected override string HeadBoneName  => null;
    protected override float  HPBarHeadOffset => 0.5f;
}
}
