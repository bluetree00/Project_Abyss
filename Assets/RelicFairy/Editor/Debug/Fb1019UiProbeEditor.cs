#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 10-19 빌드 피드백 UI 반입 실측(테스트 허브 · 플레이 중) — 바탕화면 구현설계/세션별_남은작업_20261019/00_모음.md.
/// <list type="number">
/// <item>⑥ 파츠 공방 — 힉스필드 새 틀(카드 · 창 · 잠긴 칸)이 깔렸나 · 고른 표시.</item>
/// <item>② 거점에서 ESC 「로비로」 → 로비에 HUD가 남지 않나.</item>
/// <item>① 로비 세이브 카드 — 거점 세이브면 「포션 0/3」 대신 최고 챕터 · 완주 · 정수.</item>
/// </list>
/// 결과 Temp/fb1019_ui.txt · Temp/ui_shots/FB19_*.png · 로그 「[FB19ui] 끝」. 마지막엔 로비에 머문다(실측 뒤 플레이를 멈출 것).
/// </summary>
public static class Fb1019UiProbeEditor
{
    private const string Tag     = "[FB19ui]";
    private const string OutTxt  = "Temp/fb1019_ui.txt";
    private const string ShotDir = "Temp/ui_shots";
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    [MenuItem("RelicFairy/Debug/10-19 UI 반입 촬영 — 파츠 공방 · 로비 HUD · 세이브 카드 (테스트 허브, 플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning($"{Tag} 플레이 중(테스트 허브)에만 동작한다."); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("10-19 UI 반입 실측\n");
        Directory.CreateDirectory(ShotDir);
        try
        {
            await Wait(2f);
            Invoke("HideTestHubGui");

            // ── ⑥ 파츠 공방
            var all = Managers.WeaponParts?.All;
            if (all == null || all.Count == 0) sb.AppendLine("⑥ 파츠 데이터 없음");
            else
            {
                var cards = new List<UI_StartPartPopup.Card>(all.Count);
                foreach (var def in all)
                    cards.Add(new UI_StartPartPopup.Card { Part = def, Unlocked = StartPartStation.IsUnlocked(def.part_id), UnlockHint = "기억의 제단에서 해금" });
                var popup = await Managers.UI.ShowPopupUIAndGetAsync<UI_StartPartPopup>();
                popup.Setup(cards, all[0].part_id);
                await Wait(0.9f);
                int framed = 0, lockedFramed = 0;
                foreach (var img in popup.GetComponentsInChildren<Image>(true))
                {
                    if (img.sprite == null || img.sprite.name != "Frame_PartsWorkshopCard") continue;
                    if (img.transform.parent != null && img.transform.parent.name.StartsWith("Locked_")) lockedFramed++; else framed++;
                }
                bool panel = false;
                foreach (var img in popup.GetComponentsInChildren<Image>(true))
                    if (img.sprite != null && img.sprite.name == "Frame_PartsWorkshopPanel") { panel = true; break; }
                int unlocked = 0; foreach (var c in cards) if (c.Unlocked) unlocked++;
                Check(sb, $"⑥ 공방 카드 틀 {framed}/{unlocked} · 잠긴 칸 틀 {lockedFramed}/{cards.Count - unlocked} · 창 틀 {panel}",
                      framed == unlocked && lockedFramed == cards.Count - unlocked && panel);
                await ShotAsync("FB19_1_workshop");
                typeof(UI_StartPartPopup).GetMethod("SetSelected", Any)?.Invoke(popup, new object[] { 0, true });
                await Wait(0.6f);
                await ShotAsync("FB19_2_workshop_selected");
                Managers.UI.CloseAllPopupUI();
                Time.timeScale = 1f;
                await Wait(0.5f);
            }

            // ── ⑦ 원거리 무기 고르기 창 부제(96 다시 고르기와 짝)
            var bow = AssetDatabase.LoadAssetAtPath<WeaponSO>("Assets/RelicFairy/Weapon/Bow/Data/T1_Bow.asset");
            var forge = bow != null ? await Managers.UI.ShowPopupUIAndGetAsync<UI_RangedForgePopup>() : null;
            if (forge != null)
            {
                forge.Setup(new List<UI_RangedForgePopup.Entry> { new() { Weapon = bow } });
                await Wait(0.6f);
                string subText = "";
                foreach (var t in forge.GetComponentsInChildren<TMP_Text>(true)) if (t.name == "Sub") { subText = t.text; break; }
                Check(sb, $"⑦ 원거리 무기 창 부제 「{subText}」에 「다시 바꿀 수 있다」", subText.Contains("다시 바꿀 수 있다"));
                await ShotAsync("FB19_6_forge");
                Managers.UI.CloseAllPopupUI();
                Time.timeScale = 1f;
                await Wait(0.5f);
            }
            else sb.AppendLine("⑦ 원거리 무기 창을 못 열었다");

            // ── ② 거점 → ESC 「로비로」
            var esc = Object.FindFirstObjectByType<UI_EscMenu>(FindObjectsInactive.Include);   // ESC 메뉴는 팝업이 아니라 늘 있는 창(다른 실측 도구와 같은 길)
            if (esc == null) { sb.AppendLine("② ESC 창을 못 찾았다"); return; }
            esc.Open();
            await Wait(0.4f);
            typeof(UI_EscMenu).GetMethod("OnLobby", Any)?.Invoke(esc, null);
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 40f && SceneManager.GetActiveScene().name != "Lobby")
                await UniTask.Delay(300, DelayType.Realtime);
            sb.AppendLine($"② 씬 = {SceneManager.GetActiveScene().name}");
            await Wait(3f);
            var hud = Object.FindFirstObjectByType<HudBootstrapper>(FindObjectsInactive.Include);
            var visual = hud != null ? typeof(HudBootstrapper).GetField("hudVisualRoot", Any)?.GetValue(hud) as Component : null;
            bool hudShown = visual != null ? visual.gameObject.activeInHierarchy : hud != null && hud.gameObject.activeInHierarchy;
            Check(sb, $"② 로비에 HUD가 남지 않는다(HUD 보임 {hudShown})", SceneManager.GetActiveScene().name == "Lobby" && !hudShown);
            await ShotAsync("FB19_3_lobby");

            // ── ① 세이브 카드
            var lobby = Object.FindFirstObjectByType<UI_Lobby>();
            if (lobby != null) typeof(UI_Lobby).GetMethod("OnClickStartRun", Any)?.Invoke(lobby, null);
            await Wait(1.6f);
            var slotCards = Object.FindObjectsByType<UI_SaveSlotCard>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            bool potionAtHub = false;
            foreach (var card in slotCards)
            {
                string name = CardLine(card, "nameText"), kit = CardLine(card, "progressText"), state = CardLine(card, "retryText"), foot = CardLine(card, "savedAtText");
                if (string.IsNullOrEmpty(name)) continue;
                sb.AppendLine($"① 카드 「{name}」 · 「{kit}」 · 「{state}」 · 「{foot.Replace('\n', ' ')}」");
                if (name.Contains("베이스캠프") && state.Contains("포션")) potionAtHub = true;
            }
            Check(sb, "① 거점 세이브 카드에 「포션」 줄이 없다", !potionAtHub);
            await ShotAsync("FB19_4_slots");

            // ①b 거점 기록 — 테스트 세이브가 런 중이면 거점 카드가 안 나온다 → 같은 카드 코드에 거점 기록을 넣어 본다(화면만 · 저장 없음)
            if (slotCards.Length > 0)
            {
                var card = slotCards[0];
                int slot = typeof(UI_SaveSlotCard).GetField("_slotIndex", Any)?.GetValue(card) is int si ? si : 0;
                var hubRec = new RunSaveData { hasActiveRun = true, isInStartRoom = true, retryCount = 1, playSeconds = 840,
                                               savedAt = System.DateTime.UtcNow.ToString("o"), potionCapacity = 3 };
                card.Setup(hubRec, slot, (_, __) => { }, null);
                await Wait(0.4f);
                string kit = CardLine(card, "progressText"), state = CardLine(card, "retryText");
                Check(sb, $"①b 거점 카드 = 「{CardLine(card, "nameText")}」 · 「{kit}」 · 「{state}」 — 포션 없음 · 최고 챕터 · 정수",
                      !state.Contains("포션") && kit.Contains("최고 챕터") && state.Contains("정수"));
                await ShotAsync("FB19_5_hub_card");
            }
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            Invoke("RestoreTestHubGui");
            File.WriteAllText(OutTxt, sb.ToString());
            Debug.Log($"{Tag} 끝\n{sb}");
        }
    }

    private static void Check(StringBuilder sb, string what, bool ok) => sb.AppendLine($"CHECK {ok} — {what}");

    private static string CardLine(UI_SaveSlotCard card, string field)
        => (typeof(UI_SaveSlotCard).GetField(field, Any)?.GetValue(card) as TMP_Text)?.text ?? "";

    private static UniTask Wait(float seconds) => UniTask.Delay(System.TimeSpan.FromSeconds(seconds), DelayType.Realtime);

    /// <summary>테스트 허브 GUI 숨김 · 복원(UILayoutRuntimeProbeEditor의 같은 이름 도우미 — 다른 실측 도구와 같은 길).</summary>
    private static void Invoke(string method)
        => typeof(UILayoutRuntimeProbeEditor).GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)?.Invoke(null, null);

    private static async UniTask ShotAsync(string name)
    {
        var runner = Object.FindFirstObjectByType<Managers>();
        if (runner == null) return;
        await UniTask.WaitForEndOfFrame(runner);
        var tex = ScreenCapture.CaptureScreenshotAsTexture();
        if (tex == null) return;
        File.WriteAllBytes(Path.Combine(ShotDir, name + ".png"), tex.EncodeToPNG());
        Object.Destroy(tex);
    }
}
#endif
