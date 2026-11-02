using System.Collections.Generic;
using Cysharp.Threading.Tasks;

/// <summary>
/// 멀린 룬 보관(11-02 Q26 사용자 「플레이어가 죽은 뒤 멀린이 룬 하나를 보관하겠다며 — 배치했던 룬 중 하나를 다음 런 시작 때 받는다」).
/// 죽은 런의 판에 놓였던 룬을 정산 전에 떠 두고(<see cref="CapturePlaced"/>) → 부활 연출 뒤 하나를 고르면(<see cref="Keep"/>)
/// 슬롯 메타 <see cref="UserGameData.keptRune"/>(로컬만 · 뒤끝 Param엔 안 넣음 — lastLoadout과 같은 규약)에 「룬 id|속성」으로 적는다.
/// 다음 런 시작 대기방에서 보관함으로 돌려주고 비운다(<see cref="GrantPending"/>). 한 번에 하나 — 새로 맡기면 앞의 것을 덮는다.
/// </summary>
public static class MerlinRuneKeep
{
    private const char Sep = '|';

    /// <summary>맡긴 룬이 있는가.</summary>
    public static bool HasPending => !string.IsNullOrEmpty(BackendGameData.Instance?.Data?.keptRune);

    /// <summary>판에 놓인 룬 사본 목록 — 정산(EndRun)이 인벤토리를 비우기 전에 부른다. 없으면 빈 목록.</summary>
    public static List<RuntimeItemData> CapturePlaced(GameRunSession run)
    {
        var list = new List<RuntimeItemData>();
        var placed = run?.ItemInventory?.PlacedItems;
        if (placed == null) return list;
        foreach (var it in placed)
            if (it != null && !string.IsNullOrEmpty(it.itemId)) list.Add(it);
        return list;
    }

    /// <summary>고른 룬을 맡긴다(룬 id · 속성) — 슬롯 메타에 바로 저장.</summary>
    public static void Keep(RuntimeItemData rune)
    {
        var gd = BackendGameData.Instance;
        if (gd?.Data == null || rune == null || string.IsNullOrEmpty(rune.itemId)) return;
        gd.Data.keptRune = rune.itemId + Sep + (rune.element ?? "");
        gd.SaveAsync().Forget();
    }

    /// <summary>런 시작 — 맡긴 룬을 보관함으로 돌려주고 비운다. 돌려준 룬(없거나 데이터에서 사라졌으면 null).</summary>
    public static RuntimeItemData GrantPending(GameRunSession run)
    {
        var gd = BackendGameData.Instance;
        string raw = gd?.Data?.keptRune;
        var inv = run?.ItemInventory;
        if (string.IsNullOrEmpty(raw) || inv == null) return null;

        int cut = raw.IndexOf(Sep);
        string id = cut >= 0 ? raw.Substring(0, cut) : raw;
        string element = cut >= 0 ? raw.Substring(cut + 1) : "";
        var data = RuntimeItemData.FromSO(ItemSORegistry.Find(id));
        if (data != null)
        {
            if (!string.IsNullOrEmpty(element)) data.element = element;   // 일반 · 희귀는 그 판의 속성 그대로
            data.EnsureElement();
            inv.AddToStagingOverflow(data);   // 받은 룬 — 보관함이 차 있어도 넣는다(넘친 만큼은 룬판에서 놓거나 분해)
        }
        gd.Data.keptRune = "";                // 돌려줬다(데이터에서 사라진 룬이면 그냥 비운다)
        gd.SaveAsync().Forget();
        return data;
    }
}
