#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 11-02 UI 몫 실측(테스트 허브 런 중 · 시작 대기방) — 결과 Temp/ui1102.txt · 사진 Temp/ui_shots/U1102_*.png · 로그 「[U1102] 끝」.
///  · Q17 요정의 샘: 체력을 절반 · 포션을 0으로 두고 샘 곁으로 옮긴다 → 0.6초 뒤 체력 · 포션 · 「마셨다」 · 사진.
///  · Q16 서약 뒤 문 연출: 시작방 문 열림 연출을 다시 틀고 0.8초 뒤 「[F] 넘기기」 안내가 떴나 · 연출이 끝나면 걷혔나(F 입력은 흉내 못 냄 — 빌드에서 확인).
///  · Q20 시너지 꺼짐: 룬판을 열어 불 존을 1단계(6칸)까지 채운 뒤 하나를 판에서 빼 → 0.3초 뒤 알림 띠 글 · 사진.
/// </summary>
public static class Ui1102ProbeEditor
{
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private const BindingFlags SAny = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
    private static readonly StringBuilder s_sb = new();

    [MenuItem("RelicFairy/Debug/11-02 UI 몫 실측 — 샘 · 문 연출 넘기기 · 시너지 꺼짐 (테스트 허브, 플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[U1102] 플레이 중에만"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        s_sb.Clear().AppendLine("11-02 UI 몫 실측");
        Directory.CreateDirectory("Temp/ui_shots");
        try
        {
            await CloseStartGiftAsync();
            await SpringAsync();
            await GateSkipHintAsync();
            await SynergyOffAsync();
        }
        catch (System.Exception e) { s_sb.AppendLine("예외: " + e); }
        finally
        {
            File.WriteAllText("Temp/ui1102.txt", s_sb.ToString());
            Debug.Log("[U1102] 끝 → Temp/ui1102.txt");
        }
    }

    /// <summary>해방기 런이면 대기방 시작에 「해방의 선물」이 뜬다 — 「유물 기억 하나」를 눌러 첫 칸을 고르고 닫는다(이벤트 방 실측과 같은 길).</summary>
    private static async UniTask CloseStartGiftAsync()
    {
        for (float t = 0f; t < 3f && !StartGift.IsOpen; t += 0.2f) await UniTask.Delay(200, ignoreTimeScale: true);
        if (!StartGift.IsOpen) { s_sb.AppendLine("시작 선물 창 없음"); return; }
        foreach (var b in Object.FindObjectsByType<UnityEngine.UI.Button>(FindObjectsSortMode.None))
        {
            var label = b.GetComponentInChildren<TMP_Text>();
            if (label == null || !label.text.Contains("유물 기억")) continue;
            b.onClick.Invoke();
            break;
        }
        int drafts = 0;
        float quiet = 0f;
        for (float t = 0f; t < 15f && quiet < 3f; t += 0.25f)   // 「두 번 고른다」면 둘째 창이 2~3초 늦게 뜬다 — 3초 동안 안 뜰 때까지
        {
            await UniTask.Delay(250, ignoreTimeScale: true);
            var draft = Object.FindFirstObjectByType<UI_RelicPartDraftPopup>();
            if (draft == null) { quiet += StartGift.IsOpen ? 0f : 0.25f; continue; }
            quiet = 0f;
            await UniTask.Delay(800, ignoreTimeScale: true);
            typeof(UI_RelicPartDraftPopup).GetMethod("CloseGuide", Inst)?.Invoke(draft, null);
            typeof(UI_RelicPartDraftPopup).GetField("_selected", Inst)?.SetValue(draft, 0);
            typeof(UI_RelicPartDraftPopup).GetMethod("OnConfirmClicked", Inst)?.Invoke(draft, null);
            drafts++;
            await UniTask.Delay(500, ignoreTimeScale: true);
        }
        s_sb.AppendLine($"  고르기 창 {drafts}번 닫음");
        for (float t = 0f; t < 4f && (StartGift.IsOpen || UIInputGate.Blocked); t += 0.2f) await UniTask.Delay(200, ignoreTimeScale: true);
        s_sb.AppendLine($"시작 선물 — 유물 기억 고름 · 창 열림 {StartGift.IsOpen} · 입력 막힘 {UIInputGate.Blocked}");
    }

    private static async UniTask SpringAsync()
    {
        var run = GameRunBootstrapper.Instance?.Run;
        var p = run?.Player != null ? run.Player : Object.FindFirstObjectByType<PlayerController>();
        var spring = Object.FindFirstObjectByType<ChapterSpring>();
        bool made = false;
        if (spring == null && p != null)   // 샘은 앞 챕터를 마치고 다음 대기방에 들어올 때만 선다 — 테스트 허브 직행엔 없어 곁에 세운다
        {
            spring = ChapterSpring.SpawnAt(p.transform.position + p.transform.right * 8f, null);
            made = true;
            await UniTask.Delay(300, ignoreTimeScale: true);
        }
        if (spring == null || p == null || run == null) { s_sb.AppendLine($"Q17 샘 {(spring != null)} · 플레이어 {(p != null)} · 런 {(run != null)}"); return; }
        p.RuntimeStats.SetHp(Mathf.Max(1, p.RuntimeStats.MaxHp / 2));
        int potCap = run.PlayerState.PotionCapacity;
        while (run.PlayerState.PotionCount > 0 && run.PlayerState.TryConsumePotion()) { }
        int hp0 = p.RuntimeStats.Hp, pot0 = run.PlayerState.PotionCount;
        Vector3 far = spring.transform.position + Vector3.forward * 6f;
        Place(p, far);
        await UniTask.Delay(400, ignoreTimeScale: true);
        Place(p, spring.transform.position + Vector3.forward * 1.2f);
        await UniTask.Delay(600, ignoreTimeScale: true);
        bool used = (bool)(typeof(ChapterSpring).GetField("_used", Inst)?.GetValue(spring) ?? false);
        var label = typeof(ChapterSpring).GetField("_label", Inst)?.GetValue(spring) as TMP_Text;
        s_sb.AppendLine($"Q17 샘 — 다가감(1.2 m) 0.6초 뒤: 체력 {hp0} → {p.RuntimeStats.Hp}/{p.RuntimeStats.MaxHp} · 포션 {pot0} → {run.PlayerState.PotionCount}/{potCap} · 마심 {used} · 이름표 「{label?.text?.Replace("\n", " ")}」");
        ScreenCapture.CaptureScreenshot("Temp/ui_shots/U1102_spring.png");
        await UniTask.Delay(400, ignoreTimeScale: true);
        Place(p, far);
        await UniTask.Delay(300, ignoreTimeScale: true);
        Place(p, spring.transform.position + Vector3.forward * 1.2f);   // 다시 들어와도 두 번 마시지 않는다
        await UniTask.Delay(400, ignoreTimeScale: true);
        s_sb.AppendLine($"Q17 샘 — 다시 들어옴: 포션 {run.PlayerState.PotionCount}(그대로여야)");
        if (made && spring != null) Object.Destroy(spring.gameObject);
    }

    private static async UniTask GateSkipHintAsync()
    {
        var gate = Object.FindFirstObjectByType<StartRoomGate>();
        if (gate == null) { s_sb.AppendLine("Q16 시작방 문 없음"); return; }
        typeof(StartRoomGate).GetField("_gateRevealStarted", Inst)?.SetValue(gate, false);
        gate.TriggerGateReveal();
        await UniTask.Delay(800, ignoreTimeScale: true);
        var hint = GameObject.Find("GateRevealSkipHint");
        s_sb.AppendLine($"Q16 문 연출 0.8초 — 「[F] 넘기기」 안내 {(hint != null ? "있음 · 글 「" + hint.GetComponentInChildren<TMP_Text>()?.text + "」" : "없음")}");
        ScreenCapture.CaptureScreenshot("Temp/ui_shots/U1102_gate_hint.png");
        float t0 = Time.realtimeSinceStartup;
        while (GameObject.Find("GateRevealSkipHint") != null && Time.realtimeSinceStartup - t0 < 12f) await UniTask.Delay(200, ignoreTimeScale: true);
        s_sb.AppendLine($"Q16 연출 끝 — 안내 걷힘 {(GameObject.Find("GateRevealSkipHint") == null)} · 걸린 시간 {Time.realtimeSinceStartup - t0 + 0.8f:0.0}초(넘기지 않으면 약 6초)");
    }

    private static async UniTask SynergyOffAsync()
    {
        var inv = GameRunBootstrapper.Instance?.Run?.ItemInventory;
        if (inv == null) { s_sb.AppendLine("Q20 인벤토리 없음"); return; }
        if (UI_GridPanel.Instance == null) Managers.UI?.ShowOverlayUI<UI_GridPanel>();
        var panel = UI_GridPanel.Instance;
        if (panel == null) { s_sb.AppendLine("Q20 룬판 없음"); return; }
        panel.ShowWithNewItem(null);
        await UniTask.Delay(1200, ignoreTimeScale: true);
        var fill = typeof(RuneDense1030ProbeEditor).GetMethod("FillElementAsync", SAny);
        var added = new List<RuntimeItemData>();
        var placed = new List<RuntimeItemData>();
        if (fill == null) { s_sb.AppendLine("Q20 RD30 도우미 없음"); return; }
        await (UniTask)fill.Invoke(null, new object[] { panel, inv, "FIRE", added, placed, 8 });
        await UniTask.Delay(400, ignoreTimeScale: true);
        var bridge = MerlinRuneBridge.Instance;
        var evField = typeof(MerlinRuneBridge).GetField("OnSynergiesUpdated", Inst);
        var ev = evField?.GetValue(bridge) as System.Delegate;
        bool subscribed = false;
        if (ev != null) foreach (var d in ev.GetInvocationList()) if (ReferenceEquals(d.Target, panel)) subscribed = true;
        var tiers = typeof(UI_GridPanel).GetField("_zoneTiers", Inst)?.GetValue(panel) as Dictionary<string, int>;
        s_sb.AppendLine($"Q20 판 창 구독 {subscribed} · 판이 기억한 불 단계 {(tiers != null && tiers.TryGetValue("FIRE", out int ft) ? ft : -1)} · 열림 {panel.IsOpen}");
        int tierUp = bridge != null ? bridge.GetZoneTier("FIRE") : -1;
        s_sb.AppendLine($"Q20 불 존 채움 — 놓은 룬 {placed.Count} · 단계 {tierUp}");
        if (placed.Count == 0) return;
        var remove = typeof(UI_GridPanel).GetMethod("RemovePlacedItem", Inst);
        var toast = typeof(UI_GridPanel).GetField("_synergyToastText", Inst)?.GetValue(panel) as TMP_Text;
        string lastToast = "";
        for (int i = placed.Count - 1; i >= 0 && bridge != null && bridge.GetZoneTier("FIRE") >= tierUp && tierUp > 0; i--)
        {
            remove?.Invoke(panel, new object[] { placed[i] });
            await UniTask.Delay(300, ignoreTimeScale: true);
            lastToast = toast != null && toast.gameObject.activeInHierarchy ? toast.text : "(띠 없음)";
        }
        s_sb.AppendLine($"Q20 하나씩 뺌 → 단계 {bridge?.GetZoneTier("FIRE")} · 알림 띠 「{lastToast}」");
        ScreenCapture.CaptureScreenshot("Temp/ui_shots/U1102_synergy_off.png");
        await UniTask.Delay(500, ignoreTimeScale: true);
        // 되돌리기 — 판의 나머지 · 보관함에 넣은 룬 회수
        foreach (var d in placed) if (inv.IsPlaced(d.instanceId)) remove?.Invoke(panel, new object[] { d });
        typeof(RuneDense1030ProbeEditor).GetMethod("DiscardAdded", SAny)?.Invoke(null, new object[] { panel, inv, added });
        panel.Close();
    }

    private static void Place(PlayerController p, Vector3 at)
    {
        p.transform.position = at;
        if (p.TryGetComponent<Rigidbody>(out var rb)) { rb.position = at; rb.linearVelocity = Vector3.zero; }
    }
}
#endif
