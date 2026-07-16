/// <summary>
/// 런 지속 연료 종류(RunFuelBank). 확정 경제: 강화재료(#1 무기강화 소비)·원석(#2 룬재련 소비).
/// 유물코어는 메타(각성) 재화(abyssEssence)라 런 연료에서 제외 — 미소비 연료의 "환산 목적지"일 뿐.
/// </summary>
public enum FuelKind
{
    EnhanceMaterial = 0,   // 강화재료
    RuneOre,               // 원석
}
