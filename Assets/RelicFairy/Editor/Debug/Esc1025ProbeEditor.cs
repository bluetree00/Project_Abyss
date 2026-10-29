#if UNITY_EDITOR
using System.IO;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// 10-25 사용자 「모든 UI를 ESC로 닫을 수 있게 · 없으면 설정 창」 실측 — ESC 듣개(EscKeyListener.Handle)를 키 없이 불러
/// 룬판 · 룬판 초기화 확인 창 · 빈 화면(일시정지 메뉴) · 메뉴 안 「되찾은 기억」 · 「장비」 · 대사 창 · 맨 위 오버레이(그 밑에 메뉴 안 열림)를 본다.
/// 결과: Temp/esc1025.txt · Temp/ui_shots/ESC25_*.png · 로그 「[Esc25] 끝」.
/// </summary>
public static class Esc1025ProbeEditor
{
    private const string Tag = "[Esc25]";
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    [MenuItem("RelicFairy/Debug/10-25 ESC로 닫기 실측 — 룬판 · 메뉴 안 화면 · 대사 · 맨 위 오버레이 (런 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying || GameRunBootstrapper.Instance?.Run?.Player == null) { Debug.LogWarning($"{Tag} 런 안에서"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("10-25 ESC로 닫기 실측\n");
        int pass = 0, total = 0;
        void Check(string what, bool ok) { total++; if (ok) pass++; sb.AppendLine($"CHECK {what}: {ok}"); }
        GameObject top = null;
        try
        {
            var esc  = Object.FindFirstObjectByType<EscKeyListener>();
            var menu = esc != null ? typeof(EscKeyListener).GetField("_menu", Inst)?.GetValue(esc) as UI_EscMenu : null;
            var handle = typeof(EscKeyListener).GetMethod("Handle", Inst);
            if (esc == null || menu == null || handle == null) { sb.AppendLine("  ESC 듣개 · 메뉴 없음"); return; }
            async UniTask Esc(float wait = 0.5f) { handle.Invoke(esc, null); await UniTask.Delay((int)(wait * 1000), ignoreTimeScale: true); }

            // ① 룬판 — ESC가 판을 닫고 메뉴는 안 연다
            if (UI_GridPanel.Instance == null) Managers.UI?.ShowOverlayUI<UI_GridPanel>();
            var panel = UI_GridPanel.Instance;
            panel.ShowWithNewItem(null);
            await UniTask.Delay(1000, ignoreTimeScale: true);
            Check("① 룬판 열림", panel.IsOpen);
            await Esc(0.8f);
            Check("① ESC → 룬판 닫힘 · 일시정지 메뉴 안 열림", !panel.IsOpen && !menu.IsOpen);

            // ② 룬판 초기화 확인 창 — 첫 ESC는 확인 창만, 두 번째가 판
            panel.ShowWithNewItem(null);
            await UniTask.Delay(800, ignoreTimeScale: true);
            typeof(UI_GridPanel).GetMethod("ShowResetConfirmDialog", Inst)?.Invoke(panel, null);
            var reset = typeof(UI_GridPanel).GetField("_resetDialog", Inst)?.GetValue(panel) as GameObject;
            Check("② 초기화 확인 창 열림", reset != null && reset.activeSelf);
            await Esc();
            Check("② ESC → 확인 창만 닫히고 판은 열린 채", reset != null && !reset.activeSelf && panel.IsOpen);
            await Esc(0.8f);
            Check("② 다시 ESC → 판 닫힘", !panel.IsOpen && !menu.IsOpen);

            // ③ 빈 화면 → 일시정지 메뉴 · 다시 ESC → 닫힘
            await Esc();
            Check("③ 빈 화면 ESC → 일시정지 메뉴 열림", menu.IsOpen);
            await Shot("ESC25_menu");
            await Esc();
            Check("③ 다시 ESC → 메뉴 닫힘", !menu.IsOpen);

            // ④ 메뉴 안 「되찾은 기억」 · 「장비」 — ESC는 그 쪽만 닫고 메뉴로 돌아간다
            foreach (var (label, opener, field) in new[] { ("되찾은 기억", "OnMemory", "_memoryPage"), ("장비", "OnEquipment", "_equipPage") })
            {
                menu.Open();
                await UniTask.Delay(400, ignoreTimeScale: true);
                typeof(UI_EscMenu).GetMethod(opener, Inst)?.Invoke(menu, null);
                await UniTask.Delay(600, ignoreTimeScale: true);
                var page = typeof(UI_EscMenu).GetField(field, Inst)?.GetValue(menu) as Component;
                Check($"④ 「{label}」 열림", page != null && page.gameObject.activeSelf);
                await Esc();
                Check($"④ ESC → 「{label}」만 닫히고 메뉴는 열린 채", page != null && !page.gameObject.activeSelf && menu.IsOpen);
                await Esc();
                Check($"④ 다시 ESC → 메뉴 닫힘({label})", !menu.IsOpen);
            }

            // ⑤ 대사 창 — ESC가 대화를 닫고 기다리던 쪽(ShowAsync)이 예외 없이 끝난다
            var popup = await Managers.UI.ShowPopupUIAndGetAsync<UI_DialoguePopup>();
            if (popup != null)
            {
                var lines = new[]
                {
                    new DialogueLine { speaker = DialogueSpeaker.Merlin, text = "ESC 실측 첫 줄." },
                    new DialogueLine { speaker = DialogueSpeaker.Merlin, text = "ESC 실측 둘째 줄." },
                };
                bool finished = false;
                popup.ShowAsync(lines).ContinueWith(() => finished = true).Forget();
                await UniTask.Delay(1200, ignoreTimeScale: true);
                await Esc(1.2f);
                Check("⑤ ESC → 대사 창 닫힘 · 기다리던 흐름이 끝남 · 메뉴 안 열림", popup == null && finished && !menu.IsOpen);
                Check("⑤ 시간 다시 흐름", Time.timeScale > 0f);
            }
            else sb.AppendLine("  대사 창 못 띄움");

            // ⑥ 맨 위 오버레이(심연 갈림길 · 런 끝 문구 자리 970) — 그 밑에 일시정지 메뉴를 열지 않는다
            top = new GameObject("@Esc25TopOverlay", typeof(Canvas));
            var c = top.GetComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            c.sortingOrder = UISortingOrder.RunChoice;
            await UniTask.Yield();
            await Esc();
            Check("⑥ 맨 위 오버레이 동안 ESC → 메뉴 안 열림", !menu.IsOpen);
            Object.Destroy(top); top = null;
            await UniTask.Yield();

            Check("끝 — 시간 흐름(정지 붙잡이 안 남음)", Time.timeScale > 0f);
            sb.AppendLine($"  시간 {Time.timeScale}");
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            if (top != null) Object.Destroy(top);
            sb.AppendLine($"CHECK 합계 {pass}/{total}");
            File.WriteAllText("Temp/esc1025.txt", sb.ToString());
            Debug.Log($"{Tag} 끝\n" + sb);
        }
    }

    private static UniTask Shot(string name)
    {
        var m = typeof(UILayoutRuntimeProbeEditor).GetMethod("ShotAsync", BindingFlags.Static | BindingFlags.NonPublic);
        return m != null ? (UniTask)m.Invoke(null, new object[] { name, 0 }) : UniTask.CompletedTask;
    }
}
#endif
