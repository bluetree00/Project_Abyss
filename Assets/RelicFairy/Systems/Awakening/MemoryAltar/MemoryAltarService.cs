using System.Collections.Generic;
using UnityEngine;

/// <summary>노드 1개를 화면에 그리기 위해 필요한 전부. 뷰는 이 값만 보고 그린다.</summary>
public struct AltarNodeState
{
    public MemoryAltarNode Node;
    /// <summary>1단계 이상 샀다(해금 노드면 「열었다」).</summary>
    public bool Unlocked;

    /// <summary>산 단계 · 최대 단계(10-14 강화형). 해금 노드는 0/1.</summary>
    public int Level, MaxLevel;
    /// <summary>끝까지 샀다 — 더 살 것이 없다(해금 노드면 Unlocked와 같다).</summary>
    public bool Maxed;

    /// <summary>그 갈래 포인트가 모자라 아직 안 열렸다(10-14 세피리아식 — 깊이 대신 「갈래에 쓴 포인트」).</summary>
    public bool BlockedByPoints;
    /// <summary>이 노드가 열리는 갈래 포인트 · 지금 그 갈래 포인트.</summary>
    public int RequiredPoints, BranchPoints;

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
    public static bool IsUnlocked(string nodeId) => Level(nodeId) > 0;

    /// <summary>노드 단계(0 = 안 삼 · 10-14 강화형). 카탈로그에 없는 id는 0 · 최대 단계를 넘지 않는다.</summary>
    public static int Level(string nodeId)
    {
        var node = MemoryAltarCatalog.Get(nodeId);
        var data = Data;
        if (node == null || data == null) return 0;
        EnsureLevelsReset(data);
        return Mathf.Min(data.GetLevel(nodeId), node.MaxLevel);
    }

    /// <summary>
    /// 갈래 포인트(10-14 세피리아식) — 그 갈래에서 산 단계 수의 합(해금 노드는 1). 노드의 <see cref="MemoryAltarNode.RequiredPoints"/>와 견준다.
    /// </summary>
    public static int BranchPoints(AltarBranch branch)
    {
        int n = 0;
        foreach (var node in MemoryAltarCatalog.All)
            if (node.Branch == branch) n += Level(node.Id);
        return n;
    }

    /// <summary>할인 조건 기록 — 봉인 수(<see cref="MemoryAltarCatalog.Rec.Seals"/>) · 해방기 도달(<see cref="MemoryAltarCatalog.Rec.Liberated"/>)만 이야기 기록에서 세고, 나머지는 영구 기록.</summary>
    public static int Record(string key)
    {
        if (key == MemoryAltarCatalog.Rec.Liberated) return StoryProgress.IsLiberated ? 1 : 0;   // 해방 대비 3칸 자물쇠(10-22)
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
        state.Level    = Level(node.Id);
        state.MaxLevel = node.MaxLevel;
        state.Unlocked = state.Level > 0;
        state.Maxed    = state.Level >= node.MaxLevel;

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

        // 다음 단계 값 — 할인 조건은 첫 단계만 깎는다(10-14 강화형)
        state.Cost = node.CostForLevel(state.Level + 1, state.ConditionMet && state.Level == 0);
        state.BlockedByRequirement = node.ConditionRequired && !state.ConditionMet;

        // 뜻으로 묶인 앞 노드(Epic 확률 ← Epic 등장 등) — 그것만 먼저. 깊이(트리 사슬)로 막지 않는다(10-14 사용자 「깊이에 따라 해금은 폐기」).
        state.MissingParent  = MemoryAltarCatalog.FirstLockedParent(node, IsUnlocked);
        state.BlockedByChain = state.MissingParent != null;
        // 갈래 포인트 — 그 갈래에서 산 단계 수가 모자라면 아직 안 열렸다(세피리아 재능 5 · 10 · 20 투자 지점의 결)
        state.RequiredPoints  = node.RequiredPoints;
        state.BranchPoints    = node.RequiredPoints > 0 ? BranchPoints(node.Branch) : 0;
        state.BlockedByPoints = state.BranchPoints < state.RequiredPoints;
        state.IsNextInChain   = !state.Maxed && !state.BlockedByChain && !state.BlockedByPoints;

        // 시기 고리 · 가운데 기억(10-02) — 드러나지 않은 시기는 그리지도 팔지도 않고, 기억 노드는 공명 그물이 받을 때까지 보이기만 한다.
        state.EraHidden = !MemoryAltarCatalog.IsEraRevealed(node.Era);
        state.Pending   = node.Branch == AltarBranch.Memory && !MemoryAltarCatalog.MemoryNodesLive;
        if (state.EraHidden || state.Pending) state.IsNextInChain = false;

        state.CanBuy = !state.Maxed
                    && !state.EraHidden
                    && !state.Pending
                    && !state.BlockedByRequirement
                    && !state.BlockedByChain
                    && !state.BlockedByPoints
                    && (data?.abyssEssence ?? 0) >= state.Cost;

        return state;
    }

    public static AltarNodeState GetState(string nodeId) => GetState(MemoryAltarCatalog.Get(nodeId));

    /// <summary>
    /// 지금 가리켜야 할 "다음" 목표 하나. 열 개를 나열하면 목표가 아니라 목록이 된다(정본 §3-1).
    /// <para>미해금 중 <b>가장 싼</b> 것을 고른다 — 가장 가까운 것이 곧 다음이다.
    /// 자물쇠 노드는 조건을 못 채웠으면 후보에서 뺀다(가리켜도 살 수 없다).</para>
    /// <para>10-22 D5 — 해방기엔 해방 대비 3칸(<see cref="MemoryAltarCatalog.LiberationNodes"/>) 중 가장 싼 칸을 먼저 가리킨다.
    /// 가장 싼 칸만 따라가면 재화 · 확률 칸이 먼저 차고 해방 칸은 끝내 안 산다(시뮬 0). 셋이 다 찼거나 막혔으면 예전처럼 전체에서 가장 싼 칸.
    /// 봉인기엔 자물쇠가 셋을 막아 저절로 예전과 같다. 「지금 열 수 있는 칸」을 말해야 하는 곳은 <paramref name="preferLiberation"/> = false.</para>
    /// </summary>
    public static AltarNodeState? GetNextGoal(bool preferLiberation = true)
    {
        AltarNodeState? best = null;

        if (preferLiberation)
        {
            foreach (var id in MemoryAltarCatalog.LiberationNodes)
                best = CheaperGoal(best, GetState(id));
            if (best != null) return best;
        }

        foreach (var node in MemoryAltarCatalog.All)
            best = CheaperGoal(best, GetState(node));
        return best;
    }

    /// <summary>「다음 목표」 후보(다 안 샀고 · 막히지 않음)면 지금 고른 것과 견줘 더 싼 쪽.</summary>
    private static AltarNodeState? CheaperGoal(AltarNodeState? best, AltarNodeState s)
    {
        if (s.Maxed || s.BlockedByRequirement || s.BlockedByChain || s.BlockedByPoints || s.EraHidden || s.Pending) return best;
        return best == null || s.Cost < best.Value.Cost ? s : best;
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
    /// 10-14부터 시기 드러남이 없어 모든 칸을 센다(10-02엔 드러난 시기의 칸만 셌다).
    /// </summary>
    public static (int unlocked, int total) BranchProgress(AltarBranch branch)
    {
        // 10-14 강화형 — (산 단계 = 갈래 포인트, 드러난 노드의 최대 단계 합)
        int unlocked = 0, total = 0;
        foreach (var node in MemoryAltarCatalog.All)
        {
            if (node.Branch != branch || !MemoryAltarCatalog.IsEraRevealed(node.Era)) continue;
            total    += node.MaxLevel;
            unlocked += Level(node.Id);
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
        if (state.Maxed) return false;

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

        if (state.BlockedByPoints)
        {
            Debug.Log($"[MemoryAltar] 갈래 포인트 부족: {node.DisplayName} — {state.RequiredPoints} 필요, {state.BranchPoints}");
            return false;
        }

        if (!data.TryBuyLevel(node.Id, state.Cost, node.MaxLevel))
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

    // ── 10-14 강화형 전환 리셋 (1회성) ─────────────────

    private static UserGameData s_levelsResetFor;   // 이 데이터(로그인 한 번)에 대해 이미 확인했다 — 단계를 읽을 때마다 기록을 다시 읽지 않게
    private static bool s_levelsResetUnsaved;        // 리셋했는데 아직 저장 안 함 — 제단을 열 때 저장한다(TakeResetPendingSave)

    /// <summary>강화형 전환 리셋을 막 했으면 true를 한 번 돌려준다 — 제단 패널이 저장을 맡는다.</summary>
    public static bool TakeResetPendingSave()
    {
        bool v = s_levelsResetUnsaved;
        s_levelsResetUnsaved = false;
        return v;
    }

    /// <summary>
    /// 강화형 전환 — 사용자 10-14 「아직 출시 전이니 리셋」. 옛 노드(한 번 사는 칸)에 쓴 정수를 옛 기본가로 돌려주고 산 칸을 비운다.
    /// 앞선 환급 묶음(10-02 · 10-14 「챕터 4」)을 먼저 끝낸 뒤 비운다 — 비운 뒤엔 그 환급이 산 칸을 못 본다.
    /// 처음 단계를 읽을 때 한 번(기록 키 가드) — 저장은 그 뒤의 평소 저장(제단 열기 · 런 끝)이 맡는다.
    /// </summary>
    /// <returns>돌려준 정수(이번에 리셋했으면). 이미 했거나 데이터가 없으면 0.</returns>
    public static int EnsureLevelsReset(UserGameData data)
    {
        if (data == null || ReferenceEquals(s_levelsResetFor, data)) return 0;
        s_levelsResetFor = data;
        if (data.GetRecord(MemoryAltarCatalog.Rec.LevelsReset1014) > 0) return 0;

        // 앞 묶음 환급(각자 정수를 더한다) → 옛 칸 환급 → 비우기
        int refund = RefundBatch(data, MemoryAltarCatalog.RemovedNodes, MemoryAltarCatalog.Rec.RemovedNodesRefunded)
                   + RefundBatch(data, MemoryAltarCatalog.RemovedNodes1014, MemoryAltarCatalog.Rec.RemovedNodesRefunded1014);
        int legacy = 0;
        foreach (var (id, cost) in MemoryAltarCatalog.LegacyNodes1014)
            if (data.GetLevel(id) > 0) legacy += cost;
        data.unlockedIds   = "";
        data.abyssEssence += legacy;
        data.SetRecordMax(MemoryAltarCatalog.Rec.LevelsReset1014, 1);
        s_levelsResetUnsaved = true;
        refund += legacy;
        if (refund > 0) Debug.Log($"[MemoryAltar] 강화형 전환 리셋 — 산 칸을 비우고 정수 {refund} 환급");
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
        if (data == null) return 0;
        // 묶음마다 기록 키가 따로다 — 10-02 환급을 이미 받은 세이브도 10-14(「챕터 4 개방」) 몫은 받는다.
        int refund = RefundBatch(data, MemoryAltarCatalog.RemovedNodes, MemoryAltarCatalog.Rec.RemovedNodesRefunded)
                   + RefundBatch(data, MemoryAltarCatalog.RemovedNodes1014, MemoryAltarCatalog.Rec.RemovedNodesRefunded1014);
        if (refund > 0)
            Debug.Log($"[MemoryAltar] 카탈로그에서 뺀 노드 — 정수 {refund} 환급");
        return refund;
    }

    private static int RefundBatch(UserGameData data, (string id, int refund)[] batch, string recordKey)
    {
        if (data.GetRecord(recordKey) > 0) return 0;
        int refund = 0;
        foreach (var (id, cost) in batch)
            if (data.IsUnlocked(id)) refund += cost;
        data.abyssEssence += refund;
        data.SetRecordMax(recordKey, 1);
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
    /// <summary>룬 보관함 칸 수 — 기본 5 + 「룬 보관함」 단계(최대 7).</summary>
    public static int RuneStorageCapacity => 5 + Level(MemoryAltarCatalog.RuneStorage1);

    /// <summary>정제소가 룬 1개 대신 <b>2장 중 고르기</b>를 주는가.</summary>
    public static bool IsRefinePickUnlocked => IsUnlocked(MemoryAltarCatalog.RefinePick);

    /// <summary>상점 새로고침이 열렸는가(디자이너 플래그 shopRerollEnabled와 OR).</summary>
    public static bool IsShopRerollUnlocked => IsUnlocked(MemoryAltarCatalog.ShopReroll);

    /// <summary>상점마다 새로고침 횟수 = 「상점 새로고침」 단계(10-14 강화형 · 0이면 없음).</summary>
    public static int ShopRerollCount => Level(MemoryAltarCatalog.ShopReroll);

    /// <summary>베이스캠프 파츠 작업대에서 들고 나가는 시작 파츠 수 — 기본 1, 해금 시 2.</summary>
    public static int StartPartSlots => IsUnlocked(MemoryAltarCatalog.StartParts2) ? 2 : 1;

    /// <summary>새 런 시작 포션 칸 — 기본 3 + 「포션 칸」 단계(최대 5).</summary>
    public static int PotionCapacity => 3 + Level(MemoryAltarCatalog.PotionSlot);

    // ── 강화형(10-14) 효과 — 노드 글(<see cref="MemoryAltarNode.ValueText"/>)과 같은 몫을 읽는다 ──
    /// <summary>최대 체력 배율 몫(+4%/단계) — 제단에 남은 유일한 영구 스탯.</summary>
    public static float MaxHpPct          => MemoryAltarCatalog.MaxHpPctPerLevel * Level(MemoryAltarCatalog.MaxHpUp);
    /// <summary>방 보상 룬 등급 가중 더하기(%p) — Rare · Epic(등장 연 뒤) · Legendary(등장 연 뒤).</summary>
    public static float RareOddsBonus     => MemoryAltarCatalog.RareOddsPerLevel * Level(MemoryAltarCatalog.RuneRareOdds);
    public static float EpicOddsBonus     => IsEpicUnlocked ? MemoryAltarCatalog.EpicOddsPerLevel * Level(MemoryAltarCatalog.RuneEpicOdds) : 0f;
    public static float LegendOddsBonus   => IsLegendaryUnlocked ? MemoryAltarCatalog.LegendOddsPerLevel * Level(MemoryAltarCatalog.RuneLegendOdds) : 0f;
    /// <summary>정제소 Epic 더하기(0 ~ 0.15).</summary>
    public static float RefineEpicBonus   => MemoryAltarCatalog.RefineEpicPerLevel * Level(MemoryAltarCatalog.RefineQuality);
    /// <summary>재련 강화 성공률 더하기(0 ~ 0.09).</summary>
    public static float SmithBonus        => MemoryAltarCatalog.SmithPerLevel * Level(MemoryAltarCatalog.SigilSmith);
    /// <summary>획득 배율(1 = 그대로) — 원석 · 강화재료 · 골드 · 정수.</summary>
    public static float OreGainMult       => 1f + MemoryAltarCatalog.OreGainPerLevel * Level(MemoryAltarCatalog.OreGain);
    public static float EnhanceGainMult   => 1f + MemoryAltarCatalog.EnhanceGainPerLevel * Level(MemoryAltarCatalog.EnhanceGain);
    public static float GoldGainMult      => 1f + MemoryAltarCatalog.GoldGainPerLevel * Level(MemoryAltarCatalog.GoldGain);
    public static float EssenceGainMult   => 1f + MemoryAltarCatalog.EssenceGainPerLevel * Level(MemoryAltarCatalog.EssenceGain);
    /// <summary>상점 할인(0 ~ 0.15).</summary>
    public static float ShopDiscount      => MemoryAltarCatalog.ShopDiscountPerLevel * Level(MemoryAltarCatalog.SigilMerchant);
    /// <summary>포션 회복 더하기(최대 체력 비 · 0 ~ 0.12) — 기본 0.40에 더한다.</summary>
    public static float PotionHealBonus   => MemoryAltarCatalog.PotionHealPerLevel * Level(MemoryAltarCatalog.PotionHeal);
    /// <summary>심연 깊이 하나당 정수 더하기(0 ~ 0.15).</summary>
    public static float DepthRewardPerDepth => MemoryAltarCatalog.DepthRewardPerLevel * Level(MemoryAltarCatalog.DepthReward);

    /// <summary>
    /// 서약 카드(원인·효과) 개방 단계 0~3. 선으로 이어져 앞 단계 없이 뒤 단계가 열려 있을 수 없지만,
    /// 재정렬 이전 세이브처럼 구멍이 날 수 있어 <b>가장 높은 열린 단계</b>를 그대로 쓴다.
    /// </summary>
    public static int CovenantPartStep => Level(MemoryAltarCatalog.CovenantParts1);   // 10-14 강화형 — 「서약 카드」 단계 0 ~ 3

    /// <summary>한 런에 맺을 수 있는 서약 수(기본 3, 「서약 칸 +1」 해금 시 4).</summary>
    public static int CovenantSlots => IsUnlocked(MemoryAltarCatalog.CovenantSlot) ? 4 : 3;

    public static bool IsAbyssDepthUnlocked=> IsUnlocked(MemoryAltarCatalog.AbyssDepth);

    // ── 해방 대비 3칸(10-22 C10 권장안 B) — 자물쇠(해방기 도달)를 지나야 살 수 있어 효과엔 시기 조건을 따로 걸지 않는다 ──
    // (봉인기로 돌아가는 길이 생기면 그때 효과에 시기 조건을 더한다 — 설계 ⑤ 「A10에 달린 것」.)
    /// <summary>「해방의 내성」 받는 피해 감소(0 ~ 0.12) — 피격 받피감소 채널에 더한다(상한 0.75 공유).</summary>
    public static float LiberationWardReduction => MemoryAltarCatalog.LibWardPerLevel * Level(MemoryAltarCatalog.LibWard);
    /// <summary>「결의」 체력 문턱(0 = 안 삼 · 1단계 0.60 · 2단계 0.45).</summary>
    public static float ResolveHpThreshold => MemoryAltarCatalog.ResolveThreshold(Level(MemoryAltarCatalog.LibResolve));
    /// <summary>「재정비의 샘」 챕터 시작 샘 회복 더하기(최대 체력 비) — 평소 +0.20/단계 · 악몽 +0.10/단계.</summary>
    public static float SpringHealBonus(bool nightmare)
        => (nightmare ? MemoryAltarCatalog.NightmareSpringHealPerLevel : MemoryAltarCatalog.SpringHealPerLevel) * Level(MemoryAltarCatalog.LibSpring);
    public static bool HasRevive           => IsUnlocked(MemoryAltarCatalog.Revive);

    /// <summary>
    /// 기억 카드 다시 굴리기(「다시 떠올리기」) — 런당 1회. 유물 성장 「공명 그물」이 읽는다.
    /// <see cref="MemoryAltarCatalog.MemoryNodesLive"/>가 꺼져 있으면 늘 false(살 수도 없다).
    /// </summary>
    public static bool HasMemoryRedraw => MemoryAltarCatalog.MemoryNodesLive && IsUnlocked(MemoryAltarCatalog.MemRedraw);

    /// <summary>기억 카드 등급 띠 단계 0 · 1(선명한 기억) · 2(찬란한 기억). 확률표는 유물 성장 설계 §3.</summary>
    public static int MemoryGradeBand =>
        !MemoryAltarCatalog.MemoryNodesLive ? 0 : Mathf.Clamp(Level(MemoryAltarCatalog.MemClear), 0, 2);   // 10-14 강화형 — 「기억 카드 등급」 2단계

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
