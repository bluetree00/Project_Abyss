#if UNITY_EDITOR
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-14 정예 보스 번갈이 시드 확인(5회차 — 챕터마다 마스터 시드가 새로 뽑혀 「직전 챕터에 없었으면 확정」이 끊겼다).
/// ① 런 시드가 한 번 정해지면 그대로 · 이어하기 JSON 왕복에도 그대로 · 구 세이브(필드 없음)는 새로 정함
/// ② 런 시드 하나로 챕터 순번 0~5를 굴리면 「연속 없음」 0 · 챕터별 비율이 설계(50 · 75 · 62.5 · 68.8%)
/// ③ 옛 방식(챕터마다 다른 시드)은 연속 없음이 생긴다(대조). 결과: Temp/midboss_seed_probe.txt · 로그 「[MidBossSeed] 끝」. 플레이 없이 돈다.
/// </summary>
public static class MidBossSeedProbeEditor
{
    [MenuItem("RelicFairy/Debug/10-14 정예 보스 번갈이 시드 확인 (플레이 없이)")]
    private static void Run()
    {
        var sb = new StringBuilder("정예 보스 번갈이 시드 확인\n");
        int pass = 0, total = 0;
        void Check(string what, bool ok) { total++; if (ok) pass++; sb.AppendLine($"CHECK {what}: {ok}"); }

        // ①
        var s = new RunStats();
        int a = s.MidBossSeed, b = s.MidBossSeed;
        Check($"런 시드 한 번 정하면 그대로({a})", a != 0 && a == b);
        Check("이어하기 JSON 왕복에도 그대로", RunStats.FromJson(s.ToJson()).MidBossSeed == a);
        Check("구 세이브(필드 없음)는 0이 아닌 새 시드", RunStats.FromJson("{}").MidBossSeed != 0);

        // ② ③
        const int Runs = 4000, Chapters = 6;
        var hits = new int[Chapters];
        int gaps = 0, oldGaps = 0;
        var rng = new System.Random(1234);
        for (int r = 0; r < Runs; r++)
        {
            int seed = rng.Next() | 1;
            bool prev = true, oldPrev = true;
            for (int i = 0; i < Chapters; i++)
            {
                bool now = RunSequencer.MidBossScheduled(seed, i);
                if (now) hits[i]++;
                if (!now && !prev) gaps++;
                prev = now;
                bool oldNow = RunSequencer.MidBossScheduled(rng.Next() | 1, i);   // 옛 방식 — 챕터마다 새 시드
                if (!oldNow && !oldPrev) oldGaps++;
                oldPrev = oldNow;
            }
        }
        float[] design = { 50f, 75f, 62.5f, 68.75f, 65.6f, 67.2f };
        for (int i = 0; i < Chapters; i++)
        {
            float pct = hits[i] * 100f / Runs;
            sb.AppendLine($"  챕터 순번 {i}: {pct:0.0}% (설계 {design[i]:0.#}%)");
            Check($"순번 {i} 비율 설계 ±3%", Mathf.Abs(pct - design[i]) <= 3f);
        }
        Check($"런 시드 하나 — 연속 없음 0번(지금 {gaps})", gaps == 0);
        sb.AppendLine($"  (대조) 챕터마다 새 시드 — 연속 없음 {oldGaps}번 / {Runs}런");
        Check("(대조) 옛 방식은 연속 없음이 생긴다", oldGaps > 0);

        sb.AppendLine($"CHECK 합계 {pass}/{total}");
        File.WriteAllText("Temp/midboss_seed_probe.txt", sb.ToString());
        Debug.Log("[MidBossSeed] 끝\n" + sb);
    }
}
#endif
