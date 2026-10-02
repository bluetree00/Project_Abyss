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
    /// <summary>한 칸의 거리(시기 띠 셋이 들어가도록 09-29의 116보다 짧게).</summary>
    public const float RingStep     = 100f;
    /// <summary>같은 갈래 안 차선 간격.</summary>
    public const float LaneSpacing  = 100f;
    /// <summary>가운데 기억 노드가 앉는 반지름(제단 문양 바깥 · 첫 칸 안쪽).</summary>
    public const float MemoryRadius = 150f;
    /// <summary>고리를 가로로 늘리는 비(화면이 가로로 넓다).</summary>
    public const float ScaleX       = 1.18f;
    public const float ScaleY       = 0.90f;

    // ── Static ───────────────────────────────────────────
    /// <summary>화면 순서 — 위에서 시계 방향(바깥 갈래만; 가운데 유물의 기억은 따로).</summary>
    public static readonly AltarBranch[] BranchOrder =
        { AltarBranch.Weapon, AltarBranch.Covenant, AltarBranch.Journey, AltarBranch.Rune };

    private static readonly int[] s_eraOffset = new int[4];   // 시기 띠가 시작하기 전 칸 수
    private static readonly int[] s_eraDepth  = new int[4];   // 시기 띠 안 가장 깊은 칸

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

    /// <summary>시기 <paramref name="era"/> 띠의 바깥 경계 칸 — 그 시기 고리를 그리는 자리. <see cref="Compute"/> 뒤에 부른다.</summary>
    public static float EraRingLevel(int era)
    {
        era = Mathf.Clamp(era, 1, 3);
        return s_eraOffset[era] + Mathf.Max(1, s_eraDepth[era]) + 0.5f;
    }

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
        var local  = MeasureEraBands(nodes);

        foreach (var branch in BranchOrder)
        {
            var list = new List<MemoryAltarNode>(10);
            foreach (var n in nodes) if (n.Branch == branch) list.Add(n);
            if (list.Count == 0) continue;
            PlaceBranch(branch, list, local, result);
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
    /// 시기 띠를 잰다 — 노드마다 「같은 시기 · 같은 갈래 부모를 따라 몇 칸째인가」, 시기마다 가장 깊은 칸,
    /// 그리고 띠가 시작하는 칸(앞 시기 띠의 깊이 합). 반환 = 노드별 띠 안 깊이.
    /// </summary>
    private static Dictionary<string, int> MeasureEraBands(IReadOnlyList<MemoryAltarNode> nodes)
    {
        var byId  = new Dictionary<string, MemoryAltarNode>(nodes.Count);
        foreach (var n in nodes) byId[n.Id] = n;
        var local = new Dictionary<string, int>(nodes.Count);

        System.Array.Clear(s_eraDepth, 0, s_eraDepth.Length);
        foreach (var n in nodes)
        {
            if (n.Branch == AltarBranch.Memory) continue;
            int d = LocalDepth(n, byId, local);
            s_eraDepth[n.Era] = Mathf.Max(s_eraDepth[n.Era], d);
        }
        s_eraOffset[1] = 0;
        s_eraOffset[2] = s_eraOffset[1] + s_eraDepth[1];
        s_eraOffset[3] = s_eraOffset[2] + s_eraDepth[2];
        return local;
    }

    private static int LocalDepth(MemoryAltarNode n, Dictionary<string, MemoryAltarNode> byId, Dictionary<string, int> local)
    {
        if (local.TryGetValue(n.Id, out int cached)) return cached;
        int best = 0;
        foreach (var p in n.Parents)
            if (byId.TryGetValue(p, out var parent) && parent.Era == n.Era && parent.Branch == n.Branch)
                best = Mathf.Max(best, LocalDepth(parent, byId, local));
        local[n.Id] = best + 1;
        return best + 1;
    }

    /// <summary>
    /// 갈래 하나 — 첫 부모를 배치용 부모로 삼은 나무를 만들고, 잎마다 차선 한 칸, 부모는 자식 차선의 가운데.
    /// 반지름 = 시기 띠 시작 + 띠 안 깊이.
    /// </summary>
    private static void PlaceBranch(AltarBranch branch, List<MemoryAltarNode> list, Dictionary<string, int> local,
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
            int   depth   = s_eraOffset[n.Era] + local[n.Id];
            float along   = CenterRadius + depth * RingStep;
            float lateral = (lane[n.Id] - center) * LaneSpacing;
            var   p       = dir * along + perp * lateral;
            result[n.Id]  = new AltarNodePlacement(new Vector2(p.x * ScaleX, p.y * ScaleY), depth, n.Era, Diameter(n.Size));
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
