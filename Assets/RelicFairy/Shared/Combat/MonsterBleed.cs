using RelicFairy.Monster;
using UnityEngine;

/// <summary>
/// 출혈(bleed) 상태 헬퍼 — 전용 핸들러 없이 몬스터의 범용 DoT(<see cref="MonsterStatusReceiver.ApplyDot"/>)를
/// id="bleed"로 감싼 정적 진입점. 화상(MonsterBurnHandler)과 역할이 대응된다.
/// 랜슬롯 파츠(출혈의 낙인·피의 만찬)가 출혈 부여·전염에 사용한다.
/// </summary>
public static class MonsterBleed
{
    private const string Id           = "bleed";
    private const float  TickInterval = 0.5f;

    /// <summary>대상에 출혈을 부여한다. dps = 초당 피해, duration = 지속(초).</summary>
    public static void Apply(GameObject target, float dps, float duration, GameObject instigator)
    {
        if (target == null || dps <= 0f || duration <= 0f) return;
        var mb = target.GetComponentInParent<MonsterBase>();
        if (mb == null || mb.Status == null) return;

        int ticks = Mathf.Max(1, Mathf.RoundToInt(duration / TickInterval));
        mb.Status.ApplyDot(Id, dps * TickInterval, TickInterval, ticks, instigator);
    }

    /// <summary>대상의 남은 출혈 총피해(없으면 0). 전염 시 옮길 양 계산에 쓴다.</summary>
    public static float Remaining(GameObject target)
    {
        if (target == null) return 0f;
        var mb = target.GetComponentInParent<MonsterBase>();
        return mb != null && mb.Status != null ? mb.Status.GetRemainingDotDamage(Id) : 0f;
    }
}
