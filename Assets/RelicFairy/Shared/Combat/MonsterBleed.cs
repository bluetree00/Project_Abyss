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

    /// <summary>
    /// 출혈 <b>중첩</b> 부여 — 같은 대상에 다시 걸면 dps가 누적된다(최대 maxStacks 스택).
    ///
    /// <see cref="Apply"/>는 잔여 틱만 top-up하고 dps는 갱신 값으로 덮어쓴다. 같은 세기로 몇 번을 걸어도
    /// 피해가 1도 오르지 않는다는 뜻이다 — "계속 걸어서 키운다"가 성립하지 않아 출혈이 통화 노릇을 못 했다.
    /// 기존 Apply는 그대로 둔다(랜슬롯 파츠의 전염 등은 중첩이 아니라 이식이라 top-up이 맞다).
    /// </summary>
    public static void ApplyStacked(GameObject target, float dpsPerStack, float duration, int maxStacks, GameObject instigator)
    {
        if (target == null || dpsPerStack <= 0f || duration <= 0f) return;
        var mb = target.GetComponentInParent<MonsterBase>();
        if (mb == null || mb.Status == null) return;

        float perStack = dpsPerStack * TickInterval;
        float cap      = perStack * Mathf.Max(1, maxStacks);
        float next     = Mathf.Min(mb.Status.GetDotDamagePerTick(Id) + perStack, cap);

        int ticks = Mathf.Max(1, Mathf.RoundToInt(duration / TickInterval));
        mb.Status.ApplyDot(Id, next, TickInterval, ticks, instigator);
    }

    /// <summary>대상의 남은 출혈 총피해(없으면 0). 전염 시 옮길 양 계산에 쓴다.</summary>
    public static float Remaining(GameObject target)
    {
        if (target == null) return 0f;
        var mb = target.GetComponentInParent<MonsterBase>();
        return mb != null && mb.Status != null ? mb.Status.GetRemainingDotDamage(Id) : 0f;
    }

    /// <summary>출혈 잔량의 fraction만큼을 소모하고 그 피해 가치를 반환한다(피해는 넣지 않는다). id를 밖으로 새지 않게 하는 창구.</summary>
    public static float Consume(GameObject target, float fraction)
    {
        if (target == null) return 0f;
        var mb = target.GetComponentInParent<MonsterBase>();
        return mb != null && mb.Status != null ? mb.Status.ConsumeDot(Id, fraction) : 0f;
    }
}
