#if UNITY_EDITOR
using System.IO;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-31 사용자 「허수아비는 누적 켜자 · 누적은 갱신해 줘야 해」 실측(거점, 플레이 중).
/// 허수아비를 0.3초마다 100씩 8번 → 매 타 뒤 누적 숫자의 목표 · 화면 글자 → 2초 쉼(끊김) → 3번 더(새로 세는가).
/// 결과 Temp/dummy_total1031.txt · 로그 「[DT31] 끝」.
/// </summary>
public static class DummyTotal1031ProbeEditor
{
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    [MenuItem("RelicFairy/Debug/10-31 허수아비 누적 숫자 갱신 실측 (거점, 플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[DT31] 플레이 중에만"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("10-31 허수아비 누적 숫자 갱신\n");
        GameObject spawned = null;
        try
        {
            var player = Object.FindFirstObjectByType<PlayerController>();
            var dummy = Object.FindFirstObjectByType<TrainingDummy>();
            if (dummy == null && player != null)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/RelicFairy/Characters/Monster/TrainingDummy/TrainingDummy.prefab");
                if (prefab != null)
                {
                    spawned = Object.Instantiate(prefab, player.transform.position + player.transform.forward * 3f, Quaternion.identity);
                    await UniTask.Delay(500, ignoreTimeScale: true);
                    dummy = spawned.GetComponentInChildren<TrainingDummy>();
                    sb.AppendLine("거점에 허수아비가 없어 프리팹을 앞 3 m에 세웠다");
                }
            }
            if (dummy == null) { sb.AppendLine("허수아비 없음"); return; }
            float t0 = Time.time;
            await UniTask.Delay(500, ignoreTimeScale: true);
            sb.AppendLine($"게임 시간 — timeScale {Time.timeScale:0.00} · 실제 0.5초 동안 Time.time +{Time.time - t0:0.00}");
            for (int i = 0; i < 8; i++)
            {
                dummy.TakeDamage(100f, player != null ? player.gameObject : null, 0f, false);
                await UniTask.Delay(300, ignoreTimeScale: true);
                sb.AppendLine($"  {i + 1}타 뒤 — {Read()}");
            }
            await UniTask.Delay(2000, ignoreTimeScale: true);
            sb.AppendLine($"  2초 쉰 뒤 — {Read()}");
            for (int i = 0; i < 3; i++)
            {
                dummy.TakeDamage(100f, player != null ? player.gameObject : null, 0f, false);
                await UniTask.Delay(300, ignoreTimeScale: true);
                sb.AppendLine($"  다시 {i + 1}타 뒤 — {Read()}");
            }
            await UniTask.Delay(1500, ignoreTimeScale: true);
            sb.AppendLine($"  끝 1.5초 뒤 — {Read()}");
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            if (spawned != null) Object.Destroy(spawned);
            File.WriteAllText("Temp/dummy_total1031.txt", sb.ToString());
            Debug.Log("[DT31] 끝 → Temp/dummy_total1031.txt");
        }
    }

    /// <summary>떠 있는 누적 숫자(들) — 목표 · 보이는 값 · 글자.</summary>
    private static string Read()
    {
        var t = typeof(DamagePopup);
        var sb = new StringBuilder();
        int n = 0;
        foreach (var p in Object.FindObjectsByType<DamagePopup>(FindObjectsSortMode.None))
        {
            if (p == null || !p.gameObject.activeInHierarchy) continue;
            if (!(t.GetField("_isTotal", Inst)?.GetValue(p) is bool tot && tot)) continue;
            n++;
            var label = t.GetField("label", Inst)?.GetValue(p) as TMPro.TMP_Text;
            float alpha = (t.GetField("canvasGroup", Inst)?.GetValue(p) as CanvasGroup)?.alpha ?? -1f;
            sb.Append($"[목표 {t.GetField("_totalTarget", Inst)?.GetValue(p)} · 보임 {t.GetField("_totalShown", Inst)?.GetValue(p):0} · 글자 「{label?.text?.Replace("\n", " ")}」 · 불투명 {alpha:0.00}] ");
        }
        return n == 0 ? "누적 숫자 없음" : $"누적 {n}개 " + sb;
    }
}
#endif
