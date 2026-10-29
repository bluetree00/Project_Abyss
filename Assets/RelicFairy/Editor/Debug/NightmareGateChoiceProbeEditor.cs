#if UNITY_EDITOR
using System;
using System.IO;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 10-29 설계 §4 「악몽은 해방기 클리어 뒤 던전 입구에서 고른다」 확인 — 거점(Build/BaseCamp) · 오버라이드 「Ended (Nightmare Off)」로 플레이.
/// 옛 받침(~NightmareCrossroad)이 없는지 → 포탈 트리거에 들어서면 두 장 창 → 닫기(안 내려감 · 머무는 동안 다시 안 뜸) →
/// 나갔다 다시 들어서면 또 뜸 → 「악몽으로」 → 시기 · 내려감 시작. 로드아웃이 비면 시험용(가웨인 · 무형검 · 활 · 파츠)을 얹는다.
/// 결과: Temp/nightmare_gate_choice/ 사진 · view.txt · 로그 「[GateChoice] 끝」.
/// </summary>
public static class NightmareGateChoiceProbeEditor
{
    [MenuItem("RelicFairy/Debug/10-29 §4 악몽 입구 선택 확인 (거점, Play)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[GateChoice] 거점에서 플레이 중에"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        const string Dir = "Temp/nightmare_gate_choice";
        Directory.CreateDirectory(Dir);
        var sb = new StringBuilder($"악몽 입구 선택 — 시기 {StoryProgress.Era} · 엔딩 {StoryProgress.HasEnded} · 해금 {StoryProgress.IsNightmareModeUnlocked}\n");
        try
        {
            await UniTask.Delay(6000, DelayType.Realtime);
            // 엔딩 뒤 귀환 대사(시간 0)가 떠 있으면 트리거가 안 돈다 — 대사가 없고 시간이 흐를 때까지 넘긴다
            float calmSince = -1f;
            for (float until = Time.realtimeSinceStartup + 60f; Time.realtimeSinceStartup < until;)
            {
                bool talking = UnityEngine.Object.FindFirstObjectByType<UI_DialoguePopup>() != null;
                if (talking) { TestHubDebugMenu.AdvanceDialogue(); calmSince = -1f; }
                else if (Time.timeScale > 0f) { if (calmSince < 0f) calmSince = Time.realtimeSinceStartup; else if (Time.realtimeSinceStartup - calmSince > 2f) break; }
                await UniTask.Delay(300, DelayType.Realtime);
            }
            sb.AppendLine($"대사 넘김 뒤 시간 배율 {Time.timeScale:0.00}");
            sb.AppendLine($"옛 받침 ~NightmareCrossroad {(GameObject.Find("~NightmareCrossroad") != null ? "있음(✗)" : "없음(✓)")}");

            var loadout = AppBootstrapper.Instance?.Loadout;
            if (loadout != null)
            {
                if (loadout.Relic == null) loadout.SetRelic(AssetDatabase.LoadAssetAtPath<RelicClassSO>("Assets/RelicFairy/Systems/Relic/Data/RelicClass_Gawain.asset"));
                if (loadout.WeaponSlot0 == null) loadout.SetWeaponSlot0(AssetDatabase.LoadAssetAtPath<WeaponSO>("Assets/RelicFairy/Weapon/Nameless/Data/T0_Nameless.asset"));
                if (loadout.WeaponSlot1 == null) loadout.SetWeaponSlot1(AssetDatabase.LoadAssetAtPath<WeaponSO>("Assets/RelicFairy/Weapon/Bow/Data/T1_Bow.asset"));
                if (string.IsNullOrEmpty(loadout.StartPartId)) loadout.SetStartPart("probe_part");
                sb.AppendLine($"로드아웃 유물 {loadout.Relic?.name} · 근접 {loadout.WeaponSlot0?.name} · 원거리 {loadout.WeaponSlot1?.name} · 파츠 {loadout.StartPartId}");
            }
            var gate = UnityEngine.Object.FindFirstObjectByType<BaseCampDungeonGate>();
            var player = UnityEngine.Object.FindFirstObjectByType<PlayerController>();
            if (gate == null || player == null) { sb.AppendLine("게이트 · 플레이어 없음"); return; }
            var col = gate.GetComponent<Collider>();
            Vector3 inside = col.bounds.center;
            Vector3 away = player.transform.position - inside; away.y = 0f;
            Vector3 outside = inside + (away.sqrMagnitude > 0.01f ? away.normalized : Vector3.back) * (Mathf.Max(col.bounds.extents.x, col.bounds.extents.z) + 3f);
            sb.AppendLine($"트리거 {col.bounds.center} 크기 {col.bounds.size} · 바깥 자리 {outside}");

            // 1) 들어서면 창
            Place(player, outside); await UniTask.Delay(1000, DelayType.Realtime);
            Place(player, inside);  await UniTask.Delay(1200, DelayType.Realtime);
            sb.AppendLine($"1 들어섬 → 창 {NightmareGateChoice.IsOpen} · 시간 배율 {Time.timeScale:0.00}");
            ScreenCapture.CaptureScreenshot($"{Dir}/1_choice.png"); await UniTask.Delay(400, DelayType.Realtime);

            // 2) 닫기 → 안 내려감 · 머무는 동안 다시 안 뜸
            Click("Veil"); await UniTask.Delay(1500, DelayType.Realtime);
            sb.AppendLine($"2 닫음 → 창 {NightmareGateChoice.IsOpen} · 씬 {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name} · 시간 배율 {Time.timeScale:0.00}");
            await UniTask.Delay(1500, DelayType.Realtime);
            sb.AppendLine($"2b 그대로 머묾 1.5초 → 창 {NightmareGateChoice.IsOpen}(False여야)");

            // 3) 나갔다 다시 → 또 뜸
            Place(player, outside); await UniTask.Delay(1000, DelayType.Realtime);
            Place(player, inside);  await UniTask.Delay(1200, DelayType.Realtime);
            sb.AppendLine($"3 나갔다 다시 → 창 {NightmareGateChoice.IsOpen}");
            ScreenCapture.CaptureScreenshot($"{Dir}/2_choice_again.png"); await UniTask.Delay(400, DelayType.Realtime);

            // 4) 악몽으로 → 시기 · 물듦 · 내려감
            Click("Card_악몽으로 들어간다"); await UniTask.Delay(500, DelayType.Realtime);
            sb.AppendLine($"4 「악몽으로」 → 창 {NightmareGateChoice.IsOpen} · 시기 {StoryProgress.Era} · 악몽 {StoryProgress.IsNightmareMode}");
            ScreenCapture.CaptureScreenshot($"{Dir}/3_after_pick.png");
        }
        catch (Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            File.WriteAllText($"{Dir}/view.txt", sb.ToString(), Encoding.UTF8);
            Debug.Log($"[GateChoice] 끝\n{sb}");
        }
    }

    private static void Click(string name)
    {
        var root = GameObject.Find("@NightmareGateChoice");
        if (root == null) { Debug.LogWarning("[GateChoice] 창이 없다 — " + name); return; }
        foreach (var b in root.GetComponentsInChildren<Button>(true))
            if (b.name == name) { b.onClick.Invoke(); return; }
        Debug.LogWarning("[GateChoice] 버튼 없음 — " + name);
    }

    private static void Place(PlayerController p, Vector3 at)
    {
        if (Physics.Raycast(at + Vector3.up * 6f, Vector3.down, out var hit, 20f, LayerMask.GetMask("Ground"), QueryTriggerInteraction.Ignore)) at.y = hit.point.y + 0.05f;
        p.transform.position = at;
        if (p.TryGetComponent<Rigidbody>(out var rb)) { rb.position = at; rb.linearVelocity = Vector3.zero; }
    }
}
#endif
