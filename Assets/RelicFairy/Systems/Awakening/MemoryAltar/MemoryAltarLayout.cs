using System.Collections.Generic;
using UnityEngine;

/// <summary>노드 한 개가 트리에서 앉을 자리(제단 가운데가 원점, 위가 +y).</summary>
public readonly struct AltarNodePlacement
{
    public readonly Vector2 Position;
    /// <summary>가운데에서 몇 칸째(시기 띠 시작 + 시기 안 깊이). 가운데 기억 노드는 0.</summary>
    public readonly int     Depth;
    /// <summary>노드의 시기(고리) 1~3.</summary>
    public readonly int     Era;
    public readonly float   Diameter;

    public AltarNodePlacement(Vector2 position, int depth, int era, float diameter)
    {
        Position = position;
        Depth    = depth;
        Era      = era;
        Diameter = diameter;
    }
}

/// <summary>
/// 기억의 제단 트리의 <b>자동 배치</b>. 화면을 모르는 순수 계산이다 — 노드를 추가해도 자리는 여기서 다시 잡힌다.
///
/// <para><b>10-14 강화형</b> — 반지름 = <b>갈래 포인트 고리</b>(0 · 3 · 8 · 15포인트에 열리는 칸이 안에서 바깥으로). 같은 고리 칸은 옆으로 나란히.
/// 선은 앞 고리의 가장 가까운 칸에서 잇는 <b>자리 표시</b>일 뿐 사는 순서가 아니다(살 수 있는가는 갈래 포인트 — 사용자 「깊이에 따라 해금은 폐기」).
/// 아래 옛 규칙(시기 띠 · 부모 사슬)은 10-14에 걷었다.</para>
/// <para><b>규칙(10-02 재설계 「기억을 모시는 제단」)</b> — 바깥 갈래 넷이 90°씩 십자로 퍼진다(위 무기 · 오른쪽 서약 · 아래 여정 · 왼쪽 룬).
/// 반지름은 <b>시기 띠</b>로 나뉜다: 봉인기 노드가 안쪽 띠, 해방기 · 악몽이 차례로 바깥 띠. 띠 안에서는 같은 시기 부모를 따라 한 칸씩 나간다.
/// (예전엔 트리 깊이 하나가 반지름이었는데, 그러면 엔딩 뒤 노드 「심연 입장」이 부모 「챕터 4」 바로 옆 — 봉인기 띠 안에 끼었다.)
/// 갈래 안 가지는 잎의 수로 나눈 차선이 옆으로 벌린다(부모는 자식 차선의 가운데). 가운데 유물의 기억 노드 셋은 갈래 사이 대각선에 앉는다.
/// 가로가 넓은 화면이라 고리는 가로로 긴 타원이다.</para>
/// </summary>
public static class MemoryAltarLayout
{
    // ── Constants ────────────────────────────────────────
    /// <summary>가운데 제단 문양의 반지름.</summary>
    public const float CenterRadius = 80f;
    /// <summary>한 칸(포인트 고리 하나)의 거리. 10-14 강화형 105 · 차선 95 · 기억 120 — 룬 첫 고리 칸 셋이 가운데 기억 노드에 붙었고(5px)
    /// 원석 획득 ↔ Epic 룬 확률 이름표가 겹쳤다(실측) → 오프라인 흉내로 고른 값(최소 간격 22px · 이름표 겹침 0 · 글자 폭 ±20%에도 0).</summary>
    public const float RingStep     = 105f;
    /// <summary>같은 고리 안 옆자리 간격.</summary>
    public const float LaneSpacing  = 95f;
    /// <summary>가운데 기억 노드가 앉는 반지름(제단 문양 바깥 · 첫 칸 안쪽).</summary>
    public const float MemoryRadius = 120f;
    /// <summary>고리를 가로로 늘리는 비(화면이 가로로 넓다).</summary>
    public const float ScaleX       = 1.18f;
    public const float ScaleY       = 0.90f;

    // ── Static ───────────────────────────────────────────
    /// <summary>화면 순서 — 위에서 시계 방향(바깥 갈래만; 가운데 유물의 기억은 따로).</summary>
    public static readonly AltarBranch[] BranchOrder =
        { AltarBranch.Weapon, AltarBranch.Covenant, AltarBranch.Journey, AltarBranch.Rune };

    private static readonly Dictionary<string, string> s_layoutParent = new(40);   // 선을 그을 앞 칸(null = 가운데) — Compute가 채운다

    // ── Public Methods ───────────────────────────────────

    /// <summary>노드 크기 등급의 지름(px).</summary>
    public static float Diameter(AltarNodeSize size) => size switch
    {
        AltarNodeSize.Small    => 44f,
        AltarNodeSize.Keystone => 72f,
        _                      => 56f,
    };

    /// <summary>갈래 가운데 각도(도, 0 = 오른쪽 · 반시계 +). 위(90°)에서 시계 방향으로 90°씩.</summary>
    public static float BranchAngle(AltarBranch branch)
    {
        int i = System.Array.IndexOf(BranchOrder, branch);
        return i < 0 ? 45f : 90f - 90f * i;
    }

    /// <summary>가운데 기억 노드 각도 — 갈래 사이(1 오른쪽 위 · 2 왼쪽 위 · 3 왼쪽 아래). 45°는 무기 뿌리와 붙어 30° 쪽으로 비켰다(10-02 실측).</summary>
    public static float MemoryAngle(int era) => era switch { 1 => 30f, 2 => 150f, _ => 210f };

    /// <summary>칸 <paramref name="level"/>(소수 허용)의 타원 반지름(가로, 세로).</summary>
    public static Vector2 RingRadii(float level)
    {
        float r = CenterRadius + level * RingStep;
        return new Vector2(r * ScaleX, r * ScaleY);
    }

    /// <summary>갈래 포인트 고리 <paramref name="tier"/>(1 ~ 4)의 바깥 경계 칸 — 그 고리 선을 그리는 자리(10-14).</summary>
    public static float TierRingLevel(int tier) => Mathf.Clamp(tier, 1, 4) + 0.5f;

    /// <summary>선을 그을 앞 칸 id(null = 가운데에서). 사는 순서가 아니라 자리 표시다 — <see cref="Compute"/> 뒤에 부른다.</summary>
    public static string LayoutParent(string id) => id != null && s_layoutParent.TryGetValue(id, out var p) ? p : null;

    /// <summary>갈래 방향으로 거리 <paramref name="along"/>만큼 나간 점(갈래 이름표 자리 등).</summary>
    public static Vector2 AlongBranch(AltarBranch branch, float along)
    {
        float a = BranchAngle(branch) * Mathf.Deg2Rad;
        return new Vector2(Mathf.Cos(a) * along * ScaleX, Mathf.Sin(a) * along * ScaleY);
    }

    /// <summary>
    /// 전 노드의 자리. 시기 띠를 먼저 재고, 바깥 갈래마다 따로 놓는다 — 갈래 사이를 잇는 선은 없다(부모는 같은 갈래).
    /// </summary>
    public static Dictionary<string, AltarNodePlacement> Compute(IReadOnlyList<MemoryAltarNode> nodes)
    {
        var result = new Dictionary<string, AltarNodePlacement>(nodes.Count);
        s_layoutParent.Clear();

        foreach (var branch in BranchOrder)
        {
            var list = new List<MemoryAltarNode>(12);
            foreach (var n in nodes) if (n.Branch == branch) list.Add(n);
            if (list.Count == 0) continue;
            PlaceBranch(branch, list, result);
        }

        foreach (var n in nodes)
        {
            if (n.Branch != AltarBranch.Memory) continue;
            float a = MemoryAngle(n.Era) * Mathf.Deg2Rad;
            var   p = new Vector2(Mathf.Cos(a) * MemoryRadius * ScaleX, Mathf.Sin(a) * MemoryRadius * ScaleY);
            result[n.Id] = new AltarNodePlacement(p, 0, n.Era, Diameter(n.Size));
        }
        return result;
    }

    // ── Private Methods ──────────────────────────────────

    /// <summary>
    /// 갈래 하나(10-14 강화형) — 고리 = 열리는 갈래 포인트 단계(<see cref="MemoryAltarCatalog.Tier"/>). 같은 고리 칸은 정의 순서로 옆으로 나란히(가운데 맞춤),
    /// 선은 앞 고리(있는 것 중 가장 바깥)의 옆자리가 가장 가까운 칸에서 잇는다(첫 고리는 가운데에서).
    /// </summary>
    private static void PlaceBranch(AltarBranch branch, List<MemoryAltarNode> list, Dictionary<string, AltarNodePlacement> result)
    {
        float a    = BranchAngle(branch) * Mathf.Deg2Rad;
        var   dir  = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
        var   perp = new Vector2(-dir.y, dir.x);

        var byTier = new SortedDictionary<int, List<MemoryAltarNode>>();
        foreach (var n in list)
        {
            int t = MemoryAltarCatalog.Tier(n);
            if (!byTier.TryGetValue(t, out var row)) byTier[t] = row = new List<MemoryAltarNode>(4);
            row.Add(n);
        }

        var lateral = new Dictionary<string, float>(list.Count);
        List<MemoryAltarNode> prev = null;
        foreach (var kv in byTier)
        {
            var row = kv.Value;
            float center = (row.Count - 1) * 0.5f;
            for (int i = 0; i < row.Count; i++)
            {
                var   n   = row[i];
                float lat = (i - center) * LaneSpacing;
                lateral[n.Id] = lat;
                float along = CenterRadius + kv.Key * RingStep;
                var   p     = dir * along + perp * lat;
                result[n.Id] = new AltarNodePlacement(new Vector2(p.x * ScaleX, p.y * ScaleY), kv.Key, n.Era, Diameter(n.Size));

                string parent = null;
                if (prev != null)
                {
                    float best = float.MaxValue;
                    foreach (var q in prev)
                    {
                        float d = Mathf.Abs(lateral[q.Id] - lat);
                        if (d < best) { best = d; parent = q.Id; }
                    }
                }
                s_layoutParent[n.Id] = parent;
            }
            prev = row;
        }
    }
}
