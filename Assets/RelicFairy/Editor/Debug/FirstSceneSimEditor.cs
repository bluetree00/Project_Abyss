#if UNITY_EDITOR
using System;
using System.Reflection;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// [전주기 시뮬 · 첫 씬] 빌드 첫 씬(Lobby)에서 <b>빈 슬롯</b>으로 「새 게임」 → 시작 영상 → 프롤로그(Game_Intro) → 거점 온보딩까지 기록한다(10-09 사용자 「전체 플레이 시뮬」).
/// 사용자 기록은 건드리지 않는다:
///  · 쓰는 슬롯은 런 세이브가 없고 메타가 없거나 새 게임 기본값 그대로인 빈 슬롯뿐(없으면 멈춤) — 끝나면 그 슬롯을 다시 비운다(ResetSlot).
///  · 대사 방문 횟수 · 귀환 기록은 슬롯별(10-09)이라 그 슬롯 것만 끝에 지운다.
/// 기록 = 전주기 시뮬 기록기(FullCycleSimRecorder) 구간 S0_first — Temp/sim/S0_first/(장면 · log.txt). 2.5초마다 한 장 · 대사는 자동으로 넘긴다.
/// 끝: 거점에 닿고 90초 뒤, 또는 7분. 로그 「[FirstScene] 끝」.
/// </summary>
public static class FirstSceneSimEditor
{
    private const string SkipDialogueMenu = "RelicFairy/Debug/서약 발동 실측/대사 전부 넘기기 (플레이 중)";
    private const BindingFlags Any = BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private const float ShotGap = 2.5f, SkipGap = 2f, MaxSeconds = 420f, BaseCampHold = 90f;

    [MenuItem("RelicFairy/Debug/전주기 시뮬/0 첫 씬 — 빈 슬롯 새 게임 → 프롤로그 → 거점 (Build/Lobby에서 플레이)")]
    private static void Begin()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[FirstScene] 플레이 모드에서만(Build/Lobby 씬)."); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var rec = typeof(FullCycleSimRecorder);
        void Write(string s) => rec.GetMethod("Write", Any)?.Invoke(null, new object[] { s });
        void Shot(string tag) => rec.GetMethod("Capture", Any)?.Invoke(null, new object[] { tag, 0 });

        var rpm = RunProgressManager.Instance;
        int origSlot = rpm != null ? rpm.ActiveSlotIndex : 0;
        int slot = -1;
        try
        {
            // 로비가 뜰 때까지
            float t0 = Time.realtimeSinceStartup;
            UI_SaveSlotPanel panel = null;
            while (Time.realtimeSinceStartup - t0 < 60f)
            {
                panel = UnityEngine.Object.FindFirstObjectByType<UI_SaveSlotPanel>(FindObjectsInactive.Include);
                if (panel != null && UnityEngine.Object.FindFirstObjectByType<UI_Lobby>() != null) break;
                await UniTask.Delay(500, DelayType.Realtime);
            }
            rpm = RunProgressManager.Instance;
            if (panel == null || rpm == null) { Debug.LogWarning("[FirstScene] 로비 · 슬롯 패널 없음"); return; }

            // 빈 슬롯(런 세이브 없음 · 메타 세이브가 없거나 새 게임 기본값 그대로) — 뒤 칸부터.
            // 기본값 메타 = 새 게임만 누르고 아무것도 안 한 칸(이전 시뮬이 남긴 칸 포함) — 지워도 잃는 것이 없다.
            var meta = typeof(RunProgressManager).GetField("_metaStore", Any)?.GetValue(rpm) as LocalFileMetaStore;
            var fresh = new UserGameData();
            fresh.Reset();
            SaveSanitizer.Sanitize(fresh);   // Load와 같은 정규화
            string freshJson = JsonUtility.ToJson(fresh);
            for (int s = RunProgressManager.SlotCount - 1; s >= 0 && slot < 0; s--)
                if (!rpm.HasLocalRun(s) && meta != null
                    && (!meta.HasSave(s) || JsonUtility.ToJson(meta.Load(s)) == freshJson)) slot = s;
            if (slot < 0) { Debug.LogWarning("[FirstScene] 빈 슬롯이 없다 — 사용자 기록을 지우지 않으려고 멈춘다"); return; }

            rec.GetMethod("Begin", Any)?.Invoke(null, new object[] { "S0_first" });
            Write($"── 첫 씬 구간 — 빈 슬롯 {slot} 새 게임 · 원래 슬롯 {origSlot}");
            Shot("lobby");
            await UniTask.Delay(1500, DelayType.Realtime);

            typeof(UI_SaveSlotPanel).GetMethod("OnSlotStartClicked", Any)?.Invoke(panel, new object[] { slot, false });
            Write("새 게임 클릭");

            string scene = SceneManager.GetActiveScene().name;
            float lastShot = 0f, lastSkip = 0f, baseCampAt = -1f, introAt = -1f;
            bool picked = false;
            t0 = Time.realtimeSinceStartup;
            while (true)
            {
                float t = Time.realtimeSinceStartup - t0;
                if (t > MaxSeconds) { Write("최대 시간"); break; }
                string now = SceneManager.GetActiveScene().name;
                if (now != scene) { Write($"씬 → {now}"); scene = now; Shot("scene " + now); }
                if (now == "BaseCamp" && baseCampAt < 0f) baseCampAt = t;
                if (baseCampAt >= 0f && t - baseCampAt > BaseCampHold) { Write("거점 90초"); break; }
                if (t - lastShot >= ShotGap) { lastShot = t; Shot($"t{t:000}"); }
                if (t - lastSkip >= SkipGap) { lastSkip = t; EditorApplication.ExecuteMenuItem(SkipDialogueMenu); }
                // 프롤로그 — 회랑 끝 검을 F로 집어야 모르드레드 장면이 열린다. 사람처럼 걸어가는 대신 검 앞으로 옮겨 집는다(인트로 25초 뒤 한 번).
                if (now == "Game_Intro" && !picked && t - introAt > 25f && introAt >= 0f) picked = TryPickSword(Write);
                if (now == "Game_Intro" && introAt < 0f) introAt = t;
                await UniTask.Delay(250, DelayType.Realtime);
            }
        }
        catch (Exception e) { Write("예외: " + e.Message); Debug.LogException(e); }
        finally
        {
            // 되돌리기 — 시뮬이 쓴 슬롯을 비운다. 대사 방문 · 복귀 기록은 10-09부터 슬롯별이라 그 슬롯 것만 지우면 된다.
            if (slot >= 0 && RunProgressManager.Instance != null)
            {
                RunProgressManager.Instance.ActiveSlotIndex = slot;
                DialogueDataManager.ResetVisitCounts();
                RunReturnTracker.ResetAll();
                RunProgressManager.Instance.ResetSlot(slot);
                IntroCompletionTracker.ClearForSlot(slot);
                RunProgressManager.Instance.ActiveSlotIndex = origSlot;
                BackendGameData.Instance?.ApplyActiveSlot();
            }
            Write($"── 되돌림: 슬롯 {slot} 비움");
            rec.GetMethod("End", Any)?.Invoke(null, new object[] { "first-scene" });
            Debug.Log($"[FirstScene] 끝 — 슬롯 {slot}");
        }
    }

    /// <summary>검 앞(1.2 m)으로 옮기고 집기 사건을 낸다 — F 입력과 같은 길(_consumed · OnPickup).</summary>
    private static bool TryPickSword(Action<string> write)
    {
        var pickup = UnityEngine.Object.FindFirstObjectByType<IntroSwordPickup>();
        var player = UnityEngine.Object.FindFirstObjectByType<PlayerController>();
        if (pickup == null || player == null) return false;
        var to = pickup.transform.position + (player.transform.position - pickup.transform.position).normalized * 1.2f;
        to.y = player.transform.position.y;
        if (player.TryGetComponent<CharacterController>(out var cc)) { cc.enabled = false; player.transform.position = to; cc.enabled = true; }
        else player.transform.position = to;
        typeof(IntroSwordPickup).GetField("_consumed", Any)?.SetValue(pickup, true);
        var ev = typeof(IntroSwordPickup).GetField("OnPickup", Any)?.GetValue(pickup) as Delegate;
        ev?.DynamicInvoke(player);
        write($"검 집기(도구) — {(ev != null ? "사건 냄" : "구독 없음")}");
        return true;
    }
}
#endif
