using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;

/// <summary>
/// 룬 성소 — 비전투 이벤트방(11-05 #20 사용자 「한 칸짜리 고성능 룬을 속성별로 특정 이벤트방에서 챙길 수 있게」).
/// 제단 셋에 속성이 다른 <b>한 칸 영웅 룬</b>(「응축된 ○○의 룬」 · ITEM_DATA <c>item_t3_shrine_*</c>)을 하나씩 올린다.
/// 하나를 고르면 그 룬을 받고 룬판이 열린다 — 나머지 제단은 닫힌다.
///   · 이 룬들은 이 방에서만 나온다: ItemSO가 없어 드랍 · 시작 선물 · 정제소 풀(ItemSORegistry)에 없고, 상점은 <see cref="IsShrineRune"/>로 뺀다.
///   · 이미 가진 룬(상한 1)은 올리지 않는다. 받을 룬이 하나도 없으면(여섯 다 가짐 · 차트에 아직 없음) 정수 · 금화로 대신한다.
///   · 영웅 등급이지만 기억의 제단 「영웅 룬 등장」 잠금과 무관하다 — 이벤트가 정해 주는 보상이다(정예 보스 방의 영웅 하한과 같은 결).
/// </summary>
public sealed class RuneShrineChallenge : WorldInteractionChallenge
{
    // ── Constants ──────────────────────────────────────────────
    public const string IdPrefix = "item_t3_shrine_";
    private const int    AltarCount  = 3;
    private const string AltarProp   = "EventProp/Altar";
    private const float  AltarHeight = 1.3f;
    private const float  AltarDist   = 3.4f;
    private const float  AltarYaw    = 60f;   // 제물 제단과 같은 간격 — ±38°면 이웃 제단이 누르는 거리 안에 들었다(10-30)
    private const string RoomTitle   = "룬 성소";
    private const string TagEmpty    = "empty";
    private const float  PromptWrapWidth = 4f;     // [F] 글 폭(m) — 6.5 m면 이웃 제단(3.4 m) 이름표를 덮었다(11-05 Ch3 사진)
    private static readonly Color ClosedColor = new(0.45f, 0.47f, 0.52f);

    // ── Private ────────────────────────────────────────────────
    private readonly List<string> _offered = new(AltarCount);
    private System.Random _rng;
    private string _chosen;

    // ── Public Methods ─────────────────────────────────────────
    /// <summary>룬 성소 전용 룬인가 — 상점 진열에서 뺀다.</summary>
    public static bool IsShrineRune(string itemId)
        => itemId != null && itemId.StartsWith(IdPrefix, StringComparison.Ordinal);

    /// <summary>SetupEventRoom이 호출 — <paramref name="seed"/>는 방 시드(이어하기로 돌아와도 같은 제단이 선다).</summary>
    public void Initialize(GameRunSession run, int seed)
    {
        InitBase(run);
        _rng = new System.Random(seed);
    }

    // ── 서브클래스 훅 ─────────────────────────────────────
    protected override string AutoResolveHint => "안 고르면 룬 없이 끝난다";   // 길면 남은 시간 아래 줄이 화면 밖으로 잘렸다(11-05 사진)

    protected override void PlaceMarkers(Transform player)
    {
        PickOffers();
        var ct = this.GetCancellationTokenOnDestroy();
        if (_offered.Count == 0)
        {
            Vector3 p = StageSpot(0f, AltarDist);
            AddMarker(p, Loc.T(RoomTitle), ClosedColor, Loc.T("<color=#B8C0CC>[F]</color> 빈 제단 — 정수 · 금화 받기"), TagEmpty);
            SpawnPropAsync(AltarProp, p, FacingEntry(p), AltarHeight, ct).Forget();
            Notice(Loc.F("{0} — 받을 룬이 남아 있지 않다 · 정수와 금화를 대신 받는다", Loc.T(RoomTitle)));
            return;
        }

        float yaw0 = -AltarYaw * (_offered.Count - 1) * 0.5f;
        for (int i = 0; i < _offered.Count; i++)
        {
            string id = _offered[i];
            var meta  = Managers.ItemData.GetItemMeta(id);
            string hex = ElementDef.IdHex(meta?.element, "#E6D2A0");
            Vector3 p = StageSpot(yaw0 + AltarYaw * i, AltarDist);
            int at = AddMarker(p, Loc.T(meta?.item_name ?? id), ElementDef.IdColor(meta?.element, Color.white),
                               Loc.F("<color={0}>[F]</color> 받기\n<size=60%>{1}</size>", hex, EffectLines(id)), id);
            GrowUpward(MarkerAt(at)?.Prompt);
            SpawnPropAsync(AltarProp, p, FacingEntry(p), AltarHeight, ct).Forget();
        }
        Notice(Loc.F("{0} — 제단 {1}개 중 <color=#B892E6>1개만</color> 고른다 · 한 칸짜리 영웅 룬", Loc.T(RoomTitle), _offered.Count));
    }

    protected override void OnActivate(int markerIndex)
    {
        var m = MarkerAt(markerIndex);
        if (m == null) return;
        _chosen = (string)m.Tag;

        for (int i = 0; i < AllMarkers.Count; i++)
            if (i != markerIndex) SetLabel(i, Loc.T("닫힘"), ClosedColor);
        Managers.Sound?.PlayEvent(SoundEvent.OracleChoose);
        FinishWith(ChallengeGrade.Gold, m.Pos);
    }

    /// <summary>고른 룬을 보관함에 넣고 룬판을 연다. 안 고르고 시간이 다 됐으면 없다 · 빈 제단이면 정수 · 금화.</summary>
    protected override UniTask GrantRewardAsync(ChallengeGrade grade, Vector3 center, CancellationToken ct)
    {
        if (_offered.Count == 0)
        {
            int ch = EventRewards.Chapter(Run);
            int essence = EventRewards.Essence(ChallengeGrade.Platinum, ch);
            int gold    = EventRewards.Gold(ChallengeGrade.Platinum, ch);
            Vector3 at  = EventRewards.PlayerPosOr(center);
            EventRewards.DropEssence(at, essence);
            EventRewards.DropGold(at, gold);
            Notice(Loc.F("룬 대신 — 정수 +{0} · 금화 +{1}", essence, gold));
            return UniTask.CompletedTask;
        }
        if (string.IsNullOrEmpty(_chosen) || _chosen == TagEmpty) return UniTask.CompletedTask;

        var data = RuntimeItemData.FromServer(Managers.ItemData?.GetItem(_chosen));
        if (data == null || Run?.ItemInventory == null) return UniTask.CompletedTask;

        Run.EffectManager?.OnItemPickup(data);
        Run.ItemInventory.AddToStagingOverflow(data);   // 가득이어도 받는다 — 넘친 만큼은 룬판에서 놓거나 분해
        Notice(Loc.F("<color={0}>{1}</color> — 룬판의 {2} 칸이나 가운데에 놓는다",
                     ElementDef.IdHex(data.element), Loc.T(data.displayName), Loc.T(ElementDef.GetById(data.element)?.Name ?? "")));

        if (UI_GridPanel.Instance == null) Managers.UI?.ShowOverlayUI<UI_GridPanel>();
        UI_GridPanel.Instance?.ShowWithNewItem(data);
        return UniTask.CompletedTask;
    }

    // ── Private Methods ────────────────────────────────────────

    /// <summary>제단에 올릴 룬 — 차트의 성소 룬 중 아직 안 가진 것에서 셋(속성 순서로 늘어놓는다).</summary>
    private void PickOffers()
    {
        _offered.Clear();
        var chart = Managers.ItemData;
        if (chart == null || !chart.IsInitialized) return;

        var inv  = Run?.ItemInventory;
        var pool = new List<string>();
        foreach (var kv in chart.GetAllItems())
            if (IsShrineRune(kv.Key) && kv.Value != null && kv.Value.Count > 0 && (inv == null || !inv.IsAtMaxStack(kv.Key)))
                pool.Add(kv.Key);
        pool.Sort(StringComparer.Ordinal);   // 사전 순서는 정해져 있지 않다 — 같은 시드면 같은 제단이 서게

        _rng ??= new System.Random();
        for (int i = 0; i < pool.Count && _offered.Count < AltarCount; i++)
        {
            int j = _rng.Next(i, pool.Count);
            (pool[i], pool[j]) = (pool[j], pool[i]);
            _offered.Add(pool[i]);
        }
        _offered.Sort((a, b) => ElementRank(a).CompareTo(ElementRank(b)));
    }

    private static int ElementRank(string id)
    {
        var order = ElementDef.Order;
        string elem = Managers.ItemData?.GetItemMeta(id)?.element;
        for (int i = 0; i < order.Count; i++) if (order[i] == elem) return i;
        return order.Count;
    }

    /// <summary>
    /// [F] 글이 이름표 쪽(아래)으로 자라지 않게 바닥을 글 자리에 붙인다 — 가운데 정렬이면 효과 줄이 그 제단 이름표에 겹쳤다(11-05 사진).
    /// 긴 효과 줄은 <see cref="PromptWrapWidth"/>에서 접는다 — 안 접으면 옆 제단 이름표까지 뻗었다(얼음 룬 · Ch2 사진).
    /// </summary>
    private static void GrowUpward(TextMeshPro prompt)
    {
        if (prompt == null) return;
        prompt.rectTransform.pivot     = new Vector2(0.5f, 0f);
        prompt.rectTransform.sizeDelta = new Vector2(PromptWrapWidth, prompt.rectTransform.sizeDelta.y);
        prompt.alignment         = TextAlignmentOptions.Bottom;
        prompt.textWrappingMode  = TextWrappingModes.Normal;
    }

    /// <summary>제단 [F] 아래 — 효과 한 줄씩.</summary>
    private static string EffectLines(string id)
    {
        var entries = Managers.ItemData?.GetItem(id);
        if (entries == null) return string.Empty;
        var parts = new List<string>(entries.Count);
        foreach (var e in entries)
            if (!string.IsNullOrEmpty(e?.description)) parts.Add(Loc.T(e.description));
        return string.Join("\n", parts);
    }
}
