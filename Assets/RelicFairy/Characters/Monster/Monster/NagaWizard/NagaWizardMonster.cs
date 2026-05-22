namespace RelicFairy.Monster
{
/// <summary>
/// 나가 마법사. 원거리 마법 투사체 공격 (attackShape = MonsterRangedAttackSO).
/// </summary>
public class NagaWizardMonster : MonsterBase
{
    public const string PrefabAddress = "NagaWizard/NagaWizard";
    protected override string ConfigAddress => "NagaWizard/NagaWizardConfig";
    protected override string DataAddress   => string.Empty;
}
}
