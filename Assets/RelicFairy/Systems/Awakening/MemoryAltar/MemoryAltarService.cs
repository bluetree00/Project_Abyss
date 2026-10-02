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

    /// <summary>
    /// 선으로 이어진 <b>앞 노드(부모)</b> 중 아직 안 열린 것이 있다(09-29 트리 개편 — 예전엔 같은 갈래의 앞 칸 하나).
    /// <para>기록 자물쇠와 다르다 — 앞 노드는 정수만으로 항상 넘을 수 있고 선은 가운데에서 바깥으로만 뻗는다.</para>
    /// </summary>
    public bool BlockedByChain;

    /// <summary>안 열린 부모 중 첫 번째(「앞 노드 먼저」 안내). 막히지 않았으면 null.</summary>
    public MemoryAltarNode MissingParent;

    /// <summary>부모가 전부 열려 <b>지금 살 수 있는 자리</b>에 있다(정수와 무관). 트리에서 선 끝에 켜지는 칸.</summary>
    public bool IsNextInChain;

    /// <summary>그 시기의 고리가 아직 드러나지 않았다(10-02 — 해방기는 붕괴 뒤, 악몽은 엔딩 뒤). 그리지도 팔지도 않는다.</summary>
    public bool EraHidden;

    /// <summary>가운데 기억 노드인데 유물 성장 「공명 그물」이 아직 받지 못한다 — 보이지만 살 수 없다(「유물 성장 개편과 함께 열린다」).</summary>
    public bool Pending;
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

    /// <summary>
    /// 해금했는가. <b>카탈로그에 없는 id는 늘 false</b> — 10-02 재설계로 뺀 유물 노드(파츠 선택지 +1 · 코어 2·3종 · 이어받기)를
    /// 산 옛 세이브도 그 효과가 꺼진다(정수는 <see cref="TryRefundRemovedNodes"/>가 돌려준다).
    /// </summary>
    public static bool IsUnlocked(string nodeId) => MemoryAltarCatalog.Get(nodeId) != null && (Data?.IsUnlocked(nodeId) ?? false);

    /// <summary>할인 조건 기록 — 봉인 수(<see cref="MemoryAltarCatalog.Rec.Seals"/>)만 이야기 기록에서 세고, 나머지는 영구 기록.</summary>
    public static int Record(string key)
    {
        if (key == MemoryAltarCatalog.Rec.Seals)
        {
            int n = 0;
            if (StoryProgress.IsSealed(StoryProgress.ForestGuardian)) n++;
            if (StoryProgress.IsSealed(StoryProgress.Dragon))         n++;
            if (StoryProgress.IsSealed(StoryProgress.DeathKnight))    n++;
            if (StoryProgress.IsSealed(StoryProgress.Lich))           n++;
            return n;
        }
        return Data?.GetRecord(key) ?? 0;
    }

    public static int Essence => Data?.abyssEssence ?? 0;

    public static AltarNodeState GetState(MemoryAltarNode node)
    {
        var state = new AltarNodeState { Node = node };
        if (node == null) return state;

        var data = Data;
        state.Unlocked = data?.IsUnlocked(node.Id) ?? false;

        if (node.HasCondition)
        {
            state.Progress     = Record(node.ConditionKey);
            state.Target       = node.ConditionTarget;
            state.ConditionMet = state.Progress >= node.ConditionTarget;
        }
        else
        {
            state.ConditionMet = true;
        }

        state.Cost = state.ConditionMet ? node.DiscountCost : node.BaseCost;
        state.BlockedByRequirement = node.ConditionRequired && !state.ConditionMet;

        // 부모 — 선으로 이어진 앞 노드가 전부 열려야 차례가 온다.
        state.MissingParent  = MemoryAltarCatalog.FirstLockedParent(node, id => data?.IsUnlocked(id) ?? false);
        state.BlockedByChain = state.MissingParent != null;
        state.IsNextInChain  = !state.Unlocked && !state.BlockedByChain;

        // 시기 고리 · 가운데 기억(10-02) — 드러나지 않은 시기는 그리지도 팔지도 않고, 기억 노드는 공명 그물이 받을 때까지 보이기만 한다.
        state.EraHidden = !MemoryAltarCatalog.IsEraRevealed(node.Era);
        state.Pending   = node.Branch == AltarBranch.Memory && !MemoryAltarCatalog.MemoryNodesLive;
        if (state.EraHidden || state.Pending) state.IsNextInChain = false;

        state.CanBuy = !state.Unlocked
                    && !state.EraHidden
                    && !state.Pending
                    && !state.BlockedByRequirement
                    && !state.BlockedByChain
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
            if (s.Unlocked || s.BlockedByRequirement || s.BlockedByChain || s.EraHidden || s.Pending) continue;
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

    /// <summary>
    /// 지금 정수로 바로 열 수 있는 칸 수 — 부모가 다 열린 칸만 센다(<see cref="AltarNodeState.CanBuy"/>는 부모 조건을 포함).
    /// 제단 밖 안내(성소 수정 · 제단 이름표 · 멀린 한 줄)가 쓴다 — 유도설계 A-2 「다음 해금을 제단 밖으로」.
    /// </summary>
    public static int AffordableCount()
    {
        int n = 0;
        foreach (var node in MemoryAltarCatalog.All)
            if (GetState(node).CanBuy) n++;
        return n;
    }

    /// <summary>
    /// 갈래 진척 — (연 칸, 전체 칸). 베이스캠프 기억 성소의 갈래 수정 밝기 · 제단 갈래 제목.
    /// <b>드러난 시기의 칸만 센다</b> — 숨은 고리의 칸을 세면 「아직 더 있다」를 숫자가 먼저 말해 버린다(10-02).
    /// </summary>
    public static (int unlocked, int total) BranchProgress(AltarBranch branch)
    {
        int unlocked = 0, total = 0;
        foreach (var node in MemoryAltarCatalog.All)
        {
            if (node.Branch != branch || !MemoryAltarCatalog.IsEraRevealed(node.Era)) continue;
            total++;
            if (IsUnlocked(node.Id)) unlocked++;
        }
        return (unlocked, total);
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

        if (state.EraHidden || state.Pending)
        {
            Debug.Log($"[MemoryAltar] 아직 열 수 없는 칸: {node.DisplayName} ({(state.EraHidden ? "시기 고리 미공개" : "유물 성장 개편 대기")})");
            return false;
        }

        if (state.BlockedByRequirement)
        {
            Debug.Log($"[MemoryAltar] 선행 조건 미충족: {node.DisplayName} ({node.ConditionLabel})");
            return false;
        }

        if (state.BlockedByChain)
        {
            Debug.Log($"[MemoryAltar] 앞 노드 미해금: {node.DisplayName} — 「{state.MissingParent?.DisplayName}」이 먼저다");
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

    // ── 10-02 재설계로 뺀 노드 환급 (1회성) ──────────────

    /// <summary>
    /// 10-02 재설계로 카탈로그에서 뺀 유물 노드(<see cref="MemoryAltarCatalog.RemovedNodes"/>)를 산 세이브에 정수를 돌려준다 — 옛 기본가.
    /// <para>제단을 열 때 부른다(각성 환급과 같은 자리). 기록 키 하나로 가드해 <b>몇 번 불러도 한 번만</b>.
    /// 해금 id는 지우지 않는다 — 카탈로그에 없으니 <see cref="IsUnlocked"/>가 이미 false다.</para>
    /// </summary>
    /// <returns>돌려준 정수. 0이면 돌려줄 것이 없었거나 이미 돌려줬다.</returns>
    public static int TryRefundRemovedNodes()
    {
        var data = Data;
        if (data == null || data.GetRecord(MemoryAltarCatalog.Rec.RemovedNodesRefunded) > 0) return 0;

        int refund = 0;
        foreach (var (id, cost) in MemoryAltarCatalog.RemovedNodes)
            if (data.IsUnlocked(id)) refund += cost;

        data.abyssEssence += refund;
        data.SetRecordMax(MemoryAltarCatalog.Rec.RemovedNodesRefunded, 1);
        if (refund > 0)
            Debug.Log($"[MemoryAltar] 10-02 재설계로 뺀 유물 노드 — 정수 {refund} 환급");
        return refund;
    }

    // ── 해금 효과 조회 (게임플레이 쪽에서 읽는 창구) ────────

    /// <summary>드랍 풀에 들어와 있는 최고 등급. 해금 전에는 하위 등급으로 폴백된다(정본 §4).</summary>
    public static bool IsEpicUnlocked      => IsUnlocked(MemoryAltarCatalog.RuneEpic);
    public static bool IsLegendaryUnlocked => IsUnlocked(MemoryAltarCatalog.RuneLegendary);

    /// <summary>
    /// 굴려 나온 등급을 <b>해금된 범위로 내린다</b>. 이것이 「룬」 갈래의 등급 해금이 실제로 작동하는 지점이다.
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

    /// <summary>
    /// 지금 나올 수 있는 룬 최고 등급. 등급이 <b>정해진 채로</b> 고르는 경로(상점 진열)는 굴림이 없어
    /// <see cref="ClampRarity"/>를 탈 수 없으니 이 값으로 후보를 거른다.
    /// </summary>
    public static ItemRarity MaxRuneRarity => ClampRarity(ItemRarity.Legendary);

    /// <summary>
    /// 등급 확률 세 몫을 해금 범위로 <b>접는다</b> — 잠긴 등급의 몫은 한 단계 아래로 넘어간다.
    /// <para>굴림 결과만 내리는 <see cref="ClampRarity"/>와 같은 규칙인데, 확률을 <b>화면에 보여주는</b> 경로(정제소)는
    /// 표시와 굴림이 같은 값을 봐야 해서 확률 자체를 접는다. 접지 않으면 "전설 25%"를 보여 주고 영웅을 준다.</para>
    /// </summary>
    public static (float rare, float epic, float legend) FoldOdds(float rare, float epic, float legend)
    {
        if (!IsLegendaryUnlocked) { epic += legend; legend = 0f; }
        if (!IsEpicUnlocked)      { rare += epic;   epic   = 0f; }
        return (rare, epic, legend);
    }

    // ── 판 넓히기(09-29 개편 신설) ──
    /// <summary>룬 보관함 칸 수 — 기본 5, 「룬 보관함 +1」 6, 「+2」 7.</summary>
    public static int RuneStorageCapacity =>
        IsUnlocked(MemoryAltarCatalog.RuneStorage2) ? 7 :
        IsUnlocked(MemoryAltarCatalog.RuneStorage1) ? 6 : 5;

    /// <summary>정제소가 룬 1개 대신 <b>2장 중 고르기</b>를 주는가.</summary>
    public static bool IsRefinePickUnlocked => IsUnlocked(MemoryAltarCatalog.RefinePick);

    /// <summary>상점 새로고침이 열렸는가(디자이너 플래그 shopRerollEnabled와 OR).</summary>
    public static bool IsShopRerollUnlocked => IsUnlocked(MemoryAltarCatalog.ShopReroll);

    /// <summary>베이스캠프 파츠 작업대에서 들고 나가는 시작 파츠 수 — 기본 1, 해금 시 2.</summary>
    public static int StartPartSlots => IsUnlocked(MemoryAltarCatalog.StartParts2) ? 2 : 1;

    /// <summary>새 런 시작 포션 칸 — 기본 3, 해금 시 4.</summary>
    public static int PotionCapacity => IsUnlocked(MemoryAltarCatalog.PotionSlot) ? 4 : 3;

    /// <summary>
    /// 서약 카드(원인·효과) 개방 단계 0~3. 선으로 이어져 앞 단계 없이 뒤 단계가 열려 있을 수 없지만,
    /// 재정렬 이전 세이브처럼 구멍이 날 수 있어 <b>가장 높은 열린 단계</b>를 그대로 쓴다.
    /// </summary>
    public static int CovenantPartStep =>
        IsUnlocked(MemoryAltarCatalog.CovenantParts3) ? 3 :
        IsUnlocked(MemoryAltarCatalog.CovenantParts2) ? 2 :
        IsUnlocked(MemoryAltarCatalog.CovenantParts1) ? 1 : 0;

    /// <summary>한 런에 맺을 수 있는 서약 수(기본 3, 「서약 칸 +1」 해금 시 4).</summary>
    public static int CovenantSlots => IsUnlocked(MemoryAltarCatalog.CovenantSlot) ? 4 : 3;

    public static bool IsChapter4Unlocked  => IsUnlocked(MemoryAltarCatalog.Chapter4);
    public static bool IsAbyssDepthUnlocked=> IsUnlocked(MemoryAltarCatalog.AbyssDepth);
    public static bool HasRevive           => IsUnlocked(MemoryAltarCatalog.Revive);

    /// <summary>
    /// 기억 카드 다시 굴리기(「다시 떠올리기」) — 런당 1회. 유물 성장 「공명 그물」이 읽는다.
    /// <see cref="MemoryAltarCatalog.MemoryNodesLive"/>가 꺼져 있으면 늘 false(살 수도 없다).
    /// </summary>
    public static bool HasMemoryRedraw => MemoryAltarCatalog.MemoryNodesLive && IsUnlocked(MemoryAltarCatalog.MemRedraw);

    /// <summary>기억 카드 등급 띠 단계 0 · 1(선명한 기억) · 2(찬란한 기억). 확률표는 유물 성장 설계 §3.</summary>
    public static int MemoryGradeBand =>
        !MemoryAltarCatalog.MemoryNodesLive ? 0 :
        IsUnlocked(MemoryAltarCatalog.MemRadiant) ? 2 :
        IsUnlocked(MemoryAltarCatalog.MemClear)   ? 1 : 0;

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
