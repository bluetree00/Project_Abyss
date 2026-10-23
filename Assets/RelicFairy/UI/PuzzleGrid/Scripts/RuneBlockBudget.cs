/// <summary>
/// 룬판 룬 수 상한 — 판 전체에 놓을 수 있는 <b>룬 개수</b>의 상한. 사용자 10-08 「선택의 내용으로 블록의 최대 개수를 제한 —
/// 모든 시너지를 받지 못하고 특정 시너지를 대표로 진행하게」 · 10-10 「20칸 제한이 아니라 20개 제한 — 칸 제한이라 금방 막힌다」.
/// 큰 룬(높은 등급 · 칸이 많다)을 들일수록 같은 20개로 판을 더 덮는다 — 등급별 칸 수 사다리(Common 1~2 · Rare 3 · Epic 4~5 · Legendary 9~12)와 맞물린다.
/// <para>
/// 배치 확정(<see cref="GridManager.TryPlaceShape"/> — 끌어 놓기 · 칸 누르기 · 자리 비우고 배치)이 여기를 지나고,
/// 표시(룬판 시너지 머리띠 · 룬 선택 카드 · 상점 · 미니 판)가 같은 값을 읽는다. 세이브 복원은 막지 않는다 — 규칙 이전에 넘게 놓인 판도 잃지 않게.
/// </para>
/// </summary>
public static class RuneBlockBudget
{
    /// <summary>판 전체에 놓을 수 있는 룬 수.</summary>
    public const int Max = 20;

    /// <summary>상한 때문에 놓기가 막혔을 때 문구의 머리. 룬판이 이 문구면 룬 수를 붉게 깜빡인다.</summary>
    public const string FullPrefix = "룬판이 가득 찼다";   // 안내 끝맺음 「-다」(10-19)

    /// <summary>지금 판에 놓인 룬 수(이번 런 인벤토리의 배치 목록 — 룬판이 놓기 · 빼기마다 갱신, 세이브 · 결과창과 같은 값). 런이 없으면 0.</summary>
    public static int Used => GameRunBootstrapper.Instance?.Run?.ItemInventory?.PlacedCount ?? 0;

    /// <summary><paramref name="item"/>을 뺀 룬 수 — 이미 판에 놓인 룬을 옮길 때는 자기 자신을 세지 않는다.</summary>
    public static int UsedExcept(RuntimeItemData item)
    {
        var inv = GameRunBootstrapper.Instance?.Run?.ItemInventory;
        if (inv == null) return 0;
        int n = inv.PlacedCount;
        if (item != null && !string.IsNullOrEmpty(item.instanceId) && inv.IsPlaced(item.instanceId)) n--;
        return n;
    }

    /// <summary>룬 <paramref name="adding"/>개를 더 놓을 수 있는가 — <paramref name="freed"/>는 같이 치우는 룬 수.</summary>
    public static bool Fits(int used, int adding = 1, int freed = 0) => used - freed + adding <= Max;

    /// <summary>룬 선택 · 상점 카드 문구 — 「룬 12 + 1 / 20」, 넘치면 끝에 「· 넘침」.</summary>
    public static string Label(int adding = 1)
    {
        int used = Used;
        return Fits(used, adding) ? $"룬 {used} + {adding} / {Max}" : $"룬 {used} + {adding} / {Max} · 넘침";
    }

    /// <summary>상한 때문에 막힌 놓기의 토스트 문구.</summary>
    public static string FullMessage(int used) => $"{FullPrefix} ({used}/{Max}) — 판의 룬을 빼거나 분해한다";

    // ── 같은 룬 상한(10-14) ──────────────────────────────

    /// <summary>
    /// 같은 룬(같은 itemId — 속성이 달라도 같은 룬)을 판에 놓을 수 있는 개수. 사용자 10-14 「중복 룬 배치의 한계」 —
    /// 한 룬을 겹겹이 쌓아 한 효과로 몰지 않게. 개수 상한과 같은 자리(배치 확정)에서 막고, 세이브 복원은 막지 않는다.
    /// </summary>
    public const int MaxSame = 2;

    /// <summary>판에 놓인 같은 룬 수 — <paramref name="item"/> 자신은 빼고(이미 판에 놓인 룬을 옮길 때 자기를 세지 않는다).</summary>
    public static int SameExcept(RuntimeItemData item)
    {
        var inv = GameRunBootstrapper.Instance?.Run?.ItemInventory;
        if (inv == null || item == null || string.IsNullOrEmpty(item.itemId)) return 0;
        var placed = inv.PlacedItems;
        int n = 0;
        for (int i = 0; i < placed.Count; i++)
        {
            var it = placed[i];
            if (it != null && it.itemId == item.itemId && it.instanceId != item.instanceId) n++;
        }
        return n;
    }

    /// <summary>같은 룬을 하나 더 놓을 수 있는가 — <paramref name="freed"/>는 같이 치우는 같은 룬 수.</summary>
    public static bool FitsSame(RuntimeItemData item, int freed = 0) => SameExcept(item) - freed < MaxSame;

    /// <summary>같은 룬 상한 때문에 막힌 놓기의 토스트 문구.</summary>
    public static string SameMessage(RuntimeItemData item) =>
        $"같은 룬은 판에 {MaxSame}개까지 놓는다 (「{item?.displayName}」 {SameExcept(item)}/{MaxSame})";

    /// <summary>룬 선택 · 상점 카드 문구 — 같은 룬이 이미 상한이면 「같은 룬 2/2 · 판에 못 놓음」, 아니면 null.</summary>
    public static string SameLabel(RuntimeItemData item) =>
        FitsSame(item) ? null : $"같은 룬 {SameExcept(item)}/{MaxSame} · 판에 못 놓음";
}
