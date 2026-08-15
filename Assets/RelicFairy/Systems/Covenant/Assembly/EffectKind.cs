/// <summary>
/// 조립 서약 — 효과(Effect) 결과. 각 값은 AssembledCovenant.ApplyEffect의 분기에 매핑.
/// (기존 훅으로 확실히 동작하는 것만. 소환·시간정지 등은 후속 등급.)
/// </summary>
public enum EffectKind
{
    AoeBurst,     // 광역 폭발 (DealAoe)
    DamageBuff,   // 일시 피해 증폭 (ModifyOutgoingDamage, duration초)
    Shield,       // 발동 시 보호막 (PlayerRuntimeStats.AddShield). 흡혈(회복) 폐기 후 생존 슬롯 대체.
    GoldBurst,    // 발동 시 골드 (GameRunSession.AddGold)
    Curse,        // 저주: 대상 받는 피해 증폭 (MonsterBase.ApplyDamageTakenAmp, duration초)
    Execute,      // 처형: 저체력(N%↓) 대상 즉사. 보스는 즉사 대신 최대HP 비례 피해.
    Burn,         // 화상 DoT 부여 (MonsterBurnHandler.Apply). dps = 유효공격력 × 유효수치.
    Invincible,   // 짧은 무적 (PlayerController.SetInvincible). ICD 필수 — 스케일 금지(ScaleMode.None).
    DeathSave,    // 치명 피해 1회 생존 (TryPreventDeath). 유효 '횟수'가 충전량(ScaleMode.Count).
    StatBuff,     // 이속/공속 중첩 버프 (GetStatModifiers + 버프창). 「박차」.

    // ── 상태 통화 걸기/먹기 (B급) ────────────────────────
    // 소모형(Detonate/Sanguine/Harvest)은 어떤 상태도 <b>부여하지 않는다</b>.
    // 먹으면서 걸면 자기가 먹을 것을 자기가 만들어 무한 기폭이 된다.
    BleedStack,   // 출혈 중첩 부여 (MonsterBleed.ApplyStacked). dps = 유효공격력 × 유효수치 / 스택.
    Detonate,     // 기폭: 화상+출혈 잔량을 소모해 즉시 피해 + 반경 확산. 원인 형상에 따라 단일/광역.
    Sanguine,     // 흡정: 화상+출혈 잔량을 소모해 보호막으로 환산.
    Harvest,      // 수확: 화상+출혈 잔량을 소모해 스킬 쿨감 + 골드로 환산.
}
