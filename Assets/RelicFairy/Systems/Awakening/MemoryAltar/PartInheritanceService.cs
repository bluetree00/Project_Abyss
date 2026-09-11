using UnityEngine;

/// <summary>
/// 「파츠 영구 계승」 — 지난 런에서 마지막으로 고른 파츠 <b>하나</b>를 다음 런으로 넘긴다.
/// 노드 문구 그대로 「다음 런 계승 0개 → 1개」다.
///
/// <para><b>왜 자동인가</b> — 고르는 화면을 따로 두지 않는다. 무엇을 계승할지는 이미
/// <b>런 중 마지막 드래프트에서 고른 것</b>으로 정해져 있다. 선택은 그때 한 번 하는 것으로 족하고,
/// 거점에 팝업을 하나 더 끼우면 반복 플레이에서 성가신 관문이 된다.</para>
///
/// <para><b>코어 파츠는 계승하지 않는다</b> — 코어는 최종 직전 보스의 보상(진화)이라,
/// 그것을 들고 1챕터를 시작하면 초반이 무너지고 「코어 파츠 후보」 해금들의 의미도 함께 사라진다.
/// 기능 파츠(effect·behavior·trigger)만 넘어간다.</para>
/// </summary>
public static class PartInheritanceService
{
    /// <summary>계승이 해금돼 있는가.</summary>
    public static bool Unlocked => MemoryAltarService.IsUnlocked(MemoryAltarCatalog.PartsInherit);

    /// <summary>다음 런으로 넘어갈 파츠 id. 없으면 빈 문자열.</summary>
    public static string InheritedPartId
    {
        get
        {
            var data = BackendGameData.Instance?.Data;
            return Unlocked && data != null ? data.inheritedPartId ?? "" : "";
        }
    }

    /// <summary>계승 파츠의 표시 이름. 없거나 차트에 없으면 빈 문자열.</summary>
    public static string InheritedPartName
    {
        get
        {
            string id = InheritedPartId;
            if (string.IsNullOrEmpty(id)) return "";
            var entry = Managers.RelicParts?.GetById(id);
            return entry != null ? entry.part_name : "";
        }
    }

    /// <summary>
    /// 런이 끝날 때 계승 대상을 확정한다. <b>Loadout이 비워지기 전에</b> 불러야 한다.
    /// <para>해금 전이면 아무 것도 하지 않는다 — 해금하는 순간부터 <b>그 다음 런</b>이 대상이다.</para>
    /// </summary>
    public static void CaptureFromRun(PlayerLoadout loadout)
    {
        if (!Unlocked || loadout == null) return;

        var data = BackendGameData.Instance?.Data;
        if (data == null) return;

        string picked = LastFunctionalPart(loadout);
        if (string.IsNullOrEmpty(picked)) return;   // 이번 런에 기능 파츠가 없으면 <b>직전 계승을 유지</b>한다

        data.inheritedPartId = picked;
        Debug.Log($"[계승] 다음 런으로 넘길 파츠 확정 — {picked}");
    }

    /// <summary>
    /// 런 시작 시 계승 파츠를 실제로 얹는다. 이미 갖고 있으면 아무 일도 하지 않는다.
    ///
    /// <para><b>유물이 다르면 얹지 않는다</b> — 파츠는 유물별(<c>relic_id</c>)이라
    /// 가웨인 파츠를 랜슬롯에 붙이면 효과 훅이 걸리지 않는 <b>죽은 파츠</b>가 된다.
    /// 이때 계승 기록은 지우지 않는다 — 그 유물로 돌아오면 다시 살아나야 한다.</para>
    /// </summary>
    public static void ApplyToRun(PlayerLoadout loadout)
    {
        if (loadout == null) return;

        string id = InheritedPartId;
        if (string.IsNullOrEmpty(id)) return;
        if (loadout.HasRelicPart(id)) return;

        var entry = Managers.RelicParts?.GetById(id);
        if (entry == null) return;

        var relic = loadout.Relic;
        if (relic == null || relic.Id == RelicId.None) return;

        string relicId = relic.Id.ToString().ToLower();   // Gawain → "gawain"
        if (!string.Equals(entry.relic_id, relicId, System.StringComparison.OrdinalIgnoreCase))
        {
            Debug.Log($"[계승] 유물이 달라 건너뜀 — 파츠={entry.relic_id} / 이번 런={relicId}");
            return;
        }

        loadout.AddRelicPart(id);
        Debug.Log($"[계승] 이번 런에 계승 파츠 적용 — {entry.part_name} ({id})");
    }

    // ── Private Methods ──────────────────────────────────────

    /// <summary>보유 목록에서 <b>마지막</b> 기능 파츠를 고른다(획득 순서대로 쌓이므로 끝이 최신이다).</summary>
    private static string LastFunctionalPart(PlayerLoadout loadout)
    {
        var ids = loadout.RelicPartIds;
        for (int i = ids.Count - 1; i >= 0; i--)
        {
            var entry = Managers.RelicParts?.GetById(ids[i]);
            if (entry == null) continue;
            if (entry.part_kind == RelicPartKind.Core) continue;   // 코어는 넘기지 않는다
            return ids[i];
        }
        return "";
    }
}
