using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using RelicFairy.UI;

/// <summary>
/// 형이 처음 터질 때 화면 아래 자막 한 줄 — 「칼바람 — 끊기지 않고 몰아치면…」(계정당 1회, RelicMemoryHints와 같은 판단).
/// 계정 기록 <c>seen.wf.&lt;형 id&gt;</c>. 기록을 못 읽으면 띄우지 않는다.
/// </summary>
public static class WeaponFormHints
{
    private const string Prefix = "seen.wf.";
    private static readonly HashSet<string> s_checked = new();

    public static void TryShowOnce(WeaponFormSO form)
    {
        if (form == null || string.IsNullOrEmpty(form.FormId) || !s_checked.Add(form.FormId)) return;
        var backend = BackendGameData.Instance;
        var data    = backend?.Data;
        if (data == null) return;
        if (!data.SetRecordMax(Prefix + form.FormId, 1)) return;   // 이미 본 형
        UI_BossBark.Show($"{form.DisplayName} — {form.Line}", BossBarkType.Bark);
        backend.SaveAsync().Forget();
    }
}
