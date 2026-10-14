#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 10-10(10-11 v2) 「되살아난 기억」 화면 촬영 — 리서치 B §6 「고칠 값」 반영 확인.
/// 런 안에서 보스 처치를 흉내 내(<c>Simulate Boss Clear with Relic</c>) 창이 뜨면 카드 셋의 등급을 흐릿 · 선명 · 찬란으로 바꿔
/// 같은 Setup으로 다시 그리고(등급 셋이 한 화면에), 고르기 전 · 테가 그려지는 중 · 고른 상태(찬란 · 선명)를 찍는다.
/// 끝에 가운데 카드로 확정해 창을 닫는다 — 바꾼 등급이 그대로 적용되므로 실측 전용이다. 실행 동안 플레이어 무적.
/// 결과: Temp/relic_ux2_probe.txt · Temp/ui_shots/RelicUx2_*.png · 로그 「[RelicUx2] 끝」.
/// </summary>
public static class RelicDraftUx2ProbeEditor
{
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string SkipDialogueMenu = "RelicFairy/Debug/서약 발동 실측/대사 전부 넘기기 (플레이 중)";
    private static readonly RelicMemoryGrade[] Forced = { RelicMemoryGrade.Faint, RelicMemoryGrade.Clear, RelicMemoryGrade.Radiant };

    [MenuItem("RelicFairy/Debug/10-10 되살아난 기억 화면 촬영 (런 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[RelicUx2] 플레이 모드(런 안)에서만 동작한다."); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("되살아난 기억 화면 촬영(10-11 v2)\n");
        try
        {
            Directory.CreateDirectory("Temp/ui_shots");
            GameRunBootstrapper.Instance?.Run?.Player?.SetInvincible(90f);
            EditorApplication.ExecuteMenuItem("RelicFairy/Debug/Simulate Boss Clear with Relic (Play)");

            UI_RelicPartDraftPopup popup = null;
            float t0 = Time.unscaledTime;
            while (popup == null && Time.unscaledTime - t0 < 45f)
            {
                EditorApplication.ExecuteMenuItem(SkipDialogueMenu);
                await UniTask.Delay(500, ignoreTimeScale: true);
                popup = Object.FindFirstObjectByType<UI_RelicPartDraftPopup>(FindObjectsInactive.Exclude);
            }
            if (popup == null) { sb.AppendLine("45초 안에 창이 안 떴다"); return; }
            string relicName = AppBootstrapper.Instance?.Loadout?.Relic?.DisplayName ?? "유물";
            sb.AppendLine($"창 뜸 — 보스 처치 흉내부터 {Time.unscaledTime - t0:0.0}초 · 유물 {relicName} · 화면 {Screen.width}×{Screen.height}");
            await UniTask.Delay(700, ignoreTimeScale: true);

            // ① 등급 셋을 한 화면에 — 카드 등급만 바꿔 같은 Setup으로 다시 그린다(새 조각 · 선명하게 카드만 등급이 있다)
            var t = typeof(UI_RelicPartDraftPopup);
            var draft = t.GetField("_draft", Inst)?.GetValue(popup) as RelicDraft;
            int n = draft != null ? draft.Cards.Count : 0;
            for (int i = 0; i < n; i++)
            {
                var c = draft.Cards[i];
                bool graded = c.Kind == RelicDraftCardKind.NewFragment || c.Kind == RelicDraftCardKind.Sharpen;
                if (graded && i < Forced.Length) c.Grade = Forced[i];
                sb.AppendLine($"  카드 {i}: {c.Kind} · {c.Grade}{(graded ? "" : "(등급 없음 — 못 바꿈)")} · {c.Entry?.part_name} · 자리 {c.Entry?.anchor ?? c.Anchor}");
            }
            var redraw = t.GetField("_redrawBtn", Inst)?.GetValue(popup) as GameObject;
            if (draft != null) popup.Setup(draft, redraw != null && redraw.activeSelf, relicName);
            await UniTask.Delay(600, ignoreTimeScale: true);
            Shot("RelicUx2_grades");
            Audit(popup, sb, "고르기 전", chrome: true);
            await UniTask.Delay(250, ignoreTimeScale: true);

            // ② 찬란 카드를 고른다 — 테가 그려지는 중(0.12초) · 다 그려진 뒤(0.45초)
            var setSel = t.GetMethod("SetSelected", Inst);
            int radiant = Mathf.Clamp(2, 0, Mathf.Max(0, n - 1));
            setSel?.Invoke(popup, new object[] { radiant });
            await UniTask.Delay(120, ignoreTimeScale: true);
            Shot("RelicUx2_trace");
            await UniTask.Delay(330, ignoreTimeScale: true);
            Shot("RelicUx2_selected_radiant");
            Audit(popup, sb, $"카드 {radiant}(찬란) 고름", chrome: false);
            await UniTask.Delay(250, ignoreTimeScale: true);

            // ③ 가운데(선명)로 바꿔 고른다 — 찬란 후광이 물러난 카드에 남아도 선택 표시가 하나로 읽히는지
            int mid = Mathf.Clamp(1, 0, Mathf.Max(0, n - 1));
            setSel?.Invoke(popup, new object[] { mid });
            await UniTask.Delay(450, ignoreTimeScale: true);
            Shot("RelicUx2_selected");
            Audit(popup, sb, $"카드 {mid}(선명) 고름", chrome: false);
            await UniTask.Delay(250, ignoreTimeScale: true);

            t.GetMethod("OnConfirmClicked", Inst)?.Invoke(popup, null);
            await UniTask.Delay(1500, ignoreTimeScale: true);
            sb.AppendLine($"확정(카드 {mid}) 뒤 창 {(popup != null ? "남음" : "닫힘")}");

            // ④ 일시정지 「되찾은 기억」 쪽(10-13 Artsystack — 남색 판 · 묶음 문양 · 밑줄 · 구분선)
            var menu = Object.FindFirstObjectByType<UI_EscMenu>(FindObjectsInactive.Include);
            if (menu == null) menu = new GameObject("ProbeEscMenu").AddComponent<UI_EscMenu>();
            menu.Open();
            await UniTask.Delay(400, ignoreTimeScale: true);
            typeof(UI_EscMenu).GetMethod("OnMemory", Inst)?.Invoke(menu, null);
            await UniTask.Delay(600, ignoreTimeScale: true);
            var page = Object.FindFirstObjectByType<UI_RelicMemoryPage>();
            var content = page != null ? page.transform.Find("Panel/Viewport/Content") : null;
            Shot("RelicUx2_memory_page");
            sb.AppendLine($"되찾은 기억 쪽 — {(page != null && page.gameObject.activeSelf ? "열림" : "못 엶")} · 줄 {(content != null ? content.childCount : 0)}");
            await UniTask.Delay(300, ignoreTimeScale: true);
            menu.Close();
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            File.WriteAllText("Temp/relic_ux2_probe.txt", sb.ToString());
            Debug.Log("[RelicUx2] 끝\n" + sb);
        }
    }

    private static void Shot(string name) => ScreenCapture.CaptureScreenshot($"Temp/ui_shots/{name}.png");

    /// <summary>
    /// 카드마다 크기 · 배율 · 알파 · 테 상태 · 후광 · 테 상자 수 · 글자(글꼴 · 크기 · 굵게/기울임 · 따옴표)를 적는다.
    /// 테 상자 = MakeFrame이 남기는 안쪽 「Fill」(옛 칩 · 효과 박스) — 0이어야 카드 테 + 효과 바탕 2개다.
    /// </summary>
    private static void Audit(UI_RelicPartDraftPopup popup, StringBuilder sb, string label, bool chrome)
    {
        sb.AppendLine($"[{label}]");
        if (chrome) AuditChrome(popup, sb);
        for (int i = 0; i < 3; i++)
        {
            var card = ShopUIStyle.FindDeep(popup.transform, $"Card{i}") as RectTransform;
            if (card == null) continue;
            var frame = card.GetComponentInChildren<UIThinFrame>(true);
            var group = card.GetComponent<CanvasGroup>();
            var halo  = card.Find("GradeHalo")?.GetComponent<Image>();
            var body  = card.Find("Fill");
            int framedBoxes = 0;
            bool effectFill = false;
            if (body != null)
                foreach (var tr in body.GetComponentsInChildren<Transform>(true))
                {
                    if (tr != body && tr.name == "Fill") framedBoxes++;
                    if (tr.name == "Headline") effectFill = true;
                }
            sb.AppendLine($"  카드 {i}: {card.rect.width:0}×{card.rect.height:0} · 배율 {card.localScale.x:0.000} · 알파 {(group != null ? group.alpha : 1f):0.00}"
                        + $" · 테 {(frame != null ? frame.State.ToString() : "없음")} · 후광 {(halo != null ? halo.color.a.ToString("0.00") : "없음")}"
                        + $" · 안쪽 테 상자 {framedBoxes} · 효과 바탕 {(effectFill ? "있음" : "없음")}");
            AuditTexts(card, sb, "    ");
        }
    }

    private static void AuditChrome(UI_RelicPartDraftPopup popup, StringBuilder sb)
    {
        var bar  = ShopUIStyle.FindDeep(popup.transform, "TitleBar");
        var band = bar != null ? bar.GetComponent<Image>() : null;
        sb.AppendLine($"  창 머리: 제목 띠 {(band == null ? "없음" : band.enabled ? "켜짐" : "꺼짐")} · 금선 {(bar != null && bar.Find("TitleRule") != null ? "있음" : "없음")}");
        foreach (var name in new[] { "Title", "Subtitle", "ConfirmBtn", "RedrawBtn" })
        {
            var tr = ShopUIStyle.FindDeep(popup.transform, name);
            if (tr != null) AuditTexts(tr, sb, $"  {name} ");
        }
    }

    private static void AuditTexts(Transform root, StringBuilder sb, string indent)
    {
        int bold = 0, italic = 0, notNoto = 0, quotes = 0;
        var sizes = new SortedSet<int>();
        foreach (var tx in root.GetComponentsInChildren<TMP_Text>(true))
        {
            if (!tx.gameObject.activeInHierarchy || string.IsNullOrEmpty(tx.text)) continue;
            if ((tx.fontStyle & FontStyles.Bold) != 0) bold++;
            if ((tx.fontStyle & FontStyles.Italic) != 0) italic++;
            if (tx.font == null || !tx.font.name.Contains("Noto")) notNoto++;
            if (tx.text.IndexOf('“') >= 0 || tx.text.IndexOf('”') >= 0) quotes++;
            sizes.Add(Mathf.RoundToInt(tx.fontSize));
            sb.AppendLine($"{indent}「{Plain(tx.text)}」 {tx.font?.name} {tx.fontSize:0.#} {tx.fontStyle}");
        }
        sb.AppendLine($"{indent}= 굵게 {bold} · 기울임 {italic} · Noto 아님 {notNoto} · 따옴표 {quotes} · 크기 {string.Join("/", sizes)}");
    }

    private static string Plain(string s)
    {
        s = System.Text.RegularExpressions.Regex.Replace(s, "<[^>]+>", "").Replace('\n', ' ');
        return s.Length > 40 ? s.Substring(0, 40) + "…" : s;
    }
}
#endif
