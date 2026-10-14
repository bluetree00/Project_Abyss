#if UNITY_EDITOR
using System.IO;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using RelicFairy.UI;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-14 (cf 알림) ① 컷신 중 방 클리어가 HUD 모드를 덮지 않는가 — 런 세션 하나를 만들어 상태 전환만 돌린다(보스전 없이).
/// ② 보스 말풍선 화자줄이 그린 글의 왼쪽 끝 · 바로 위에 붙는가 — 멀린 한 줄 · 리치 두 줄 · 보스 등장 줄을 띄워 잰다 · 사진.
/// 결과: Temp/cutscene_hud_speaker_probe.txt · Temp/ui_shots/CHS_*.png · 로그 「[CutsceneHudSpeaker] 끝」.
/// </summary>
public static class CutsceneHudSpeakerProbeEditor
{
    private const BindingFlags Any = BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    [MenuItem("RelicFairy/Debug/10-14 컷신 HUD 유지 · 화자줄 자리 (플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[CutsceneHudSpeaker] 플레이 모드에서"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("컷신 HUD 유지 · 화자줄 자리\n");
        int pass = 0, total = 0;
        void Check(string what, bool ok) { total++; if (ok) pass++; sb.AppendLine($"CHECK {what}: {ok}"); }
        var hub = Object.FindFirstObjectByType<TestHubLauncher>();
        bool hubWas = hub != null && hub.enabled;
        if (hub != null) hub.enabled = false;   // 허브 OnGUI 판이 사진을 가린다
        try
        {
            // ① 상태 전환 — ChangeRunState만(아이템 훅 · 기록 없이)
            var change = typeof(GameRunSession).GetMethod("ChangeRunState", Any);
            var a = new GameRunSession();
            a.RequestHudMode(HUDIds.Mode.Cutscene);
            change.Invoke(a, new object[] { GameRunSession.RunState.Standby });
            Check($"컷신 중 방 클리어 → HUD 모드 Cutscene 유지(지금 {a.CurrentHudMode})", a.CurrentHudMode == HUDIds.Mode.Cutscene);
            Check($"런 상태는 Standby로 바뀜(지금 {a.CurrentRunState})", a.CurrentRunState == GameRunSession.RunState.Standby);
            change.Invoke(a, new object[] { GameRunSession.RunState.Map });
            Check($"지도(Map) 전환은 컷신을 닫는다 → None(지금 {a.CurrentHudMode})", a.CurrentHudMode == HUDIds.Mode.None);

            var b = new GameRunSession();
            b.RequestHudMode(HUDIds.Mode.BossCutscene);
            change.Invoke(b, new object[] { GameRunSession.RunState.Standby });
            Check($"페이즈 전환(BossCutscene) 중 방 클리어 → 유지(지금 {b.CurrentHudMode})", b.CurrentHudMode == HUDIds.Mode.BossCutscene);

            var c = new GameRunSession();
            c.RequestHudMode(HUDIds.Mode.Boss);
            change.Invoke(c, new object[] { GameRunSession.RunState.Standby });
            Check($"컷신 아닐 때 방 클리어 → Combat 그대로(지금 {c.CurrentHudMode})", c.CurrentHudMode == HUDIds.Mode.Combat);

            var d = new GameRunSession();
            d.RequestHudMode(HUDIds.Mode.Cutscene);
            change.Invoke(d, new object[] { GameRunSession.RunState.CombatRoom });
            Check($"컷신 중 다른 방 진입은 그대로 바꾼다 → Combat(지금 {d.CurrentHudMode})", d.CurrentHudMode == HUDIds.Mode.Combat);

            // ② 화자줄 자리
            (string text, BossBarkType type, DialogueSpeaker who, string shot)[] lines =
            {
                ("날개를 접어. …이제 잠들 시간이야.", BossBarkType.MerlinNarration, DialogueSpeaker.Merlin, "CHS_Merlin"),
                ("알고 있었다. 네가 올 것을. 영혼이여, 이번에도 같은 자리에서 무너지리라. 대제단은 너를 기억한다.", BossBarkType.Bark, DialogueSpeaker.Lich, "CHS_Lich2"),
                ("또 기어 왔군.", BossBarkType.Bark, DialogueSpeaker.Dragon, "CHS_DragonShort"),
            };
            var bark = UI_BossBark.Instance;
            if (bark == null) { sb.AppendLine("말풍선 없음"); return; }
            var label   = typeof(UI_BossBark).GetField("_label", Any)?.GetValue(bark) as TMP_Text;
            var speaker = typeof(UI_BossBark).GetField("_speakerLabel", Any)?.GetValue(bark) as TMP_Text;
            var divider = typeof(UI_BossBark).GetField("_speakerDivider", Any)?.GetValue(bark) as UnityEngine.UI.Image;
            var portrait = typeof(UI_BossBark).GetField("_portraitGroup", Any)?.GetValue(bark) as RectTransform;
            foreach (var (text, type, who, shot) in lines)
            {
                UI_BossBark.ShowLatest(text, type, who);
                await Wait(1.2f);
                float scale = label.canvas.scaleFactor;
                var (tl, tr, tb, tt) = Ink(label);
                var sc = new Vector3[4]; speaker.rectTransform.GetWorldCorners(sc);
                var (sl, _, sbot, _) = Ink(speaker);
                var dc = new Vector3[4]; divider.rectTransform.GetWorldCorners(dc);
                sb.AppendLine($"  {who} 「{text}」 줄 {label.textInfo.lineCount} · 글 x {tl / scale:0}~{tr / scale:0} y {tb / scale:0}~{tt / scale:0} · 화자 x {sl / scale:0} 아래 {sbot / scale:0} · 밑줄 x {dc[0].x / scale:0}~{dc[2].x / scale:0} y {dc[0].y / scale:0}");
                Check($"화자줄 왼쪽 = 글 왼쪽(±12 · {who})", Mathf.Abs(sl - tl) / scale <= 12f);
                float gap = (sbot - tt) / scale;
                Check($"화자줄이 글 바로 위(틈 4~40 · 지금 {gap:0} · {who})", gap >= 4f && gap <= 40f);
                Check($"밑줄이 글 위 · 화자줄 아래({who})", dc[0].y >= tt - 2f * scale && dc[2].y <= sbot + 2f * scale);
                Check($"밑줄 폭 ≤ 글 폭 + 4({who})", (dc[2].x - dc[0].x) <= (tr - tl) + 4f * scale);
                if (portrait != null && portrait.gameObject.activeInHierarchy)
                {
                    var pc = new Vector3[4]; portrait.GetWorldCorners(pc);
                    Check($"초상과 화자줄 안 겹침({who})", pc[2].x < sl);
                    float pg = (tl - pc[2].x) / scale;
                    Check($"초상 오른쪽 끝 ~ 글 왼쪽 끝 = 28±12(지금 {pg:0} · {who})", Mathf.Abs(pg - 28f) <= 12f);
                }
                await Shot(shot);
            }
            UI_BossBark.Dismiss(0f);
            await Wait(0.6f);
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            if (hub != null) hub.enabled = hubWas;
            sb.AppendLine($"CHECK 합계 {pass}/{total}");
            File.WriteAllText("Temp/cutscene_hud_speaker_probe.txt", sb.ToString());
            Debug.Log("[CutsceneHudSpeaker] 끝\n" + sb);
        }
    }

    /// <summary>보이는 글자 모양의 왼 · 오 · 아래 · 위 끝(화면 픽셀).</summary>
    private static (float l, float r, float b, float t) Ink(TMP_Text t)
    {
        t.ForceMeshUpdate();
        var info = t.textInfo;
        float l = float.MaxValue, r = float.MinValue, b = float.MaxValue, top = float.MinValue;
        for (int i = 0; i < info.characterCount; i++)
        {
            var ch = info.characterInfo[i];
            if (!ch.isVisible) continue;
            l = Mathf.Min(l, ch.bottomLeft.x); r = Mathf.Max(r, ch.topRight.x);
            b = Mathf.Min(b, ch.bottomLeft.y); top = Mathf.Max(top, ch.topRight.y);
        }
        var lo = t.rectTransform.TransformPoint(new Vector3(l, b, 0f));
        var hi = t.rectTransform.TransformPoint(new Vector3(r, top, 0f));
        return (lo.x, hi.x, lo.y, hi.y);
    }

    private static UniTask Wait(float seconds) => UniTask.Delay((int)(seconds * 1000), DelayType.Realtime);

    private static UniTask Shot(string name)
    {
        var m = typeof(UILayoutRuntimeProbeEditor).GetMethod("ShotAsync", BindingFlags.Static | BindingFlags.NonPublic);
        return m != null ? (UniTask)m.Invoke(null, new object[] { name, 0 }) : UniTask.CompletedTask;
    }
}
#endif
