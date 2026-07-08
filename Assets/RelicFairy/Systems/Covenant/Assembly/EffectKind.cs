/// <summary>
/// 조립 서약 — 효과(Effect) 결과. 각 값은 AssembledCovenant.ApplyEffect의 분기에 매핑.
/// (MVP: CovenantBase 헬퍼/기존 API로 확실히 동작하는 것만. 상태이상/소환 등은 후속.)
/// </summary>
public enum EffectKind
{
    AoeBurst,     // 광역 폭발 (DealAoe)
    DamageBuff,   // 일시 피해 증폭 (ModifyOutgoingDamage, duration초)
    Lifesteal,    // 발동 시 회복 (PlayerController.Heal)
    GoldBurst,    // 발동 시 골드 (GameRunSession.AddGold)
    Curse,        // 저주: 대상 받는 피해 증폭 (MonsterBase.ApplyDamageTakenAmp, duration초)
    Execute,      // 처형: 저체력(N%↓) 대상 즉사 (MonsterBase.TakeDamage)
}
