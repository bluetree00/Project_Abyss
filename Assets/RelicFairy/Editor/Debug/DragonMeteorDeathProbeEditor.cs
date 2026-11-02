#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using RelicFairy.Monster;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 10-30 11차 빌드 피드백 「화룡 메테오 패턴에 잡혀 죽은 뒤 다시 시작하면 맵에 메테오가 계속 떨어진다」 재현 — 화룡방 Fight Sim 중에 부른다.
/// 화룡 체력 55%(번개 — 수동 운석이 늘 돈다) → 운석이 3발 이상 떠 있을 때 플레이어 사망 → 결과창 [계속] → 씬이 바뀐 뒤 2초 · 5초에
/// 보스 이펙트 풀(@Pools/EffectPool/BossEffects)의 켜진 인스턴스 · 바닥 칸 풀(@QuadTilePool)의 켜진 칸을 센다.
/// 결과 Temp/meteor_death/view.txt · 사진 1_before · 2_after · 로그 「[MeteorDeath] 끝」.
/// </summary>
public static class DragonMeteorDeathProbeEditor
{
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private const string Dir = "Temp/meteor_death";

    [MenuItem("RelicFairy/Debug/10-30 화룡 메테오 중 사망 → 남는 메테오 확인 (화룡방 Fight Sim 중, Play)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[MeteorDeath] 플레이 중에"); return; }
        RunAsync().Forget();
    }

    [MenuItem("RelicFairy/Debug/10-30 화룡 운석 칸 반납 확인 — 죽지 않고 30초 (화룡방 Fight Sim 중, Play)")]
    private static void RunAlive()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[MeteorDeath] 플레이 중에"); return; }
        AliveAsync().Forget();
    }

    // 전투 중 반납 — 수동 운석 버스트가 지나면 켜진 칸 · 이펙트가 0으로 돌아오는가(10-30 반납 규칙을 바꾼 뒤 회귀)
    private static async UniTaskVoid AliveAsync()
    {
        var sb = new StringBuilder("화룡 운석 칸 반납(죽지 않고)\n");
        try
        {
            DragonBossMonster dragon = null;
            for (float until = Time.realtimeSinceStartup + 40f; Time.realtimeSinceStartup < until && dragon == null; )
            {
                dragon = UnityEngine.Object.FindFirstObjectByType<DragonBossMonster>();
                if (dragon == null) await UniTask.Delay(250, DelayType.Realtime);
            }
            if (dragon == null) { sb.AppendLine("화룡 없음"); return; }
            await UniTask.Delay(8000, DelayType.Realtime);
            SetHp(dragon, Mathf.RoundToInt(dragon.EffectiveMaxHp * 0.55f));
            var p = GameRunBootstrapper.Instance?.Run?.Player;
            if (p != null) typeof(PlayerController).GetField("debugInvincible", Inst)?.SetValue(p, true);
            int maxQ = 0, maxFx = 0, zeroAfterPeak = 0;
            for (int i = 0; i < 40; i++)
            {
                var c = Count();
                if (c.quads > maxQ) maxQ = c.quads;
                if (c.fx > maxFx) maxFx = c.fx;
                if (maxQ > 0 && c.quads == 0) zeroAfterPeak++;
                sb.Append($"{c.quads}/{c.fx} ");
                await UniTask.Delay(1000, DelayType.Realtime);
            }
            sb.AppendLine($"\n칸 최대 {maxQ} · 이펙트 최대 {maxFx} · 정점 뒤 칸 0인 초 {zeroAfterPeak}");
        }
        catch (Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText($"{Dir}/alive.txt", sb.ToString(), Encoding.UTF8);
            Debug.Log($"[MeteorDeath] 반납 끝\n{sb}");
        }
    }

    private static async UniTaskVoid RunAsync()
    {
        Directory.CreateDirectory(Dir);
        var sb = new StringBuilder("화룡 메테오 중 사망 → 씬 전환 뒤 남는 것\n");
        try
        {
            // 덤 — Ch4 추락 바닥(CreatePrimitive) 분홍 근거: 기본 도형 재질의 셰이더
            var prim = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            var pr = prim.GetComponent<MeshRenderer>();
            sb.AppendLine($"기본 도형 재질 {pr.sharedMaterial.name} · 셰이더 {pr.sharedMaterial.shader.name}");
            RuntimePrimitiveMaterial.Apply(pr, Color.gray);
            sb.AppendLine($"  RuntimePrimitiveMaterial.Apply 뒤 셰이더 {pr.sharedMaterial.shader.name}");
            UnityEngine.Object.Destroy(prim);

            DragonBossMonster dragon = null;
            for (float until = Time.realtimeSinceStartup + 40f; Time.realtimeSinceStartup < until && dragon == null; )
            {
                dragon = UnityEngine.Object.FindFirstObjectByType<DragonBossMonster>();
                if (dragon == null) await UniTask.Delay(250, DelayType.Realtime);
            }
            if (dragon == null) { sb.AppendLine("화룡 없음"); return; }
            for (float until = Time.realtimeSinceStartup + 20f; Time.realtimeSinceStartup < until; )
            {
                var r = GameRunBootstrapper.Instance?.Run;
                bool cut = r != null && r.TryGetHudMode(out var mode) && (mode == HUDIds.Mode.Cutscene || mode == HUDIds.Mode.BossCutscene);
                if (!cut) break;
                await UniTask.Delay(250, DelayType.Realtime);
            }
            await UniTask.Delay(1500, DelayType.Realtime);

            SetHp(dragon, Mathf.RoundToInt(dragon.EffectiveMaxHp * 0.55f));
            sb.AppendLine($"화룡 체력 {dragon.CurrentHp}/{dragon.EffectiveMaxHp} · 원소 {dragon.CurrentElement}");

            // 화염구 비를 바로 건다 — 첫 판(수동 운석만)은 씬 전환 뒤 0개였다. 화염구 비는 운석을 상태(SO)가 쥐고 Exit에서만 돌려준다
            var runner = typeof(DragonBossMonster).GetField("_runner", Inst)?.GetValue(dragon);
            var cfg = runner?.GetType().GetField("_config", Inst)?.GetValue(runner) as BossConfigSO;
            DragonFireballRainPatternSO rain = null;
            if (cfg != null)
                foreach (var e in cfg.patternEntries)
                    foreach (var pat in e.patterns)
                        if (pat is DragonFireballRainPatternSO r) rain = r;
            var rainState = rain != null ? rain.GetRuntimeState() : null;
            if (rainState == null) { sb.AppendLine("화염구 비 패턴 없음"); return; }
            dragon.ChangeState(rainState);
            sb.AppendLine($"화염구 비 시작 — {rain.name}");

            float t0 = Time.realtimeSinceStartup;
            var c = Count();
            while (c.rain < 6 && Time.realtimeSinceStartup - t0 < 30f)
            {
                await UniTask.Delay(100, DelayType.Realtime);
                c = Count();
            }
            sb.AppendLine($"[사망 직전 {Time.realtimeSinceStartup - t0:0.0}초] 켜진 보스 이펙트 {c.fx} · 켜진 바닥 칸 {c.quads}\n{c.detail}");
            ScreenCapture.CaptureScreenshot($"{Dir}/1_before.png");
            await UniTask.Delay(200, DelayType.Realtime);

            var p = GameRunBootstrapper.Instance?.Run?.Player;
            if (p == null) { sb.AppendLine("플레이어 없음"); return; }
            string scene0 = SceneManager.GetActiveScene().path;
            typeof(PlayerController).GetField("debugInvincible", Inst)?.SetValue(p, false);
            var deadF = typeof(PlayerController).GetField("_dead", Inst);
            int tries = 0;
            for (; tries < 6 && !(deadF?.GetValue(p) is bool dd && dd); tries++)   // 결의 · 서약 · 부활이 차례로 막는다 — 죽을 때까지
            {
                p.RuntimeStats.SetHp(0);
                p.NotifyHpDepleted();
            }
            c = Count();
            sb.AppendLine($"플레이어 사망 처리 {tries}번 — 씬 {scene0} · 그 순간 화염구 비 칸 {c.rain}");

            float td = Time.realtimeSinceStartup, nextLog = 0f;
            while (SceneManager.GetActiveScene().path == scene0 && Time.realtimeSinceStartup - td < 90f)
            {
                float now = Time.realtimeSinceStartup - td;
                if (RunResultView.Active != null) RunResultView.Active.Continue();
                if (now >= nextLog)
                {
                    nextLog = now + 2f;
                    c = Count();
                    sb.AppendLine($"  [사망 뒤 {now,4:0.0}초] 켜진 이펙트 {c.fx} · 칸 {c.quads} · 화룡 {(dragon != null ? "있음" : "없음")} · timeScale {Time.timeScale:0.00}");
                }
                await UniTask.Delay(100, DelayType.Realtime);
            }
            sb.AppendLine($"씬 전환 → {SceneManager.GetActiveScene().path} (사망 뒤 {Time.realtimeSinceStartup - td:0.0}초)");

            foreach (float wait in new[] { 2f, 3f })
            {
                await UniTask.Delay(TimeSpan.FromSeconds(wait), DelayType.Realtime);
                c = Count();
                sb.AppendLine($"[씬 전환 +{(wait == 2f ? 2 : 5)}초] 켜진 보스 이펙트 {c.fx} · 켜진 바닥 칸 {c.quads} · 화룡 {(UnityEngine.Object.FindFirstObjectByType<DragonBossMonster>() != null ? "있음" : "없음")}\n{c.detail}");
            }
            ScreenCapture.CaptureScreenshot($"{Dir}/2_after.png");
            await UniTask.Delay(300, DelayType.Realtime);
        }
        catch (Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            File.WriteAllText($"{Dir}/view.txt", sb.ToString(), Encoding.UTF8);
            Debug.Log($"[MeteorDeath] 끝\n{sb}");
        }
    }

    /// <summary>켜진 보스 이펙트 인스턴스(BossEffects 바로 아래 · BossPooledEffect) · 켜진 바닥 칸(@QuadTilePool 아래 렌더러 켜짐).</summary>
    private static (int fx, int quads, int rain, string detail) Count()
    {
        int fx = 0, quads = 0, rain = 0;
        var d = new StringBuilder();
        var pools = GameObject.Find("@Pools");
        var root = pools != null ? pools.transform.Find("EffectPool/BossEffects") : null;
        if (root != null)
            foreach (Transform t in root)
                if (t.gameObject.activeSelf && t.TryGetComponent<BossPooledEffect>(out _))
                {
                    fx++;
                    if (fx <= 6) d.AppendLine($"    이펙트 {t.name} @ {t.position:F0}");
                }
        var qp = GameObject.Find("@QuadTilePool");
        if (qp != null)
            foreach (Transform t in qp.transform)
                if (t.TryGetComponent<MeshRenderer>(out var mr) && mr.enabled)
                {
                    quads++;
                    if (t.name == "FireballWarn") rain++;
                    if (quads <= 3) d.AppendLine($"    칸 {t.name} @ {t.position:F0}");
                }
        return (fx, quads, rain, d.ToString().TrimEnd('\n'));
    }

    private static void SetHp(MonsterBase boss, int hp)
    {
        var runtime = typeof(MonsterBase).GetField("_runtime", Inst)?.GetValue(boss);
        var field   = runtime?.GetType().GetField("CurrentHp", Inst);
        field?.SetValue(runtime, Mathf.Clamp(hp, 1, boss.EffectiveMaxHp));
    }
}
#endif
