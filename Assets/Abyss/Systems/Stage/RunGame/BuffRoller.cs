using UnityEngine;

/// <summary>
/// 버프 타일에서 사용하는 랜덤 버프/디버프 롤러.
/// BuffDataManager 테이블 기반.
///
/// ■ 확률 분포
///   버프 60% / 디버프 40%
///   1티어 60% / 2티어 30% / 3티어 10%
/// </summary>
public static class BuffRoller
{
    private const float BuffChance = 0.60f;
    private const float Tier1Threshold = 0.60f;
    private const float Tier2Threshold = 0.90f;

    /// <summary>
    /// 랜덤 버프/디버프를 굴립니다.
    /// BuffDataManager 테이블에서 조회.
    /// </summary>
    public static BuffRollResult Roll(int roomDuration = 1)
    {
        bool isBuff = Random.value < BuffChance;
        int tier = RollTier();

        var buffData = Managers.BuffData;
        if (buffData == null || !buffData.IsInitialized)
            return RollFallback(isBuff, tier, roomDuration);

        // 랜덤 buff_type 선택
        string buffType = isBuff ? buffData.GetRandomBuffType() : buffData.GetRandomDebuffType();

        // 테이블에서 조회
        var entry = buffData.Get(buffType, tier);
        if (entry == null)
        {
            // 해당 티어가 없으면 가장 높은 티어로 폴백
            for (int t = tier; t >= 1; t--)
            {
                entry = buffData.Get(buffType, t);
                if (entry != null) { tier = t; break; }
            }
        }

        if (entry == null)
            return RollFallback(isBuff, tier, roomDuration);

        // StatType 파싱
        StatType statType = ParseStatType(entry.stat_type);

        return new BuffRollResult
        {
            Modifier     = new StatModifier(statType, entry.value),
            IsPercent    = entry.is_percent,
            IsDebuff     = entry.is_debuff,
            Tier         = tier,
            BuffType     = buffType,
            RoomDuration = roomDuration,
            IsInstant    = statType == StatType.InstantDamage,
        };
    }

    private static int RollTier()
    {
        float roll = Random.value;
        if (roll < Tier1Threshold) return 1;
        if (roll < Tier2Threshold) return 2;
        return 3;
    }

    private static StatType ParseStatType(string s) => s switch
    {
        "MoveSpeed"      => StatType.MoveSpeed,
        "AttackPower"    => StatType.AttackPower,
        "Defense"        => StatType.Defense,
        "AttackSpeed"    => StatType.AttackSpeed,
        "Projectile"     => StatType.Projectile,
        "InstantDamage"  => StatType.InstantDamage,
        _                => StatType.AttackPower,
    };

    /// <summary>BuffDataManager 없을 때 하드코딩 폴백.</summary>
    private static BuffRollResult RollFallback(bool isBuff, int tier, int roomDuration)
    {
        StatType type = (StatType)Random.Range(0, 5); // AttackPower~Projectile
        float value = isBuff ? tier * 0.1f : tier * -0.1f;

        return new BuffRollResult
        {
            Modifier     = new StatModifier(type, value),
            IsPercent    = true,
            IsDebuff     = !isBuff,
            Tier         = tier,
            BuffType     = type.ToString(),
            RoomDuration = roomDuration,
            IsInstant    = false,
        };
    }
}

/// <summary>
/// BuffRoller.Roll() 결과
/// </summary>
public struct BuffRollResult
{
    public StatModifier Modifier;
    public bool IsPercent;
    public bool IsDebuff;
    public int Tier;
    public string BuffType;        // 테이블 buff_type (티어 승급용)
    public int RoomDuration;
    public bool IsInstant;
}
