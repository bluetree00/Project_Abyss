using System;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// [실측 도구 · 플레이 중] 사망할 때 <b>빈사 경고(붉은 후처리)가 꺼지는지</b> 본다.
///
/// 사망 뒤에도 플레이어 오브젝트는 남아 있어서(사망 연출 → 암전 → 멀린 부활), 체력 0을 그대로 넘기면
/// 경고가 최대 세기로 깔린 채 사망 비네트와 겹치고 부활 장면까지 붉게 남는다 → 09-21에 체력 0이면 접도록 고쳤다.
/// 이 도구는 체력을 낮춰 경고를 켠 뒤 죽이고, 경고 세기를 따라가며 실제로 0으로 내려가는지 기록한다.
/// ⚠️ 런이 끝난다(사망 연출 → 베이스캠프). 검증용 런에서만 쓴다. 결과: Temp/lowhp_death_probe.txt
/// </summary>
public static class LowHpDeathProbeEditor
{
    [MenuItem("RelicFairy/Debug/사망 시 빈사 경고 확인 (플레이 중)")]
    private static void Run()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[빈사사망] 플레이 모드에서만 동작한다."); return; }
        var p = GameRunBootstrapper.Instance?.Run?.Player;
        if (p == null) { Debug.LogWarning("[빈사사망] 런 플레이어가 없다."); return; }
        RunAsync(p).Forget();
    }

    private static async UniTaskVoid RunAsync(PlayerController p)
    {
        var sb = new StringBuilder();
        ProbeOutput.Begin("Temp/lowhp_death_probe.txt", "빈사사망");
        try
        {
            // 1) 빈사까지 낮춰 경고를 켠다.
            p.RuntimeStats.SetHp(Mathf.Max(1, Mathf.RoundToInt(p.RuntimeStats.MaxHp * 0.05f)));
            await UniTask.Delay(TimeSpan.FromSeconds(1.2f), ignoreTimeScale: true);
            sb.AppendLine($"체력 5% — 경고 세기 {LowHpVolumeService.CurrentIntensity:F3} (0보다 커야 한다)");

            // 2) 실제 사망 경로로 죽인다(TakeDamage → TryHandleDeath).
            p.TakeDamage(999999, null, true, HitWeight.Heavy);
            sb.AppendLine("사망 처리 요청");

            // 3) 사망 연출 동안 경고가 실제로 꺼지는지 따라간다.
            for (int i = 1; i <= 6; i++)
            {
                await UniTask.Delay(TimeSpan.FromSeconds(0.5f), ignoreTimeScale: true);
                sb.AppendLine($"  사망 +{i * 0.5f:F1}초 — 경고 세기 {LowHpVolumeService.CurrentIntensity:F3} · 체력 {p.RuntimeStats.Hp}");
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { sb.AppendLine("예외: " + e.Message); }

        string text = sb.ToString();
        ProbeOutput.Write("Temp/lowhp_death_probe.txt", "빈사사망", text);
        Debug.Log("[빈사사망] 결과\n" + text);
    }
}
