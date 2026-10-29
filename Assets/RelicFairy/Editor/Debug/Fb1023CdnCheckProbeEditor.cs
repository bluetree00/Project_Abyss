#if UNITY_EDITOR
using System.IO;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-23 사용자 「CDN은 등록했어」 확인 — 게임이 읽은 표(서버 우선)가 바탕화면 표(올린 것)와 같은 새 글인지 몇 줄을 대조한다.
/// 유물 조각 글(RELIC_PARTS_DATA) · 대사(DIALOGUE_DATA — 기존 키 고침 · 새 키). 결과 Temp/fb1023_cdn.txt · 로그 「[Fb1023Cdn] 끝」.
/// </summary>
public static class Fb1023CdnCheckProbeEditor
{
    private static readonly (string id, string line1)[] Parts =
    {
        ("g_dawn_to_noon",  "정오가 열려 태양흔이 터질 때 적마다 1개를 남긴다(1개뿐이면 다 터진다)"),     // 10-23 U7 새 글(txt_1023 표)
        ("g_daybreak_mark", "해 게이지가 80%에 닿는 순간 5m 안 적을 내 쪽으로 끌어당긴다"),
        ("l_second_judgment", "Q(심판) 마지막 일격에 맞은 적들의 낙인이 합쳐 6개 이상이면 0.35초 뒤 일격이 한 번 더 떨어진다(심판 피해의 45% · 광란당 1번)"),
    };

    private static readonly (string key, string text)[] Lines =
    {
        ("DK_Hint_Pyramid",      "붉게 차오르는 고리가 터져. 터진 고리로 옮겨!"),            // 기존 키 고침 — CDN을 올려야 바뀐다
        ("Seal_Regroup",         "…살아 있어? 맨 처음 숲까지 떨어졌어."),
        ("RelicTip_Gawain_Dawn", "가웨인의 해는 싸울 때만 돌아. 지금은 여명 — 적을 칠 때마다 태양흔이 쌓여."),
        ("Altar_Liberation",     "기억의 제단 여정 갈래에 새 칸 셋이 열렸어."),
    };

    [MenuItem("RelicFairy/Debug/10-23 CDN 표 반영 확인 (플레이 중 · 로그인 뒤)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[Fb1023Cdn] 플레이 중에"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        // 대사 표는 처음 쓸 때 읽는다 — 1차 실측은 읽기 전에 물어 빈 글을 받았다(10-23). 읽힐 때까지 기다린다.
        var dlgMgr = Managers.DialogueData;
        if (dlgMgr != null && !dlgMgr.IsInitialized) await dlgMgr.InitializeAsync();
        var sb = new StringBuilder("CDN 표 반영 확인\n");
        int pass = 0, total = 0;

        var parts = Managers.RelicParts;
        foreach (var (id, want) in Parts)
        {
            var e = parts?.GetById(id);
            bool ok = e != null && e.line1 == want;
            total++; if (ok) pass++;
            sb.AppendLine($"[{(ok ? "O" : "X")}] RELIC_PARTS_DATA {id} line1 「{e?.line1}」");
        }

        var dlg = Managers.DialogueData;
        foreach (var (key, want) in Lines)
        {
            var lines = dlg?.GetLines(key);
            string got = lines != null && lines.Length > 0 ? lines[0].text : null;
            bool ok = got != null && got.StartsWith(want);
            total++; if (ok) pass++;
            sb.AppendLine($"[{(ok ? "O" : "X")}] DIALOGUE_DATA {key} 「{got}」");
        }

        sb.Insert(0, $"통과 {pass}/{total}\n");
        File.WriteAllText("Temp/fb1023_cdn.txt", sb.ToString());
        Debug.Log("[Fb1023Cdn] 끝\n" + sb);
    }
}
#endif
