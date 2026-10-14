#if UNITY_EDITOR
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using RelicFairy.UI;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-14 보스 「이름 + 칭호」 카드 실측 — 레터박스 + 등장 자막(두 줄) 위에 카드를 띄워 겹침 · 넘침 · 시간 · 건너뛰기를 잰다.
/// 결과: Temp/boss_title_probe.txt · Temp/ui_shots/BTC_*.png · 로그 「[BossTitleProbe] 끝」.
/// </summary>
public static class BossTitleCardProbeEditor
{
    private const BindingFlags Any = BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    [MenuItem("RelicFairy/Debug/10-14 보스 칭호 카드 촬영 (플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[BossTitleProbe] 플레이 모드에서"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("보스 칭호 카드 실측\n");
        int pass = 0, total = 0;
        void Check(string what, bool ok) { total++; if (ok) pass++; sb.AppendLine($"CHECK {what}: {ok}"); }
        var hub = Object.FindFirstObjectByType<TestHubLauncher>();
        bool hubWas = hub != null && hub.enabled;
        if (hub != null) hub.enabled = false;   // 허브 OnGUI 판이 사진을 가린다(1차 사진에서 금선 · 마름모가 가려졌다)
        try
        {
            // ① 레터박스 + 컷신 HUD 모드 + 등장 자막 두 줄 + 카드(가장 붐비는 경우 — cf 등장 연출과 같은 조건)
            await CinematicFrame.ShowAsync(0.3f, null, CancellationToken.None);
            var run      = GameRunBootstrapper.Instance?.Run;
            var prevMode = run != null ? run.CurrentHudMode : HUDIds.Mode.None;
            RelicFairy.Monster.LichCinematics.EndSceneHud(true);
            sb.AppendLine($"  HUD 모드: {(run != null ? run.CurrentHudMode.ToString() : "(런 없음 — 확인 못 함)")}");
            UI_BossBark.Show("왕을 배신한 검이 다시 일어선다. 이번에는 봉인도 맹세도 그를 묶어 두지 못하리라.", BossBarkType.BossIntro);
            await Wait(0.3f);
            BossTitleCard.Show("타락한 모르드레드", "사슬 없는 반역의 검", 2.6f);
            await Wait(0.15f); await Shot("BTC_A_0.15");
            await Wait(0.30f); await Shot("BTC_A_0.45");
            await Wait(0.75f);
            var card = Card();
            Check("1.2초에 카드 떠 있음 · 완전히 보임", card != null && Group(card).alpha > 0.99f);
            if (card != null) Measure(sb, Check, card, barkOverlap: true);
            await Shot("BTC_A_1.2");
            await Wait(1.6f);   // 2.8초 — 2.6초 카드는 끝났어야
            Check("2.8초에 카드 걷힘(2.6초 지정)", Card() == null);
            UI_BossBark.Dismiss(0f);
            if (run != null) run.RequestHudMode(prevMode);
            await CinematicFrame.HideAsync(0.3f, null, CancellationToken.None);
            await Wait(0.8f);

            // ② 레터박스 없음(대부분의 보스 등장) · 이름 넷 · 칭호 없는 카드
            (string name, string title)[] cases =
            {
                ("숲의 수호자", "황혼에 뒤틀린 고목"),
                ("화룡", "사슬을 녹인 불꽃"),
                ("리치", "잠들지 않는 주문"),
                ("타락한 모르드레드", null),
            };
            for (int i = 0; i < cases.Length; i++)
            {
                BossTitleCard.Show(cases[i].name, cases[i].title, 2.6f);
                await Wait(1.2f);
                card = Card();
                if (card != null) Measure(sb, Check, card, barkOverlap: false);
                var title = Field<TMP_Text>(card, "_title");
                Check($"칭호 표시 = 칭호 있음({cases[i].name})", title != null && title.gameObject.activeSelf == !string.IsNullOrEmpty(cases[i].title));
                await Shot($"BTC_B{i}");
                await Wait(1.8f);
            }

            // ②b 밝은 바탕(숲 아레나 베이지 · 10-14 5회차에서 칭호가 안 읽힘) — 뒤에 밝은 판을 깔고 찍는다
            var bright = new GameObject("@BtcBrightBackdrop", typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler));
            var bc = bright.GetComponent<Canvas>();
            bc.renderMode = RenderMode.ScreenSpaceOverlay;
            bc.sortingOrder = UISortingOrder.Scene;
            var fill = new GameObject("Fill", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            fill.transform.SetParent(bright.transform, false);
            var fr = (RectTransform)fill.transform;
            fr.anchorMin = Vector2.zero; fr.anchorMax = Vector2.one; fr.offsetMin = fr.offsetMax = Vector2.zero;
            fill.GetComponent<UnityEngine.UI.Image>().color = new Color(0.80f, 0.72f, 0.58f, 1f);   // 숲 아레나 바닥 베이지(5회차 사진 평균)
            BossTitleCard.Show("숲의 수호자", "다시 숨 쉬는 뿌리", 2.6f);
            await Wait(1.2f);
            await Shot("BTC_Bright");
            await Wait(1.8f);
            Object.Destroy(bright);
            await Wait(0.3f);

            // ③ 건너뛰기 — 0.6초에 Hide → 0.25초 안에 걷힘
            BossTitleCard.Show("숲의 수호자", "봉인의 숲", 2.6f);
            await Wait(0.6f);
            BossTitleCard.Hide();
            await Wait(0.4f);
            Check("건너뛰기 0.4초 뒤 걷힘", Card() == null);

            // ④ 연달아 부르면 앞 카드가 바로 바뀐다(둘이 겹치지 않음)
            BossTitleCard.Show("화룡", "잿더미의 군주", 2.6f);
            await Wait(0.5f);
            BossTitleCard.Show("리치", "악몽의 대마법사", 2.6f);
            await Wait(0.2f);
            Check("연달아 부르면 카드 하나", Object.FindObjectsByType<BossTitleCard>(FindObjectsSortMode.None).Length == 1);
            BossTitleCard.Hide();
            await Wait(0.5f);
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            if (hub != null) hub.enabled = hubWas;
            sb.AppendLine($"CHECK 합계 {pass}/{total}");
            File.WriteAllText("Temp/boss_title_probe.txt", sb.ToString());
            Debug.Log("[BossTitleProbe] 끝\n" + sb);
        }
    }

    /// <summary>글자 넘침 · 크기 · 화면 안 · (선택) 등장 자막과 세로 겹침.</summary>
    private static void Measure(StringBuilder sb, System.Action<string, bool> check, BossTitleCard card, bool barkOverlap)
    {
        var name  = Field<TMP_Text>(card, "_name");
        var title = Field<TMP_Text>(card, "_title");
        float scale = card.GetComponent<Canvas>().scaleFactor;
        var (nLo, nHi) = TextY(name);
        sb.AppendLine($"  이름 「{name.text}」 글자 {name.fontSize:0} · 폭 {name.textBounds.size.x:0}/{name.rectTransform.rect.width:0} · 화면 y {nLo / scale:0}~{nHi / scale:0}(1080 기준)");
        check($"이름 넘침 없음 · 글자 ≥ 48({name.text})", !name.isTextOverflowing && name.textBounds.size.x <= name.rectTransform.rect.width && name.fontSize >= 48f);
        float top = nHi;
        if (title.gameObject.activeSelf)
        {
            var (tLo, tHi) = TextY(title);
            top = tHi;
            sb.AppendLine($"  칭호 「{title.text}」 글자 {title.fontSize:0} · 화면 y {tLo / scale:0}~{tHi / scale:0}");
            check($"칭호 글자 ≥ 16 · 이름과 안 겹침({title.text})", title.fontSize >= 16f && tLo > nHi);
        }
        var rule = Field<RectTransform>(card, "_rule");
        var c = new Vector3[4]; rule.GetWorldCorners(c);
        check($"금선 · 카드가 화면 안({name.text})", c[0].y > 0f && top < Screen.height * 0.5f);
        var frameBar = GameObject.Find("@CinematicFrame/BottomBar");
        if (frameBar != null && frameBar.activeInHierarchy)
        {
            var b = new Vector3[4]; ((RectTransform)frameBar.transform).GetWorldCorners(b);
            sb.AppendLine($"  레터박스 아래 띠 위 끝 {b[1].y / scale:0} · 금선 {c[0].y / scale:0}");
            if (b[1].y > 1f) check($"금선이 레터박스 아래 띠보다 위({name.text})", c[0].y > b[1].y);
        }
        if (!barkOverlap) return;
        var bark = UI_BossBark.Instance;
        var label = bark != null ? typeof(UI_BossBark).GetField("_label", Any)?.GetValue(bark) as TMP_Text : null;
        if (label == null) { sb.AppendLine("  등장 자막 없음"); return; }
        var (bLo, bHi) = TextY(label);
        sb.AppendLine($"  등장 자막 줄 수 {label.textInfo.lineCount} · 화면 y {bLo / scale:0}~{bHi / scale:0} · 카드 위 끝 {top / scale:0} · 틈 {(bLo - top) / scale:0}");
        check("등장 자막과 세로로 안 겹침(틈 ≥ 16)", (bLo - top) / scale >= 16f);
    }

    /// <summary>보이는 글자 모양의 위아래 끝(화면 픽셀) — textBounds는 줄 높이 칸이라 실제 글자보다 위아래로 넓다(1차 판정 오탐).</summary>
    private static (float lo, float hi) TextY(TMP_Text t)
    {
        t.ForceMeshUpdate();
        var info = t.textInfo;
        float lo = float.MaxValue, hi = float.MinValue;
        for (int i = 0; i < info.characterCount; i++)
        {
            var ch = info.characterInfo[i];
            if (!ch.isVisible) continue;
            lo = Mathf.Min(lo, ch.bottomLeft.y);
            hi = Mathf.Max(hi, ch.topRight.y);
        }
        if (lo > hi) { lo = t.textBounds.min.y; hi = t.textBounds.max.y; }
        return (t.rectTransform.TransformPoint(new Vector3(0f, lo, 0f)).y, t.rectTransform.TransformPoint(new Vector3(0f, hi, 0f)).y);
    }

    private static BossTitleCard Card() => typeof(BossTitleCard).GetField("_instance", Any)?.GetValue(null) as BossTitleCard;
    private static CanvasGroup Group(BossTitleCard c) => Field<CanvasGroup>(c, "_group");
    private static T Field<T>(BossTitleCard c, string f) where T : class => c != null ? typeof(BossTitleCard).GetField(f, Any)?.GetValue(c) as T : null;

    private static UniTask Wait(float seconds) => UniTask.Delay((int)(seconds * 1000), DelayType.Realtime);

    private static UniTask Shot(string name)
    {
        var m = typeof(UILayoutRuntimeProbeEditor).GetMethod("ShotAsync", BindingFlags.Static | BindingFlags.NonPublic);
        return m != null ? (UniTask)m.Invoke(null, new object[] { name, 0 }) : UniTask.CompletedTask;
    }
}
#endif
