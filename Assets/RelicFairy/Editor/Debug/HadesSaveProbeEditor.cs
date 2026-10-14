#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// [실측 · 플레이 중] 10-10 Hades식 세이브 — 방 입장 저장 · 피해 잠금 · 잠금 중 저장 막힘 · ESC 게임 종료 확인 · 클리어 해제 ·
/// 보스 클리어 뒤 저장 → 다음 챕터 대기방 · 잠긴 세이브 이어하기 → 사망 정산.
/// 테스트 허브에서 부른다. <b>빈 슬롯(2번)만 쓰고 끝에 비운다</b>(2번에 진행이 있으면 멈춘다).
/// 결과 Temp/hades_save_probe.txt · 로그 「[HadesSave] 끝」.
/// </summary>
public static class HadesSaveProbeEditor
{
    private const string AutoRoot = "RelicFairy/Debug/런 구조 자동 실측/";
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
    private const int TestSlot = 2;

    private static readonly StringBuilder s_sb = new();
    private static int  s_pass, s_fail;
    private static int  s_origSlot;
    private static int  s_rooms;

    [MenuItem("RelicFairy/Debug/10-10 Hades식 세이브 (테스트 허브, 플레이 중)")]
    private static void Begin()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[HadesSave] 플레이 모드에서만(테스트 허브)."); return; }
        var rpm = RunProgressManager.Instance;
        if (rpm == null) { Debug.LogWarning("[HadesSave] 런 진행 관리자가 없다"); return; }
        if (rpm.HasLocalRun(TestSlot)) { Debug.LogWarning("[HadesSave] 2번 슬롯에 기록이 있다 — 지우지 않으려고 멈춘다"); return; }

        s_sb.Clear().AppendLine("10-10 Hades식 세이브");
        s_pass = s_fail = 0;
        s_origSlot = rpm.ActiveSlotIndex;
        rpm.ActiveSlotIndex = TestSlot;
        BackendGameData.Instance?.ApplyActiveSlot();

        // 자동 실측으로 두 번째 방까지 간 뒤 멈춘다(첫 방만이 아니라 방마다 저장하는지 보려고)
        s_rooms = 0;
        Application.logMessageReceived -= OnLog;
        Application.logMessageReceived += OnLog;
        EditorApplication.ExecuteMenuItem(AutoRoot + "시작 (테스트 허브에서, 플레이 중)");
    }

    private static void OnLog(string msg, string stack, LogType type)
    {
        // 「[RunAuto] 방 진입」은 전환 시작에 찍혀 입장 연출(드러내기 · 디졸브)이 끝나기 전이다 — 저장 로그를 센다
        if (!msg.StartsWith("[RunFlow] 방 입장 저장")) return;
        if (++s_rooms < 2) return;
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
            await UniTask.Delay(2500, DelayType.Realtime);
            var run  = GameRunBootstrapper.Instance?.Run;
            var flow = RunFlowController.Active;
            if (run?.Player == null || flow == null) { Check("런 · 절차 흐름", false, "없음"); return; }

            // ① 방 입장 저장 — 두 번째 방(챕터 첫 방이 아닌 방)도 저장한다
            var save = rpm.LoadLocalRun(TestSlot);
            Check("① 두 번째 방 입장 저장", save != null && save.visitCount >= 1 && !save.combatLocked && !save.chapterStartPending,
                  $"방문 {save?.visitCount} · 방 {save?.currentRoomPoolKey} · 잠금 {save?.combatLocked}");

            // ② 피해 → 잠금(메모리 · 파일)
            var p = run.Player;
            var invField = typeof(PlayerController).GetField("debugInvincible", Any);
            object inv = invField?.GetValue(p);
            invField?.SetValue(p, false);
            typeof(PlayerController).GetField("_invincibleEnd", Any)?.SetValue(p, 0f);
            p.TakeDamage(30);   // 방어 · 보호막에 다 깎여 0이 되지 않을 만큼
            if (inv != null) invField.SetValue(p, inv);
            save = rpm.LoadLocalRun(TestSlot);
            Check("② 피해 → 잠금", flow.IsCombatLocked && save != null && save.combatLocked, $"메모리 {flow.IsCombatLocked} · 파일 {save?.combatLocked}");

            // ③ 잠금 중 방 도중 저장(게임 종료 저장 포함)은 쓰지 않는다 — 잠금이 남아야 끄면 사망 정산
            string at = save?.savedAt;
            flow.SaveNow("app-quit");
            var after = rpm.LoadLocalRun(TestSlot);
            Check("③ 잠금 중 저장 막힘", after != null && after.savedAt == at && after.combatLocked, $"{at} → {after?.savedAt}");

            // ④ ESC 「게임 종료」 — 잠금 중 첫 누름은 확인만(두 번째는 누르지 않는다 — 플레이가 끝난다)
            var esc = UnityEngine.Object.FindFirstObjectByType<UI_EscMenu>(FindObjectsInactive.Include);
            if (esc != null)
            {
                esc.Open();
                typeof(UI_EscMenu).GetMethod("OnQuit", Any)?.Invoke(esc, null);
                var label = typeof(UI_EscMenu).GetField("_quitLabel", Any)?.GetValue(esc) as TMPro.TMP_Text;
                bool armed = Application.isPlaying && label != null && label.text.Contains("런 사망");
                esc.Close();
                Check("④ ESC 게임 종료 — 전투 중 첫 누름은 확인만", armed && label.text == "게임 종료", label?.text);
            }
            else Check("④ ESC 메뉴", false, "없음");

            // ⑤ 전투가 끝나면 잠금 해제(HandleRoomCleared가 부르는 것과 같은 메서드)
            typeof(RunFlowController).GetMethod("UnlockCombat", Any)?.Invoke(flow, null);
            save = rpm.LoadLocalRun(TestSlot);
            Check("⑤ 클리어 → 잠금 해제", !flow.IsCombatLocked && save != null && !save.combatLocked, $"메모리 {flow.IsCombatLocked} · 파일 {save?.combatLocked}");

            // ⑥ 보스 클리어 뒤 저장 → 이어하면 다음 챕터 대기방부터(여기선 같은 챕터 번호로 대기방 경로만 확인)
            flow.SaveChapterAdvance();
            save = rpm.LoadLocalRun(TestSlot);
            bool flag = save != null && save.chapterStartPending;
            bool branch = await RestoreAndWaitLog("다음 챕터 대기방부터 이어하기", 90f);
            float tw = Time.unscaledTime;   // 대기방 대사가 입력을 기다릴 수 있어 「대기방 진입」 로그 대신 플레이어가 서는지 본다
            while (GameRunBootstrapper.Instance?.Run?.Player == null && Time.unscaledTime - tw < 30f) await UniTask.Yield();
            bool stood = GameRunBootstrapper.Instance?.Run?.Player != null;
            Check("⑥ 보스 클리어 뒤 저장 → 대기방으로 이어하기", flag && branch && stood, $"표시 {flag} · 분기 {branch} · 플레이어 {stood}");

            // ⑥b 대기방 저장(10-11) — 서약 제단 선택 같은 대기방 저장이 「다음 챕터 대기방부터」 세이브를 지금 상태로 다시 쓴다
            string beforeAt = rpm.LoadLocalRun(TestSlot)?.savedAt;
            await UniTask.Delay(1100, DelayType.Realtime);   // savedAt이 초 단위로 달라지게
            RunFlowController.Active?.SaveNow("probe-altar");
            var wr = rpm.LoadLocalRun(TestSlot);
            Check("⑥b 대기방 저장 → 다음 챕터 대기방 세이브 갱신", wr != null && wr.chapterStartPending && wr.savedAt != beforeAt,
                  $"표시 {wr?.chapterStartPending} · {beforeAt} → {wr?.savedAt}");

            // ⑦ 잠긴 세이브 이어하기 → 사망 정산(세이브는 허브 기록으로) · 결과창
            rpm.SetCombatLock(true);
            bool settled = await RestoreAndWaitLog("잠긴 세이브(전투 중 종료)", 90f);
            float t0 = Time.unscaledTime;
            while (RunResultView.Active == null && Time.unscaledTime - t0 < 15f) await UniTask.Yield();
            var hub = rpm.LoadLocalRun(TestSlot);
            bool result = RunResultView.Active != null;
            bool noRun  = hub == null || hub.isInStartRoom;   // 이어할 런이 남지 않았다(프롤로그 미완 슬롯은 지워지고, 완료 슬롯은 허브 기록)
            Check("⑦ 잠긴 세이브 → 사망 정산 · 결과창", settled && result && noRun,
                  $"정산 로그 {settled} · 결과창 {result} · 남은 기록 {(hub == null ? "없음" : hub.isInStartRoom ? "허브" : "런")}");
            // 결과창은 닫지 않는다 — 닫으면 정산 마지막 단계(AppBootstrapper.EndRun)가 <b>그때의 현재 슬롯</b>을 정리하는데,
            // 아래 finally가 슬롯을 사용자 슬롯으로 되돌린 뒤라 사용자 런 세이브가 허브 기록으로 바뀐다. 실측 끝에 플레이를 멈춘다.
        }
        catch (Exception e) { Check("예외 없음", false, e.Message); Debug.LogException(e); }
        finally
        {
            if (rpm != null)
            {
                rpm.ActiveSlotIndex = TestSlot;
                rpm.ResetSlot(TestSlot);
                IntroCompletionTracker.ClearForSlot(TestSlot);
                rpm.ActiveSlotIndex = s_origSlot;
                BackendGameData.Instance?.ApplyActiveSlot();
            }
            s_sb.AppendLine($"합계 통과 {s_pass} · 실패 {s_fail}");
            File.WriteAllText("Temp/hades_save_probe.txt", s_sb.ToString());
            Debug.Log($"[HadesSave] 끝 — 통과 {s_pass} · 실패 {s_fail} → Temp/hades_save_probe.txt");
        }
    }

    /// <summary>로비 「이어하기」와 같은 길(RequestRestoreRun)로 다시 열고, 기대 로그가 나올 때까지 기다린다.</summary>
    private static async UniTask<bool> RestoreAndWaitLog(string needle, float timeout)
    {
        bool seen = false;
        void Hook(string m, string s, LogType t) { if (m.Contains(needle)) seen = true; }
        Application.logMessageReceived += Hook;
        try
        {
            AppBootstrapper.Instance?.RequestRestoreRun();
            float t0 = Time.unscaledTime;
            while (!seen && Time.unscaledTime - t0 < timeout) await UniTask.Yield();
            await UniTask.Delay(1500, DelayType.Realtime);
            return seen;
        }
        finally { Application.logMessageReceived -= Hook; }
    }

    private static void Check(string name, bool ok, string note)
    {
        if (ok) s_pass++; else s_fail++;
        s_sb.AppendLine($"{(ok ? "통과" : "실패")} — {name}{(string.IsNullOrEmpty(note) ? "" : " · " + note)}");
    }
}
#endif
