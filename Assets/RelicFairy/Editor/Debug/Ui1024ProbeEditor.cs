#if UNITY_EDITOR
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// 10-24 밤 요청 묶음 화면 확인(테스트 허브, 플레이 중) — 세이브는 건드리지 않는다.
/// ① 조작 안내 카드(인트로 시작 · 보스전 단계 카드와 같은 내용) ② 대사창 넘김 표시 「F · 클릭 ▼」
/// ③ 설정 창 「카메라 — 거리 · 높이」 · 취소/완료 자리 ④ 카메라 거리 · 높이 배율이 실제 시점에 먹나(플레이어 ↔ 카메라 거리 · 높이).
/// 결과 Temp/ui1024.txt · Temp/ui_shots/UI24_*.png · 로그 「[Ui24] 끝」.
/// </summary>
public static class Ui1024ProbeEditor
{
    private const string Tag = "[Ui24]";
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

    [MenuItem("RelicFairy/Debug/10-24 안내 카드 · 대사 넘김 · 설정 카메라 확인 (테스트 허브, 플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning($"{Tag} 플레이 중에만"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("10-24 화면 확인\n");
        int pass = 0, total = 0;
        void Check(string what, bool ok) { total++; if (ok) pass++; sb.AppendLine($"CHECK {ok} — {what}"); }
        var hub = Object.FindFirstObjectByType<TestHubLauncher>();
        bool hubWas = hub != null && hub.enabled;
        if (hub != null) hub.enabled = false;
        float d0 = CameraUserSettings.Distance, h0 = CameraUserSettings.Height;
        try
        {
            // ① 조작 카드
            KeyGuideCard.Show("조작", new[] { ("W A S D", "이동"), ("좌클릭", "공격 · 꾹 누르면 연속"), ("Shift · 우클릭", "회피"), ("1 · 2", "무기 전환") }, 6f);
            await Wait(0.8f);
            var card = Object.FindFirstObjectByType<KeyGuideCard>();
            Check("① 조작 카드가 뜬다", card != null);
            var act1 = card != null ? card.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault(t => t.name == "Act1") : null;
            Check($"① 「{act1?.text}」 글이 칸 안({act1?.preferredWidth:0}/{act1?.rectTransform.rect.width:0}px · 10-25 꾹 누르기)",
                  act1 != null && act1.preferredWidth <= act1.rectTransform.rect.width + 1f);
            await Shot("UI24_1_controls");
            KeyGuideCard.HideNow();

            // ② 대사창 넘김 표시
            var popup = await Managers.UI.ShowPopupUIAndGetAsync<UI_DialoguePopup>();
            if (popup != null)
            {
                popup.ShowAsync(new[] { new DialogueLine { speaker = DialogueSpeaker.Merlin, text = "눈 떴어. 준비되면 넘겨." } }).Forget();
                await Wait(2.4f);
                var hint = typeof(UI_DialoguePopup).GetField("_advanceHint", Any)?.GetValue(popup) as TMP_Text;
                var key = hint != null ? hint.transform.Find("AdvanceKey")?.GetComponent<TMP_Text>() : null;
                Check($"② 넘김 표시 「{hint?.text}」 + 「{key?.text}」 · 보임 {(hint != null && hint.gameObject.activeInHierarchy)}", hint != null && key != null && key.text.Contains("F키"));
                await Shot("UI24_3_dialogue_hint");
                Managers.UI.ClosePopupUI(popup);
                await Wait(0.5f);
            }

            // ③ 설정 창 — 카메라 줄 · 버튼 자리
            UI_Settings.Open();
            await Wait(0.9f);
            var set = Object.FindFirstObjectByType<UI_Settings>();
            var dist = set != null ? typeof(UI_Settings).GetField("_camDistanceSlider", Any)?.GetValue(set) as UnityEngine.UI.Slider : null;
            Check($"③ 설정 창에 카메라 거리 슬라이더 · 값 {dist?.value:0.00}", dist != null);
            await Shot("UI24_4_settings");
            UI_Settings.CloseIfOpen();
            await Wait(0.4f);

            // ④ 시점 배율이 먹나 — 거리 1.3 · 높이 1.4로 바꿨다 되돌린다(저장 안 함)
            var cam = Camera.main;
            var player = Object.FindFirstObjectByType<PlayerController>();
            if (cam != null && player != null)
            {
                await Wait(0.3f);
                Vector3 a = cam.transform.position - player.transform.position;
                CameraUserSettings.SetDistance(1.3f); CameraUserSettings.SetHeight(1.4f);
                await Wait(1.2f);
                Vector3 b = cam.transform.position - player.transform.position;
                sb.AppendLine($"    카메라 오프셋 기본 {a} ({a.magnitude:0.00} m · 높이 {a.y:0.00}) → 거리 1.3 · 높이 1.4 {b} ({b.magnitude:0.00} m · 높이 {b.y:0.00})");
                Check("④ 거리 · 높이 배율이 시점에 먹는다(더 멀고 더 높게)", b.magnitude > a.magnitude * 1.1f && b.y > a.y * 1.1f);
                await Shot("UI24_5_camera_far");
            }
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            CameraUserSettings.SetDistance(d0); CameraUserSettings.SetHeight(h0);
            if (hub != null) hub.enabled = hubWas;
            sb.Insert(0, $"통과 {pass}/{total}\n");
            File.WriteAllText("Temp/ui1024.txt", sb.ToString());
            Debug.Log($"{Tag} 끝\n" + sb);
        }
    }

    private static UniTask Wait(float s) => UniTask.Delay((int)(s * 1000), DelayType.Realtime);

    private static UniTask Shot(string name)
    {
        var m = typeof(UILayoutRuntimeProbeEditor).GetMethod("ShotAsync", BindingFlags.Static | BindingFlags.NonPublic);
        return m != null ? (UniTask)m.Invoke(null, new object[] { name, 0 }) : UniTask.CompletedTask;
    }
}
#endif
