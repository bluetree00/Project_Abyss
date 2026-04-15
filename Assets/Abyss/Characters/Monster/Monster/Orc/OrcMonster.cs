namespace Abyss.Monster
{
/// <summary>
/// 오크. 느리고 강한 표준 근접 몬스터.
/// </summary>
public class OrcMonster : MonsterBase
{
    public const string PrefabAddress = "Orc/Orc";
    protected override string ConfigAddress => "Orc/OrcConfig";
    protected override string DataAddress   => string.Empty;
}
}
