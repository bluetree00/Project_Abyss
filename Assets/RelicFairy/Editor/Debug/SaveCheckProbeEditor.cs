#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// [실측 · 플레이 중] 10-09 출시 점검 — 세이브 · 리소스 · UI 유지/초기화 수정 확인.
/// 테스트 허브에서 부른다. <b>빈 슬롯(2번)만 쓰고 끝에 비운다</b> — 사용자 슬롯(0 · 1)은 건드리지 않는다(2번에 진행이 있으면 멈춘다).
///  허브 단계: 오프라인 저장이 메타를 지우지 않음 · 손상 메타 → .bak 복구 · 대사 방문 · 사망 기록 슬롯별 · 런 종료 = 허브 기록 · 지워진 슬롯 복구
///  런 단계(첫 방): 상점 버프 · 룬 제거 · 정제 단계 · 골드 통계 저장 → 복원 · 새 플레이어에 룬 시너지 재적용 · ESC 「로비로 가기」 두 번 누름
/// 결과 Temp/save_check_probe.txt · 로그 「[SaveCheck] 끝」.
/// </summary>
public static class SaveCheckProbeEditor
{
    private const string AutoRoot = "RelicFairy/Debug/런 구조 자동 실측/";
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
    private const int TestSlot = 2;

    private static readonly StringBuilder s_sb = new();
    private static int  s_pass, s_fail;
    private static int  s_origSlot;
    private static bool s_armed;

    [MenuItem("RelicFairy/Debug/10-09 세이브 점검 (테스트 허브, 플레이 중)")]
    private static void Begin()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[SaveCheck] 플레이 모드에서만(테스트 허브)."); return; }
        HubPhaseAsync().Forget();
    }

    private static async UniTaskVoid HubPhaseAsync()
    {
        s_sb.Clear().AppendLine("10-09 세이브 점검");
        s_pass = s_fail = 0;
        var rpm = RunProgressManager.Instance;
        var backend = BackendGameData.Instance;
        if (rpm == null || backend == null) { Debug.LogWarning("[SaveCheck] 런 진행 · 메타 관리자가 없다"); return; }
        s_origSlot = rpm.ActiveSlotIndex;

        // 2번 슬롯이 비었는지(기록 없음 · 메타 없음/기본값) — 아니면 멈춘다
        var meta = typeof(RunProgressManager).GetField("_metaStore", Any)?.GetValue(rpm) as LocalFileMetaStore;
        var fresh = new UserGameData(); fresh.Reset(); SaveSanitizer.Sanitize(fresh);
        bool empty = !rpm.HasLocalRun(TestSlot) && meta != null
                     && (!meta.HasSave(TestSlot) || JsonUtility.ToJson(meta.Load(TestSlot)) == JsonUtility.ToJson(fresh));
        if (!empty) { Debug.LogWarning("[SaveCheck] 2번 슬롯에 진행이 있다 — 사용자 기록을 지우지 않으려고 멈춘다"); return; }

        try
        {
            rpm.ActiveSlotIndex = TestSlot;
            backend.ApplyActiveSlot();

            // ① 오프라인 · 서버 행 없음 저장 — 메모리 메타가 0이 되지 않는다
            var rowField  = typeof(BackendGameData).GetField("_rowInDate", Any);
            var needField = typeof(BackendGameData).GetField("_needsInsert", Any);
            string row = rowField?.GetValue(backend) as string;
            object need = needField?.GetValue(backend);
            rowField?.SetValue(backend, null);
            needField?.SetValue(backend, false);
            backend.Data.abyssEssence = 4321;
            await backend.SaveAsync();
            await backend.SaveAsync();   // 두 번 — 예전엔 첫 저장 뒤 0이 되고 두 번째가 0을 디스크에 썼다
            Check("오프라인 저장 — 메모리 정수 유지", backend.Data.abyssEssence == 4321, $"{backend.Data.abyssEssence}");
            Check("오프라인 저장 — 파일 정수 유지", meta.Load(TestSlot)?.abyssEssence == 4321, $"{meta.Load(TestSlot)?.abyssEssence}");
            rowField?.SetValue(backend, row);
            if (need != null) needField.SetValue(backend, need);

            // ② 손상된 본파일 → .bak 복구 · .corrupt 보관
            string main = SaveFileIO.PathFor($"meta_save_{TestSlot}.json");
            File.WriteAllText(main, "{ broken json");
            var restored = meta.Load(TestSlot);
            Check("손상 메타 → .bak 복구", restored != null && restored.abyssEssence == 4321, $"{restored?.abyssEssence}");
            Check("손상본 .corrupt 보관", File.Exists(Path.ChangeExtension(main, ".corrupt")), "");
            backend.InvalidateSlot(TestSlot);   // 메모리를 파일 기준으로

            // ③ 대사 방문 · 사망 기록 — 슬롯별 키
            Managers.DialogueData?.GetVisitLines("SaveProbe_Key");
            Check("대사 방문 — 2번 슬롯 키", PlayerPrefs.HasKey("dlgVisit_s2_SaveProbe_Key") && !PlayerPrefs.HasKey("dlgVisit_SaveProbe_Key"), "");
            int death0 = PlayerPrefs.GetInt("run_death_count", 0);
            RunReturnTracker.RecordRunEnd(false);
            Check("사망 수 — 2번 슬롯 키", PlayerPrefs.GetInt("run_death_count_s2", 0) == 1 && PlayerPrefs.GetInt("run_death_count", 0) == death0,
                  $"s2={PlayerPrefs.GetInt("run_death_count_s2", 0)} · 0번={PlayerPrefs.GetInt("run_death_count", 0)}(전 {death0})");
            RunReturnTracker.ConsumeReason(out _);

            // ④ 런 종료 = 허브 기록 · 지워진 슬롯 복구 · 프롤로그 전 슬롯은 지움
            PlayerPrefs.SetInt("game_intro_done_v1_slot" + TestSlot, 1);
            rpm.ClearLocalRun(TestSlot);
            var hub = rpm.LoadLocalRun(TestSlot);
            Check("런 종료 → 허브 기록", hub != null && hub.hasActiveRun && hub.isInStartRoom, "");
            var store = typeof(RunProgressManager).GetField("_localStore", Any)?.GetValue(rpm);
            store?.GetType().GetMethod("Delete")?.Invoke(store, new object[] { TestSlot });
            rpm.RepairHubRecord(TestSlot);
            Check("기록 없는 프롤로그 완료 슬롯 → 허브 기록 복구", rpm.LoadLocalRun(TestSlot)?.isInStartRoom == true, "");
            IntroCompletionTracker.ClearForSlot(TestSlot);
            rpm.ClearLocalRun(TestSlot);
            Check("프롤로그 전 슬롯 — 런 종료는 지움", !rpm.HasLocalRun(TestSlot), "");
        }
        catch (Exception e) { Check("허브 단계 예외 없음", false, e.Message); Debug.LogException(e); }

        // 런 단계 — 자동 실측으로 첫 방까지 간 뒤 멈추고 확인한다(2번 슬롯 그대로)
        s_armed = true;
        Application.logMessageReceived -= OnLog;
        Application.logMessageReceived += OnLog;
        EditorApplication.ExecuteMenuItem(AutoRoot + "시작 (테스트 허브에서, 플레이 중)");
    }

    private static void OnLog(string msg, string stack, LogType type)
    {
        if (!s_armed || !msg.StartsWith("[RunAuto]") || !msg.Contains("방 진입")) return;
        s_armed = false;
        Application.logMessageReceived -= OnLog;
        EditorApplication.delayCall += () =>
        {
            EditorApplication.ExecuteMenuItem(AutoRoot + "중지·기록");
            RunPhaseAsync().Forget();
        };
    }

    private static async UniTaskVoid RunPhaseAsync()
    {
        var rpm = RunProgressManager.Instance;
        try
        {
            await UniTask.Delay(2000, DelayType.Realtime);
            var run = GameRunBootstrapper.Instance?.Run;
            if (run == null || run.Player == null) { Check("런 세션 · 플레이어", false, "없음"); return; }

            // ⑤ 상점 버프 · 누진 단계 · 골드 통계 — 저장 → 읽기 → 복원
            run.BuffHandler.AddBuff(new StatModifier(StatType.MoveSpeed, 0.15f), 99, true, false, "MoveSpeed", 2);
            run.RuneExtractCount = 3;
            run.Refinery.RestoreCraftCount(4);
            run.RunDelta.GainedGold = 77;
            rpm.SaveRunLocal(run, default);
            var save = rpm.LoadLocalRun(TestSlot);
            Check("버프 저장", save != null && !string.IsNullOrEmpty(save.roomBuffsJson) && save.roomBuffsJson.Contains("MoveSpeed"), save?.roomBuffsJson);
            Check("누진 · 통계 저장", save != null && save.runeExtractCount == 3 && save.refineryCraftCount == 4 && save.runGoldGained == 77,
                  $"룬 {save?.runeExtractCount} · 정제 {save?.refineryCraftCount} · 골드 {save?.runGoldGained}");
            int before = run.BuffHandler.BuffCount;
            run.BuffHandler.ClearAll();
            typeof(GameRunSession).GetMethod("RestoreRoomBuffs", Any)?.Invoke(run, new object[] { save?.roomBuffsJson });
            var b = run.BuffHandler.BuffCount > 0 ? run.BuffHandler.ActiveBuffs[run.BuffHandler.BuffCount - 1] : null;
            Check("버프 복원", b != null && b.BuffType == "MoveSpeed" && b.RoomsRemaining == 99 && b.Tier == 2 && b.IsPercent,
                  $"{before} → {run.BuffHandler.BuffCount}");

            // ⑥ 룬 시너지 — 새 플레이어(빈 디스패처)에 다시 붙는가
            var bridge = MerlinRuneBridge.Instance;
            if (bridge != null)
            {
                bridge.OnZoneCellsUpdated(new Dictionary<string, int> { { "FIRE", 19 } });
                int a0 = run.Player.RuneEffects.Active.Count;
                typeof(PlayerController).GetField("_runeEffects", Any)?.SetValue(run.Player, null);   // 새 몸처럼 빈 디스패처
                int a1 = run.Player.RuneEffects.Active.Count;
                bridge.ReapplyToCurrentPlayer();
                int a2 = run.Player.RuneEffects.Active.Count;
                Check("룬 시너지 재적용", a0 > 0 && a1 == 0 && a2 == a0, $"적용 {a0} → 빈 몸 {a1} → 재적용 {a2}");
                bridge.OnZoneCellsUpdated(new Dictionary<string, int>());
            }
            else Check("룬 브리지", false, "없음");

            // ⑦ ESC 「로비로 가기」 — 런 중엔 한 번 더 눌러야
            var esc = UnityEngine.Object.FindFirstObjectByType<UI_EscMenu>(FindObjectsInactive.Include);
            if (esc != null)
            {
                esc.Open();
                string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                typeof(UI_EscMenu).GetMethod("OnLobby", Any)?.Invoke(esc, null);
                var label = typeof(UI_EscMenu).GetField("_lobbyLabel", Any)?.GetValue(esc) as TMPro.TMP_Text;
                bool stayed = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == scene && run.IsRunning;
                Check("ESC 로비 — 첫 누름은 확인만", stayed && label != null && label.text.Contains("한 번 더"), label?.text);
                esc.Close();
                Check("ESC 닫으면 원래 글", label != null && label.text == "로비로 가기", label?.text);
            }
            else Check("ESC 메뉴", false, "없음");
        }
        catch (Exception e) { Check("런 단계 예외 없음", false, e.Message); Debug.LogException(e); }
        finally
        {
            // 2번 슬롯 비우기 — 메타 · 런 · 퀘스트 · 대사 · 복귀 기록
            if (rpm != null)
            {
                rpm.ActiveSlotIndex = TestSlot;
                DialogueDataManager.ResetVisitCounts();
                RunReturnTracker.ResetAll();
                rpm.ResetSlot(TestSlot);
                IntroCompletionTracker.ClearForSlot(TestSlot);
                try { File.Delete(SaveFileIO.PathFor($"meta_save_{TestSlot}.corrupt")); } catch { }
                rpm.ActiveSlotIndex = s_origSlot;
                BackendGameData.Instance?.ApplyActiveSlot();
            }
            s_sb.AppendLine($"합계 통과 {s_pass} · 실패 {s_fail}");
            File.WriteAllText("Temp/save_check_probe.txt", s_sb.ToString());
            Debug.Log($"[SaveCheck] 끝 — 통과 {s_pass} · 실패 {s_fail} → Temp/save_check_probe.txt");
        }
    }

    private static void Check(string name, bool ok, string note)
    {
        if (ok) s_pass++; else s_fail++;
        s_sb.AppendLine($"{(ok ? "통과" : "실패")} — {name}{(string.IsNullOrEmpty(note) ? "" : " · " + note)}");
    }
}
#endif
