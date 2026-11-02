using System.Collections.Generic;
using Cysharp.Threading.Tasks;

/// <summary>
/// 멀린 룬 보관(11-02 Q26 사용자 「플레이어가 죽은 뒤 멀린이 룬 하나를 보관하겠다며 — 배치했던 룬 중 하나를 다음 런 시작 때 받는다」).
/// 죽은 런의 판에 놓였던 룬을 정산 전에 떠 두고(<see cref="CapturePlaced"/>) → 부활 연출 뒤 하나를 고르면(<see cref="Keep"/>)
/// 슬롯 메타 <see cref="UserGameData.keptRune"/>(로컬만 · 뒤끝 Param엔 안 넣음 — lastLoadout과 같은 규약)에 「룬 id|속성」으로 적는다.
/// 다음 런 시작 대기방에서 보관함으로 돌려주고 비운다(<see cref="GrantPending"/>). 한 번에 하나 — 새로 맡기면 앞의 것을 덮는다.
/// 11-02 밤 사용자 — 돌려받은 룬은 다시 맡길 수 없다(그 사본에 <see cref="RuntimeItemData.fromMerlinKeep"/>) · 다음 런에서 새로 얻은 같은 룬은 맡길 수 있다.
/// </summary>
public static class MerlinRuneKeep
{
    private const char Sep = '|';

    /// <summary>맡긴 룬이 있는가.</summary>
    public static bool HasPending => !string.IsNullOrEmpty(BackendGameData.Instance?.Data?.keptRune);

    /// <summary>
    /// 이번 런에서 얻은 룬(판 + 보관함) — 정산(EndRun)이 인벤토리를 비우기 전에 부른다. 멀린이 돌려준 룬은 빠진다(다시 맡길 수 없다). 없으면 빈 목록.
    /// </summary>
    public static List<RuntimeItemData> CaptureOwned(GameRunSession run)
    {
        var list = new List<RuntimeItemData>();
        var inv = run?.ItemInventory;
        if (inv == null) return list;
        Add(list, inv.PlacedItems);
        Add(list, inv.StagingItems);
        return list;
    }

    private static void Add(List<RuntimeItemData> list, IReadOnlyList<RuntimeItemData> items)
    {
        if (items == null) return;
        foreach (var it in items)
            if (it != null && !string.IsNullOrEmpty(it.itemId) && !it.fromMerlinKeep) list.Add(it);
    }

    /// <summary>고른 룬을 맡긴다(룬 id · 속성) — 슬롯 메타에 바로 저장.</summary>
    public static void Keep(RuntimeItemData rune)
    {
        var gd = BackendGameData.Instance;
        if (gd?.Data == null || rune == null || string.IsNullOrEmpty(rune.itemId)) return;
        gd.Data.keptRune = rune.itemId + Sep + (rune.element ?? "");
        gd.SaveAsync().Forget();
    }

    /// <summary>
    /// 런 시작 — 맡긴 룬을 보관함으로 돌려준다. 돌려준 룬(없거나 데이터에서 사라졌으면 null).
    /// 맡긴 칸은 이 런의 첫 런 저장 때 비운다(<see cref="CommitClaim"/> · 11-02 Q32 B4 — 대기방에서 끄거나 로비로 가면 런 세이브가 없어 룬이 사라졌다).
    /// </summary>
    public static RuntimeItemData GrantPending(GameRunSession run)
    {
        var gd = BackendGameData.Instance;
        string raw = gd?.Data?.keptRune;
        var inv = run?.ItemInventory;
        if (string.IsNullOrEmpty(raw) || inv == null || run.KeptRunePending) return null;

        int cut = raw.IndexOf(Sep);
        string id = cut >= 0 ? raw.Substring(0, cut) : raw;
        string element = cut >= 0 ? raw.Substring(cut + 1) : "";
        var data = RuntimeItemData.FromSO(ItemSORegistry.Find(id));
        if (data != null)
        {
            if (!string.IsNullOrEmpty(element)) data.element = element;   // 일반 · 희귀는 그 판의 속성 그대로
            data.EnsureElement();
            data.fromMerlinKeep = true;       // 11-02 밤 사용자 — 돌려받은 이 룬은 다시 맡길 수 없다(런이 끝나면 사라진다)
            inv.AddToStagingOverflow(data);   // 받은 룬 — 보관함이 차 있어도 넣는다(넘친 만큼은 룬판에서 놓거나 분해)
            run.KeptRunePending = true;       // 비우기는 첫 런 저장 때
            return data;
        }
        gd.Data.keptRune = "";                // 데이터에서 사라진 룬 — 돌려줄 것이 없으니 바로 비운다
        gd.SaveAsync().Forget();
        return null;
    }

    /// <summary>이 런의 런 저장 — 돌려준 룬이 런 세이브(보관함 · 판)에 실렸으니 맡긴 칸을 비운다(11-02 Q32 B4).</summary>
    public static void CommitClaim(GameRunSession run)
    {
        if (run == null || !run.KeptRunePending) return;
        run.KeptRunePending = false;
        var gd = BackendGameData.Instance;
        if (gd?.Data == null || string.IsNullOrEmpty(gd.Data.keptRune)) return;
        gd.Data.keptRune = "";
        gd.SaveAsync().Forget();
    }
}
