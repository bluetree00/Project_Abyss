using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using RelicFairy.UI;

/// <summary>
/// 유물 성장 v2 — 처음 일어난 반응 · 처음 켜진 조각을 화면 아래 자막 한 줄로 알려 준다(설계 v2 §6-3, 계정당 1회).
/// 자연스럽게 배우게 하는 장치다: 「이름 — 첫 줄」. 계정 기록 <c>seen.rm.&lt;조각 id&gt;</c>에 남겨 다시 띄우지 않는다.
/// 기록을 못 읽으면(오프라인 등) 띄우지 않는다 — 매 런 같은 안내가 반복되느니 안 보이는 쪽이 낫다(FirstRunService와 같은 판단).
/// </summary>
public static class RelicMemoryHints
{
    private const string Prefix = "seen.rm.";
    private static readonly HashSet<string> s_checked = new();   // 이번 실행에서 이미 본 키 — 기록 조회를 되풀이하지 않는다

    /// <summary>그 조각을 처음 겪는 순간이면 자막 한 줄을 띄우고 기록한다.</summary>
    public static void TryShowOnce(string partId)
    {
        if (string.IsNullOrEmpty(partId) || !s_checked.Add(partId)) return;
        var backend = BackendGameData.Instance;
        var data    = backend?.Data;
        var entry   = Managers.RelicParts?.GetById(partId);
        if (data == null || entry == null) return;
        if (!data.SetRecordMax(Prefix + partId, 1)) return;   // 이미 본 조각
        UI_BossBark.Show($"{entry.part_name} — {entry.Line(1)}", BossBarkType.Bark);
        backend.SaveAsync().Forget();
    }
}
