using UnityEngine;

/// <summary>
/// 무기 스킬 단계(1~3)를 <b>성장 축에서 파생</b>한다.
///
/// 예전엔 <c>WeaponData.tier</c>(SO/CSV 정적값)를 읽었는데, 런 안의 무기는 전부 T1이라
/// 행동 SO마다 들어 있는 2·3단계 분기가 한 번도 열리지 않았다. 강화(재련소)는 공격력 배율에만
/// 닿고 스킬엔 닿지 않았던 정확한 원인.
///
/// <list type="bullet">
/// <item>근접(무형검·카타나·대검) — 강화 레벨이 임계(기본 +5 / +10)를 넘을 때마다 한 단계.
///       진화 시 강화 레벨이 계승되므로 절대값 기준이 자연스럽게 이어진다.</item>
/// <item>원거리(활·석궁) — 원거리 무기 강화는 폐지됐으므로 파츠 총레벨이 임계(기본 4 / 10)를 넘을 때마다 한 단계.
///       종류가 아니라 총량을 보므로 어떤 파츠를 골랐든 스킬은 같이 자란다.</item>
/// </list>
/// 임계는 <see cref="EnhanceTableSO"/>가 소유한다(밸런스 데이터). 테이블 미로드면 내장 기본값.
/// 스킬 Runtime은 매 발동 OnEnter에서 읽으므로 재련소를 나온 다음 발동부터 바로 반영된다.
/// </summary>
public static class SkillTierResolver
{
    public const int MinTier = 1;
    public const int MaxTier = 3;

    private static readonly int[] DefaultMeleeMilestones  = { 5, 10 };
    private static readonly int[] DefaultRangedMilestones = { 4, 10 };

    /// <summary>현재 스킬 단계(1~3).</summary>
    public static int Resolve(WeaponData wd)
    {
        if (wd == null) return MinTier;
        int reached = CountReached(MilestonesFor(wd), ProgressOf(wd));
        return Mathf.Clamp(MinTier + reached, MinTier, MaxTier);
    }

    /// <summary>
    /// 다음 단계 정보(표시용). 다음 임계가 <paramref name="reachableMax"/>를 넘으면(무형검 상한 6에서 +10 등)
    /// 도달 불가로 보고 false.
    /// </summary>
    public static bool TryGetNext(WeaponData wd, int reachableMax, out int nextTier, out int remaining)
    {
        nextTier = 0; remaining = 0;
        if (wd == null) return false;

        var milestones = MilestonesFor(wd);
        int progress   = ProgressOf(wd);
        int tier       = Resolve(wd);
        if (tier >= MaxTier) return false;

        int idx = tier - MinTier;              // 다음 임계의 인덱스
        if (idx >= milestones.Length) return false;
        int next = milestones[idx];
        if (reachableMax > 0 && next > reachableMax) return false;

        nextTier  = tier + 1;
        remaining = Mathf.Max(0, next - progress);
        return true;
    }

    public static bool IsRanged(WeaponData wd)
        => wd != null && wd.weaponType.GetAttackStatKind() == AttackStatKind.Ranged;

    private static int[] MilestonesFor(WeaponData wd)
    {
        var table = WeaponEnhanceService.Table;
        if (IsRanged(wd))
            return table != null && table.RangedSkillTierMilestones.Length > 0 ? table.RangedSkillTierMilestones : DefaultRangedMilestones;
        return table != null && table.MeleeSkillTierMilestones.Length > 0 ? table.MeleeSkillTierMilestones : DefaultMeleeMilestones;
    }

    private static int ProgressOf(WeaponData wd)
        => IsRanged(wd) ? (RangedPartsState.Current?.TotalLevel ?? 0) : Mathf.Max(0, wd.enhanceLevel);

    private static int CountReached(int[] milestones, int progress)
    {
        int n = 0;
        for (int i = 0; i < milestones.Length; i++)
            if (progress >= milestones[i]) n++;
        return n;
    }
}
