#if UNITY_EDITOR
using System.IO;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-06 「되살아난 기억」 개편 실측(사용자 「유물 내용을 한눈에 · 선택 후 메인 이펙트나 피드백」).
/// 런 안에서 보스 처치를 흉내 내(<c>Simulate Boss Clear with Relic</c>) 기억의 빛 · 회상 뒤 창이 뜨면
/// 창 · 고른 상태 · 고른 뒤 연출(번쩍 → 가라앉음 → 날아감) · 창이 닫힌 뒤 몸 이펙트를 차례로 찍는다. 실행 동안 플레이어 무적.
/// 결과: Temp/relic_revamp_probe.txt · Temp/ui_shots/Relic_*.png · 로그 「[RelicRevamp] 끝」.
/// </summary>
public static class RelicDraftRevampProbeEditor
{
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string SkipDialogueMenu = "RelicFairy/Debug/서약 발동 실측/대사 전부 넘기기 (플레이 중)";

    [MenuItem("RelicFairy/Debug/10-06 되살아난 기억 개편 실측 (런 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) return;
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("되살아난 기억 개편 실측\n");
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
            sb.AppendLine($"창 뜸 — 보스 처치 흉내부터 {Time.unscaledTime - t0:0.0}초 · 유물 {AppBootstrapper.Instance?.Loadout?.Relic?.DisplayName}");

            await UniTask.Delay(900, ignoreTimeScale: true);
            ScreenCapture.CaptureScreenshot("Temp/ui_shots/Relic_popup.png");
            await UniTask.Delay(250, ignoreTimeScale: true);

            var draft = typeof(UI_RelicPartDraftPopup).GetField("_draft", Inst)?.GetValue(popup) as RelicDraft;
            int n = draft != null ? draft.Cards.Count : 0;
            for (int i = 0; i < n; i++)
            {
                var c = draft.Cards[i];
                sb.AppendLine($"  카드 {i}: {c.Kind} · {c.Grade} · {c.Entry?.part_name} · 자리 {c.Entry?.anchor ?? c.Anchor}");
            }
            int pick = n >= 2 ? 1 : 0;
            typeof(UI_RelicPartDraftPopup).GetMethod("SetSelected", Inst)?.Invoke(popup, new object[] { pick });
            await UniTask.Delay(300, ignoreTimeScale: true);
            ScreenCapture.CaptureScreenshot("Temp/ui_shots/Relic_selected.png");
            await UniTask.Delay(200, ignoreTimeScale: true);

            typeof(UI_RelicPartDraftPopup).GetMethod("OnConfirmClicked", Inst)?.Invoke(popup, null);
            float tc = Time.unscaledTime;
            int k = 0;
            foreach (int ms in new[] { 120, 200, 250, 230 })   // 0.12 · 0.32 · 0.57 · 0.80초 — 번쩍 · 가라앉음 · 날아감
            {
                await UniTask.Delay(ms, ignoreTimeScale: true);
                ScreenCapture.CaptureScreenshot($"Temp/ui_shots/Relic_seq_{k++}.png");
            }
            k = 0;
            foreach (float at in new[] { 1.0f, 1.35f, 1.8f })   // 몸 이펙트 — 확정 시각 기준
            {
                float wait = tc + at - Time.unscaledTime;
                if (wait > 0f) await UniTask.Delay(System.TimeSpan.FromSeconds(wait), ignoreTimeScale: true);
                ScreenCapture.CaptureScreenshot($"Temp/ui_shots/Relic_world_{k++}.png");
                sb.AppendLine($"  몸 사진 {at:0.00}초 · 창 {(popup != null ? "남음" : "닫힘")}");
            }
            var parts = AppBootstrapper.Instance?.Loadout?.RelicPartIds;
            sb.AppendLine($"로드아웃 조각 {parts?.Count ?? 0}개");
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            File.WriteAllText("Temp/relic_revamp_probe.txt", sb.ToString());
            Debug.Log("[RelicRevamp] 끝\n" + sb);
        }
    }
}
#endif
