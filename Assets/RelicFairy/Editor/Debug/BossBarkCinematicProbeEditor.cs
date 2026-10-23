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
/// 10-21 보스 자막 · 연출 UI 겹침 실측 — 레터박스(보스 등장 620 · 인트로 150 · 전환 HUD 아래) · 칭호 카드와 자막 종류(등장 · 대사 · 멀린 · 컷신 배치)를
/// 함께 띄워 자막이 연출 UI 위에 그려지는지(정렬) · 어디가 겹치는지(1080 기준 높이)를 잰다(사용자 「보스의 대사가 시네마틱 UI 연출 아래로 가려진다」).
/// 결과: Temp/boss_bark_cine_probe.txt · Temp/ui_shots/BBC_*.png · 로그 「[BossBarkCine] 끝」.
/// </summary>
public static class BossBarkCinematicProbeEditor
{
    private const BindingFlags Any = BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private const string IntroLine  = "왕을 배신한 검이 다시 일어선다. 이번에는 봉인도 맹세도 그를 묶어 두지 못하리라.";
    private const string BossLine   = "봉인의 사슬이 아직도 내 뼈를 붙잡고 있구나. 그러나 오늘 그 사슬은 끊어진다.";
    private const string MerlinLine = "조심해, 저 녀석의 숨이 바뀌었어. 바닥의 붉은 원이 보이면 바로 빠져나와.";

    [MenuItem("RelicFairy/Debug/10-21 보스 자막 · 레터박스 겹침 촬영 (플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[BossBarkCine] 플레이 모드에서"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("보스 자막 · 연출 UI 겹침 실측\n");
        int pass = 0, total = 0;
        void Check(string what, bool ok) { total++; if (ok) pass++; sb.AppendLine($"CHECK {what}: {ok}"); }
        var hub = Object.FindFirstObjectByType<TestHubLauncher>();
        bool hubWas = hub != null && hub.enabled;
        if (hub != null) hub.enabled = false;   // 허브 OnGUI 판이 사진을 가린다
        try
        {
            if (UI_BossBark.Instance == null) { sb.AppendLine("UI_BossBark 없음"); return; }

            // A 보스 등장 — 레터박스(620 · LichCinematics.LetterboxInAsync와 같은 값) + 등장 자막 + 칭호 카드
            await CinematicFrame.ShowAsync(0.3f, null, CancellationToken.None, dimAlpha: 0f);
            UI_BossBark.ShowLatest(IntroLine, BossBarkType.BossIntro, DialogueSpeaker.ForestGuardian);
            await Wait(0.3f);
            BossTitleCard.Show("숲의 수호자", "황혼에 뒤틀린 고목", 2.6f);
            await Wait(0.6f);
            Measure(sb, Check, "A 등장 자막 + 칭호 카드(레터박스 620)", mustBelowPopup: false);
            await Shot("BBC_A_entrance_card");
            await Wait(2.4f);   // 카드 걷힘
            Measure(sb, Check, "A' 등장 자막 · 카드 걷힌 뒤", mustBelowPopup: false);

            // B 레터박스 620 + 보스 대사(초상 원)
            UI_BossBark.ShowLatest(BossLine, BossBarkType.Bark, DialogueSpeaker.Lich);
            await Wait(0.8f);
            Measure(sb, Check, "B 보스 대사(리치 · 레터박스 620)", mustBelowPopup: false);
            await Shot("BBC_B_bark");

            // C 레터박스 620 + 멀린 내레이션(가장 아래 −280)
            UI_BossBark.ShowLatest(MerlinLine, BossBarkType.MerlinNarration, DialogueSpeaker.Merlin);
            await Wait(0.8f);
            Measure(sb, Check, "C 멀린 내레이션(레터박스 620)", mustBelowPopup: false);
            await Shot("BBC_C_merlin");
            UI_BossBark.Dismiss(0f);
            await CinematicFrame.HideAsync(0.3f, null, CancellationToken.None);
            await Wait(0.7f);

            // D 인트로 — 레터박스 150(Game_Intro 직렬화 값) + 컷신 배치(왼쪽 아래 서 있는 그림)
            await CinematicFrame.ShowAsync(0.3f, null, CancellationToken.None, 2.39f, 0f, null, 150);
            UI_BossBark.CinematicLayout = true;
            UI_BossBark.ShowLatest(BossLine, BossBarkType.Bark, DialogueSpeaker.Lich);
            await Wait(0.9f);
            Measure(sb, Check, "D 인트로 컷신 배치(레터박스 150)", mustBelowPopup: true);
            await Shot("BBC_D_intro_standing");
            UI_BossBark.CinematicLayout = false;
            UI_BossBark.Dismiss(0f);
            await CinematicFrame.HideAsync(0.3f, null, CancellationToken.None);
            await Wait(0.7f);

            // E 페이즈 전환 · 끝 장면 — 레터박스가 HUD 아래(LetterboxUnderHudAsync) → 자막은 HUD 자리 그대로
            await CinematicFrame.ShowAsync(0.3f, null, CancellationToken.None, dimAlpha: 0f, sortingOrder: UISortingOrder.Hud - 1);
            UI_BossBark.ShowLatest(BossLine, BossBarkType.Bark, DialogueSpeaker.Lich);
            await Wait(0.8f);
            Measure(sb, Check, "E 레터박스 HUD 아래(99)", mustBelowPopup: true);
            Check("E 자막은 HUD 자리(올리지 않음)", Effective(out _) == UISortingOrder.Hud);
            await Shot("BBC_E_under_hud");
            UI_BossBark.Dismiss(0f);
            await CinematicFrame.HideAsync(0.3f, null, CancellationToken.None);
            await Wait(0.7f);

            // F 연출 없음 — 자막이 HUD 자리로 돌아왔나
            UI_BossBark.ShowLatest(BossLine, BossBarkType.Bark, DialogueSpeaker.Lich);
            await Wait(0.5f);
            Check("F 연출 없으면 HUD 자리(100)", Effective(out _) == UISortingOrder.Hud);
            UI_BossBark.Dismiss(0f);
            await Wait(0.6f);
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            UI_BossBark.CinematicLayout = false;
            if (hub != null) hub.enabled = hubWas;
            sb.AppendLine($"CHECK 합계 {pass}/{total}");
            File.WriteAllText("Temp/boss_bark_cine_probe.txt", sb.ToString());
            Debug.Log("[BossBarkCine] 끝\n" + sb);
        }
    }

    /// <summary>자막의 실제 정렬 · 떠 있는 연출 UI 정렬 · 자막 조각(글 · 화자 · 초상 · 서 있는 그림)이 아래 띠 · 카드 글자와 겹친 높이.</summary>
    private static void Measure(StringBuilder sb, System.Action<string, bool> check, string name, bool mustBelowPopup)
    {
        var bark = UI_BossBark.Instance;
        int eff  = Effective(out float scale);
        int top  = int.MinValue;
        var frame = GameObject.Find("@CinematicFrame");
        var frameCanvas = frame != null ? frame.GetComponent<Canvas>() : null;
        bool frameUp = CinematicFrame.IsShown;
        if (frameUp) top = frameCanvas.sortingOrder;
        var card = typeof(BossTitleCard).GetField("_instance", Any)?.GetValue(null) as BossTitleCard;
        if (card != null) top = Mathf.Max(top, card.GetComponent<Canvas>().sortingOrder);
        sb.AppendLine($"[{name}] 자막 정렬 {eff} · 레터박스 {(frameUp ? frameCanvas.sortingOrder.ToString() : "없음")} · 카드 {(card != null ? card.GetComponent<Canvas>().sortingOrder.ToString() : "없음")}");

        float barTop = 0f;
        if (frameUp)
        {
            var c = new Vector3[4]; ((RectTransform)frame.transform.Find("BottomBar")).GetWorldCorners(c);
            barTop = c[1].y;
        }
        (float lo, float hi) cardText = (0f, 0f);
        if (card != null)
        {
            var n = typeof(BossTitleCard).GetField("_name", Any)?.GetValue(card) as TMP_Text;
            var t = typeof(BossTitleCard).GetField("_title", Any)?.GetValue(card) as TMP_Text;
            var (nLo, nHi) = TextY(n);
            cardText = (nLo, nHi);
            if (t != null && t.gameObject.activeInHierarchy) { var (tLo, tHi) = TextY(t); cardText = (Mathf.Min(nLo, tLo), Mathf.Max(nHi, tHi)); }
        }

        void Piece(string what, float lo, float hi)
        {
            float bar  = Mathf.Max(0f, Mathf.Min(hi, barTop) - lo) / scale;
            float onCd = card != null ? Mathf.Max(0f, Mathf.Min(hi, cardText.hi) - Mathf.Max(lo, cardText.lo)) / scale : 0f;
            sb.AppendLine($"    {what}: y {lo / scale:0}~{hi / scale:0} · 아래 띠와 겹침 {bar:0} · 카드 글자와 겹침 {onCd:0}");
        }
        var label = Field<TMP_Text>(bark, "_label");
        if (label != null) { var (lo, hi) = TextY(label); Piece($"글({label.textInfo.lineCount}줄)", lo, hi); }
        var speaker = Field<TMP_Text>(bark, "_speakerLabel");
        if (speaker != null && speaker.gameObject.activeInHierarchy) { var (lo, hi) = TextY(speaker); Piece($"화자 「{speaker.text}」", lo, hi); }
        foreach (var f in new[] { "_portraitGroup", "_standing" })
        {
            var o = typeof(UI_BossBark).GetField(f, Any)?.GetValue(bark) as Component;
            if (o == null || !o.gameObject.activeInHierarchy) continue;
            var c = new Vector3[4]; ((RectTransform)o.transform).GetWorldCorners(c);
            Piece(f == "_standing" ? "서 있는 그림" : "초상 원", c[0].y, c[1].y);
        }

        if (top != int.MinValue) check($"{name} — 자막이 떠 있는 연출 UI({top})보다 위", eff > top);
        if (mustBelowPopup) check($"{name} — 자막이 팝업({UISortingOrder.Popup})보다 아래", eff < UISortingOrder.Popup);
        check($"{name} — 화면 효과({UISortingOrder.Impact})보다 아래", eff < UISortingOrder.Impact);
    }

    /// <summary>자막이 실제로 그려지는 정렬 — 덮어쓴 중첩 캔버스면 그 값, 아니면 HUD 캔버스 값.</summary>
    private static int Effective(out float scale)
    {
        var bark = UI_BossBark.Instance;
        var own  = bark.GetComponent<Canvas>();
        var hud  = bark.transform.parent.GetComponentInParent<Canvas>().rootCanvas;
        scale = hud.scaleFactor;
        return own != null && own.overrideSorting ? own.sortingOrder : hud.sortingOrder;
    }

    /// <summary>보이는 글자 모양의 위아래 끝(화면 픽셀).</summary>
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

    private static T Field<T>(UI_BossBark b, string f) where T : class => b != null ? typeof(UI_BossBark).GetField(f, Any)?.GetValue(b) as T : null;

    private static UniTask Wait(float seconds) => UniTask.Delay((int)(seconds * 1000), DelayType.Realtime);

    private static UniTask Shot(string name)
    {
        var m = typeof(UILayoutRuntimeProbeEditor).GetMethod("ShotAsync", BindingFlags.Static | BindingFlags.NonPublic);
        return m != null ? (UniTask)m.Invoke(null, new object[] { name, 0 }) : UniTask.CompletedTask;
    }
}
#endif
