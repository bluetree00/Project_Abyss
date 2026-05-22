namespace RelicFairy.Monster
{
/// <summary>
/// 식물 몬스터.
/// 특수 상태: HP 70% 이하 도달 시 1회 발동 — 독 포자 즉시 분사 (MonsterPlantPoisonSprayState).
/// </summary>
public class MonsterPlantMonster : MonsterBase
{
    public const string PrefabAddress = "MonsterPlant/MonsterPlant";
    protected override string ConfigAddress => "MonsterPlant/MonsterPlantConfig";
    protected override string DataAddress   => string.Empty;
}
}
