#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 10-11 기억의 제단 위계 · 가독성 개편(비교 리서치 B §7 — 캡처 없이 코드로 추정한 값) 실측. 거점에서 제단 화면을 열어 세 장을 찍는다.
/// ① 전체(창을 연 그대로 · 자동 초점) ② 노드 하나 고른 상태(흐린 노드에 초점 + 마우스를 올린 길) ③ 상세 패널(값이 모자란 노드).
/// ①③은 상세 패널만 잘라 낸 사진도 남긴다. ④ 해금 배너(10-13 Artsystack 띠 · 배지) — 연출만 띄운다(해금 · 저장 없음).
/// <para>먼저 실제 상태로 한 번 열고 닫는다 — 환급 · 새 시기 고리 기록을 게임 규칙대로 끝내야, 아래에서 바꾼 상태로 열 때 창이 저장을 부르지 않는다.
/// 그 뒤 해금 · 정수 · 진척 기록을 <b>메모리에서만</b> 중간 진행(산 것 · 살 수 있음 · 열림 · 조건 잠김 · 한 칸 앞이 다 보이는 상태)으로 바꿔 연다.
/// 저장 호출 0 · 끝나면 원래 값으로 되돌린다.</para>
/// 결과: Temp/ui_shots/AltarUx2_*.png · Temp/altar_ux2_probe.txt · 로그 「[AltarUx2] 끝」. ⚠️ 거점(BaseCamp) 플레이 중.
/// </summary>
public static class AltarTreeUx2ProbeEditor
{
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags StaticNonPub = BindingFlags.Static | BindingFlags.NonPublic;
    private const string Tag = "[AltarUx2]";
    private const string ShotDir = "Temp/ui_shots";
    private const int MidEssence = 1000;

    // 중간 진행 — 갈래마다 앞 칸 몇 개. 정수 1000 + 진척 기록을 비우면(할인 없음) 다섯 상태가 다 나온다.
    private static readonly string[] MidUnlocked =
    {
        MemoryAltarCatalog.CovenantParts1, MemoryAltarCatalog.PartPierce, MemoryAltarCatalog.WeaponCrossbow,
        MemoryAltarCatalog.SigilSmith, MemoryAltarCatalog.Revive,
        $"{MemoryAltarCatalog.MaxHpUp}:1",   // 10-14 강화형 — 일부만 올린 칸(Lv 1/5)
    };

    [MenuItem("RelicFairy/Debug/10-11 기억의 제단 촬영 (거점, 플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning($"{Tag} 플레이 중에 실행해야 한다."); return; }
        if (!InBaseCamp()) { Debug.LogWarning($"{Tag} 거점(BaseCamp) 씬에서 실행해야 한다."); return; }
        if (BackendGameData.Instance?.Data == null) { Debug.LogWarning($"{Tag} 계정 데이터가 없다(로그인 뒤 실행)."); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("기억의 제단 위계 · 가독성 실측(10-11)\n");
        var data = BackendGameData.Instance.Data;
        string keepUnlocked = data.unlockedIds, keepRecords = data.records;
        int keepEssence = data.abyssEssence;
        bool forced = false;
        UI_AwakeningPanel panel = null;
        Invoke("HideTestHubGui");
        try
        {
            Directory.CreateDirectory(ShotDir);
            Managers.UI.CloseAllPopupUI();
            await UniTask.Delay(250, ignoreTimeScale: true);

            // 0 — 실제 상태로 한 번(첫 열기 연출 · 환급 · 시기 고리 기록). 기록이 다 끝날 때까지 기다린다.
            panel = await OpenAsync(sb);
            if (panel == null) return;
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 6f && !SaveGuardsDone(data)) await UniTask.Delay(200, ignoreTimeScale: true);
            await UniTask.Delay(800, ignoreTimeScale: true);
            panel.ClosePopupUI(); panel = null;
            await UniTask.Delay(300, ignoreTimeScale: true);
            if (!SaveGuardsDone(data))
            {
                sb.AppendLine("중단 — 환급 · 시기 고리 기록이 끝나지 않아 바꾼 상태로 열면 저장될 수 있다(실제 상태로 제단을 한 번 열어 둔 뒤 다시).");
                return;
            }

            // 바꾼 상태 — 메모리에서만(저장 호출 없음). 진척 기록을 비우는 것은 할인이 끼어 상태가 갈리지 않게 하려는 것.
            keepUnlocked = data.unlockedIds; keepRecords = data.records; keepEssence = data.abyssEssence;
            forced = true;
            data.unlockedIds  = string.Join(",", MidUnlocked);
            data.abyssEssence = MidEssence;
            data.records      = StripProgress(data.records);
            sb.AppendLine($"상태: 해금 {MidUnlocked.Length}개 · 정수 {MidEssence} · 진척 기록 비움 · 드러난 시기 {MemoryAltarCatalog.RevealedEra}");

            panel = await OpenAsync(sb);
            if (panel == null) return;
            var tree = Get<AltarTreeView>(panel, "_tree");
            var detail = Get<AltarDetailPanel>(panel, "_detail");
            if (tree == null || detail == null) { sb.AppendLine("트리 · 상세 패널을 못 찾았다"); return; }
            await UniTask.Delay(900, ignoreTimeScale: true);

            // ① 전체
            await ShotAsync("AltarUx2_1_Overview", detail);
            sb.AppendLine("\n① 전체 — 자동 초점 " + (tree.FocusedNode?.DisplayName ?? "없음"));
            DumpTree(sb, tree);
            DumpDetail(sb, detail);
            await DumpMotionAsync(sb, tree);
            await DragZoomAsync(sb, tree);

            // ② 노드 하나 고른 상태 — 흐린 노드(열림 → 한 칸 앞 → 조건 잠김 순)에 초점 + 마우스를 올린 길
            var nodes = Get<Dictionary<string, AltarTreeNodeView>>(tree, "_nodes");
            var pick = PickDim(tree, nodes);
            if (pick != null)
            {
                tree.Focus(pick.Node, select: true);
                pick.OnPointerEnter(null);
                await UniTask.Delay(450, ignoreTimeScale: true);
                await ShotAsync("AltarUx2_2_Focus", null);
                sb.AppendLine($"\n② 고른 노드 — {pick.Node.DisplayName}({pick.Visual}) · 이름표 알파 {pick.Label.color.a:0.00}(1이어야)");
                DumpEdges(sb, tree, "② 선(마우스를 올린 길 포함)");
                pick.OnPointerExit(null);
            }
            else sb.AppendLine("\n② 흐린 노드가 없다 — 고른 상태를 못 찍었다");

            // ③ 상세 패널 — 값이 모자란 노드(효과에 수치 · 할인 조건 · 모자람 한 줄이 다 있다)
            var rich = MemoryAltarCatalog.Get(MemoryAltarCatalog.SigilMerchant);
            if (nodes != null && rich != null && nodes.ContainsKey(rich.Id))
            {
                tree.Focus(rich, select: true);
                await UniTask.Delay(450, ignoreTimeScale: true);
                await ShotAsync("AltarUx2_3_Detail", detail);
                sb.AppendLine($"\n③ 상세 패널 — {rich.DisplayName}");
                DumpDetail(sb, detail);
            }

            // ④ 해금 배너 — 띄우기만(해금 · 저장 없음). 창이 닫히면 배너 연출도 함께 끊긴다.
            var show = typeof(UI_AwakeningPanel).GetMethod("ShowUnlockBannerAsync", Inst);
            var bannerNode = tree.FocusedNode;
            if (show != null && bannerNode != null)
            {
                show.Invoke(panel, new object[] { bannerNode, panel.GetCancellationTokenOnDestroy() });
                await UniTask.Delay(600, ignoreTimeScale: true);
                await ShotAsync("AltarUx2_4_Banner", null);
                sb.AppendLine($"\n④ 해금 배너 — {bannerNode.DisplayName}");
            }
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            if (panel != null) panel.ClosePopupUI();
            if (forced) { data.unlockedIds = keepUnlocked; data.records = keepRecords; data.abyssEssence = keepEssence; }
            Invoke("RestoreTestHubGui");
            File.WriteAllText("Temp/altar_ux2_probe.txt", sb.ToString());
            Debug.Log($"{Tag} 끝 → Temp/altar_ux2_probe.txt · {ShotDir}/AltarUx2_*.png (세이브 무변경)\n" + sb);
        }
    }

    // ── 10-14 해금 확인 카드 · 길잡이 · 업적 화면 ─────────────────

    /// <summary>
    /// 10-14 사용자 「클릭하면 바로 해금된다 · 해금 장소가 잘 보이게 · 업적과 해금이 같은 팝업이라 보기 힘들다」 실측.
    /// ① 살 수 있는 노드를 실제 클릭 순서(누름 = 고름 → 뗌 = 클릭)로 한 번 — 사지 않고 고르기만 ② 0.5초 뒤 다시 — 확인 카드(사지 않음) · 사진
    /// ③ ESC — 카드만 닫힘(제단 그대로) ④ 상세 [해금] → 카드 → [취소] — 정수 그대로 ⑤ 거점 길잡이 줄에 「기억의 제단」 · 사진
    /// ⑥ 업적 화면(ESC 메뉴 길) — 탭 없이 제목 「업적」 · 사진. 해금 · 저장은 하지 않는다(메모리 값만 바꾸고 되돌림).
    /// 결과: Temp/altar_confirm_probe.txt · Temp/ui_shots/AltarConfirm_*.png · 로그 「[AltarUx2] 확인 카드 끝」.
    /// </summary>
    [MenuItem("RelicFairy/Debug/10-14 기억의 제단 확인 카드 · 길잡이 · 업적 화면 (거점, 플레이 중)")]
    private static void RunConfirm()
    {
        if (!EditorApplication.isPlaying || !InBaseCamp() || BackendGameData.Instance?.Data == null)
        { Debug.LogWarning($"{Tag} 거점 플레이 중(로그인 뒤)에 실행해야 한다."); return; }
        RunConfirmAsync().Forget();
    }

    private static async UniTaskVoid RunConfirmAsync()
    {
        var sb = new StringBuilder("기억의 제단 확인 카드 · 길잡이 · 업적 화면(10-14)\n");
        var data = BackendGameData.Instance.Data;
        string keepUnlocked = data.unlockedIds, keepRecords = data.records;
        int keepEssence = data.abyssEssence;
        bool forced = false;
        int pass = 0, total = 0;
        void Check(string what, bool ok) { total++; if (ok) pass++; sb.AppendLine($"CHECK {what}: {ok}"); }
        UI_AwakeningPanel panel = null;
        Invoke("HideTestHubGui");
        try
        {
            Directory.CreateDirectory(ShotDir);
            Managers.UI.CloseAllPopupUI();
            await UniTask.Delay(250, ignoreTimeScale: true);
            panel = await OpenAsync(sb);
            if (panel == null) return;
            float g0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - g0 < 6f && !SaveGuardsDone(data)) await UniTask.Delay(200, ignoreTimeScale: true);
            await UniTask.Delay(600, ignoreTimeScale: true);
            panel.ClosePopupUI(); panel = null;
            await UniTask.Delay(300, ignoreTimeScale: true);
            if (!SaveGuardsDone(data)) { sb.AppendLine("중단 — 환급 · 시기 고리 기록이 남아 있다"); return; }

            keepUnlocked = data.unlockedIds; keepRecords = data.records; keepEssence = data.abyssEssence;
            forced = true;
            data.unlockedIds  = string.Join(",", MidUnlocked);
            data.abyssEssence = MidEssence;
            data.records      = StripProgress(data.records);

            panel = await OpenAsync(sb);
            if (panel == null) return;
            var tree    = Get<AltarTreeView>(panel, "_tree");
            var confirm = Get<AltarConfirmCard>(panel, "_confirm");
            var nodes   = Get<Dictionary<string, AltarTreeNodeView>>(tree, "_nodes");
            if (tree == null || confirm == null || nodes == null) { sb.AppendLine("트리 · 확인 카드를 못 찾았다"); return; }
            await UniTask.Delay(900, ignoreTimeScale: true);

            AltarTreeNodeView v = null;
            // 10-14 강화형 — 일부만 올린 칸이 있으면 그것(카드가 「강화하기 — Lv 1 → 2」)
            foreach (var n in nodes.Values)
                if (n.gameObject.activeInHierarchy && n.Visual == AltarNodeVisual.Affordable && n.Node.IsLeveled && MemoryAltarService.Level(n.Node.Id) > 0) { v = n; break; }
            if (v == null) foreach (var n in nodes.Values)
                if (n.gameObject.activeInHierarchy && n.Visual == AltarNodeVisual.Affordable && n.Node != tree.FocusedNode) { v = n; break; }
            if (v == null) foreach (var n in nodes.Values) if (n.Visual == AltarNodeVisual.Affordable) { v = n; break; }
            if (v == null) { sb.AppendLine("살 수 있는 노드가 없다"); return; }
            string id = v.Node.Id;
            int ess0 = data.abyssEssence;
            int lv0  = MemoryAltarService.Level(id);   // 10-14 — 「안 삼」 = 단계가 그대로
            var focused = typeof(AltarTreeView).GetMethod("OnNodeFocused", Inst);
            var clicked = typeof(AltarTreeView).GetMethod("OnNodeClicked", Inst);
            sb.AppendLine($"대상 노드 「{v.Node.DisplayName}」 · 정수 {ess0}");

            // ① 실제 클릭 순서 — 누르는 순간 고른 물체가 바뀌고(OnNodeFocused) 떼면 onClick(OnNodeClicked)
            focused.Invoke(tree, new object[] { v });
            clicked.Invoke(tree, new object[] { v });
            await UniTask.Delay(200, ignoreTimeScale: true);
            Check("① 한 번 클릭 = 고르기만(안 삼 · 카드 안 뜸)", tree.FocusedNode == v.Node && MemoryAltarService.Level(id) == lv0 && !confirm.IsOpen && data.abyssEssence == ess0);

            // ② 고른 노드를 따로 한 번 더 — 확인 카드(아직 안 삼)
            await UniTask.Delay(400, ignoreTimeScale: true);
            clicked.Invoke(tree, new object[] { v });
            await UniTask.Delay(350, ignoreTimeScale: true);
            Check("② 다시 누름 = 확인 카드(아직 안 삼)", confirm.IsOpen && MemoryAltarService.Level(id) == lv0 && data.abyssEssence == ess0);
            var link = panel.transform.Find("Panel_Main/Btn_ViewLink/Label")?.GetComponent<TMP_Text>();
            Check($"해금 화면 머리 — 탭 대신 건너가는 버튼(「{link?.text}」)", link != null && link.gameObject.activeInHierarchy && link.text.StartsWith("업적"));
            await ShotAsync("AltarConfirm_1_Card", null);
            DumpConfirm(sb, confirm);

            // ③ ESC = 카드만 닫힘
            panel.ClosePopupUI();
            await UniTask.Delay(300, ignoreTimeScale: true);
            Check("③ ESC — 카드만 닫히고 제단은 그대로", !confirm.IsOpen && panel != null && panel.gameObject.activeInHierarchy && MemoryAltarService.Level(id) == lv0);

            // ④ 상세 [해금] → 카드 → [취소]
            typeof(UI_AwakeningPanel).GetMethod("OnDetailAction", Inst)?.Invoke(panel, null);
            await UniTask.Delay(300, ignoreTimeScale: true);
            bool opened = confirm.IsOpen;
            typeof(AltarConfirmCard).GetMethod("OnCancel", Inst)?.Invoke(confirm, null);
            await UniTask.Delay(300, ignoreTimeScale: true);
            Check("④ 상세 [해금] → 카드 → [취소] — 정수 그대로", opened && !confirm.IsOpen && MemoryAltarService.Level(id) == lv0 && data.abyssEssence == ess0);

            panel.ClosePopupUI(); panel = null;
            await UniTask.Delay(1300, ignoreTimeScale: true);

            // ⑤ 거점 길잡이 — 정수 1000이면 열 수 있는 것이 있다
            var guide = Object.FindFirstObjectByType<BaseCampObjectiveGuide>();
            var tracker = guide != null ? Get<TMP_Text>(guide, "_tracker") : null;
            sb.AppendLine($"\n⑤ 길잡이 — {(guide == null ? "이 씬엔 길잡이가 없다" : $"줄 「{tracker?.text?.Replace("\n", " / ")}」")}");
            if (guide != null) Check("⑤ 길잡이 줄에 기억의 제단", tracker != null && tracker.text.Contains("기억의 제단"));
            await ShotAsync("AltarConfirm_2_Guide", null);

            // ⑥ 업적 화면(ESC 메뉴 길)
            UI_AwakeningPanel.OpenAchievementsAsync(readOnlyView: true).Forget();
            float a0 = Time.realtimeSinceStartup;
            while (panel == null && Time.realtimeSinceStartup - a0 < 5f) { await UniTask.Delay(100, ignoreTimeScale: true); panel = Object.FindFirstObjectByType<UI_AwakeningPanel>(); }
            await UniTask.Delay(900, ignoreTimeScale: true);
            if (panel != null)
            {
                var title = panel.transform.Find("Panel_Main/Txt_Title")?.GetComponent<TMP_Text>();
                var tabA  = panel.transform.Find("Panel_Main/Tab_Achievements");
                var tabU  = panel.transform.Find("Panel_Main/Tab_Unlocks");
                var rootA = panel.transform.Find("Panel_Main/Root_Achievements");
                Check($"⑥ 업적 화면 — 제목 「{title?.text}」 · 탭 없음 · 업적 목록", title != null && title.text == "업적"
                      && (tabA == null || !tabA.gameObject.activeSelf) && (tabU == null || !tabU.gameObject.activeSelf)
                      && rootA != null && rootA.gameObject.activeInHierarchy);
                await ShotAsync("AltarConfirm_3_Achievements", null);
            }
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            if (panel != null) panel.ClosePopupUI();
            if (forced) { data.unlockedIds = keepUnlocked; data.records = keepRecords; data.abyssEssence = keepEssence; }
            Invoke("RestoreTestHubGui");
            sb.AppendLine($"CHECK 합계 {pass}/{total}");
            File.WriteAllText("Temp/altar_confirm_probe.txt", sb.ToString());
            Debug.Log($"{Tag} 확인 카드 끝 → Temp/altar_confirm_probe.txt (세이브 무변경)\n" + sb);
        }
    }

    /// <summary>확인 카드 글 — 크기(루트 캔버스 단위 = 1080 화면 px) · 칸 넘침(줄 상자).</summary>
    private static void DumpConfirm(StringBuilder sb, AltarConfirmCard card)
    {
        var canvas = card.GetComponentInParent<Canvas>()?.rootCanvas;
        float cs = canvas != null ? canvas.transform.lossyScale.x : 1f;
        int over = 0; float min = 999f;
        foreach (var t in card.GetComponentsInChildren<TMP_Text>(true))
        {
            if (!t.gameObject.activeInHierarchy || string.IsNullOrWhiteSpace(t.text)) continue;
            float eff = t.fontSize * t.transform.lossyScale.x / Mathf.Max(0.0001f, cs);
            min = Mathf.Min(min, eff);
            t.ForceMeshUpdate();
            var r = t.rectTransform.rect; var b = t.textBounds;
            bool o = b.size.x > r.width + 2f || b.size.y > r.height + 4f;
            if (o) over++;
            sb.AppendLine($"  {t.name} 「{t.text.Replace("\n", " ")}」 {eff:0.0}px{(o ? " ⚠ 넘침" : "")}");
        }
        sb.AppendLine($"CHECK 확인 카드 글자 최소 16(지금 {min:0.0}) · 넘침 0(지금 {over}): {min >= 16f && over == 0}");
    }

    // ── 열기 · 가드 ─────────────────────────────────────────

    private static bool InBaseCamp()
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).name.StartsWith("BaseCamp")) return true;
        return false;
    }

    /// <summary>제단 상호작용(F)과 같은 길로 연다 — 거점에 제단이 없으면 팝업을 바로 띄운다.</summary>
    private static async UniTask<UI_AwakeningPanel> OpenAsync(StringBuilder sb)
    {
        var altar = Object.FindFirstObjectByType<WorldAwakeningAltar>();
        var open = altar != null ? typeof(WorldAwakeningAltar).GetMethod("OpenAsync", Inst) : null;
        if (open != null) open.Invoke(altar, new object[] { CancellationToken.None });
        else Managers.UI.ShowPopupUIAndGetAsync<UI_AwakeningPanel>().Forget();

        UI_AwakeningPanel panel = null;
        float t0 = Time.realtimeSinceStartup;
        while (panel == null && Time.realtimeSinceStartup - t0 < 5f)
        {
            await UniTask.Delay(100, ignoreTimeScale: true);
            panel = Object.FindFirstObjectByType<UI_AwakeningPanel>();
        }
        if (panel == null) { sb.AppendLine("5초 안에 제단 창이 안 떴다"); return null; }
        sb.AppendLine("창 열기 — " + (open != null ? "거점 제단 상호작용 경로" : "팝업 직접(제단 없음)"));
        // 받아갈 업적이 있으면 업적 탭으로 열린다 — 해금 탭으로 돌린다
        typeof(UI_AwakeningPanel).GetMethod("SetMode", Inst)?.Invoke(panel, new object[] { false });
        return panel;
    }

    /// <summary>창이 여는 순간 저장을 부를 일(옛 각성 환급 · 뺀 노드 환급 · 새 시기 고리)이 남지 않았나.</summary>
    private static bool SaveGuardsDone(UserGameData data)
    {
        if (data.GetRecord(MemoryAltarCatalog.Rec.RemovedNodesRefunded) <= 0) return false;
        if (data.GetRecord(MemoryAltarCatalog.Rec.LevelsReset1014) <= 0) return false;   // 10-14 강화형 전환 리셋도 끝나 있어야
        if (data.GetRecord(MemoryAltarCatalog.Rec.AwakeningRefunded) > 0) return true;
        foreach (var cat in AwakeningCategory.All)
            if (data.GetAwakeningLevel(cat) > 0) return false;
        return true;
    }

    /// <summary>진척 기록(할인 조건)만 뺀다 — 환급 · 고리 같은 가드 기록은 남긴다.</summary>
    private static string StripProgress(string records)
    {
        if (string.IsNullOrEmpty(records)) return records;
        var drop = new HashSet<string>(MemoryAltarCatalog.Rec.All);
        var keep = new List<string>();
        foreach (var kv in records.Split(','))
        {
            int c = kv.IndexOf(':');
            if (c > 0 && drop.Contains(kv.Substring(0, c))) continue;
            keep.Add(kv);
        }
        return string.Join(",", keep);
    }

    /// <summary>
    /// 10-18 사용자 「노드를 알아볼 수 있게 키우고 마우스 클릭 이동으로 넓게」 — ⑤ 끌기(트리 내용 자리가 옮겨지나) ·
    /// ⑥ 휠로 끝까지 물러나기(트리 전체가 창에 드나). 끝나면 확대 · 위치를 연 그대로 돌린다.
    /// </summary>
    private static async UniTask DragZoomAsync(StringBuilder sb, AltarTreeView tree)
    {
        var es = UnityEngine.EventSystems.EventSystem.current;
        var content = Get<RectTransform>(tree, "_content");
        sb.AppendLine();
        if (es == null || content == null) { sb.AppendLine("⑤ 이벤트 시스템 · 트리 내용을 못 찾았다"); return; }
        object keepPan = tree.GetType().GetField("_pan", Inst)?.GetValue(tree);
        float keepZoom = GetF(tree, "_zoom");

        Vector2 before = content.anchoredPosition;
        var drag = new UnityEngine.EventSystems.PointerEventData(es) { delta = new Vector2(-360f, 200f) };
        tree.OnBeginDrag(drag); tree.OnDrag(drag); tree.OnEndDrag(drag);
        await UniTask.Delay(300, ignoreTimeScale: true);
        Vector2 after = content.anchoredPosition;
        sb.AppendLine($"⑤ 끌기 —내용 자리 {before} → {after} · 트리 배율 {GetF(tree, "_fit") * GetF(tree, "_zoom"):0.00}(전체 맞춤 {GetF(tree, "_fitAll"):0.00})");
        sb.AppendLine($"CHECK {(after - before).sqrMagnitude > 100f} — ⑤ 끌면 트리가 움직인다");
        await ShotAsync("AltarUx2_5_Dragged", null);

        var wheel = new UnityEngine.EventSystems.PointerEventData(es) { scrollDelta = new Vector2(0f, -1f) };
        for (int i = 0; i < 16; i++) tree.OnScroll(wheel);
        await UniTask.Delay(300, ignoreTimeScale: true);
        float s = GetF(tree, "_fit") * GetF(tree, "_zoom");
        sb.AppendLine($"⑥ 휠로 물러남 — 트리 배율 {s:0.00} · 전체 맞춤 {GetF(tree, "_fitAll"):0.00}");
        sb.AppendLine($"CHECK {Mathf.Abs(s - Mathf.Min(GetF(tree, "_fitAll"), 0.8f * GetF(tree, "_fit"))) < 0.02f} — ⑥ 휠로 트리 전체가 드는 배율까지 물러난다");
        await ShotAsync("AltarUx2_6_ZoomOut", null);

        tree.GetType().GetField("_zoom", Inst)?.SetValue(tree, keepZoom);
        if (keepPan != null) tree.GetType().GetField("_pan", Inst)?.SetValue(tree, keepPan);
        tree.GetType().GetMethod("ApplyTransform", Inst)?.Invoke(tree, null);
    }

    private static AltarTreeNodeView PickDim(AltarTreeView tree, Dictionary<string, AltarTreeNodeView> nodes)
    {
        if (nodes == null) return null;
        foreach (var want in new[] { AltarNodeVisual.Open, AltarNodeVisual.Next, AltarNodeVisual.OpenLocked })
            foreach (var v in nodes.Values)
                if (v.gameObject.activeInHierarchy && v.Visual == want && v.Node != tree.FocusedNode) return v;
        return null;
    }

    // ── 덤프 ───────────────────────────────────────────────

    private static void DumpTree(StringBuilder sb, AltarTreeView tree)
    {
        float fit = GetF(tree, "_fit"), zoom = GetF(tree, "_zoom");
        var canvas = tree.GetComponentInParent<Canvas>();
        float sf = canvas != null ? canvas.rootCanvas.scaleFactor : 1f;
        float px = fit * zoom * sf;   // 트리 한 단위가 화면에서 몇 픽셀인가
        sb.AppendLine($"화면 {Screen.width}×{Screen.height} · 캔버스 배율 {sf:0.000} · 트리 맞춤 {fit:0.000} × 확대 {zoom:0.00}");

        var nodes = Get<Dictionary<string, AltarTreeNodeView>>(tree, "_nodes");
        if (nodes == null) { sb.AppendLine("노드 없음"); return; }
        var count = new Dictionary<AltarNodeVisual, int>();
        int clear = 0, dim = 0, chips = 0, emphBad = 0, goldBad = 0, satBad = 0, bold = 0;
        var fonts = new HashSet<string>();
        foreach (var kv in nodes)
        {
            var v = kv.Value;
            if (!v.gameObject.activeInHierarchy) continue;
            count[v.Visual] = count.TryGetValue(v.Visual, out var n) ? n + 1 : 1;
            if (v.Visual == AltarNodeVisual.Hidden) continue;

            var label = v.Label; var chip = Get<TMP_Text>(v, "_chip");
            var rim = Get<Image>(v, "_rim"); var mark = Get<Image>(v, "_mark"); var bas = Get<Image>(v, "_base");
            bool focused = tree.FocusedNode == v.Node;
            float want = v.Visual == AltarNodeVisual.Bought || v.Visual == AltarNodeVisual.Affordable || focused ? 1f : 0.45f;
            if (Mathf.Abs(label.color.a - want) > 0.05f) emphBad++;
            if (label.color.a > 0.95f) clear++; else dim++;
            bool chipOn = chip != null && chip.gameObject.activeSelf && chip.text.Length > 0;
            if (chipOn) chips++;
            fonts.Add(label.font != null ? label.font.name : "?");
            if (chipOn) fonts.Add(chip.font != null ? chip.font.name : "?");
            if ((label.fontStyle & FontStyles.Bold) != 0 || (chipOn && (chip.fontStyle & FontStyles.Bold) != 0)) bold++;

            bool gold = rim != null && Near(rim.color, UITheme.Gold);
            bool goldOk = v.Visual == AltarNodeVisual.Bought || v.Visual == AltarNodeVisual.Affordable;
            if (gold != goldOk) goldBad++;
            float markS = Sat(mark.color), baseS = Sat(bas.color);
            if (v.Visual != AltarNodeVisual.Bought && markS > 0.31f) satBad++;
            float rimPx = RimPx(rim, v.Diameter);
            sb.AppendLine($"  {v.Node.DisplayName,-12} {v.Visual,-10} 이름표 a {label.color.a:0.00} {label.fontSize:0} · " +
                          $"칩 {(chipOn ? $"\"{chip.text}\" a {chip.alpha:0.00}" : "없음")} · 테 {rimPx:0.0}(화면 {rimPx * px:0.0}px){(gold ? " 금" : "")} · " +
                          $"판 채도 {baseS:0.00} · 표식 채도 {markS:0.00}");
        }
        var states = new StringBuilder();
        foreach (var kv in count) states.Append($"{kv.Key} {kv.Value} · ");
        sb.AppendLine($"상태: {states}");
        sb.AppendLine($"이름표 또렷 {clear} · 흐림 {dim} · 값 칩 {chips} · 글꼴 [{string.Join(", ", fonts)}] · 가짜 굵게 {bold}");
        sb.AppendLine($"검사 — 또렷함 규칙 위반 {emphBad} · 금 테 규칙 위반 {goldBad} · 산 것 아닌 표식 채도 0.30 초과 {satBad} (모두 0이어야)");
        DumpEdges(sb, tree, "선");
        DumpLabelOverlap(sb, tree);
    }

    private static void DumpEdges(StringBuilder sb, AltarTreeView tree, string title)
    {
        if (!(tree.GetType().GetField("_edges", Inst)?.GetValue(tree) is IList edges)) return;
        var hist = new SortedDictionary<string, int>();
        foreach (var e in edges)
        {
            var line = e.GetType().GetField("Line").GetValue(e) as UIPolylineGraphic;
            if (line == null || !line.gameObject.activeInHierarchy) continue;
            string key = $"{line.Width:0.0}px a {line.color.a:0.00}";
            hist[key] = hist.TryGetValue(key, out var n) ? n + 1 : 1;
        }
        var s = new StringBuilder();
        foreach (var kv in hist) s.Append($"{kv.Key} ×{kv.Value} · ");
        sb.AppendLine($"{title}: {s}(산 길 2px a 0.90 · 열린 길 1px a 0.35 · 한 칸 앞 1px a 0.18 · 가려짐 1px a 0.10)");
    }

    private static void DumpLabelOverlap(StringBuilder sb, AltarTreeView tree)
    {
        var rects = new List<(string, Rect)>();
        foreach (var t in tree.GetComponentsInChildren<TMP_Text>(false))
        {
            if (t.transform.parent == null || t.transform.parent.name != "Labels" || string.IsNullOrEmpty(t.text)) continue;
            var b = t.textBounds;
            var c = t.rectTransform.TransformPoint(b.center);
            var ext = Vector3.Scale(b.extents, t.rectTransform.lossyScale);
            rects.Add((t.text, new Rect(c.x - ext.x, c.y - ext.y, ext.x * 2f, ext.y * 2f)));
        }
        int hits = 0;
        for (int i = 0; i < rects.Count; i++)
        for (int j = i + 1; j < rects.Count; j++)
            if (rects[i].Item2.Overlaps(rects[j].Item2)) { hits++; sb.AppendLine($"  이름표 겹침: {rects[i].Item1} ↔ {rects[j].Item1}"); }
        sb.AppendLine($"이름표 {rects.Count}개 · 겹침 {hits}건");
    }

    private static void DumpDetail(StringBuilder sb, AltarDetailPanel detail)
    {
        foreach (var name in new[] { "_header", "_name", "_badge", "_effect", "_cost", "_condition", "_reason", "_buttonLabel", "_hint" })
        {
            var t = Get<TMP_Text>(detail, name);
            if (t == null || !t.gameObject.activeInHierarchy || string.IsNullOrEmpty(t.text)) continue;
            sb.AppendLine($"  {name,-12} {(t.font != null ? t.font.name : "?")} {t.fontSize:0.#}{((t.fontStyle & FontStyles.Bold) != 0 ? " Bold" : "")} " +
                          $"#{ColorUtility.ToHtmlStringRGBA(t.color)} · \"{t.GetParsedText()}\"");
        }
        var effect = Get<TMP_Text>(detail, "_effect");
        if (effect != null)
        {
            bool tagged = effect.text.Contains("<font");
            bool broken = (bool)(typeof(AltarDetailPanel).GetField("s_mediumBroken", StaticNonPub)?.GetValue(null) ?? false);
            sb.AppendLine($"  효과 수치 500: {(tagged ? "적용" : "없음")} · 태그가 글자로 찍힘 {broken} · 금색 {Near(effect.color, UITheme.Gold)}(false여야)");
        }
    }

    /// <summary>계속 움직이는 것 — 0.6초 사이 제단 문양 · 열쇠 고리 회전 · 초점 고리 크기가 바뀌었나, 숨쉬는 후광은 몇 개.</summary>
    private static async UniTask DumpMotionAsync(StringBuilder sb, AltarTreeView tree)
    {
        var nodes = Get<Dictionary<string, AltarTreeNodeView>>(tree, "_nodes");
        var center = Get<Image>(tree, "_centerGlyph");
        var before = new List<float>();
        void Sample(List<float> into)
        {
            into.Add(center != null ? center.rectTransform.localEulerAngles.z : 0f);
            foreach (var v in nodes.Values)
            {
                into.Add(Get<Image>(v, "_glyph").rectTransform.localEulerAngles.z);
                into.Add(Get<Image>(v, "_focus").rectTransform.localScale.x);
            }
        }
        Sample(before);
        await UniTask.Delay(600, ignoreTimeScale: true);
        var after = new List<float>();
        Sample(after);
        int moved = 0;
        for (int i = 0; i < before.Count; i++) if (Mathf.Abs(before[i] - after[i]) > 0.001f) moved++;
        int glows = 0;
        foreach (var v in nodes.Values) { var g = Get<Image>(v, "_glow"); if (g != null && g.enabled && v.gameObject.activeInHierarchy) glows++; }
        var mote = typeof(AltarTreeView).GetField("MoteInterval", StaticNonPub);
        sb.AppendLine($"움직임 — 회전 · 맥동하는 것 {moved}(0이어야) · 숨쉬는 후광 {glows}(= 살 수 있음 노드 수) · 빛 알갱이 간격 {mote?.GetRawConstantValue()}초");
    }

    // ── 촬영 ───────────────────────────────────────────────

    /// <summary>화면 전체를 찍고, <paramref name="crop"/>이 있으면 그 판만 잘라 「_panel」로 한 장 더.</summary>
    private static async UniTask ShotAsync(string name, Component crop)
    {
        var runner = Object.FindFirstObjectByType<Managers>();
        Texture2D tex = null;
        for (int attempt = 0; attempt < 20; attempt++)
        {
            if (runner != null) await UniTask.WaitForEndOfFrame(runner); else await UniTask.Yield();
            tex = ScreenCapture.CaptureScreenshotAsTexture();
            if (tex == null) continue;
            if (!IsBlack(tex) || attempt == 19) break;
            Object.Destroy(tex); tex = null;
        }
        if (tex == null) return;
        File.WriteAllBytes($"{ShotDir}/{name}.png", tex.EncodeToPNG());
        if (crop != null && crop.transform is RectTransform rt)
        {
            var corners = new Vector3[4];
            rt.GetWorldCorners(corners);
            var canvas = rt.GetComponentInParent<Canvas>()?.rootCanvas;
            Camera cam = canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            Vector2 a = RectTransformUtility.WorldToScreenPoint(cam, corners[0]);
            Vector2 b = RectTransformUtility.WorldToScreenPoint(cam, corners[2]);
            float sx = tex.width / (float)Mathf.Max(1, Screen.width), sy = tex.height / (float)Mathf.Max(1, Screen.height);
            int x0 = Mathf.Clamp(Mathf.FloorToInt(a.x * sx), 0, tex.width - 1), y0 = Mathf.Clamp(Mathf.FloorToInt(a.y * sy), 0, tex.height - 1);
            int x1 = Mathf.Clamp(Mathf.CeilToInt(b.x * sx), x0 + 1, tex.width), y1 = Mathf.Clamp(Mathf.CeilToInt(b.y * sy), y0 + 1, tex.height);
            var part = new Texture2D(x1 - x0, y1 - y0, TextureFormat.RGBA32, false);
            part.SetPixels(tex.GetPixels(x0, y0, x1 - x0, y1 - y0));
            part.Apply();
            File.WriteAllBytes($"{ShotDir}/{name}_panel.png", part.EncodeToPNG());
            Object.Destroy(part);
        }
        Object.Destroy(tex);
    }

    private static bool IsBlack(Texture2D tex)
    {
        for (int y = 0; y < tex.height; y += Mathf.Max(1, tex.height / 12))
            for (int x = 0; x < tex.width; x += Mathf.Max(1, tex.width / 16))
                if (tex.GetPixel(x, y).maxColorComponent > 0.02f) return false;
        return true;
    }

    // ── 작은 도우미 ─────────────────────────────────────────

    private static T Get<T>(object o, string field) where T : class => o?.GetType().GetField(field, Inst)?.GetValue(o) as T;

    private static float GetF(object o, string field) => o?.GetType().GetField(field, Inst)?.GetValue(o) is float f ? f : 0f;

    private static bool Near(Color a, Color b) => Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) < 0.03f;

    private static float Sat(Color c) { Color.RGBToHSV(c, out _, out float s, out _); return s; }

    /// <summary>테 굵기(트리 단위) — 코드 고리 스프라이트 이름(Procedural/ring_{지름 비}_{크기})에서 읽는다.</summary>
    private static float RimPx(Image rim, float diameter)
    {
        if (rim == null || rim.sprite == null) return 0f;
        var parts = rim.sprite.name.Split('_');
        return parts.Length >= 3 && float.TryParse(parts[1], System.Globalization.NumberStyles.Float,
                                                   System.Globalization.CultureInfo.InvariantCulture, out float t01) ? t01 * diameter : 0f;
    }

    private static void Invoke(string method)
        => typeof(UILayoutRuntimeProbeEditor).GetMethod(method, StaticNonPub)?.Invoke(null, null);
}
#endif
