using System.Collections.Generic;

/// <summary>
/// 유물 성장 v2 공명 — 두 유물이 <b>같은 모양</b>(그 자리 조각 수)으로 깊어진다(설계 v2 §2-2 · §3-2 · 10-22 E1 C1 「공명 규칙 한 모양」).
/// <list type="bullet">
/// <item>가웨인 「시간대 공명」 — 같은 시간대에 매달린 조각(궤적 조각은 두 시간대에) + 메아리 수가 2 · 4에 닿으면 그 시간대가 깊어진다.</item>
/// <item>랜슬롯 「광기 칸 공명」 — 광기 10 · 20 · 30 · 40 칸에 매달린 조각 + 메아리 수 2 · 3 · 4(순서 · 빈틈 상관없음 — 옛 「10부터 이어진 계단」을 걷었다).</item>
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

    /// <summary>랜슬롯 광기 칸 하나에 조각(또는 메아리)이 있는가 — HUD 칸 표식 · 메아리 자리 고르기.</summary>
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

    /// <summary>랜슬롯 광기 칸 조각 수 — 광기 10 · 20 · 30 · 40 칸에 매달린 조각 + 메아리(순서 · 빈틈 상관없음 · 10-22 E1 D7).</summary>
    public static int LancelotCount(PlayerLoadout l, System.Func<string, RelicPartEntry> lookup = null)
    {
        if (l == null) return 0;
        int n = 0;
        foreach (var rung in RelicPartAnchor.LancelotRungs) n += l.GetRelicEcho(rung);
        var ids = l.RelicPartIds;
        for (int i = 0; i < ids.Count; i++)
        {
            var e = Get(ids[i], lookup);
            if (e != null && e.relic_id == "lancelot" && System.Array.IndexOf(RelicPartAnchor.LancelotRungs, e.anchor) >= 0) n++;
        }
        return n;
    }

    /// <summary>랜슬롯 광기 칸 공명이 쓰는 단계(2 미만이면 0 · 4가 끝).</summary>
    public static int LancelotTier(int count) => count >= 2 ? (count > 4 ? 4 : count) : 0;

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
                if (tierAfter > tierBefore) return $"{RelicPartAnchor.Label(phase)} 조각 {before} → {before + 1}: {GawainTierText(phase, tierAfter)}";
            }
            return null;
        }
        if (relicId == "lancelot")
        {
            if (System.Array.IndexOf(RelicPartAnchor.LancelotRungs, anchor) < 0) return null;
            int before = LancelotCount(l, lookup);
            if (LancelotTier(before + 1) > LancelotTier(before)) return $"광기 10~40 조각 {before} → {before + 1}: {LadderText(before + 1)}";
            return null;
        }
        return null;
    }

    /// <summary>가웨인 시간대 단계 효과 한 줄(카드 · 보유 보기 · 첫 발동 자막).</summary>
    public static string GawainTierText(string phase, int tier) => (phase, tier) switch
    {
        (RelicPartAnchor.Dawn, 2) => "적에게 쌓이는 태양흔 최대치 3 → 5",
        (RelicPartAnchor.Dawn, 4) => "새벽이 길다 — 여명이 끝나도 해가 최대 10초 기다리고, Q를 누르는 순간 정오가 열린다",
        (RelicPartAnchor.Noon, 2) => "태양흔이 터지면 2m 안 다른 태양흔 적에게 1개씩 더 쌓인다(최대치까지)",
        (RelicPartAnchor.Noon, 4) => "한낮이 길다 — 정오에도 맞히면 태양흔이 쌓이고, 가득 차면 그 자리에서 터진다",
        (RelicPartAnchor.Dusk, 2) => "황혼에 붙인 불이 꺼지지 않고 다음 여명 첫 1초까지 탄다",
        (RelicPartAnchor.Dusk, 4) => "밤이 오지 않는다 — 황혼이 끝나면 3초짜리 정오가 한 번 더 온다(태양흔이 터지고 Q)",
        _ => string.Empty,
    };

    /// <summary>랜슬롯 광기 칸 공명 효과 한 줄(조각 수 2 · 3 · 4 — 수치는 옛 이어진 계단과 같다: 받침 = 광기 20 · 3부터 30).</summary>
    public static string LadderText(int count) => count switch
    {
        2 => "받침 — 광기가 줄어도 20 아래로 떨어지지 않는다",
        3 => "재출발 — 광란이 끝나면 광기 30에서 다시 시작(받침도 30)",
        4 => "두 번째 광란 — 광란 중 광기가 다시 40이 되면 광란이 이어지고 Q(심판)를 1번 더 쓴다",
        _ => string.Empty,
    };
}
