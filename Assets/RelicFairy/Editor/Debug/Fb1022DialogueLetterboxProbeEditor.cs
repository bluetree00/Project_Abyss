#if UNITY_EDITOR
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-22 사용자 「리치의 대사도 가려진다」 실측 — 보스 등장 레터박스(620) + 이야기 대사 팝업(UI_DialoguePopup)을 함께 띄워
/// 팝업이 레터박스 위로 올라오는지(정렬) · 대사 줄이 아래 띠와 겹치는지 · 레터박스가 걷히면 원래 정렬로 돌아오는지 잰다.
/// 결과 Temp/fb1022_dlg_letterbox.txt · 사진 Temp/ui_shots/DLG22_*.png · 로그 「[Fb1022Dlg] 끝」.
/// </summary>
public static class Fb1022DialogueLetterboxProbeEditor
{
    private const string Tag = "[Fb1022Dlg]";
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    [MenuItem("RelicFairy/Debug/10-22 보스 이야기 대사 · 레터박스 겹침 촬영 (플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying || Managers.UI == null) { Debug.LogWarning($"{Tag} 플레이 중에"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("보스 이야기 대사 · 레터박스 겹침 실측\n");
        int pass = 0, total = 0;
        void Check(string what, bool ok) { total++; if (ok) pass++; sb.AppendLine($"[{(ok ? "O" : "X")}] {what}"); }
        Directory.CreateDirectory("Temp/ui_shots");
        UI_DialoguePopup popup = null;
        try
        {
            await CinematicFrame.ShowAsync(0.3f, null, CancellationToken.None, dimAlpha: 0f);   // 보스 등장(LichCinematics.LetterboxInAsync와 같은 값)
            popup = await Managers.UI.ShowPopupUIAndGetAsync<UI_DialoguePopup>();
            if (popup == null) { sb.AppendLine("팝업 로드 실패"); return; }
            var lines = new[]
            {
                new DialogueLine { speaker = DialogueSpeaker.Lich, text = "또 왔구나, 봉인의 아이야. 이번에는 네가 묶일 차례다." },
            };
            popup.ShowAsync(lines).Forget();
            await UniTask.Delay(1800, DelayType.Realtime);

            var canvas = popup.GetComponent<Canvas>();
            int frame  = CinematicFrame.CurrentSortingOrder;
            Check($"레터박스 떠 있음 · 팝업 정렬 {canvas?.sortingOrder} > 레터박스 {frame}", CinematicFrame.IsShown && canvas != null && canvas.sortingOrder > frame);

            var body = typeof(UI_DialoguePopup).GetField("bodyText", Inst)?.GetValue(popup) as TMP_Text;
            if (body != null)
            {
                body.ForceMeshUpdate();
                var b = body.textBounds;
                var c = body.canvas;
                Camera cam = c != null && c.renderMode != RenderMode.ScreenSpaceOverlay ? c.worldCamera : null;
                float bottom = RectTransformUtility.WorldToScreenPoint(cam, body.transform.TransformPoint(new Vector3(b.center.x, b.min.y, 0f))).y;
                float bar = CinematicFrame.BarHeightFraction * Screen.height;
                sb.AppendLine($"  대사 줄 아래끝 y {bottom * 1080f / Screen.height:0} · 아래 띠 위끝 y {bar * 1080f / Screen.height:0}(1080 기준) — 겹쳐도 팝업이 위라 보인다");
            }
            await Shot("DLG22_1_lich_over_letterbox");

            await CinematicFrame.HideAsync(0.2f, null, CancellationToken.None);
            await UniTask.Delay(400, DelayType.Realtime);
            Check($"레터박스 걷힌 뒤 팝업 정렬 {canvas?.sortingOrder} < 600(원래 자리로)", canvas != null && canvas.sortingOrder < 600);
            await Shot("DLG22_2_after_letterbox");
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            if (popup != null) popup.ClosePopupUI();
            if (CinematicFrame.IsShown) CinematicFrame.HideAsync(0f, null).Forget();
            sb.Insert(0, $"통과 {pass}/{total}\n");
            File.WriteAllText("Temp/fb1022_dlg_letterbox.txt", sb.ToString());
            Debug.Log($"{Tag} 끝 — 통과 {pass}/{total}\n{sb}");
        }
    }

    /// <summary>
    /// 실제 길 — 다음에 뜨는 이야기 대사 팝업(예: Ch4 리치 등장)을 기다렸다가 1.5초 뒤 정렬 · 레터박스를 재고 찍는다.
    /// 보스방에 들어가기 직전에 누른다. 90초 안에 안 뜨면 「안 뜸」. 로그 「[Fb1022DlgReal] 끝」.
    /// </summary>
    [MenuItem("RelicFairy/Debug/10-22 다음 이야기 대사 팝업 뜨면 촬영 (플레이 중)")]
    private static void RunReal()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[Fb1022DlgReal] 플레이 중에"); return; }
        RealAsync().Forget();
    }

    private static async UniTaskVoid RealAsync()
    {
        var sb = new StringBuilder("실제 이야기 대사 팝업 · 레터박스\n");
        Directory.CreateDirectory("Temp/ui_shots");
        try
        {
            UI_DialoguePopup popup = null;
            float t0 = Time.realtimeSinceStartup;
            while (popup == null && Time.realtimeSinceStartup - t0 < 90f)
            {
                popup = Object.FindFirstObjectByType<UI_DialoguePopup>();
                await UniTask.Delay(100, DelayType.Realtime);
            }
            if (popup == null) { sb.AppendLine("90초 안에 이야기 대사 팝업이 안 떴다"); return; }
            await UniTask.Delay(1500, DelayType.Realtime);
            var canvas = popup != null ? popup.GetComponent<Canvas>() : null;
            bool frame = CinematicFrame.IsShown;
            int order  = CinematicFrame.CurrentSortingOrder;
            sb.AppendLine($"레터박스 {(frame ? $"떠 있음({order})" : "없음")} · 칭호 카드 {BossTitleCard.IsShown} · 팝업 정렬 {canvas?.sortingOrder}");
            sb.AppendLine($"CHECK 팝업이 연출 UI 위: {(canvas != null && (!frame || canvas.sortingOrder > order))}");
            await Shot("DLG22_3_real_entrance");
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            File.WriteAllText("Temp/fb1022_dlg_real.txt", sb.ToString());
            Debug.Log("[Fb1022DlgReal] 끝\n" + sb);
        }
    }

    private static async UniTask Shot(string name)
    {
        var runner = Object.FindFirstObjectByType<Managers>();
        if (runner == null) return;
        await UniTask.WaitForEndOfFrame(runner);
        var tex = ScreenCapture.CaptureScreenshotAsTexture();
        if (tex == null) return;
        File.WriteAllBytes(Path.Combine("Temp/ui_shots", name + ".png"), tex.EncodeToPNG());
        Object.Destroy(tex);
    }
}
#endif
