#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using RelicFairy.Monster;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-29 사용자 「화룡 메테오 구간 무적이 다른 패턴 · 행동 중에 켜져 버그처럼 보인다」 확인 — 화룡방 Fight Sim 중에 부른다.
/// 체력을 다음 소환 기준선 + 400으로 → 시뮬 화살이 기준선까지 깎는다. 프레임마다 상태 · 무적(IsDamageImmuneNow)을 읽어
/// 기준선 닿음 · 소환 시작 · 무적 켜짐 · 무적 꺼짐 시각과 「소환 전 무적이 켜진 적이 있나」를 적는다. 무적이 켜지고 3초 뒤 새끼 용을 모두 쓰러뜨린다.
/// 사진: 소환 시작 +0.4초(알림) · 무적 켜짐 +0.5초(체력 바 막힘) · 무적 꺼짐 +0.3초. 결과 Temp/dragon_gate/ · 로그 「[DragonGate] 끝」.
/// </summary>
public static class DragonSummonGateProbeEditor
{
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    [MenuItem("RelicFairy/Debug/10-29 화룡 소환 무적 시점 확인 (화룡방 Fight Sim 중, Play)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[DragonGate] 플레이 중에"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        Directory.CreateDirectory("Temp/dragon_gate");
        var sb = new StringBuilder("화룡 소환 무적 시점\n");
        try
        {
            DragonBossMonster dragon = null;
            for (float until = Time.realtimeSinceStartup + 40f; Time.realtimeSinceStartup < until && dragon == null; )
            {
                dragon = UnityEngine.Object.FindFirstObjectByType<DragonBossMonster>();
                if (dragon == null) await UniTask.Delay(250, DelayType.Realtime);
            }
            if (dragon == null) { sb.AppendLine("화룡 없음"); return; }
            // 등장 컷신(레터박스 · HUD 숨김)이 끝난 뒤에 — 첫 판은 컷신 중에 소환이 걸려 알림 · 체력 바가 사진에 없었다
            for (float until = Time.realtimeSinceStartup + 20f; Time.realtimeSinceStartup < until; )
            {
                var run = GameRunBootstrapper.Instance?.Run;
                bool cut = run != null && run.TryGetHudMode(out var mode) && (mode == HUDIds.Mode.Cutscene || mode == HUDIds.Mode.BossCutscene);
                if (!cut) break;   // 컷신만 기다린다 — 걷기 상태까지 기다리면 시뮬이 소환을 다 써 버렸다(남은 소환 없음)
                await UniTask.Delay(250, DelayType.Realtime);
            }
            await UniTask.Delay(1500, DelayType.Realtime);
            int floor = (int)(typeof(DragonBossMonster).GetProperty("SummonFloorHp", Inst)?.GetValue(dragon) ?? 0);
            if (floor <= 0) { sb.AppendLine("남은 소환 없음"); return; }
            SetHp(dragon, floor + 400);
            sb.AppendLine($"기준선 {floor} · 체력 {dragon.CurrentHp}/{dragon.EffectiveMaxHp}");

            var stateField = typeof(MonsterBase).GetField("_debugState", Inst);
            float t0 = Time.time, reach = -1f, start = -1f, on = -1f, off = -1f;
            bool immuneBeforeSummon = false, prevImmune = dragon.IsDamageImmuneNow;
            string prevState = null;
            int shots = 0; float shotAt = -1f; string shotName = null;
            while (Time.time - t0 < 70f && dragon != null && !dragon.IsDead)
            {
                float now = Time.time - t0;
                string st = stateField?.GetValue(dragon) as string;
                bool summon = st == "DragonSummonState";
                bool imm = dragon.IsDamageImmuneNow;
                if (reach < 0f && dragon.CurrentHp <= floor) { reach = now; sb.AppendLine($"[{now,5:0.00}] 기준선 닿음 — 상태 {st}"); }
                if (st != prevState) sb.AppendLine($"[{now,5:0.00}] 상태 {prevState} → {st} · 무적 {imm}");
                if (summon && start < 0f) { start = now; shotAt = Time.realtimeSinceStartup + 0.4f; shotName = "1_start"; }
                if (imm && !prevImmune)
                {
                    sb.AppendLine($"[{now,5:0.00}] 무적 켜짐 — 상태 {st}");
                    if (start < 0f) immuneBeforeSummon = true;
                    if (on < 0f) { on = now; shotAt = Time.realtimeSinceStartup + 0.5f; shotName = "2_immune"; }
                }
                if (!imm && prevImmune)
                {
                    sb.AppendLine($"[{now,5:0.00}] 무적 꺼짐 — 상태 {st}");
                    if (off < 0f && on >= 0f) { off = now; shotAt = Time.realtimeSinceStartup + 0.3f; shotName = "3_released"; }
                }
                if (on >= 0f && off < 0f && now - on > 3f)
                    foreach (var mini in UnityEngine.Object.FindObjectsByType<DragonMiniDragon>(FindObjectsSortMode.None))
                        if (mini != null && !mini.IsDead) mini.TakeDamage(999999f, null);
                if (shotName != null && Time.realtimeSinceStartup >= shotAt && shots < 3)
                {
                    ScreenCapture.CaptureScreenshot($"Temp/dragon_gate/{shotName}.png");
                    shots++; shotName = null;
                }
                prevImmune = imm; prevState = st;
                if (off >= 0f && now - off > 3f) break;
                await UniTask.Yield(PlayerLoopTiming.Update);
            }
            sb.AppendLine($"요약 — 기준선 닿음 {reach:0.00} · 소환 시작 {start:0.00} · 무적 켜짐 {on:0.00}(소환 시작 뒤 {(on >= 0f && start >= 0f ? on - start : -1f):0.00}초) · 무적 꺼짐 {off:0.00} · 소환 전 무적 {immuneBeforeSummon}");
        }
        catch (Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            File.WriteAllText("Temp/dragon_gate/view.txt", sb.ToString(), Encoding.UTF8);
            Debug.Log($"[DragonGate] 끝\n{sb}");
        }
    }

    private static void SetHp(MonsterBase boss, int hp)
    {
        var runtime = typeof(MonsterBase).GetField("_runtime", Inst)?.GetValue(boss);
        var field   = runtime?.GetType().GetField("CurrentHp", Inst);
        field?.SetValue(runtime, Mathf.Clamp(hp, 1, boss.EffectiveMaxHp));
    }
}
#endif
