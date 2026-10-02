using System.Collections.Generic;

/// <summary>
/// 유물 성장 v2 공명 — 두 유물이 <b>다른 모양</b>으로 서로를 증폭한다(설계 v2 §2-2 · §3-2).
/// <list type="bullet">
/// <item>가웨인 「시간대 공명」 — 같은 시간대에 매달린 조각(궤적 조각은 두 시간대에) + 메아리 수가 2 · 4에 닿으면 그 시간대가 깊어진다.</item>
/// <item>랜슬롯 「계단 공명」 — 광기 10 → 20 → 30 → 40 중 <b>아래부터 끊김 없이</b> 조각(또는 메아리 「빈 받침」)이 있는 계단 수 2 · 3 · 4.</item>
/// </list>
/// 상태 없는 계산만 한다 — 효과 · HUD · 카드 미리 보기가 같은 값을 읽는다.
/// </summary>
public static class RelicResonance
{
    /// <summary>조회 — 주입받은 함수가 있으면 그것, 없으면 데이터 매니저.</summary>
    private static RelicPartEntry Get(string id, System.Func<string, RelicPartEntry> lookup)
        => lookup != null ? lookup(id) : Managers.RelicParts?.GetById(id);

    /// <summary>가웨인 시간대 <paramref name="phase"/>(dawn · noon · dusk)에 매달린 조각 + 메아리 수.</summary>
    public static int GawainCount(PlayerLoadout l, string phase, System.Func<string, RelicPartEntry> lookup = null)
    {
        if (l == null) return 0;
        int n = l.GetRelicEcho(phase);
        var ids = l.RelicPartIds;
        for (int i = 0; i < ids.Count; i++)
        {
            var e = Get(ids[i], lookup);
            if (e != null && e.relic_id == "gawain" && RelicPartAnchor.Covers(e.anchor, phase)) n++;
        }
        return n;
    }

    /// <summary>개수 → 공명 단계(0 · 2 · 4).</summary>
    public static int GawainTier(int count) => count >= 4 ? 4 : count >= 2 ? 2 : 0;

    /// <summary>가웨인 시간대의 지금 공명 단계.</summary>
    public static int GawainTierOf(PlayerLoadout l, string phase) => GawainTier(GawainCount(l, phase));

    /// <summary>랜슬롯 계단 하나에 조각(또는 메아리 빈 받침)이 있는가.</summary>
    public static bool LancelotRungFilled(PlayerLoadout l, string rung, System.Func<string, RelicPartEntry> lookup = null)
    {
        if (l == null) return false;
        if (l.GetRelicEcho(rung) > 0) return true;
        var ids = l.RelicPartIds;
        for (int i = 0; i < ids.Count; i++)
        {
            var e = Get(ids[i], lookup);
            if (e != null && e.relic_id == "lancelot" && e.anchor == rung) return true;
        }
        return false;
    }

    /// <summary>랜슬롯 이어진 계단 수(10부터 끊김 없이, 0~4).</summary>
    public static int LancelotLadder(PlayerLoadout l, System.Func<string, RelicPartEntry> lookup = null)
    {
        int n = 0;
        foreach (var rung in RelicPartAnchor.LancelotRungs)
        {
            if (!LancelotRungFilled(l, rung, lookup)) break;
            n++;
        }
        return n;
    }

    /// <summary>랜슬롯 계단 공명이 쓰는 단계(2 미만이면 0).</summary>
    public static int LancelotTier(int ladder) => ladder >= 2 ? ladder : 0;

    /// <summary>
    /// 이 조각(또는 메아리 자리)을 더하면 넘는 공명 단계 한 줄 — 카드 미리 보기. 넘는 게 없으면 null.
    /// </summary>
    public static string PreviewLine(PlayerLoadout l, string relicId, string anchor, System.Func<string, RelicPartEntry> lookup = null)
    {
        if (l == null || string.IsNullOrEmpty(anchor)) return null;
        if (relicId == "gawain")
        {
            foreach (var phase in RelicPartAnchor.GawainPhases)
            {
                if (!RelicPartAnchor.Covers(anchor, phase)) continue;
                int before = GawainCount(l, phase, lookup);
                int tierBefore = GawainTier(before), tierAfter = GawainTier(before + 1);
                if (tierAfter > tierBefore) return $"{RelicPartAnchor.Label(phase)} 공명 {before} → {before + 1}: {GawainTierText(phase, tierAfter)}";
            }
            return null;
        }
        if (relicId == "lancelot")
        {
            if (System.Array.IndexOf(RelicPartAnchor.LancelotRungs, anchor) < 0) return null;
            if (LancelotRungFilled(l, anchor, lookup)) return null;
            int before = LancelotLadder(l, lookup);
            int after = LadderIfFilled(l, anchor, lookup);
            if (LancelotTier(after) > LancelotTier(before)) return $"이어진 계단 {before} → {after}: {LadderText(after)}";
            return null;
        }
        return null;
    }

    /// <summary>가웨인 시간대 단계 효과 한 줄(카드 · 보유 보기 · 첫 발동 자막).</summary>
    public static string GawainTierText(string phase, int tier) => (phase, tier) switch
    {
        (RelicPartAnchor.Dawn, 2) => "태양흔 상한 3 → 5",
        (RelicPartAnchor.Dawn, 4) => "새벽이 길다 — 여명 끝에 해를 붙들고, 낙일로 원하는 순간 정오를 연다",
        (RelicPartAnchor.Noon, 2) => "개화가 2m 안 다른 태양흔 적에게 튄다",
        (RelicPartAnchor.Noon, 4) => "한낮이 길다 — 정오에도 태양흔이 쌓이고 3스택이면 그 자리에서 개화",
        (RelicPartAnchor.Dusk, 2) => "황혼의 화상이 다음 여명까지 이어진다",
        (RelicPartAnchor.Dusk, 4) => "밤이 오지 않는다 — 황혼 끝에 3초짜리 짧은 정오가 한 번 더",
        _ => string.Empty,
    };

    /// <summary>랜슬롯 이어진 계단 효과 한 줄.</summary>
    public static string LadderText(int ladder) => ladder switch
    {
        2 => "받침 — 광기가 줄어도 이어진 맨 위 계단 아래로 떨어지지 않는다",
        3 => "재출발 — 광란이 끝나면 광기 30에서 다시 시작",
        4 => "두 번째 광란 — 광란 중 다시 40에 닿으면 광란이 이어지고 심판 1회 더",
        _ => string.Empty,
    };

    private static int LadderIfFilled(PlayerLoadout l, string extraRung, System.Func<string, RelicPartEntry> lookup)
    {
        int n = 0;
        foreach (var rung in RelicPartAnchor.LancelotRungs)
        {
            if (rung != extraRung && !LancelotRungFilled(l, rung, lookup)) break;
            n++;
        }
        return n;
    }
}
