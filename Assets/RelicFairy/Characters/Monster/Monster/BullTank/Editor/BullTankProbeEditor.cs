#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Cysharp.Threading.Tasks;
using RelicFairy.Monster;
using UnityEditor;
using UnityEngine;

/// <summary>
/// [실측 도구 · 플레이 · 전투방] 10-12 중간 보스 「황소 탱크」 — 가장 가까운 스포너에서 <see cref="MonsterSpawner.SpawnMidBossAsync"/>로 한 마리를 내고,
/// 플레이어를 3 m · 9 m · 12 m 자리로 번갈아 옮겨(무적) 세 패턴을 끌어낸다. 「[BullTank]」 로그로 예고 시작 · 굳힘 · 판정 시각을 적고
/// 예고 앞섬(굳힘 → 판정 ≥ 0.45초)을 패턴마다 계산한다. 예고가 뜨는 순간 사진. 결과: Logs/boss_fight_sim/bulltank_*.txt · *.png
/// </summary>
internal static class BullTankProbeEditor
{
    private const float RunFor   = 70f;
    private const float MoveGap  = 9f;
    private const int   MaxShots = 8;
    private static readonly float[] Distances = { 3f, 9f, 12f, 3f, 7f };

    private static StringBuilder s_log;
    private static string s_stem;
    private static float  s_t0;
    private static int    s_shots;
    private static readonly List<(float t, string msg)> s_events = new();

    private const string AutoRoot = "RelicFairy/Debug/런 구조 자동 실측/";
    private const string HoldCc   = "probe_hold";
    private static bool s_armed;

    [MenuItem("RelicFairy/Boss/BullTank/3 Probe via RunAuto (Play)")]
    private static void Probe()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[BullTankProbe] 플레이 중에만(테스트 허브)"); return; }
        s_armed = true;
        Application.logMessageReceived -= OnRunAuto;
        Application.logMessageReceived += OnRunAuto;
        EditorApplication.ExecuteMenuItem(AutoRoot + "시작 (테스트 허브에서, 플레이 중)");
    }

    // 자동 실측이 첫 전투방에 들어서면 멈추고 방 몹을 붙든 뒤 황소를 낸다
    private static void OnRunAuto(string msg, string stack, LogType type)
    {
        if (!s_armed || !msg.StartsWith("[RunAuto]") || !msg.Contains("방 진입")) return;
        s_armed = false;
        Application.logMessageReceived -= OnRunAuto;
        EditorApplication.delayCall += () =>
        {
            EditorApplication.ExecuteMenuItem(AutoRoot + "중지·기록");
            ProbeAsync().Forget();
        };
    }

    private static async UniTaskVoid ProbeAsync()
    {
        string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "boss_fight_sim");
        Directory.CreateDirectory(dir);
        s_stem = Path.Combine(dir, $"bulltank_{DateTime.Now:MMdd_HHmmss}");
        s_log = new StringBuilder();
        s_events.Clear();
        s_t0 = Time.time;
        s_shots = 0;
        Application.logMessageReceived += OnLog;
        try
        {
            var player = GameRunBootstrapper.Instance?.Run?.Player;
            if (player == null) { s_log.AppendLine("런 플레이어 없음"); return; }
            typeof(PlayerController).GetField("debugInvincible", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)?.SetValue(player, true);
            await UniTask.Delay(5000, ignoreTimeScale: true);   // 방 입장 입력 잠금 · 등장 연출
            var fw = player.transform.forward; fw.y = 0f; fw.Normalize();
            foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
            {
                if (m == null || m.IsDead) continue;
                m.Status.ApplyCc(HoldCc, 9999f);
                var hold = player.transform.position - fw * 16f;
                if (UnityEngine.AI.NavMesh.SamplePosition(hold, out var hh, 4f, UnityEngine.AI.NavMesh.AllAreas)) hold = hh.position;
                if (m.TryGetComponent<UnityEngine.AI.NavMeshAgent>(out var ma) && ma.isActiveAndEnabled && ma.isOnNavMesh) ma.Warp(hold);
            }

            MonsterSpawner best = null; float bd = float.MaxValue;
            foreach (var sp in UnityEngine.Object.FindObjectsByType<MonsterSpawner>(FindObjectsSortMode.None))
            {
                if (sp == null || !sp.isActiveAndEnabled) continue;
                float d = (sp.transform.position - player.transform.position).sqrMagnitude;
                if (d < bd) { bd = d; best = sp; }
            }
            if (best == null) { s_log.AppendLine("스포너 없음(전투방에서)"); return; }

            var boss = await best.SpawnMidBossAsync(BullTankMonster.Kind, player.GetCancellationTokenOnDestroy());
            if (boss == null) { s_log.AppendLine("황소 스폰 실패"); return; }
            await UniTask.Delay(1500, ignoreTimeScale: true);   // 설정 로드 · 초기화 뒤에 적는다
            s_log.AppendLine($"스폰 {boss.name} · 등급 {boss.Grade} · 체력 {boss.EffectiveMaxHp} · 정예 보스 {boss.IsEliteBoss} · 포이즈 {(boss.Poise != null ? "있음" : "없음")}");
            await UniTask.Delay(1200, ignoreTimeScale: true);
            Shot("spawn");

            int step = 0;
            float end = Time.time + RunFor, nextMove = Time.time + 2f;
            while (Time.time < end && boss != null && !boss.IsDead)
            {
                if (Time.time >= nextMove)
                {
                    nextMove = Time.time + MoveGap;
                    float d = Distances[step++ % Distances.Length];
                    if (FindSpot(boss, player.transform.position, d, out var pos, out float got))
                    {
                        player.transform.position = pos;
                        if (player.TryGetComponent<Rigidbody>(out var rb)) { rb.position = pos; rb.linearVelocity = Vector3.zero; }
                        s_log.AppendLine($"[{Time.time - s_t0,6:0.00}] 플레이어 → 황소에서 {got:0.#} m(목표 {d} m)");
                    }
                    else s_log.AppendLine($"[{Time.time - s_t0,6:0.00}] 황소에서 {d} m 자리 없음(방 안 NavMesh) — 제자리");
                }
                // 무적은 TakeDamage만 막는다 — 낙사 피해처럼 HP를 바로 깎는 길로 런이 끝나지 않게 채워 둔다(10-12 실측: 도약 착지 전 사망)
                var rs = player.RuntimeStats;
                if (rs != null && rs.Hp < rs.MaxHp)
                {
                    s_log.AppendLine($"[{Time.time - s_t0,6:0.00}] 플레이어 HP {rs.Hp}/{rs.MaxHp} — 무적 우회 피해, 다시 채움");
                    rs.SetHp(rs.MaxHp);
                }
                await UniTask.Yield(PlayerLoopTiming.Update);
            }
            s_log.AppendLine($"끝 — 황소 {(boss == null ? "사라짐" : boss.IsDead ? "쓰러짐" : $"체력 {boss.CurrentHp}")}");
            if (boss != null && !boss.IsDead) boss.TakeSynergyDamage(boss.CurrentHp + 10, null, 1f);   // 처치 디졸브까지 보고 끝낸다
            await UniTask.Delay(2500, ignoreTimeScale: true);
            Shot("death");
        }
        catch (Exception e) { s_log.AppendLine("예외: " + e); }
        finally
        {
            Application.logMessageReceived -= OnLog;
            s_log.Insert(0, Summary());
            File.WriteAllText(s_stem + ".txt", s_log.ToString(), Encoding.UTF8);
            Debug.Log($"[BullTankProbe] 끝 — {s_stem}.txt");
        }
    }

    // 황소에서 d m — 황소 에이전트 NavMesh 위 · 황소에서 곧게 닿는(벽 너머 · 방 밖이 아닌) 자리. 방향을 돌려 보고 없으면 거리를 줄인다
    private static bool FindSpot(MonsterBase boss, Vector3 playerPos, float d, out Vector3 pos, out float got)
    {
        Vector3 from = boss.transform.position;
        var filter = new UnityEngine.AI.NavMeshQueryFilter { agentTypeID = 0, areaMask = UnityEngine.AI.NavMesh.AllAreas };
        if (boss.TryGetComponent<UnityEngine.AI.NavMeshAgent>(out var ag)) filter.agentTypeID = ag.agentTypeID;
        Vector3 away = playerPos - from; away.y = 0f;
        away = away.sqrMagnitude > 0.01f ? away.normalized : boss.transform.forward;
        foreach (float scale in new[] { 1f, 0.75f, 0.5f })
        {
            float r = d * scale;
            for (int i = 0; i < 9; i++)
            {
                float yaw = (i + 1) / 2 * 40f * (i % 2 == 0 ? 1f : -1f);   // 0, -40, +40, -80, +80 …
                Vector3 want = from + Quaternion.Euler(0f, yaw, 0f) * away * r;
                if (!UnityEngine.AI.NavMesh.SamplePosition(want, out var hit, 1.5f, filter)) continue;
                if (UnityEngine.AI.NavMesh.Raycast(from, hit.position, out _, filter)) continue;   // 사이에 경계 — 벽 너머
                pos = hit.position;
                got = Vector3.Distance(new Vector3(from.x, 0f, from.z), new Vector3(pos.x, 0f, pos.z));
                return true;
            }
        }
        pos = playerPos; got = 0f;
        return false;
    }

    // 패턴마다 「굳힘 → 판정」 앞섬과 실행 횟수
    private static string Summary()
    {
        var sb = new StringBuilder("황소 탱크 실측 — 패턴별 예고 앞섬(굳힘 → 판정, 기준 ≥ 0.45초)\n");
        foreach (var (name, lockKey, hitKey) in new[]
        {
            ("내려찍기", "내려찍기 방향 굳힘", "내려찍기 판정"),
            ("도약",     "도약 착지 자리 굳힘", "도약 판정"),
            ("돌진",     "돌진 방향 굳힘",     "돌진 판정 시작"),
        })
        {
            int n = 0; float min = float.MaxValue, sum = 0f; float lockAt = -1f;
            foreach (var (t, msg) in s_events)
            {
                if (msg.Contains(lockKey)) lockAt = T(msg, t);
                else if (msg.Contains(hitKey) && lockAt >= 0f) { float lead = T(msg, t) - lockAt; n++; sum += lead; min = Mathf.Min(min, lead); lockAt = -1f; }
            }
            sb.AppendLine(n == 0 ? $"  {name}: 실행 0" : $"  {name}: {n}회 · 앞섬 평균 {sum / n:0.00}초 · 최소 {min:0.00}초 {(min >= 0.449f ? "통과" : "미달")}");
        }
        int walls = 0; foreach (var (_, msg) in s_events) if (msg.Contains("벽 충돌")) walls++;
        sb.AppendLine($"  돌진 벽 기절 {walls}회");
        return sb.ToString();
    }

    private static float T(string msg, float fallback)
    {
        var m = Regex.Match(msg, @"t=([0-9.]+)");
        return m.Success && float.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback;
    }

    private static void OnLog(string msg, string stack, LogType type)
    {
        if (s_log == null) return;
        bool mine = msg.StartsWith("[BullTank]") || msg.StartsWith("[MonsterSpawner] 중간 보스");
        if (!mine && type != LogType.Exception && type != LogType.Error) return;
        string line = msg.Split('\n')[0];
        s_events.Add((Time.time, line));
        s_log.AppendLine($"[{Time.time - s_t0,6:0.00}] {line}");
        if (mine && (line.Contains("예고 시작") || line.Contains("착지 자리 굳힘") || line.Contains("벽 충돌") || line.Contains("깨어남")))
            Shot(line.Contains("돌진") ? "charge" : line.Contains("도약") ? "leap" : line.Contains("내려찍기") ? "swing" : "wake");
    }

    private static void Shot(string tag)
    {
        if (s_shots >= MaxShots) return;
        ScreenCapture.CaptureScreenshot($"{s_stem}_{++s_shots}_{tag}.png");
    }
}
#endif
