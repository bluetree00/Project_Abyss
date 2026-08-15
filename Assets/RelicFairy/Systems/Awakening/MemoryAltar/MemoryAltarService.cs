using System.Collections.Generic;
using UnityEngine;

/// <summary>노드 1개를 화면에 그리기 위해 필요한 전부. 뷰는 이 값만 보고 그린다.</summary>
public struct AltarNodeState
{
    public MemoryAltarNode Node;
    public bool Unlocked;

    /// <summary>할인 조건을 만족했는가.</summary>
    public bool ConditionMet;

    /// <summary>지금 지불해야 하는 값. 조건 충족이면 할인가, 아니면 기본가.</summary>
    public int Cost;

    /// <summary>조건 진척(현재/목표). 조건이 없으면 둘 다 0.</summary>
    public int Progress;
    public int Target;

    /// <summary>지금 살 수 있는가. <b>조건 미달성이어도 정수만 있으면 true</b> — 정본 §2의 시각적 보증.</summary>
    public bool CanBuy;

    /// <summary>조건이 자물쇠인 예외 노드인데 아직 조건을 못 채웠다.</summary>
    public bool BlockedByRequirement;
}

/// <summary>
/// 기억의 제단 규칙. UI와 저장 사이에 있는 얇은 계층 — 상태는 전부 <see cref="UserGameData"/>가 갖는다.
/// <para><b>핵심 규칙:</b> 조건은 자물쇠가 아니라 <b>할인</b>이다. 모든 노드는 정수만으로 열린다.
/// 그래야 "벽에 부딪힌 플레이어가 영영 다음 해금에 도달 못 하는" 데드락이 생기지 않는다(정본 §2).</para>
/// </summary>
public static class MemoryAltarService
{
    private static UserGameData Data => BackendGameData.Instance?.Data;

    // ── 조회 ────────────────────────────────────────────

    public static bool IsUnlocked(string nodeId) => Data?.IsUnlocked(nodeId) ?? false;

    public static int Essence => Data?.abyssEssence ?? 0;

    public static AltarNodeState GetState(MemoryAltarNode node)
    {
        var state = new AltarNodeState { Node = node };
        if (node == null) return state;

        var data = Data;
        state.Unlocked = data?.IsUnlocked(node.Id) ?? false;

        if (node.HasCondition)
        {
            state.Progress     = data?.GetRecord(node.ConditionKey) ?? 0;
            state.Target       = node.ConditionTarget;
            state.ConditionMet = state.Progress >= node.ConditionTarget;
        }
        else
        {
            state.ConditionMet = true;
        }

        state.Cost = state.ConditionMet ? node.DiscountCost : node.BaseCost;
        state.BlockedByRequirement = node.ConditionRequired && !state.ConditionMet;

        state.CanBuy = !state.Unlocked
                    && !state.BlockedByRequirement
                    && (data?.abyssEssence ?? 0) >= state.Cost;

        return state;
    }

    public static AltarNodeState GetState(string nodeId) => GetState(MemoryAltarCatalog.Get(nodeId));

    /// <summary>
    /// 지금 가리켜야 할 "다음" 목표 하나. 열 개를 나열하면 목표가 아니라 목록이 된다(정본 §3-1).
    /// <para>미해금 중 <b>가장 싼</b> 것을 고른다 — 가장 가까운 것이 곧 다음이다.
    /// 자물쇠 노드는 조건을 못 채웠으면 후보에서 뺀다(가리켜도 살 수 없다).</para>
    /// </summary>
    public static AltarNodeState? GetNextGoal()
    {
        AltarNodeState? best = null;

        foreach (var node in MemoryAltarCatalog.All)
        {
            var s = GetState(node);
            if (s.Unlocked || s.BlockedByRequirement) continue;
            if (best == null || s.Cost < best.Value.Cost) best = s;
        }
        return best;
    }

    public static int UnlockedCount()
    {
        int n = 0;
        foreach (var node in MemoryAltarCatalog.All)
            if (IsUnlocked(node.Id)) n++;
        return n;
    }

    // ── 구매 ────────────────────────────────────────────

    /// <summary>
    /// 노드를 해금한다. 성공하면 true — 호출자가 <c>BackendGameData.SaveAsync()</c>를 이어서 부른다.
    /// <para>저장을 여기서 하지 않는 이유: 실패/취소 처리와 화면 갱신 순서를 뷰가 쥐어야 하기 때문이다
    /// (기존 <c>UI_AwakeningPanel</c>의 저장 규약을 그대로 잇는다).</para>
    /// </summary>
    public static bool TryUnlock(MemoryAltarNode node)
    {
        if (node == null) return false;

        var data = Data;
        if (data == null) return false;

        var state = GetState(node);
        if (state.Unlocked) return false;

        if (state.BlockedByRequirement)
        {
            Debug.Log($"[MemoryAltar] 선행 조건 미충족: {node.DisplayName} ({node.ConditionLabel})");
            return false;
        }

        if (!data.TryUnlock(node.Id, state.Cost))
        {
            Debug.Log($"[MemoryAltar] 정수 부족: {node.DisplayName} — {state.Cost} 필요, {data.abyssEssence} 보유");
            return false;
        }
        return true;
    }

    // ── 각성 6계열 환급 (1회성 마이그레이션) ──────────────

    /// <summary>
    /// 폐기된 각성 6계열에 쓴 정수를 되돌린다. 정본 §10-5.
    /// <para><b>제단을 열 때 호출한다.</b> 부팅 시점에 하지 않는 이유는 비용표(<c>RelicAwakening</c>)가
    /// CDN 비동기 로드라 부트 순서에 따라 0을 돌려줄 수 있기 때문이다 — 그러면 환급이 0이 되고,
    /// 레벨은 이미 지워진 뒤라 되돌릴 수 없다. 제단을 여는 시점엔 이미 초기화가 끝나 있다.</para>
    /// <para>기록 키 하나로 가드하므로 <b>몇 번 불러도 한 번만</b> 수행된다.</para>
    /// </summary>
    /// <returns>환급한 정수. 0이면 환급할 것이 없었거나 이미 환급됐다.</returns>
    public static int TryRefundLegacyAwakening()
    {
        var data = Data;
        if (data == null) return 0;
        if (data.GetRecord(MemoryAltarCatalog.Rec.AwakeningRefunded) > 0) return 0;

        var chart = Managers.RelicAwakening;
        if (chart == null || !chart.IsInitialized)
        {
            // 비용표가 없으면 환급액을 계산할 수 없다. 레벨을 남겨두고 다음 기회에 다시 시도한다.
            Debug.LogWarning("[MemoryAltar] 각성 비용표 미초기화 — 환급을 미룬다.");
            return 0;
        }

        int refund = 0;
        foreach (var cat in AwakeningCategory.All)
        {
            int level = data.GetAwakeningLevel(cat);
            for (int lv = 0; lv < level; lv++)
                refund += Mathf.Max(0, chart.GetUpgradeCost(cat, lv));
        }

        data.awakeningLevelSword  = 0;
        data.awakeningLevelShield = 0;
        data.awakeningLevelHeart  = 0;
        data.awakeningLevelStep   = 0;
        data.awakeningLevelMana   = 0;
        data.awakeningLevelLuck   = 0;

        data.abyssEssence += refund;
        data.SetRecordMax(MemoryAltarCatalog.Rec.AwakeningRefunded, 1);

        if (refund > 0)
            Debug.Log($"[MemoryAltar] 각성 6계열 폐기 — 정수 {refund} 환급");

        return refund;
    }

    // ── 해금 효과 조회 (게임플레이 쪽에서 읽는 창구) ────────

    /// <summary>드랍 풀에 들어와 있는 최고 등급. 해금 전에는 하위 등급으로 폴백된다(정본 §4).</summary>
    public static bool IsEpicUnlocked      => IsUnlocked(MemoryAltarCatalog.RuneEpic);
    public static bool IsLegendaryUnlocked => IsUnlocked(MemoryAltarCatalog.RuneLegendary);

    /// <summary>
    /// 굴려 나온 등급을 <b>해금된 범위로 내린다</b>. 이것이 「등장」 갈래가 실제로 작동하는 지점이다.
    /// <para>가중치를 건드리지 않고 <b>결과만</b> 내리는 이유: 굴림 자체는 시드 결정적이어야
    /// 이어하기 복원이 어긋나지 않는다. 클램프는 순수 함수라 결정성을 깨지 않는다.</para>
    /// <para>둘 다 미해금이면 Legendary → Epic → Rare로 연쇄 강등된다.</para>
    /// </summary>
    public static ItemRarity ClampRarity(ItemRarity rolled)
    {
        if (rolled == ItemRarity.Legendary && !IsLegendaryUnlocked) rolled = ItemRarity.Epic;
        if (rolled == ItemRarity.Epic      && !IsEpicUnlocked)      rolled = ItemRarity.Rare;
        return rolled;
    }

    public static bool IsChapter4Unlocked  => IsUnlocked(MemoryAltarCatalog.Chapter4);
    public static bool IsAbyssDepthUnlocked=> IsUnlocked(MemoryAltarCatalog.AbyssDepth);
    public static bool HasRevive           => IsUnlocked(MemoryAltarCatalog.Revive);

    /// <summary>룬 선택지 개수(기본 3, 해금 시 4).</summary>
    public static int RuneChoiceCount   => IsUnlocked(MemoryAltarCatalog.RuneChoice4) ? 4 : 3;

    /// <summary>보스 파츠 드래프트 개수(기본 3, 해금 시 4).</summary>
    public static int PartsDraftCount   => IsUnlocked(MemoryAltarCatalog.PartsDraft4) ? 4 : 3;

    /// <summary>
    /// 재련소 승급에서 고를 수 있는 전설 후보 수. 기본 <b>1</b>, 「전설 3종 개방」 시 전부.
    /// <para>승급 자체는 해금과 무관하다(강화 MAX면 가능) — 넓어지는 것은 <b>선택의 폭</b>뿐이다.
    /// 승급 자체를 잠그면 이미 되던 것을 빼앗는 것이 되어 「가능성의 확장」이 아니게 된다.</para>
    /// </summary>
    public static int LegendChoiceCount => IsUnlocked(MemoryAltarCatalog.WeaponEvolve) ? int.MaxValue : 1;

    /// <summary>
    /// 무기대에서 고를 수 있는 <b>원거리</b> 무기 id 목록.
    /// <para>활은 기본 지급이라 항상 들어간다. 석궁만 해금 대상이다.</para>
    /// <para>주무기(무형검)는 여기 없다 — <c>WorldSwordAwakening</c>이 매 런 하사하고,
    /// 카타나·대검은 그 무형검이 런 안에서 갈라지는 <b>진화 분기</b>지 시작 선택지가 아니다.</para>
    /// </summary>
    public static List<string> UnlockedRangedWeapons()
    {
        var list = new List<string>(2) { "bow" };
        if (IsUnlocked(MemoryAltarCatalog.WeaponCrossbow)) list.Add("crossbow");
        return list;
    }
}
