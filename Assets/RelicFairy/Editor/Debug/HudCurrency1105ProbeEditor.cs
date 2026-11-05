#if UNITY_EDITOR
using System.IO;
using System.Linq;
using System.Text;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 11-05 #3 사용자 「HUD 오른쪽 위 재화가 좀 잘린다」 — 재화 칸 4종 + 「Tab 룬판 · 보관함 N」 칸을 모두 켜고(보관함에 룬 1개)
/// 한 · 영 × 보통 수(9,999) · 큰 수(99,999)로 칸마다 글이 두 줄로 꺾였나 · 칸(테두리 안쪽) 밖으로 나갔나 · 화면 밖인가를 잰다.
/// 결과 Temp/hudcur1105.txt · Temp/ui_shots/HC1105_*.png · 로그 「[HC1105] 끝」. 런 안에서(플레이 중).
/// </summary>
public static class HudCurrency1105ProbeEditor
{
    private const string Tag = "[HC1105]";
    private const string ShotDir = "Temp/ui_shots";
    private const float  InsetX = 8f;   // 칸 좌우 끝이 뾰족해 그만큼 안쪽까지만 글자가 들어가야 한다
    private const float  InsetY = -4f;  // 세로 글 범위는 글꼴 줄 높이라 실제 획보다 크다 — 칸 밖 4px까지 봐준다(두 줄 꺾임은 줄 수로)

    [MenuItem("RelicFairy/Debug/11-05 HUD 재화 칸 넘침 점검 (런 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying || GameRunBootstrapper.Instance?.Run?.Player == null) { Debug.LogWarning($"{Tag} 런 안에서"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("11-05 HUD 재화 칸 넘침\n");
        Directory.CreateDirectory(ShotDir);
        var inv = GameRunBootstrapper.Instance.Run.ItemInventory;
        var prevLang = Loc.Current;
        RuntimeItemData item = null;
        int fails = 0;
        try
        {
            var hud = Object.FindFirstObjectByType<HudView>();
            var row = hud != null ? hud.GetComponentsInChildren<RectTransform>(true).FirstOrDefault(r => r.name == "CurrencyRow") : null;
            if (row == null) { sb.AppendLine("FAIL 재화 줄(CurrencyRow) 없음"); return; }

            foreach (var entry in Managers.ItemData.GetAllItems().Values)
            {
                var d = RuntimeItemData.FromServer(entry);
                if (d == null || d.shapeId <= 0) continue;
                d.EnsureElement();
                if (inv.AddToStaging(d)) item = d;
                break;
            }
            sb.AppendLine($"보관함 룬 {(item != null ? item.displayName : "못 넣음")} · 보관함 {inv.StagingCount}");

            foreach (var lang in new[] { Loc.Language.Korean, Loc.Language.English })
            foreach (int v in new[] { 9999, 99999 })
            {
                Loc.SetLanguage(lang);
                hud.SetGold(v); hud.SetEnhanceMaterial(v); hud.SetRuneOre(v); hud.SetEssence(v);
                await UniTask.Delay(2600, DelayType.Realtime);   // 숫자 굴림이 끝날 때까지
                Canvas.ForceUpdateCanvases();
                string label = $"{(lang == Loc.Language.Korean ? "ko" : "en")}_{v}";
                sb.AppendLine($"\n## {label} — 화면 {Screen.width}×{Screen.height}");
                fails += Audit(row, sb);
                ScreenCapture.CaptureScreenshot($"{ShotDir}/HC1105_{label}.png");
                await UniTask.Delay(300, DelayType.Realtime);
            }
            sb.AppendLine($"\n{(fails == 0 ? "PASS" : "FAIL")} 넘친 칸 {fails}");
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            Loc.SetLanguage(prevLang);
            if (item != null) inv.DiscardFromStaging(item);
            File.WriteAllText("Temp/hudcur1105.txt", sb.ToString());
            Debug.Log($"{Tag} 끝\n{sb}");
        }
    }

    private static int Audit(RectTransform row, StringBuilder sb)
    {
        int fails = 0;
        var canvas = row.GetComponentInParent<Canvas>()?.rootCanvas;
        var cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        var corners = new Vector3[4];
        for (int i = 0; i < row.childCount; i++)
        {
            var pill = row.GetChild(i) as RectTransform;
            if (pill == null || !pill.gameObject.activeInHierarchy) continue;
            var t = pill.GetComponentInChildren<TMP_Text>(false);
            if (t == null) continue;
            t.ForceMeshUpdate();
            var b = t.textBounds;
            Vector2 lo = pill.InverseTransformPoint(t.rectTransform.TransformPoint(b.min));
            Vector2 hi = pill.InverseTransformPoint(t.rectTransform.TransformPoint(b.max));
            var r = pill.rect;
            float outL = Mathf.Max(0f, r.xMin + InsetX - lo.x), outR = Mathf.Max(0f, hi.x - (r.xMax - InsetX));
            float outB = Mathf.Max(0f, r.yMin + InsetY - lo.y), outT = Mathf.Max(0f, hi.y - (r.yMax - InsetY));
            pill.GetWorldCorners(corners);
            Vector2 s0 = RectTransformUtility.WorldToScreenPoint(cam, corners[0]), s2 = RectTransformUtility.WorldToScreenPoint(cam, corners[2]);
            bool offScreen = s0.x < 0f || s0.y < 0f || s2.x > Screen.width || s2.y > Screen.height;
            bool wrapped = t.textInfo.lineCount > 1;
            bool over = outL + outR + outB + outT > 0.5f;
            bool bad = wrapped || over || offScreen;
            if (bad) fails++;
            sb.AppendLine($"{(bad ? "FAIL" : "PASS")} {pill.name} 「{t.GetParsedText()}」 줄 {t.textInfo.lineCount} · 칸 {r.width:0}×{r.height:0} · 글 {b.size.x:0}×{b.size.y:0}"
                + $" · 넘침 좌 {outL:0} 우 {outR:0} 아래 {outB:0} 위 {outT:0}{(offScreen ? " · 화면 밖" : "")}");
        }
        return fails;
    }
}
#endif
