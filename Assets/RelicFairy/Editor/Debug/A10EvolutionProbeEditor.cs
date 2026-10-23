#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-22 A10 · E1 실측 — 해방기 첫 런 Ch3 보스에서 유물 진화 칸이 실제 경로(보스 처치 → 「되살아난 기억」)로 나오는지 본다.
/// 지금 유물의 조각(반응 조각 아님 · 안 가진 것) 2개를 쥐여 준다 — 진화 조건 「조각 2 + 보스」(<see cref="RelicEvolution.Ready"/>).
/// 판정 = 이어서 보스를 처치하면 나오는 로그 「유물 기억 카드 — … Evolution:…」.
/// </summary>
public static class A10EvolutionProbeEditor
{
    [MenuItem("RelicFairy/Debug/10-22 A10 해방기 진화 — 조각 2개 쥐여 주기 (Play)")]
    private static void GiveParts()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[A10Evo] 플레이 모드에서"); return; }
        var loadout = AppBootstrapper.Instance?.Loadout;
        var relic   = loadout?.Relic;
        if (loadout == null || relic == null || relic.Id == RelicId.None) { Debug.LogWarning("[A10Evo] 유물 없음"); return; }

        string relicId = relic.Id.ToString().ToLower();
        var all = Managers.RelicParts?.GetByRelic(relicId);
        int given = 0;
        if (all != null)
            foreach (var e in all)
            {
                if (given >= 2) break;
                if (e == null || e.IsReaction || loadout.HasRelicPart(e.part_id)) continue;
                loadout.AddRelicPart(e.part_id, RelicMemoryGrade.Faint);
                given++;
            }
        Debug.Log($"[A10Evo] 조각 {given}개 쥐여 줌(유물 {relicId} · 지금 {loadout.RelicPartIds.Count}개) · 해방기 {StoryProgress.IsLiberated} · " +
                  $"진화 열림 {RelicEvolution.Unlocked} · 준비 {RelicEvolution.Ready(loadout, relicId) != null}");
    }
}
#endif
