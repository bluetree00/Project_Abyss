#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 10-18 빌드 피드백 ⑪ ⑫ 쉼터 실측 — 테스트 허브 런에서 쉼터 방 두 개(Ch1 · Ch3 풀)를 지어 들어가 [F] 선택 창을 찍고 고른다.
/// ① 방마다 [F] 상호작용 수(1이어야) · 반경 · 모닥불 · 모루가 반경 안인지 · 머리 위 글
/// ② A방: 모루 옆에 서서(사진 11 자리) 체력 가득 → [F] 창(강화 / 닫기) · 체력 40% → [F] 창(체력 회복 / 무기 강화) →
///    ESC = 아무것도 안 바뀜 → 다시 [F] → 왼쪽 「무기 강화 +1」 = 강화 +1 · 체력 그대로 · 다시 [F]는 안 열림
/// ③ B방: 모닥불 옆 · 체력 40% → [F] → 오른쪽 「체력 회복 N%」 = 체력 + · 강화 그대로
/// ④ 문 이름표 2행(쉼터) · 상점 구매 버튼 글.
/// 결과 Temp/fb1018_rest.txt · Temp/ui_shots/FB18_rest_*.png · 로그 「[FB18rest] 끝」. CHECK 줄이 전부 True면 합격.
/// ⚠️ 지금 런의 체력 · 무기 강화가 실제로 바뀐다(테스트 허브 런에서만).
/// </summary>
public static class Fb1018RestProbeEditor
{
    private const string Tag     = "[FB18rest]";
    private const string OutTxt  = "Temp/fb1018_rest.txt";
    private const string ShotDir = "Temp/ui_shots";
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    [MenuItem("RelicFairy/Debug/10-18 쉼터 선택 창 촬영 (테스트 허브, 플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning($"{Tag} 플레이 중(테스트 허브)에만 동작한다."); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("10-18 쉼터 선택 창 실측\n");
        Directory.CreateDirectory(ShotDir);
        bool prevLocal = EditorPrefs.GetBool(ZoneLayoutManager.PreferLocalPoolPrefsKey, false);
        EditorPrefs.SetBool(ZoneLayoutManager.PreferLocalPoolPrefsKey, true);   // 쉼터 방은 로컬 방 풀에서(RestRoomProbeEditor와 같은 길)
        try
        {
            var launcher = UnityEngine.Object.FindFirstObjectByType<TestHubLauncher>();
            if (launcher != null && GameRunBootstrapper.Instance?.Run?.Player == null)
            {
                launcher.SelectChapter(1);
                typeof(TestHubLauncher).GetField("_bossApproach", Any)?.SetValue(launcher, false);
                launcher.TryLaunch();
            }
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 60f && GameRunBootstrapper.Instance?.Run?.Player == null)
                await UniTask.Delay(500, DelayType.Realtime);
            var run = GameRunBootstrapper.Instance?.Run;
            if (run == null) { sb.AppendLine("런이 시작되지 않음"); return; }
            await UniTask.Delay(3000, DelayType.Realtime);
            Hub("HideTestHubGui");

            var ensure = typeof(ServiceRoomTourProbeEditor).GetMethod("EnsureProcRunAsync", BindingFlags.Static | BindingFlags.NonPublic);
            if (ensure != null) await (UniTask<bool>)ensure.Invoke(null, null);
            if (RunFlowController.Active == null) { sb.AppendLine("절차 진행 없음(대기방 게이트를 못 지났다)"); return; }

            await RoomAsync(run, 1, forge: true, sb);
            await RoomAsync(run, 3, forge: false, sb);

            sb.AppendLine("[4 글]");
            sb.AppendLine($"  문 이름표 2행(쉼터) 「{typeof(RunFlowController).GetMethod("KindSubtitle", Any)?.Invoke(null, new object[] { RoomPlanKind.Rest })}」");
            sb.AppendLine($"  상점 구매 버튼 「{typeof(UI_ShopPanel).GetField("BuyLabel", Any)?.GetValue(null)}」");
        }
        catch (Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            UI_ConfirmDialog.CloseIfOpen();
            EditorPrefs.SetBool(ZoneLayoutManager.PreferLocalPoolPrefsKey, prevLocal);
            Hub("RestoreTestHubGui");
            File.WriteAllText(OutTxt, sb.ToString());
            Debug.Log($"{Tag} 끝\n{sb}");
        }
    }

    // ── 쉼터 방 하나 ──
    private static async UniTask RoomAsync(GameRunSession run, int ch, bool forge, StringBuilder sb)
    {
        string tag = forge ? "A" : "B";
        sb.AppendLine($"[{tag} Ch{ch} 쉼터 — {(forge ? "ESC · 무기 강화" : "체력 회복")}]");
        var pool  = await Managers.ZoneLayout.LoadPoolAsync($"CHAPTER_{ch}_ROOM_POOL");
        var entry = pool?.Find(p => string.Equals(p.category, "Rest", StringComparison.OrdinalIgnoreCase));
        if (entry == null) { sb.AppendLine("  쉼터 방이 풀에 없음"); return; }

        var flow  = RunFlowController.Active;
        var enter = typeof(RunFlowController).GetMethod("EnterRoomAsync", Any);
        var cur   = typeof(RunFlowController).GetField("_current", Any);
        var plan  = new DoorPlan { kind = RoomPlanKind.Rest, entry = entry };
        var task  = (UniTask)enter.Invoke(flow, new object[] { plan, DoorEdge.North, CancellationToken.None, 0 });
        await UniTask.WhenAny(task, UniTask.Delay(20000, ignoreTimeScale: true));
        await UniTask.Delay(2200, ignoreTimeScale: true);

        var result = cur.GetValue(flow);
        var room   = result?.GetType().GetField("roomGO")?.GetValue(result) as GameObject;
        var ctrl   = room != null ? room.GetComponent<RestRoomController>() : null;
        if (ctrl == null) { sb.AppendLine($"  {entry.pool_key} — RestRoomController 없음"); return; }

        // ① [F] 수 · 반경
        var its   = room.GetComponentsInChildren<ShopNpcInteraction>(true);
        var fire  = room.transform.Find("RestFire");
        var anvil = room.transform.Find("RestAnvil");
        var it    = its.Length > 0 ? its[0] : null;
        var col   = it != null ? it.GetComponent<SphereCollider>() : null;
        float r   = col != null ? col.radius * MaxAbs(col.transform.lossyScale) : 0f;
        Vector3 c = col != null ? col.transform.position : Vector3.zero;
        float dFire  = fire  != null ? Flat(fire.position  - c) : -1f;
        float dAnvil = anvil != null ? Flat(anvil.position - c) : -1f;
        sb.AppendLine($"  {entry.pool_key} · [F] {its.Length}개 · 반경 {r:0.00} m · 가운데→모닥불 {dFire:0.00} · →모루 {dAnvil:0.00} · 모닥불↔모루 {(fire != null && anvil != null ? Flat(fire.position - anvil.position) : -1f):0.00} m");
        sb.AppendLine($"  머리 위 글 「{Get(it, "promptText")?.ToString().Replace("\n", " / ")}」");
        sb.AppendLine($"  CHECK [F] 하나 · 모닥불 · 모루 옆(1.2 m)이 반경 안: {its.Length == 1 && dFire >= 0f && dAnvil >= 0f && dFire + 1.2f <= r && dAnvil + 1.2f <= r}");

        var stats = run.Player.RuntimeStats;
        var w     = run.Player.WeaponManager?.Weapon0Data;
        int Lv() => w != null ? w.enhanceLevel : -1;

        // 사진 11 자리 — 모루(강화) · 모닥불(회복) 바깥쪽 옆 1.2 m
        Transform near = forge ? anvil : fire, other = forge ? fire : anvil;
        if (near != null)
        {
            Vector3 away = other != null ? near.position - other.position : Vector3.back;
            away.y = 0f;
            MovePlayer(run.Player, near.position + away.normalized * 1.2f);
        }
        await UniTask.Delay(900, ignoreTimeScale: true);
        sb.AppendLine($"  플레이어 범위 안 {Get(it, "_playerInRange")}");

        if (forge)
        {
            // 체력 가득 — 강화 / 닫기 두 버튼
            stats.SetHp(stats.MaxHp);
            await OpenAsync(ctrl);
            sb.AppendLine($"  체력 가득 [F] 창: {DialogText()}");
            await ShotAsync($"FB18_rest_{tag}_0_hpfull");
            UI_ConfirmDialog.CloseIfOpen();
            await UniTask.Delay(400, ignoreTimeScale: true);
        }

        stats.SetHp(Mathf.RoundToInt(stats.MaxHp * 0.4f));
        await ShotAsync($"FB18_rest_{tag}_1_prompt");
        await OpenAsync(ctrl);
        sb.AppendLine($"  체력 40% [F] 창: {DialogText()}");
        await ShotAsync($"FB18_rest_{tag}_2_dialog");

        int hp0 = stats.Hp, lv0 = Lv();
        if (forge)
        {
            UI_ConfirmDialog.CloseIfOpen();   // ESC와 같은 길
            await UniTask.Delay(400, ignoreTimeScale: true);
            bool escOk = stats.Hp == hp0 && Lv() == lv0 && !(bool)Get(ctrl, "_chosen") && !UI_ConfirmDialog.IsOpen;
            sb.AppendLine($"  CHECK ESC = 아무것도 안 바뀜(체력 {hp0}→{stats.Hp} · 강화 {lv0}→{Lv()}): {escOk}");

            await OpenAsync(ctrl);
            Click("Card/Cancel");             // 왼쪽 「무기 강화 +1」
        }
        else Click("Card/Confirm");           // 오른쪽 「체력 회복 N%」
        await UniTask.Delay(700, ignoreTimeScale: true);
        bool augment = UI_MeleeAugmentPopup.Current != null || MeleeAugmentDraft.Pending != null;
        await ShotAsync($"FB18_rest_{tag}_3_chosen");
        bool chosen = (bool)Get(ctrl, "_chosen");
        if (forge)
            sb.AppendLine($"  CHECK 왼쪽 = 강화 +1 · 체력 그대로(강화 {lv0}→{Lv()} · 체력 {hp0}→{stats.Hp} · 증강 창 {augment}): {Lv() == lv0 + 1 && stats.Hp == hp0 && chosen && !UI_ConfirmDialog.IsOpen}");
        else
            sb.AppendLine($"  CHECK 오른쪽 = 체력 회복 · 강화 그대로(체력 {hp0}→{stats.Hp}/{stats.MaxHp} · 강화 {lv0}→{Lv()}): {stats.Hp > hp0 && Lv() == lv0 && chosen && !UI_ConfirmDialog.IsOpen}");

        if (augment)
        {
            Managers.UI.CloseAllPopupUI();
            if (MeleeAugmentDraft.Pending != null) MeleeAugmentDraft.AutoChoose();
            await UniTask.Delay(400, ignoreTimeScale: true);
        }

        // 하나만 — 다시 [F]는 창을 열지 않고, 다른 쪽을 직접 불러도 안 바뀐다
        int hp1 = stats.Hp, lv1 = Lv();
        typeof(RestRoomController).GetMethod("OpenChoice", Any)?.Invoke(ctrl, null);
        await UniTask.Delay(300, ignoreTimeScale: true);
        bool reopened = UI_ConfirmDialog.IsOpen;
        UI_ConfirmDialog.CloseIfOpen();
        typeof(RestRoomController).GetMethod(forge ? "Rest" : "Forge", Any)?.Invoke(ctrl, null);
        sb.AppendLine($"  CHECK 하나만 — 다시 [F] 창 안 열림 · 다른 쪽 막힘(체력 {hp1}→{stats.Hp} · 강화 {lv1}→{Lv()} · [F] 켜짐 {Get(it, "_enabled")}): {!reopened && stats.Hp == hp1 && Lv() == lv1}");
    }

    // ── 작은 도우미 ──
    private static async UniTask OpenAsync(RestRoomController ctrl)
    {
        typeof(RestRoomController).GetMethod("OpenChoice", Any)?.Invoke(ctrl, null);   // [F]와 같은 길
        await UniTask.Delay(650, ignoreTimeScale: true);                                // 열림 페이드 · 클릭 틈(0.25초) 지나게
    }

    private static string DialogText()
    {
        var d = UnityEngine.Object.FindFirstObjectByType<UI_ConfirmDialog>();
        if (d == null || !UI_ConfirmDialog.IsOpen) return "(안 열림)";
        string T(string p) => d.transform.Find(p)?.GetComponent<TMP_Text>()?.text?.Replace("\n", " / ") ?? "-";
        var sel = UnityEngine.EventSystems.EventSystem.current?.currentSelectedGameObject;
        return $"제목 「{T("Card/Title")}」 · 본문 「{T("Card/Body")}」 · 왼쪽 「{T("Card/Cancel/Label")}」 · 오른쪽 「{T("Card/Confirm/Label")}」 · 초점 {(sel != null ? sel.name : "없음")} · 시간 {Time.timeScale:0.##}";
    }

    private static void Click(string path)
    {
        var d = UnityEngine.Object.FindFirstObjectByType<UI_ConfirmDialog>();
        var b = d != null ? d.transform.Find(path)?.GetComponent<Button>() : null;
        if (b != null) b.onClick.Invoke();
    }

    private static void MovePlayer(PlayerController player, Vector3 pos)
    {
        if (Physics.Raycast(pos + Vector3.up * 3f, Vector3.down, out var hit, 8f, LayerMask.GetMask("Ground"), QueryTriggerInteraction.Ignore))
            pos.y = hit.point.y + 0.2f;
        player.transform.position = pos;
        if (player.TryGetComponent<Rigidbody>(out var rb)) { rb.position = pos; rb.linearVelocity = Vector3.zero; }
    }

    private static object Get(object o, string field) => o?.GetType().GetField(field, Any)?.GetValue(o);
    private static float Flat(Vector3 v) { v.y = 0f; return v.magnitude; }
    private static float MaxAbs(Vector3 v) => Mathf.Max(Mathf.Abs(v.x), Mathf.Max(Mathf.Abs(v.y), Mathf.Abs(v.z)));

    private static void Hub(string method)
        => typeof(UILayoutRuntimeProbeEditor).GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)?.Invoke(null, null);

    private static async UniTask ShotAsync(string name)
    {
        var runner = UnityEngine.Object.FindFirstObjectByType<Managers>();
        if (runner == null) return;
        await UniTask.WaitForEndOfFrame(runner);
        var tex = ScreenCapture.CaptureScreenshotAsTexture();
        if (tex == null) return;
        File.WriteAllBytes(Path.Combine(ShotDir, name + ".png"), tex.EncodeToPNG());
        UnityEngine.Object.Destroy(tex);
    }
}
#endif
