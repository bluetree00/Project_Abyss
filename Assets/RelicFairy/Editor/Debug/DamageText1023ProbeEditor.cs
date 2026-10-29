#if UNITY_EDITOR
using System.IO;
using System.Reflection;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-23 데미지 숫자 정리 전후 촬영(사용자 「데미지 텍스트가 지저분하다 — 깔끔하고 강조되게」).
/// 플레이어 앞 네 자리에 전투와 같은 박자로 숫자를 띄운다 — 세 번 휘두름(일반 · 치명) · 다단 합산 · 룬 추가 피해(속성) · 화상 틱 · 정예 큰 한 방.
/// 같은 각본을 정리 전후에 돌려 같은 순간을 찍는다. 이름표 = Temp/dmg23_tag.txt(없으면 shot).
/// 결과: Temp/ui_shots/DMG23_{tag}_*.png · 로그 「[Dmg23] 끝」.
/// </summary>
public static class DamageText1023ProbeEditor
{
    private const int IdA = 900001, IdB = 900002, IdC = 900003, IdD = 900004;

    [MenuItem("RelicFairy/Debug/10-23 데미지 숫자 촬영 (플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[Dmg23] 플레이 모드에서"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        string tag = File.Exists("Temp/dmg23_tag.txt") ? File.ReadAllText("Temp/dmg23_tag.txt").Trim() : "shot";
        var hub = Object.FindFirstObjectByType<TestHubLauncher>();
        bool hubWas = hub != null && hub.enabled;
        if (hub != null) hub.enabled = false;   // 허브 OnGUI 판이 사진을 가린다
        try
        {
            var cam = Camera.main;
            if (cam == null) { Debug.LogWarning("[Dmg23] 카메라 없음"); return; }
            var player = Object.FindFirstObjectByType<PlayerController>();
            Vector3 fwd   = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
            Vector3 right = Vector3.ProjectOnPlane(cam.transform.right, Vector3.up).normalized;
            Vector3 root  = player != null ? player.transform.position : cam.transform.position + fwd * 8f;
            Vector3 up    = Vector3.up * 1.2f;   // MonsterBase와 같은 높이
            Vector3 a = root + fwd * 2.5f - right * 2.2f + up;
            Vector3 b = root + fwd * 2.8f + up;
            Vector3 c = root + fwd * 2.5f + right * 2.2f + up;
            Vector3 d = root + fwd * 5.5f + right * 0.6f + up;

            // 첫 숫자는 프리팹을 불러오느라 늦다 — 한 번 띄워 풀을 데운 뒤 사라질 때까지 기다린다.
            DamagePopupSpawner.Spawn(root + fwd * 30f, 1f);
            await Wait(1.6f);

            Scenario(a, b, c, d).Forget();
            await Wait(0.18f); await Shot($"DMG23_{tag}_1_first");
            await Wait(0.22f); await Shot($"DMG23_{tag}_2_second");
            await Wait(0.25f); await Shot($"DMG23_{tag}_3_finisher");
            await Wait(0.30f); await Shot($"DMG23_{tag}_4_settle");
            await Wait(1.6f);

            // 큰 무리 — 휩쓸기 한 번에 여섯이 맞는다(치명 둘 · 추가 피해 셋).
            for (int i = 0; i < 6; i++)
            {
                Vector3 p = root + fwd * (2.2f + (i % 2) * 1.6f) + right * (-2.5f + i * 1.0f) + up;
                int id = 900010 + i;
                bool crit = i == 1 || i == 4;
                DamagePopupSpawner.Spawn(p, crit ? 118f : 46f + i * 3f, crit, id);
                if (i % 2 == 0) DamagePopupSpawner.Spawn(p, 17f, false, id, DamageKind.Synergy, RuneElement.Electric);
            }
            await Wait(0.2f); await Shot($"DMG23_{tag}_5_crowd");
            await Wait(0.35f); await Shot($"DMG23_{tag}_6_crowd_late");
            await Wait(1.2f);
        }
        finally
        {
            if (hub != null) hub.enabled = hubWas;
            Debug.Log($"[Dmg23] 끝 — {tag}");
        }
    }

    /// <summary>세 번 휘두름 + 다단 + 추가 피해 + 화상 틱 + 정예 큰 한 방(약 0.9초).</summary>
    private static async UniTaskVoid Scenario(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        // 1타 — 셋이 맞는다 · 가운데는 치명 + 불 룬 추가 피해
        DamagePopupSpawner.Spawn(a, 42f, false, IdA);
        DamagePopupSpawner.Spawn(b, 96f, true,  IdB);
        DamagePopupSpawner.Spawn(b, 18f, false, IdB, DamageKind.Synergy, RuneElement.Fire);
        DamagePopupSpawner.Spawn(c, 45f, false, IdC);
        // 다단(분열 화살) — 오른쪽에 0.02초 간격 6발 → 하나로 합산
        for (int i = 0; i < 6; i++) { await Wait(0.02f); DamagePopupSpawner.Spawn(c, 12f, false, IdC); }
        await Wait(0.1f);
        // 2타
        DamagePopupSpawner.Spawn(a, 44f,  false, IdA);
        DamagePopupSpawner.Spawn(a, 6f,   false, IdA, DamageKind.Dot, RuneElement.Fire);
        DamagePopupSpawner.Spawn(b, 41f,  false, IdB);
        DamagePopupSpawner.Spawn(c, 102f, true,  IdC);
        DamagePopupSpawner.Spawn(d, 1240f, true, IdD);   // 정예에 큰 한 방(네 자리)
        await Wait(0.2f);
        // 3타 막타 — 치명 + 번개 추가 피해
        DamagePopupSpawner.Spawn(a, 128f, true,  IdA);
        DamagePopupSpawner.Spawn(a, 25f,  false, IdA, DamageKind.Synergy, RuneElement.Electric);
        DamagePopupSpawner.Spawn(b, 85f,  false, IdB);
        DamagePopupSpawner.Spawn(b, 25f,  false, IdB, DamageKind.Synergy, RuneElement.Electric);
        DamagePopupSpawner.Spawn(c, 90f,  false, IdC);
        DamagePopupSpawner.Spawn(d, 310f, false, IdD);
        await Wait(0.2f);
        DamagePopupSpawner.Spawn(a, 6f, false, IdA, DamageKind.Dot, RuneElement.Fire);
        DamagePopupSpawner.Spawn(b, 6f, false, IdB, DamageKind.Dot, RuneElement.Fire);
    }

    private static UniTask Wait(float seconds) => UniTask.Delay((int)(seconds * 1000), DelayType.DeltaTime);

    private static UniTask Shot(string name)
    {
        var m = typeof(UILayoutRuntimeProbeEditor).GetMethod("ShotAsync", BindingFlags.Static | BindingFlags.NonPublic);
        return m != null ? (UniTask)m.Invoke(null, new object[] { name, 0 }) : UniTask.CompletedTask;
    }
}
#endif
