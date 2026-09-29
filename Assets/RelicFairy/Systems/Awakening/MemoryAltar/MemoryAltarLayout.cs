using System.Collections.Generic;
using UnityEngine;

/// <summary>노드 한 개가 트리에서 앉을 자리(제단 가운데가 원점, 위가 +y).</summary>
public readonly struct AltarNodePlacement
{
    public readonly Vector2 Position;
    public readonly int     Depth;
    public readonly float   Diameter;

    public AltarNodePlacement(Vector2 position, int depth, float diameter)
    {
        Position = position;
        Depth    = depth;
        Diameter = diameter;
    }
}

/// <summary>
/// 기억의 제단 트리의 <b>자동 배치</b>(09-29 개편). 화면을 모르는 순수 계산이다 — 노드를 추가해도 자리는 여기서 다시 잡힌다.
///
/// <para><b>규칙</b> — 갈래 다섯이 72°씩 부채꼴을 나눠 가진다(위에서 시계 방향: 룬 · 서약 · 장비 · 원거리 · 여정).
/// 가운데에서 몇 칸째인가(<see cref="MemoryAltarCatalog.Depth"/>)가 반지름이고, 갈래 안 가지는 잎의 수로 나눈 차선이
/// 옆으로 벌린다(부모는 자식 차선의 가운데). 가로가 넓은 화면이라 고리는 가로로 긴 타원이다.</para>
/// </summary>
public static class MemoryAltarLayout
{
    // ── Constants ────────────────────────────────────────
    /// <summary>가운데 제단 문양의 반지름.</summary>
    public const float CenterRadius = 80f;
    /// <summary>깊이 한 칸의 거리.</summary>
    public const float RingStep     = 116f;
    /// <summary>같은 갈래 안 차선 간격.</summary>
    public const float LaneSpacing  = 100f;
    /// <summary>고리를 가로로 늘리는 비(화면이 가로로 넓다).</summary>
    public const float ScaleX       = 1.18f;
    public const float ScaleY       = 0.90f;

    /// <summary>화면 순서 — 위에서 시계 방향.</summary>
    public static readonly AltarBranch[] BranchOrder =
        { AltarBranch.Rune, AltarBranch.Covenant, AltarBranch.Gear, AltarBranch.Ranged, AltarBranch.Journey };

    // ── Public Methods ───────────────────────────────────

    /// <summary>노드 크기 등급의 지름(px).</summary>
    public static float Diameter(AltarNodeSize size) => size switch
    {
        AltarNodeSize.Small    => 44f,
        AltarNodeSize.Keystone => 72f,
        _                      => 56f,
    };

    /// <summary>갈래 부채꼴 가운데 각도(도, 0 = 오른쪽 · 반시계 +). 위(90°)에서 시계 방향으로 72°씩.</summary>
    public static float BranchAngle(AltarBranch branch)
    {
        int i = System.Array.IndexOf(BranchOrder, branch);
        return 90f - 72f * Mathf.Max(0, i);
    }

    /// <summary>깊이 <paramref name="depth"/> 고리의 타원 반지름(가로, 세로).</summary>
    public static Vector2 RingRadii(int depth)
    {
        float r = CenterRadius + depth * RingStep;
        return new Vector2(r * ScaleX, r * ScaleY);
    }

    /// <summary>갈래 방향으로 거리 <paramref name="along"/>만큼 나간 점(갈래 이름표 자리 등).</summary>
    public static Vector2 AlongBranch(AltarBranch branch, float along)
    {
        float a = BranchAngle(branch) * Mathf.Deg2Rad;
        return new Vector2(Mathf.Cos(a) * along * ScaleX, Mathf.Sin(a) * along * ScaleY);
    }

    /// <summary>
    /// 전 노드의 자리. 갈래마다 따로 계산한다 — 갈래 사이를 잇는 선은 없다(부모는 같은 갈래).
    /// </summary>
    public static Dictionary<string, AltarNodePlacement> Compute(IReadOnlyList<MemoryAltarNode> nodes)
    {
        var result = new Dictionary<string, AltarNodePlacement>(nodes.Count);
        foreach (var branch in BranchOrder)
        {
            var list = new List<MemoryAltarNode>(8);
            foreach (var n in nodes) if (n.Branch == branch) list.Add(n);
            if (list.Count == 0) continue;
            PlaceBranch(branch, list, result);
        }
        return result;
    }

    // ── Private Methods ──────────────────────────────────

    /// <summary>
    /// 갈래 하나 — 첫 부모를 배치용 부모로 삼은 나무를 만들고, 잎마다 차선 한 칸, 부모는 자식 차선의 가운데.
    /// </summary>
    private static void PlaceBranch(AltarBranch branch, List<MemoryAltarNode> list,
                                    Dictionary<string, AltarNodePlacement> result)
    {
        var inBranch = new HashSet<string>();
        foreach (var n in list) inBranch.Add(n.Id);

        var kids  = new Dictionary<string, List<MemoryAltarNode>>();
        var roots = new List<MemoryAltarNode>();
        foreach (var n in list)
        {
            string layoutParent = null;
            foreach (var p in n.Parents) if (inBranch.Contains(p)) { layoutParent = p; break; }
            if (layoutParent == null) { roots.Add(n); continue; }
            if (!kids.TryGetValue(layoutParent, out var k)) kids[layoutParent] = k = new List<MemoryAltarNode>(3);
            k.Add(n);
        }

        var lane = new Dictionary<string, float>(list.Count);
        float next = 0f;
        foreach (var r in roots) AssignLane(r, kids, lane, ref next);
        float center = (next - 1f) * 0.5f;

        float a    = BranchAngle(branch) * Mathf.Deg2Rad;
        var   dir  = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
        var   perp = new Vector2(-dir.y, dir.x);

        foreach (var n in list)
        {
            int   depth   = MemoryAltarCatalog.Depth(n);
            float along   = CenterRadius + depth * RingStep;
            float lateral = (lane[n.Id] - center) * LaneSpacing;
            var   p       = dir * along + perp * lateral;
            result[n.Id]  = new AltarNodePlacement(new Vector2(p.x * ScaleX, p.y * ScaleY), depth, Diameter(n.Size));
        }
    }

    private static void AssignLane(MemoryAltarNode node, Dictionary<string, List<MemoryAltarNode>> kids,
                                   Dictionary<string, float> lane, ref float next)
    {
        if (!kids.TryGetValue(node.Id, out var children) || children.Count == 0)
        {
            lane[node.Id] = next;
            next += 1f;
            return;
        }
        foreach (var c in children) AssignLane(c, kids, lane, ref next);
        lane[node.Id] = (lane[children[0].Id] + lane[children[children.Count - 1].Id]) * 0.5f;
    }
}
