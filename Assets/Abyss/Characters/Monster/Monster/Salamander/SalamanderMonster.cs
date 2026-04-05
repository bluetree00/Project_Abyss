namespace Abyss.Monster
{
/// <summary>
/// 샐러맨더 몬스터.
/// 특수 상태: HP 70% 이하일 때 반복 발동 — 정면 Cone 화염 브레스 (SalamanderFlameBreathState).
/// </summary>
public class SalamanderMonster : MonsterBase
{
    public const string PrefabAddress = "Salamander/Salamander";
    protected override string ConfigAddress => "Salamander/SalamanderConfig";
    protected override string DataAddress   => string.Empty;
}
}
