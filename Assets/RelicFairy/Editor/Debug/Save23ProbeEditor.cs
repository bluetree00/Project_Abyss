#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>
/// 10-23 로비 세이브 창 새 판 실측(플레이 중 · Lobby) — 사용자 「로비에 세이브 팝업을 다시 새로 하나 뽑자 · 톤에 맞게 · 세이브 데이터를 잘 보여 줄 만한 내용으로 배경이랑 틀」.
/// <list type="number">
/// <item>로비에서 [Start] → 창이 열린다 · 옛 계층(Dim · Panel_Main · ConfirmRoot)이 걷혔나 · 배경 · 카드 틀 그림이 깔렸나 · 카드 3장이 화면 안에 있나.</item>
/// <item>카드에 가짜 기록(진행 중 런 · 거점 · 전투 중에 끈 런)을 넣어(화면만 · 저장 없음) 줄이 다 차나 · 거점 카드에 포션 줄이 없나 · 경고 줄.</item>
/// <item>글 넘침(말줄임 · 칸 밖) · 글끼리 겹침 · 글이 카드 틀 밖으로 나가나 — 매 촬영마다.</item>
/// <item>마우스 올림 금선 · 삭제 확인 창(취소 → 기록 그대로) · 이어하기 차단 안내(blockResume 잠깐 켬).</item>
/// </list>
/// 결과 Temp/save23.txt · Temp/ui_shots/SAVE23_*.png · 로그 「[Save23] 끝」. 마지막엔 실제 기록으로 다시 연 창을 찍고 닫는다(세이브는 건드리지 않는다).
/// </summary>
public static class Save23ProbeEditor
{
    private const string Tag     = "[Save23]";
    private const string OutTxt  = "Temp/save23.txt";
    private const string ShotDir = "Temp/ui_shots";
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    private static int s_fail;

    [MenuItem("RelicFairy/Debug/10-23 로비 세이브 창 촬영 · 점검 (플레이 중 · Lobby)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning($"{Tag} 플레이 중에만 동작한다 — Build/Lobby 씬에서 플레이한 뒤 다시 누를 것."); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("10-23 로비 세이브 창 실측\n");
        s_fail = 0;
        Directory.CreateDirectory(ShotDir);
        try
        {
            // ── 0) 로비
            if (FindActive<UI_Lobby>() == null)
            {
                AppBootstrapper.Instance?.RequestLoad(Define.Scene.Lobby);
                for (int i = 0; i < 400 && FindActive<UI_Lobby>() == null; i++) await Wait(0.1f);
                await Wait(4f);   // 로딩 · 첫 화면 페이드가 걷히도록
            }
            var lobby = FindActive<UI_Lobby>();
            if (lobby == null) { Check(sb, "로비(UI_Lobby)를 찾았다", false); return; }
            var panel = typeof(UI_Lobby).GetField("saveSlotPanel", Any)?.GetValue(lobby) as UI_SaveSlotPanel;
            if (panel == null) { Check(sb, "로비의 세이브 창(saveSlotPanel)이 이어져 있다", false); return; }

            // ── 1) 열기
            typeof(UI_Lobby).GetMethod("OnClickStartRun", Any)?.Invoke(lobby, null);
            await Wait(1.2f);
            Check(sb, "창이 열렸다", panel.gameObject.activeInHierarchy);
            Check(sb, "옛 계층(Dim · Panel_Main · Btn_Back 그림 · ConfirmRoot)이 걷혔다",
                  panel.transform.Find("Dim") == null && panel.transform.Find("Panel_Main") == null && panel.transform.Find("ConfirmRoot") == null);

            var skin = UISkin.SaveSlot;
            Check(sb, $"세이브 창 스킨 로드(UI/SaveSlotSkin) {skin != null} · 배경 {skin?.Background != null} · 틀 {skin?.CardFrame != null}",
                  skin != null && skin.Background != null && skin.CardFrame != null);
            Check(sb, "배경 그림이 깔렸다(Art_SaveSlotBackground)", HasSprite(panel.transform, "Art_SaveSlotBackground") > 0);

            var cards = panel.GetComponentsInChildren<UI_SaveSlotCard>(false);
            Array.Sort(cards, (a, b) => a.SlotIndex.CompareTo(b.SlotIndex));
            Check(sb, $"카드 3장 · 틀 그림(Frame_SaveSlotCard) {HasSprite(panel.transform, "Frame_SaveSlotCard")}장",
                  cards.Length == RunProgressManager.SlotCount && HasSprite(panel.transform, "Frame_SaveSlotCard") == RunProgressManager.SlotCount);
            int offscreen = 0;
            foreach (var c in cards) if (!OnScreen((RectTransform)c.transform)) offscreen++;
            Check(sb, $"카드가 화면 안에 있다(밖 {offscreen})", offscreen == 0);
            foreach (var c in cards) sb.AppendLine($"  실제 기록 {c.SlotIndex + 1}: {(c.HasSave ? "있음" : "빈 기록")} — {CardDump(c)}");
            await ShotAsync("SAVE23_1_open");
            CheckTexts(sb, panel.transform, "열림");

            if (cards.Length < RunProgressManager.SlotCount) return;

            // ── 2) 가짜 기록으로 채운 카드(화면만 · 저장 없음)
            string now = DateTime.UtcNow.ToString("o");
            var run = new RunSaveData
            {
                hasActiveRun = true, chapter = 2, currentHp = 820, maxHp = 950, potionCount = 2, potionCapacity = 3,
                relicKey = "RelicClass_Gawain", weapon0PrefabKey = "T1_Nameless", weapon0EnhanceLevel = 6, weapon1PrefabKey = "T1_Bow",
                roomClearCount = 14, retryCount = 12, playSeconds = 4380, savedAt = now,
            };
            var hub = new RunSaveData { hasActiveRun = true, isInStartRoom = true, chapter = 1, retryCount = 3, playSeconds = 840, savedAt = now, potionCapacity = 3 };
            var locked = new RunSaveData
            {
                hasActiveRun = true, chapter = 4, currentHp = 130, maxHp = 1210, potionCount = 0, potionCapacity = 3,
                relicKey = "RelicClass_Lancelot", weapon0PrefabKey = "T1_Greatsword", weapon0EnhanceLevel = 21, weapon1PrefabKey = "T1_Crossbow",
                roomClearCount = 41, retryCount = 120, playSeconds = 98765, savedAt = now, combatLocked = true,
            };
            Action<int, bool> noStart = (_, __) => { };
            Action<int> noDelete = _ => { };
            cards[0].Setup(run,    cards[0].SlotIndex, noStart, noDelete);
            cards[1].Setup(hub,    cards[1].SlotIndex, noStart, noDelete);
            cards[2].Setup(locked, cards[2].SlotIndex, noStart, noDelete);
            await Wait(0.6f);

            int runRows = Filled(cards[0], "RowValue", 4), runFoot = Filled(cards[0], "FootValue", 3);
            Check(sb, $"런 카드 줄 {runRows}/4 · 발 {runFoot}/3 — {CardDump(cards[0])}", runRows == 4 && runFoot == 3);
            string hubTitle = TextOf(cards[1], "Title"), hubTrack = TextOf(cards[1], "TrackText");
            bool hubPotion = false;
            for (int i = 0; i < 4; i++) if (TextOf(cards[1], $"RowLabel{i}") == "포션") hubPotion = true;
            Check(sb, $"거점 카드 「{hubTitle}」 · 길 「{hubTrack}」 · 포션 줄 없음 — {CardDump(cards[1])}",
                  hubTitle == "베이스캠프" && hubTrack.Contains("/ 4") && !hubPotion && Filled(cards[1], "FootValue", 3) == 3);
            string note = TextOf(cards[2], "Note");
            Check(sb, $"전투 중에 끈 런 경고 「{note}」", note.Contains("사망으로 정산"));
            await ShotAsync("SAVE23_2_filled");
            CheckTexts(sb, panel.transform, "채운 카드");

            // ── 3) 마우스 올림 금선
            cards[0].OnPointerEnter(null);
            await Wait(0.35f);
            var glow = FindDeep(cards[0].transform, "Glow")?.GetComponent<Image>();
            Check(sb, $"마우스 올림 금선 알파 {glow?.color.a:0.00}", glow != null && glow.color.a > 0.3f);
            await ShotAsync("SAVE23_3_hover");
            cards[0].OnPointerExit(null);

            // ── 4) 삭제 확인 — 취소하면 기록은 그대로
            var rpm = RunProgressManager.Instance;
            int slot = cards[0].SlotIndex;
            bool hadRun = rpm != null && rpm.HasLocalRun(slot);
            typeof(UI_SaveSlotPanel).GetMethod("OnSlotDeleteClicked", Any)?.Invoke(panel, new object[] { slot });
            await Wait(0.5f);
            Check(sb, "삭제 확인 창이 떴다", UI_ConfirmDialog.IsOpen);
            await ShotAsync("SAVE23_4_delete_confirm");
            UI_ConfirmDialog.CloseIfOpen();
            await Wait(0.4f);
            Check(sb, $"취소 → 기록 그대로(전 {hadRun} · 뒤 {rpm != null && rpm.HasLocalRun(slot)})", !UI_ConfirmDialog.IsOpen && (rpm == null || rpm.HasLocalRun(slot) == hadRun));

            // ── 5) 이어하기 차단 안내 — blockResume을 잠깐 켠다(못 켜면 누르지 않는다: 진짜 이어하기가 돈다)
            var flag = typeof(UI_SaveSlotPanel).GetField("blockResume", Any);
            bool before = flag != null && (bool)flag.GetValue(panel);
            flag?.SetValue(panel, true);
            if (flag != null && (bool)flag.GetValue(panel))
            {
                typeof(UI_SaveSlotPanel).GetMethod("OnSlotStartClicked", Any)?.Invoke(panel, new object[] { slot, true });
                await Wait(0.5f);
                Check(sb, "이어하기 차단 안내 창이 떴다(창은 열린 채)", UI_ConfirmDialog.IsOpen && panel.gameObject.activeInHierarchy);
                await ShotAsync("SAVE23_5_blocked");
                UI_ConfirmDialog.CloseIfOpen();
                await Wait(0.4f);
            }
            else Check(sb, "blockResume 필드를 켜지 못했다", false);
            flag?.SetValue(panel, before);

            // ── 6) 실제 기록으로 다시 열기
            panel.Open();
            await Wait(0.9f);
            await ShotAsync("SAVE23_6_real");
            CheckTexts(sb, panel.transform, "실제 기록");
            panel.Close();
        }
        catch (Exception e) { sb.AppendLine("예외: " + e); s_fail++; }
        finally
        {
            sb.Insert(0, $"실패 {s_fail}\n");
            File.WriteAllText(OutTxt, sb.ToString());
            Debug.Log($"{Tag} 끝 — 실패 {s_fail}\n{sb}");
        }
    }

    // ── 점검 ────────────────────────────────────────────────

    private static void Check(StringBuilder sb, string what, bool ok)
    {
        if (!ok) s_fail++;
        sb.AppendLine($"CHECK {ok} — {what}");
    }

    /// <summary>켜진 글마다: 말줄임 · 칸 밖으로 넘침 · 다른 글과 겹침 · 카드 틀 밖으로 나감.</summary>
    private static void CheckTexts(StringBuilder sb, Transform root, string label)
    {
        Canvas.ForceUpdateCanvases();
        var list = new List<(TMP_Text t, Rect r)>();
        int overflow = 0, overlap = 0, outside = 0;
        foreach (var t in root.GetComponentsInChildren<TMP_Text>(false))
        {
            if (!t.gameObject.activeInHierarchy || string.IsNullOrEmpty(t.text)) continue;
            t.ForceMeshUpdate();
            var box = WorldRect(t.rectTransform);
            var ink = InkRect(t);
            bool over = t.isTextOverflowing || t.isTextTruncated || ink.width > box.width + 2f;
            if (over) { overflow++; sb.AppendLine($"  넘침 — {PathOf(t.transform, root)} 「{t.text}」"); }

            var card = t.GetComponentInParent<UI_SaveSlotCard>();
            if (card != null && !Inside(ink, WorldRect((RectTransform)card.transform), 12f))
            {
                outside++;
                sb.AppendLine($"  틀 밖 — {PathOf(t.transform, root)} 「{t.text}」");
            }
            list.Add((t, ink));
        }
        for (int i = 0; i < list.Count; i++)
            for (int j = i + 1; j < list.Count; j++)
            {
                var a = list[i].r; var b = list[j].r;
                float w = Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin);
                float h = Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin);
                if (w <= 1f || h <= 1f) continue;
                overlap++;
                sb.AppendLine($"  겹침 — 「{list[i].t.text}」 × 「{list[j].t.text}」");
            }
        Check(sb, $"[{label}] 글 {list.Count}개 · 넘침 {overflow} · 겹침 {overlap} · 틀 밖 {outside}", overflow == 0 && overlap == 0 && outside == 0);
    }

    private static Rect WorldRect(RectTransform rt)
    {
        var c = new Vector3[4];
        rt.GetWorldCorners(c);
        return Rect.MinMaxRect(c[0].x, c[0].y, c[2].x, c[2].y);
    }

    /// <summary>실제로 그려진 글자 사각형(화면 좌표 — 오버레이 캔버스라 월드 = 화면).</summary>
    private static Rect InkRect(TMP_Text t)
    {
        var b = t.textBounds;
        var min = t.transform.TransformPoint(b.min);
        var max = t.transform.TransformPoint(b.max);
        return Rect.MinMaxRect(Mathf.Min(min.x, max.x), Mathf.Min(min.y, max.y), Mathf.Max(min.x, max.x), Mathf.Max(min.y, max.y));
    }

    private static bool Inside(Rect inner, Rect outer, float inset)
        => inner.xMin >= outer.xMin + inset && inner.xMax <= outer.xMax - inset && inner.yMin >= outer.yMin + inset && inner.yMax <= outer.yMax - inset;

    private static bool OnScreen(RectTransform rt)
    {
        var r = WorldRect(rt);
        return r.xMin >= -1f && r.yMin >= -1f && r.xMax <= Screen.width + 1f && r.yMax <= Screen.height + 1f;
    }

    private static int HasSprite(Transform root, string spriteName)
    {
        int n = 0;
        foreach (var img in root.GetComponentsInChildren<Image>(false))
            if (img.sprite != null && img.sprite.name == spriteName) n++;
        return n;
    }

    private static int Filled(UI_SaveSlotCard card, string prefix, int count)
    {
        int n = 0;
        for (int i = 0; i < count; i++)
        {
            var t = FindDeep(card.transform, prefix + i)?.GetComponent<TMP_Text>();
            if (t != null && t.gameObject.activeInHierarchy && !string.IsNullOrEmpty(t.text)) n++;
        }
        return n;
    }

    private static string TextOf(UI_SaveSlotCard card, string name)
    {
        var t = FindDeep(card.transform, name)?.GetComponent<TMP_Text>();
        return t != null && t.gameObject.activeInHierarchy ? t.text : "";
    }

    private static string CardDump(UI_SaveSlotCard card)
    {
        var sb = new StringBuilder();
        foreach (var t in card.GetComponentsInChildren<TMP_Text>(false))
            if (t.gameObject.activeInHierarchy && !string.IsNullOrEmpty(t.text)) sb.Append(t.text.Replace('\n', ' ')).Append(" | ");
        return sb.ToString();
    }

    private static Transform FindDeep(Transform root, string name)
    {
        if (root == null) return null;
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            var hit = FindDeep(root.GetChild(i), name);
            if (hit != null) return hit;
        }
        return null;
    }

    private static string PathOf(Transform t, Transform root)
    {
        var sb = new StringBuilder(t.name);
        for (var p = t.parent; p != null && p != root; p = p.parent) sb.Insert(0, p.name + "/");
        return sb.ToString();
    }

    private static T FindActive<T>() where T : Object => Object.FindFirstObjectByType<T>(FindObjectsInactive.Exclude);

    private static UniTask Wait(float seconds) => UniTask.Delay(TimeSpan.FromSeconds(seconds), DelayType.Realtime);

    /// <summary>게임 화면을 찍는다 — 검은 프레임이면 몇 번 다시(다른 실측 도구와 같은 길).</summary>
    private static async UniTask ShotAsync(string name)
    {
        var runner = Object.FindFirstObjectByType<Managers>();
        if (runner == null) return;
        for (int attempt = 0; attempt < 8; attempt++)
        {
            await UniTask.WaitForEndOfFrame(runner);
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            if (tex == null) continue;
            bool black = true;
            for (int y = 0; y < tex.height && black; y += Mathf.Max(1, tex.height / 12))
                for (int x = 0; x < tex.width; x += Mathf.Max(1, tex.width / 16))
                    if (tex.GetPixel(x, y).maxColorComponent > 0.02f) { black = false; break; }
            if (black && attempt < 7) { Object.Destroy(tex); continue; }
            File.WriteAllBytes(System.IO.Path.Combine(ShotDir, name + ".png"), tex.EncodeToPNG());
            Object.Destroy(tex);
            return;
        }
    }
}
#endif
