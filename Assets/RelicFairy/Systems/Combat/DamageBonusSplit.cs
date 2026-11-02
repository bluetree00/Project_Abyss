using UnityEngine;

/// <summary>한 타 보너스(추가 피해)의 출처 — 숫자 색 · 표식을 고르는 열쇠(11-02 Q29).</summary>
public enum BonusSource
{
    /// <summary>룬 조건 보너스(맞서는 · 겨누는 · 질주하는 분노 · 굳히는 얼음 · 관통 결정 · 잠식 첫 타).</summary>
    Rune = 0,
    /// <summary>재련 증강(묵직한 손).</summary>
    Augment = 1,
    /// <summary>전설 룬(불 전설 불씨 일격).</summary>
    Legendary = 2,
    /// <summary>예약 · 타이밍형(다음 공격 강화 · 저스트가드).</summary>
    Item = 3,
}

/// <summary>
/// 11-02 Q29 「추가 피해」 숫자 나누기 — 사용자 「룬 · 재련 증강 · 추가 이펙트가 일으키는 피해를 총합이 아니라 추가 피해로 따로」.
/// 피해 · 적중 효과는 예전처럼 한 번에 들어가고(총합 · 밸런스 그대로) 숫자만 나눈다:
/// 주 숫자 = 받은 피해 ÷ 보너스 배율, 나머지는 출처별 몫으로 <see cref="DamageKind.Bonus"/> 숫자.
/// CombatDamage가 주 피해를 넣는 동안만 장부를 건다(MonsterPoise.PendingHitWeight와 같은 방식) — 받는 쪽(몬스터 · 허수아비)의 숫자 자리가 부른다.
/// </summary>
public static class DamageBonusSplit
{
    private const float MinPart = 0.5f;   // 반올림하면 0인 몫은 띄우지 않는다

    private static DamagePacket s_pkt;
    private static int  s_targetId;
    private static bool s_armed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        s_pkt      = default;
        s_targetId = 0;
        s_armed    = false;
    }

    /// <summary>주 피해를 넣기 직전 — 그 대상(targetId = 숫자 줄 ID)의 숫자 한 벌만 나눈다.</summary>
    public static void Begin(in DamagePacket pkt, int targetId)
    {
        s_pkt      = pkt;
        s_targetId = targetId;
        s_armed    = targetId != 0 && pkt.BonusProduct > 1.001f;
    }

    public static void End() => s_armed = false;

    /// <summary>받은 피해 숫자 — 걸린 장부가 이 대상 것이면 주 + 출처별 추가 피해로 나누고, 아니면 예전처럼 한 숫자.</summary>
    public static void Spawn(Vector3 worldPos, float actual, bool isCrit, int targetId, bool showTotal = false)
    {
        if (!s_armed || targetId != s_targetId)
        {
            DamagePopupSpawner.Spawn(worldPos, actual, isCrit, targetId, showTotal: showTotal);
            return;
        }
        s_armed = false;   // 한 타에 한 벌 — 같은 주 피해 안의 다른 숫자(처치 효과 등)는 나누지 않는다

        float main  = actual / s_pkt.BonusProduct;
        float bonus = actual - main;
        float sum   = s_pkt.BonusRune + s_pkt.BonusAugment + s_pkt.BonusLegendary + s_pkt.BonusItem;
        DamagePopupSpawner.Spawn(worldPos, main, isCrit, targetId, showTotal: showTotal);
        SpawnPart(worldPos, bonus * s_pkt.BonusRune      / sum, isCrit, targetId, showTotal, BonusSource.Rune);
        SpawnPart(worldPos, bonus * s_pkt.BonusAugment   / sum, isCrit, targetId, showTotal, BonusSource.Augment);
        SpawnPart(worldPos, bonus * s_pkt.BonusLegendary / sum, isCrit, targetId, showTotal, BonusSource.Legendary);
        SpawnPart(worldPos, bonus * s_pkt.BonusItem      / sum, isCrit, targetId, showTotal, BonusSource.Item);
    }

    private static void SpawnPart(Vector3 worldPos, float amount, bool isCrit, int targetId, bool showTotal, BonusSource source)
    {
        if (amount >= MinPart) DamagePopupSpawner.Spawn(worldPos, amount, isCrit, targetId, DamageKind.Bonus, showTotal: showTotal, source: source);
    }
}
