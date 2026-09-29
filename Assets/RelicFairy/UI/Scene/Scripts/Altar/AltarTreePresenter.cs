using System.Collections.Generic;
using UnityEngine;

/// <summary>트리에서 노드를 어떻게 보이게 할지 — 상태 5가지 + 자물쇠 예외.</summary>
public enum AltarNodeVisual
{
    /// <summary>두 칸 이상 앞 — 흐린 점만(이름 없음). 나무의 크기만 느껴진다.</summary>
    Hidden,
    /// <summary>선 끝 한 칸 앞 — 이름까지 보이지만 잠김(앞 노드를 사면 열린다).</summary>
    Next,
    /// <summary>열렸지만 정수가 모자라다.</summary>
    Open,
    /// <summary>지금 살 수 있다(불).</summary>
    Affordable,
    /// <summary>앞 노드는 다 열렸는데 자물쇠 조건(챕터 4 · 심연)이 남았다.</summary>
    OpenLocked,
    /// <summary>산 노드.</summary>
    Bought,
}

/// <summary>노드 하나의 화면 값.</summary>
public struct AltarNodeViewModel
{
    public MemoryAltarNode Node;
    public AltarNodeState  State;
    public AltarNodeVisual Visual;
}

/// <summary>오른쪽 상세 패널의 글.</summary>
public struct AltarDetailModel
{
    public string Header;       // 「룬 · 확장」
    public string Name;
    public string SizeBadge;    // 「열쇠」 / 「작은 넓힘」 / ""
    public string Effect;       // A → B
    public string CostLine;     // 값(할인 취소선 포함)
    public string Condition;    // 조건 줄(진척)
    public string Reason;       // 못 사는 이유 · 상태 한 줄
    public bool   ReasonInfo;   // 안내(해금된 노드 — 이어지는 노드 · 인장 상태)면 경고색이 아니다
    public string ButtonLabel;
    public bool   ButtonEnabled;
}

/// <summary>가운데 제단 문양의 「다음 목표」.</summary>
public struct AltarCenterModel
{
    public string Title;     // 「다음」 / 「모두 열었다」
    public string GoalName;
    public string Status;    // 「해금 가능」 / 「-432」
    public float  Fill;      // 정수 / 값
    public bool   Ready;
}

/// <summary>
/// 기억의 제단 트리 — <b>상태 → 화면 값</b>. 규칙은 <see cref="MemoryAltarService"/>가 갖고, 여기선 보이는 방식만 정한다
/// (Provider = 서비스 · Presenter = 여기 · View = <see cref="AltarTreeView"/> · <see cref="AltarDetailPanel"/>).
/// </summary>
public sealed class AltarTreePresenter
{
    // ── Private ──────────────────────────────────────────
    private readonly Dictionary<string, AltarNodeViewModel> _models = new(40);

    // ── Public Methods ───────────────────────────────────

    /// <summary>전 노드의 화면 값을 새로 만든다(해금 · 정수 변화마다).</summary>
    public IReadOnlyDictionary<string, AltarNodeViewModel> Build()
    {
        _models.Clear();
        var all = MemoryAltarCatalog.All;
        // 1차 — 서비스 상태만으로 정해지는 것
        foreach (var node in all)
        {
            var s = MemoryAltarService.GetState(node);
            _models[node.Id] = new AltarNodeViewModel { Node = node, State = s, Visual = BaseVisual(s) };
        }
        // 2차 — 부모가 전부 「산 것 또는 열린 것」이면 선 끝 한 칸 앞(Next), 아니면 가려짐
        foreach (var node in all)
        {
            var m = _models[node.Id];
            if (m.Visual != AltarNodeVisual.Hidden) continue;
            bool near = true;
            foreach (var p in node.Parents)
            {
                if (!_models.TryGetValue(p, out var pm)) continue;
                if (pm.Visual == AltarNodeVisual.Hidden || pm.Visual == AltarNodeVisual.Next) { near = false; break; }
            }
            if (near) { m.Visual = AltarNodeVisual.Next; _models[node.Id] = m; }
        }
        return _models;
    }

    /// <summary>
    /// 창을 열 때 먼저 가리킬 노드 — 지금 살 수 있는 것 중 가장 싼 것, 없으면 「다음 목표」, 그것도 없으면 첫 노드.
    /// </summary>
    public MemoryAltarNode AutoFocus()
    {
        MemoryAltarNode best = null; int bestCost = int.MaxValue;
        foreach (var m in _models.Values)
            if (m.Visual == AltarNodeVisual.Affordable && m.State.Cost < bestCost) { best = m.Node; bestCost = m.State.Cost; }
        if (best != null) return best;
        var goal = MemoryAltarService.GetNextGoal();
        if (goal != null) return goal.Value.Node;
        return MemoryAltarCatalog.All.Count > 0 ? MemoryAltarCatalog.All[0] : null;
    }

    public AltarCenterModel Center(int essence)
    {
        var next = MemoryAltarService.GetNextGoal();
        if (next == null)
            return new AltarCenterModel { Title = "모두 열었다", GoalName = "이제 남은 것은 깊이뿐", Status = "", Fill = 1f, Ready = false };

        var g = next.Value;
        int left = Mathf.Max(0, g.Cost - essence);
        return new AltarCenterModel
        {
            Title    = "다음",
            GoalName = g.Node.DisplayName,
            Status   = left <= 0 ? "해금 가능" : $"-{left:N0}",
            Fill     = g.Cost > 0 ? Mathf.Clamp01((float)essence / g.Cost) : 1f,
            Ready    = left <= 0,
        };
    }

    /// <summary>해금된 노드에서 이어지는 아직 안 연 노드 — 「다음에 무엇이 열리나」를 선이 아니라 글로도(09-29 실측: 상세 칸이 비었다).</summary>
    private static string NextLine(MemoryAltarNode node)
    {
        var names = new System.Collections.Generic.List<string>();
        foreach (var c in MemoryAltarCatalog.Children(node))
            if (!MemoryAltarService.IsUnlocked(c.Id)) names.Add($"「{c.DisplayName}」");
        return names.Count > 0 ? "이어서 열 수 있다 · " + string.Join(" ", names) : "";
    }

    public AltarDetailModel Detail(MemoryAltarNode node, int essence, bool readOnly)
    {
        var d = new AltarDetailModel();
        if (node == null) return d;
        var s = MemoryAltarService.GetState(node);

        d.Header    = $"{MemoryAltarCatalog.BranchLabel(node.Branch)} · {MemoryAltarCatalog.RingLabel(MemoryAltarCatalog.Depth(node))}";
        d.Name      = node.DisplayName;
        d.SizeBadge = node.Size switch { AltarNodeSize.Keystone => "열쇠", AltarNodeSize.Small => "작은 넓힘", _ => "" };
        d.Effect    = node.Description;
        d.CostLine  = CostLine(node, s);
        d.Condition = ConditionLine(node, s);

        if (s.Unlocked)
        {
            d.ReasonInfo = true;
            if (node.Id == MemoryAltarCatalog.SigilAscetic)
            {
                bool on = AsceticSigilService.Active;
                d.ButtonLabel   = on ? "인장 해제" : "인장 착용";
                d.ButtonEnabled = !readOnly;
                d.Reason        = on ? "지금 착용 중 · 보상 -1개 / 정수 ×1.6" : "켜면 다음 런부터 적용된다";
            }
            else if (node.Id == MemoryAltarCatalog.PartsInherit)
            {
                string part = PartInheritanceService.InheritedPartName;
                d.ButtonLabel = "해금됨";
                d.Reason      = string.IsNullOrEmpty(part) ? "런을 마치면 마지막에 고른 파츠가 이어진다" : $"지금 이어받는 파츠 · {part}";
            }
            else
            {
                d.ButtonLabel = "해금됨";
                d.Reason      = NextLine(node);
            }
            return d;
        }

        if (s.BlockedByChain)
        {
            d.ButtonLabel = "앞 노드 먼저";
            d.Reason      = s.MissingParent != null ? $"선으로 이어진 「{s.MissingParent.DisplayName}」을 먼저 연다" : "";
        }
        else if (s.BlockedByRequirement)
        {
            d.ButtonLabel = "선행 조건 필요";
            d.Reason      = node.ConditionLabel;
        }
        else if (s.CanBuy)
        {
            d.ButtonLabel   = readOnly ? "제단에서 해금" : $"해금   {s.Cost:N0} ◆";
            d.ButtonEnabled = !readOnly;
            d.Reason        = readOnly ? "로비에서는 볼 수만 있다" : "";
        }
        else
        {
            d.ButtonLabel = "정수 부족";
            d.Reason      = $"{s.Cost - essence:N0} 모자람";
        }
        return d;
    }

    /// <summary>노드 아래 값 칩 — 열린 노드만(산 · 가려진 · 한 칸 앞은 없음).</summary>
    public static string CostChip(in AltarNodeViewModel m) =>
        m.Visual == AltarNodeVisual.Affordable || m.Visual == AltarNodeVisual.Open ? $"{m.State.Cost:N0}◆" : "";

    // ── Private Methods ──────────────────────────────────

    private static AltarNodeVisual BaseVisual(in AltarNodeState s)
    {
        if (s.Unlocked)             return AltarNodeVisual.Bought;
        if (s.BlockedByChain)       return AltarNodeVisual.Hidden;   // 2차에서 Next로 올라갈 수 있다
        if (s.BlockedByRequirement) return AltarNodeVisual.OpenLocked;
        return s.CanBuy ? AltarNodeVisual.Affordable : AltarNodeVisual.Open;
    }

    private static string CostLine(MemoryAltarNode node, in AltarNodeState s)
    {
        if (s.Unlocked) return "<color=#63D9BF>해금됨</color>";
        if (!node.HasCondition || node.ConditionRequired || node.DiscountCost == node.BaseCost)
            return $"{s.Cost:N0} ◆";
        return s.ConditionMet
            ? $"<color=#8A8594><s>{node.BaseCost:N0}</s></color>   {node.DiscountCost:N0} ◆"
            : $"{node.BaseCost:N0} ◆   <color=#8A8594>조건을 이루면 {node.DiscountCost:N0}</color>";
    }

    private static string ConditionLine(MemoryAltarNode node, in AltarNodeState s)
    {
        if (s.Unlocked) return "";
        if (!node.HasCondition) return "<color=#8A8594>조건 없음 — 언제든 열 수 있다</color>";
        if (node.ConditionRequired)
            return s.ConditionMet
                ? $"<color=#63D9BF>◆</color> 선행 조건 · {node.ConditionLabel}"
                : $"선행 조건 · {node.ConditionLabel}   <color=#E8C07A>{s.Progress}/{s.Target}</color>";
        return s.ConditionMet
            ? $"<color=#63D9BF>◆</color> {node.ConditionLabel} — 할인 적용"
            : $"할인 조건 · {node.ConditionLabel}   <color=#E8C07A>{s.Progress}/{s.Target}</color>";
    }
}
