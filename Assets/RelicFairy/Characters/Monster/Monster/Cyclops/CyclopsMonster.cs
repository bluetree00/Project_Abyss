namespace RelicFairy.Monster
{
/// <summary>
/// 사이클롭스. 장거리 바위 투척 (attackShape = MonsterRangedAttackSO). 높은 체력과 공격력.
/// </summary>
public class CyclopsMonster : MonsterBase
{
    public const string PrefabAddress = "Cyclops/Cyclops";
    protected override string ConfigAddress => "Cyclops/CyclopsConfig";
    protected override string DataAddress   => string.Empty;
    protected override float  HPBarHeadOffset => 1.2f;
}
}
