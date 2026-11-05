using System.Collections.Generic;
using Cysharp.Threading.Tasks;

/// <summary>
/// 도감 기록(11-02 Q2 사용자 「지금 활성화한 내용 · 사용한 것들은 도감에 체크」).
/// 「써 봄」 = 한 번이라도 지녀 본 것 — 슬롯 메타 <see cref="UserGameData.codexSeen"/>(로컬만 · 뒤끝 Param 아님)에 「종류:id」를 쉼표로.
/// 도감을 열 때 · 런이 끝날 때 · 「로비로 가기」 때 지금 지닌 것을 훑어 더하고 저장한다(<see cref="Sync"/>).
/// 룬판 · 서약 · 각인이 바뀔 때마다도 더해 둔다(<see cref="Note"/> · 저장은 다음 Sync — 잠깐 놓았다 뺀 룬이 빠졌다 · 11-05 Q32 D3).
/// 종류: r 룬(itemId) · a 근접 증강 · c 서약 원인 · e 서약 결과 · p 유물 기억 조각(effect_key) · v 유물 진화.
/// </summary>
public static class CodexLog
{
    private static readonly HashSet<string> s_seen = new();
    private static string s_parsedFrom;
    private static bool   s_dirty;   // Note로 더했지만 아직 저장하지 않았다

    [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { s_seen.Clear(); s_parsedFrom = null; s_dirty = false; }

    /// <summary>한 번이라도 지녀 봤나.</summary>
    public static bool IsSeen(char kind, string id)
    {
        Parse();
        return !string.IsNullOrEmpty(id) && s_seen.Contains(kind + ":" + id);
    }

    /// <summary>이번 런에 지금 켜진 것 — 「종류:id」 묶음.</summary>
    public static HashSet<string> Active(GameRunSession run)
    {
        var set = new HashSet<string>();
        if (run == null) return set;
        if (run.ItemInventory != null)
            foreach (var it in run.ItemInventory.PlacedItems) if (it != null) set.Add("r:" + it.itemId);

        var wm = run.Player != null ? run.Player.WeaponManager : null;
        foreach (var w in new[] { wm?.Weapon0Data, wm?.Weapon1Data })
        {
            if (w == null) continue;
            foreach (var def in MeleeAugmentCatalog.All)
                if (MeleeAugments.RankOn(w, def.Id) > 0) set.Add("a:" + def.Id);
        }

        var covs = run.CovenantHandler?.Covenants;
        if (covs != null)
            foreach (var c in covs)
            {
                if (c is CovenantSentence s)
                {
                    set.Add("c:" + s.CauseId);
                    for (int i = 0; i < s.ResultCount; i++) set.Add("e:" + s.ResultId(i));
                }
                else if (c is AssembledCovenant a) { set.Add("c:" + a.CauseId); set.Add("e:" + a.EffectId); }
            }

        var parts = run.Player != null && run.Player.RuneEffectsOrNull != null ? run.Player.RuneEffectsOrNull.PartsOrNull : null;
        if (parts != null) foreach (var p in parts.Active) if (p != null) set.Add("p:" + p.EffectKey);

        string evo = AppBootstrapper.Instance?.Loadout?.RelicEvolutionId;
        if (!string.IsNullOrEmpty(evo)) set.Add("v:" + evo);
        return set;
    }

    /// <summary>지금 지닌 것을 「써 봄」에 더한다 — 새로 더했거나 <see cref="Note"/>로 더해 둔 게 있을 때만 저장.</summary>
    public static void Sync(GameRunSession run)
    {
        if (BackendGameData.Instance?.Data == null) return;
        if (!Add(run) && !s_dirty) return;
        s_dirty = false;
        BackendGameData.Instance.SaveAsync().Forget();
    }

    /// <summary>지금 지닌 것을 「써 봄」에 더하되 저장은 다음 <see cref="Sync"/>로 미룬다 — 룬판 · 서약 · 각인이 바뀔 때마다(11-05 Q32 D3).</summary>
    public static void Note(GameRunSession run)
    {
        if (Add(run)) s_dirty = true;
    }

    private static bool Add(GameRunSession run)
    {
        var data = BackendGameData.Instance?.Data;
        if (data == null || run == null) return false;
        Parse();
        bool added = false;
        foreach (var key in Active(run))
        {
            if (key.Length < 3 || key.IndexOf(',') >= 0 || !s_seen.Add(key)) continue;
            data.codexSeen = string.IsNullOrEmpty(data.codexSeen) ? key : data.codexSeen + "," + key;
            added = true;
        }
        s_parsedFrom = data.codexSeen;
        return added;
    }

    private static void Parse()
    {
        string raw = BackendGameData.Instance?.Data?.codexSeen ?? "";
        if (ReferenceEquals(raw, s_parsedFrom) || raw == s_parsedFrom) return;
        s_parsedFrom = raw;
        s_seen.Clear();
        foreach (var k in raw.Split(',')) if (k.Length > 2) s_seen.Add(k);
    }
}
