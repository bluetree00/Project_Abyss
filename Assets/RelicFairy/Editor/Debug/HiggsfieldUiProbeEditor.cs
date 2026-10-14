#if UNITY_EDITOR
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using RelicFairy.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 10-13 힉스필드 그림 검증 — 사용자 「힉스필드로 필수 UI들을 제작해서 넣고 검증하자」.
/// 테스트 허브 런(유물 지정) 중에: 리치 대사창 초상 4 · 보스 말풍선 초상 4 · 이야기 그림 페이지 2(사명 · 모르가나) · 엔딩 카드 배경 · 되찾은 기억 문장.
/// 띄우고 찍은 뒤 들어간 그림 이름을 적고 CHECK. 결과: Temp/hf_ui_probe.txt · Temp/ui_shots/HF_*.png · 로그 「[HfUiProbe] 끝」.
/// </summary>
public static class HiggsfieldUiProbeEditor
{
    private const BindingFlags Any = BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    [MenuItem("RelicFairy/Debug/10-13 힉스필드 그림 검증 — 리치 · 보스 초상 · 그림 페이지 · 엔딩 · 문장 (런 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[HfUiProbe] 플레이 모드(테스트 허브 런)에서"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("힉스필드 그림 검증\n");
        int pass = 0, total = 0;
        void Check(string what, bool ok) { total++; if (ok) pass++; sb.AppendLine($"CHECK {what}: {ok}"); }
        try
        {
            var dlg = Managers.DialogueData;
            if (dlg != null && !dlg.IsInitialized) await dlg.InitializeAsync();

            // ① 리치 대사창 초상 4
            string[] lichKeys = { "Lich_Portrait", "Lich_Portrait_Calm", "Lich_Portrait_Rage", "Lich_Portrait_Dying" };
            var lines = new DialogueLine[lichKeys.Length];
            for (int i = 0; i < lichKeys.Length; i++)
                lines[i] = new DialogueLine { speaker = DialogueSpeaker.Lich, illustrationKey = lichKeys[i], text = $"[검증] 리치 초상 {i + 1} — {lichKeys[i]}" };
            var popup = await Managers.UI.ShowPopupUIAndGetAsync<UI_DialoguePopup>();
            popup.ShowAsync(lines).Forget();
            for (int i = 0; i < lichKeys.Length; i++)
            {
                await Wait(1.3f);
                var img    = typeof(UI_DialoguePopup).GetField("portrait", Any)?.GetValue(popup) as Image;
                string got = img != null && img.sprite != null ? img.sprite.name : "(없음)";
                Check($"리치 초상 {lichKeys[i]} → {got}", img != null && img.sprite != null && img.color.a > 0.5f);
                await Shot($"HF_Lich_{i}");
                TestHubDebugMenu.AdvanceDialogue();
            }
            await Wait(1.0f);

            // ② 보스 말풍선 초상
            (DialogueSpeaker who, string text)[] barks =
            {
                (DialogueSpeaker.ForestGuardian, "돌아왔는가. 숲은 네 발자국을 기억한다."),
                (DialogueSpeaker.Dragon,         "끈질긴 불씨로군. …태워주마, 몇 번이고."),
                (DialogueSpeaker.DeathKnight,    "다시 무릎 꿇으러 왔는가, 그대여."),
                (DialogueSpeaker.Lich,           "알고 있었다. 네가 올 것을."),
            };
            foreach (var (who, text) in barks)
            {
                UI_BossBark.Show(text, BossBarkType.Bark, who);
                await Wait(1.4f);
                var bark  = UI_BossBark.Instance;
                var group = typeof(UI_BossBark).GetField("_portraitGroup", Any)?.GetValue(bark) as RectTransform;
                var face  = typeof(UI_BossBark).GetField("_portraitImage", Any)?.GetValue(bark) as Image;
                bool shown = group != null && group.gameObject.activeInHierarchy && face != null && face.sprite != null;
                Check($"보스 말풍선 초상 {who} → {(face != null && face.sprite != null ? face.sprite.name : "(없음)")}", shown);
                await Shot($"HF_Bark_{who}");
                UI_BossBark.Dismiss(0f);
                await Wait(0.6f);
            }

            // ③ 이야기 그림 페이지 — 사명 · 모르가나(대사창은 위에 그대로)
            foreach (var (key, seq) in new[] { ("Illust_Story_MissionSeal", "Mission_Seal"), ("Illust_Story_MorganaReveal", "Ending_Morgana") })
            {
                bool ok = await StoryPageOverlay.ShowAsync(key, CancellationToken.None);
                var page = GameObject.Find("@StoryPage");
                var pageImg = page != null ? page.transform.Find("Page")?.GetComponent<Image>() : null;
                Check($"그림 페이지 {key} → {(pageImg != null && pageImg.sprite != null ? pageImg.sprite.name : "(없음)")}", ok && pageImg != null && pageImg.sprite != null);
                var seqLines = dlg?.GetLines(seq);
                if (seqLines != null && seqLines.Length > 0)
                {
                    var p = await Managers.UI.ShowPopupUIAndGetAsync<UI_DialoguePopup>();
                    p.ShowAsync(seqLines).Forget();
                    await Wait(1.6f);
                    await Shot($"HF_Page_{seq}");
                    for (int i = 0; i < seqLines.Length + 1; i++) { TestHubDebugMenu.AdvanceDialogue(); await Wait(0.5f); }
                }
                else { await Wait(1.0f); await Shot($"HF_Page_{seq}"); sb.AppendLine($"  ({seq} 대사 없음 — 그림만)"); }
                await StoryPageOverlay.HideAsync(CancellationToken.None);
                await Wait(0.6f);
                Check($"그림 페이지 {key} 걷힘", GameObject.Find("@StoryPage") == null);
            }

            // ④ 엔딩 카드 배경
            using (var cts = new CancellationTokenSource())
            {
                var card = EndingCardOverlay.PlayAsync(dlg?.GetLines(EndingSequence.CardKey), null, cts.Token);
                await Wait(3.0f);
                var canvas   = GameObject.Find("EndingCardCanvas");
                var backdrop = canvas != null ? canvas.transform.Find("Backdrop")?.GetComponent<Image>() : null;
                Check($"엔딩 카드 배경 → {(backdrop != null && backdrop.sprite != null ? backdrop.sprite.name : "(없음)")}", backdrop != null && backdrop.sprite != null && backdrop.color.a > 0.5f);
                await Shot("HF_EndingCard");
                cts.Cancel();
                try { await card; } catch (System.OperationCanceledException) { }
                await Wait(0.5f);
                Check("엔딩 카드 걷힘", GameObject.Find("EndingCardCanvas") == null);
            }

            // ⑤ 되찾은 기억 쪽 — 제목 양옆 유물 문장
            var host = new GameObject("@HfProbeCanvas", typeof(Canvas), typeof(CanvasScaler));
            var c = host.GetComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            c.sortingOrder = UISortingOrder.Popup;
            var sc = host.GetComponent<CanvasScaler>();
            sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            sc.referenceResolution = new Vector2(1920f, 1080f);
            sc.matchWidthOrHeight = 0.5f;
            var mem = UI_RelicMemoryPage.Create(host.transform, null);
            mem.Open();
            await Wait(0.8f);
            var crest = typeof(UI_RelicMemoryPage).GetField("_crestL", Any)?.GetValue(mem) as Image;
            var relic = AppBootstrapper.Instance?.Loadout?.Relic;
            sb.AppendLine($"  런 유물: {(relic != null ? relic.DisplayName : "(없음)")}");
            Check($"되찾은 기억 문장 → {(crest != null && crest.sprite != null ? crest.sprite.name : "(없음)")}", crest != null && crest.gameObject.activeInHierarchy && crest.sprite != null);
            await Shot("HF_MemoryCrest");
            Object.Destroy(host);
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            sb.AppendLine($"CHECK 합계 {pass}/{total}");
            File.WriteAllText("Temp/hf_ui_probe.txt", sb.ToString());
            Debug.Log("[HfUiProbe] 끝\n" + sb);
        }
    }

    private static UniTask Wait(float seconds) => UniTask.Delay((int)(seconds * 1000), DelayType.Realtime);

    private static UniTask Shot(string name)
    {
        var m = typeof(UILayoutRuntimeProbeEditor).GetMethod("ShotAsync", BindingFlags.Static | BindingFlags.NonPublic);
        return m != null ? (UniTask)m.Invoke(null, new object[] { name, 0 }) : UniTask.CompletedTask;
    }
}
#endif
