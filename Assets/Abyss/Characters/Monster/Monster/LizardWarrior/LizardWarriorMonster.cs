namespace Abyss.Monster
{
/// <summary>
/// 도마뱀 전사. 균형잡힌 근접 전투형 엘리트 몬스터.
/// </summary>
public class LizardWarriorMonster : MonsterBase
{
    public const string PrefabAddress = "LizardWarrior/LizardWarrior";
    protected override string ConfigAddress => "LizardWarrior/LizardWarriorConfig";
    protected override string DataAddress   => string.Empty;
}
}
