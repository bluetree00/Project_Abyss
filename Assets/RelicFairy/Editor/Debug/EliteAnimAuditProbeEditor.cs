#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using RelicFairy.Monster;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// [실측 · 플레이 · 테스트 허브] 10-14 사용자 「중간보스랑 특정 정예 몬스터들의 패턴과 애니메이션의 문제가 많아 · 이펙트도」 —
/// 정예 등급 13종 · 황소 탱크 · 정예 접두어(돌격 · 소환 · 박격)를 무적 플레이어 앞에 하나씩 내고 행동마다 적는다:
///   상태(FSM) 구간 · 그동안 재생된 애니 상태 · 클립 · 피해 순간(애니 진행) · 휘두름 정점(뼈 속도) · 새로 뜬 이펙트(자리 · 크기) ·
///   미끄러짐(1.2 m/s 넘게 움직이는데 이동 애니가 아님) · 이동 중 애니 멈춤(반복 안 하는 클립이 끝난 채 추적) · 같은 클립을 쓰는 다른 패턴.
/// 행동마다 옆에서 한 장(피해 순간 또는 구간 가운데). 결과: Logs/boss_fight_sim/elite_audit_*/report.txt · *.png · 로그 「[EliteAudit] 끝」.
/// </summary>
internal static class EliteAnimAuditProbeEditor
{
    private const string Root     = "RelicFairy/Monster/Elite Audit/";
    private const string AutoRoot = "RelicFairy/Debug/런 구조 자동 실측/";
    private const string HoldCc   = "probe_hold";
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private const float SlideSpeed   = 1.2f;
    private const int   ShotsPerName = 2;

    private static readonly string[] GroupA =
    {
        "Beholder/Beholder", "BishopKnight/BishopKnight", "BlackKnight/BlackKnight", "ChestMonster/ChestMonster",
        "CrabMonster/CrabMonster", "Cyclops/Cyclops", "EvilMage/EvilMage",
    };
    private static readonly string[] GroupB =
    {
        "FlyingDemon/FlyingDemon", "LizardWarrior/LizardWarrior", "NagaWizard/NagaWizard", "Salamander/Salamander",
        "Specter/Specter", "Werewolf/Werewolf",
    };
    private static readonly (string key, MonsterTraitKind kind)[] PrefixRuns =
    {
        ("Werewolf/Werewolf", MonsterTraitKind.Charger), ("BlackKnight/BlackKnight", MonsterTraitKind.Charger),
        ("CrabMonster/CrabMonster", MonsterTraitKind.Charger), ("EvilMage/EvilMage", MonsterTraitKind.Summoner),
        ("Cyclops/Cyclops", MonsterTraitKind.Mortar),
    };
    private static readonly string[] Locomotion =
        { "run", "walk", "move", "fly", "charge", "jump", "dash", "rush", "roll", "crawl", "swim", "hover", "turn", "strafe", "blend", "chase" };
    private static readonly string[] PassiveParts = { "Chase", "Patrol", "Idle", "AttackReady", "Dormant", "Die", "Spawn", "GetHit" };
    private static bool IsPassive(string fsm) { foreach (var k in PassiveParts) if (fsm.Contains(k)) return true; return false; }

    private enum Run { A, B, Bull, Prefix, Stuck, FixA, FixB, FxLook, BullCh }
    private static bool s_trace;   // 추적 기록(0.5초마다 거리 · 정지 거리 · 남은 길 · 속도 · 칸 대기)
    private static Run    s_run;
    private static bool   s_armed;
    private static string s_dir;
    private static StringBuilder s_sb;

    [MenuItem(Root + "1 Elites A - 7 (Play)")]  private static void M1() => Begin(Run.A);
    [MenuItem(Root + "2 Elites B - 6 (Play)")]  private static void M2() => Begin(Run.B);
    [MenuItem(Root + "3 BullTank (Play)")]      private static void M3() => Begin(Run.Bull);
    [MenuItem(Root + "4 Prefix Anim - 5 (Play)")] private static void M4() => Begin(Run.Prefix);
    [MenuItem(Root + "5 Stuck Chase - Crab · Lizard (Play)")] private static void M5() => Begin(Run.Stuck);
    [MenuItem(Root + "6 Fixed A - 7 (Play)")] private static void M6() => Begin(Run.FixA);
    [MenuItem(Root + "7 Fixed B - 5 (Play)")] private static void M7() => Begin(Run.FixB);
    [MenuItem(Root + "8 FX Look - Beholder · BlackKnight (Play)")] private static void M8() => Begin(Run.FxLook);
    [MenuItem(Root + "9 BullTank Chapters (Play)")] private static void M9() => Begin(Run.BullCh);   // 10-16 챕터별 개성 · 벽 앞 돌진

    // 10-14 고친 뒤 확인 — 동작을 바꾼 패턴 · 휘청 · 걷기
    private static readonly string[] FixedA =
    {
        "Werewolf/Werewolf", "CrabMonster/CrabMonster", "Specter/Specter", "LizardWarrior/LizardWarrior",
        "EvilMage/EvilMage", "NagaWizard/NagaWizard", "Beholder/Beholder",
    };
    private static readonly string[] FixedB =
    {
        "Cyclops/Cyclops", "BishopKnight/BishopKnight", "Salamander/Salamander", "ChestMonster/ChestMonster", "BlackKnight/BlackKnight",
    };

    private static void Begin(Run run)
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[EliteAudit] 플레이 중에만(테스트 허브)"); return; }
        s_run = run;
        s_armed = true;
        Application.logMessageReceived -= OnRunAuto;
        Application.logMessageReceived += OnRunAuto;
        EditorApplication.ExecuteMenuItem(AutoRoot + "시작 (테스트 허브에서, 플레이 중)");
    }

    private static void OnRunAuto(string msg, string stack, LogType type)
    {
        if (!s_armed || !msg.StartsWith("[RunAuto]") || !msg.Contains("방 진입")) return;
        s_armed = false;
        Application.logMessageReceived -= OnRunAuto;
        EditorApplication.delayCall += () =>
        {
            EditorApplication.ExecuteMenuItem(AutoRoot + "중지·기록");
            RunAllAsync().Forget();
        };
    }

    private static async UniTaskVoid RunAllAsync()
    {
        s_dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "boss_fight_sim", $"elite_audit_{s_run}_{DateTime.Now:MMdd_HHmmss}");
        Directory.CreateDirectory(s_dir);
        s_sb = new StringBuilder($"정예 · 황소 패턴 · 애니 · 이펙트 점검({s_run}) — 시간은 그 구간 시작부터 초\n");
        var p = GameRunBootstrapper.Instance?.Run?.Player;
        try
        {
            if (p == null) { s_sb.AppendLine("런 플레이어 없음"); return; }
            var ct = p.GetCancellationTokenOnDestroy();
            typeof(PlayerController).GetField("debugInvincible", Inst)?.SetValue(p, true);
            await UniTask.Delay(5000, ignoreTimeScale: true, cancellationToken: ct);   // 방 입장 입력 잠금 · 등장 연출
            HoldRoomMonsters(p.transform);
            switch (s_run)
            {
                case Run.A:      foreach (var k in GroupA) await OneAsync(k, null, p, 16f, k.StartsWith("Chest") ? new[] { 1.5f, 2.5f, 1.5f } : new[] { 7f, 2.5f, 9f }, 5f, ct); break;
                case Run.B:      foreach (var k in GroupB) await OneAsync(k, null, p, 16f, new[] { 7f, 2.5f, 9f }, 5f, ct); break;
                case Run.Bull:   await OneAsync("BullTank/BullTank_Ch1", null, p, 56f, new[] { 3f, 9f, 12f, 3f, 7f, 10f }, 8f, ct); break;
                case Run.BullCh:
                    foreach (var k in new[] { "BullTank/BullTank_Ch1", "BullTank/BullTank_Ch2", "BullTank/BullTank_Ch3", "BullTank/BullTank_Ch4" })
                        await OneAsync(k, null, p, 40f, new[] { 3f, 9f, 12f, 3f, 7f, 10f }, 6f, ct);
                    break;
                case Run.Prefix: foreach (var (k, kind) in PrefixRuns) await OneAsync(k, kind, p, 15f, new[] { 8f }, 4f, ct); break;
                case Run.Stuck:
                    s_trace = true;
                    foreach (var k in new[] { "CrabMonster/CrabMonster", "LizardWarrior/LizardWarrior" }) await OneAsync(k, null, p, 14f, new[] { 6f }, 99f, ct);
                    s_trace = false;
                    break;
                case Run.FxLook:
                    foreach (var mb in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))   // 몬스터가 이펙트를 가렸다 — 숨긴다
                        if (mb != null && !mb.IsDead) mb.gameObject.SetActive(false);
                    foreach (var (tag, path, judge) in FxLookItems) await FxLookAsync(tag, path, judge, p, ct);
                    break;
                case Run.FixA: foreach (var k in FixedA) await OneAsync(k, null, p, 18f, new[] { 7f, 2.5f, 9f, 2f }, 4.5f, ct); break;
                case Run.FixB: foreach (var k in FixedB) await OneAsync(k, null, p, 18f, k.StartsWith("Chest") ? new[] { 1.5f, 2.5f, 1.5f } : new[] { 7f, 2.5f, 9f, 2f }, 4.5f, ct); break;
            }
        }
        catch (OperationCanceledException) { s_sb.AppendLine("취소됨"); }
        catch (Exception e) { s_sb.AppendLine("예외: " + e); }
        finally
        {
            File.WriteAllText(Path.Combine(s_dir, "report.txt"), s_sb.ToString(), Encoding.UTF8);
            Debug.Log($"[EliteAudit] 끝 — {s_dir}");
        }
    }

    // ── 한 마리 ─────────────────────────────────────────────
    private sealed class Seg
    {
        public string Fsm, Pattern;
        public float  Start, End, Dmg = -1f, DmgNt = -1f, Peak = -1f, PeakNt = -1f, PeakSpeed, MaxMove, Slide, Frozen;
        public readonly List<string> Anims = new();
        public readonly List<string> Fx = new();
        public string SlideAnim;
        public float  BodyMax;
        public Vector3 BodyAt;
        public bool   Shot;
        public HashSet<int> PsBefore;
    }

    private static async UniTask OneAsync(string key, MonsterTraitKind? prefix, PlayerController p, float watch, float[] dists, float moveGap,
                                          CancellationToken ct)
    {
        string name = key.Split('/')[1];
        Vector3 fw = p.transform.forward; fw.y = 0f; fw = fw.sqrMagnitude > 0.01f ? fw.normalized : Vector3.forward;
        var m = await Spawn(key, p.transform.position + fw * dists[0]);
        if (m == null) { s_sb.AppendLine($"\n■ {name}: 부르기 실패"); return; }
        float tw = Time.realtimeSinceStartup;
        var fsmF = typeof(MonsterBase).GetField("_fsm", Inst);
        while (Time.realtimeSinceStartup - tw < 8f && (m.IsDamageImmuneNow || fsmF?.GetValue(m) == null)) await UniTask.Yield(PlayerLoopTiming.Update, ct);
        await UniTask.Delay(600, ignoreTimeScale: true, cancellationToken: ct);
        if (prefix.HasValue) MonsterTraits.Attach(m, new[] { prefix.Value }, null, SummonFunc());
        if (name.StartsWith("Chest")) m.TakeDamage(1f, p.gameObject);   // 미믹은 맞아야 깬다
        float nextTrace = Time.time;

        var anim  = m.GetComponentInChildren<Animator>();
        var names = StateNames(anim);
        var cfg   = typeof(MonsterBase).GetProperty("Config", Inst)?.GetValue(m) as MonsterConfigSO;
        var a     = cfg != null ? cfg.animation : null;
        var rt    = typeof(MonsterBase).GetField("_runtime", Inst)?.GetValue(m) as MonsterRuntimeData;
        var curF  = typeof(MonsterFSM).GetField("_current", Inst);
        var bones = new List<Transform>();
        foreach (var smr in m.GetComponentsInChildren<SkinnedMeshRenderer>())
            foreach (var b in smr.bones) if (b != null && !bones.Contains(b)) bones.Add(b);
        var prevB = new Vector3[bones.Count];
        float height = Height(m);
        var ag = m.GetComponent<NavMeshAgent>();
        // 몸(루트 모션 뼈 · 없으면 스킨 루트 뼈)이 몬스터 자리에서 얼마나 벗어나는지 — 처음 자세를 0으로
        Transform body = FindDeep(m.transform, "DeformationSystem");
        if (body == null) foreach (var smr in m.GetComponentsInChildren<SkinnedMeshRenderer>()) if (smr.rootBone != null) { body = smr.rootBone; break; }
        Vector3 bodyRest = body != null ? Flat(Quaternion.Inverse(m.transform.rotation) * (body.position - m.transform.position)) : Vector3.zero;

        s_sb.AppendLine($"\n■ {name}{(prefix.HasValue ? $" + {prefix}" : "")} — 키 {height:0.0} m · 이동 {(ag != null ? ag.speed : 0f):0.0} m/s · 컨트롤러 {(anim != null && anim.runtimeAnimatorController != null ? anim.runtimeAnimatorController.name : "없음")} · 루트모션 {(anim != null && anim.applyRootMotion ? "켬" : "끔")}"
                       + (a != null ? $" · 추적 「{a.chaseStateName}」{(Has(anim, a.chaseStateName) ? "" : " ✗없음")} · 준비 「{a.attackReadyStateName}」{(Has(anim, a.attackReadyStateName) ? "" : " ✗없음")} · 대기 「{a.idleStateName}」{(Has(anim, a.idleStateName) ? "" : " ✗없음")}" : ""));

        var segs = new List<Seg>();
        Seg seg = null;
        var shotsByName = new Dictionary<string, int>();
        Vector3 lastPos = m.transform.position;
        float start = Time.time, nextMove = Time.time + moveGap;
        int step = 1;
        bool chaseShot = false;
        int   staggerHits = 8;
        bool  bishopHalf = false;
        float nextStaggerHit = 0f;
        while (Time.time - start < watch && m != null && !m.IsDead && m.isActiveAndEnabled)
        {
            await UniTask.Yield(PlayerLoopTiming.PostLateUpdate, ct);
            if (m == null) break;
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            var fsm = fsmF?.GetValue(m);
            var cur = fsm != null ? curF?.GetValue(fsm) : null;
            string fsmName = cur?.GetType().Name ?? "없음";

            if (seg == null || seg.Fsm != fsmName)
            {
                if (seg != null) { seg.End = Time.time; segs.Add(seg); }
                seg = new Seg { Fsm = fsmName, Start = Time.time, PsBefore = Snapshot() };
                var pat = cur?.GetType().GetField("_pattern", Inst)?.GetValue(cur);
                seg.Pattern = pat?.GetType().GetField("name")?.GetValue(pat) as string;
                for (int i = 0; i < bones.Count; i++) prevB[i] = m.transform.InverseTransformPoint(bones[i].position);
            }
            float tIn = Time.time - seg.Start;
            if (body != null)
            {
                Vector3 off = Flat(Quaternion.Inverse(m.transform.rotation) * (body.position - m.transform.position)) - bodyRest;
                if (off.magnitude > seg.BodyMax) { seg.BodyMax = off.magnitude; seg.BodyAt = off; }
            }
            if (s_trace && Time.time >= nextTrace && ag != null)
            {
                nextTrace = Time.time + 0.5f;
                object wu = cur?.GetType().GetField("_waitUntil", Inst)?.GetValue(cur);
                s_sb.AppendLine($"    추적 {Time.time - start:0.0} {fsmName} 거리 {rt?.DistToPlayer:0.00} · 정지 {ag.stoppingDistance:0.00} · 남은 길 {(ag.hasPath ? ag.remainingDistance : -1f):0.00} · 속도 {ag.velocity.magnitude:0.0} · 멈춤 {ag.isStopped} · 칸 대기 {(wu is float w && w > Time.time ? "예" : "아니오")} · 근접 칸 {MonsterAttackSlots.Count(AttackSlotKind.Melee)}");
            }

            // 애니 — 지금 상태(전이 중이면 다음 상태)
            string st = "?", clip = "";
            bool loop = true; float nt = 0f;
            if (anim != null)
            {
                var info = anim.IsInTransition(0) ? anim.GetNextAnimatorStateInfo(0) : anim.GetCurrentAnimatorStateInfo(0);
                st = names.TryGetValue(info.shortNameHash, out var sn) ? sn : $"#{info.shortNameHash}";
                loop = info.loop; nt = info.normalizedTime;
                var ci = anim.IsInTransition(0) ? anim.GetNextAnimatorClipInfo(0) : anim.GetCurrentAnimatorClipInfo(0);
                if (ci.Length > 0) { var top = ci.OrderByDescending(c => c.weight).First(); clip = top.clip != null ? $"{top.clip.name}({top.clip.length:0.00}s{(top.clip.isLooping ? "↻" : "")})" : ""; }
            }
            string label = clip.Length > 0 ? $"{st}:{clip}" : st;
            if (seg.Anims.Count == 0 || seg.Anims[^1] != label) { if (seg.Anims.Count < 8) seg.Anims.Add(label); }

            // 이동 — 미끄러짐 · 이동 중 애니 멈춤
            Vector3 pos = m.transform.position;
            float speed = Flat(pos - lastPos).magnitude / dt;
            lastPos = pos;
            seg.MaxMove = Mathf.Max(seg.MaxMove, speed);
            bool locoAnim = IsLoco(st) || IsLoco(clip) || (a != null && (st == a.chaseStateName || st == a.patrolStateName));
            if (speed > SlideSpeed && !locoAnim && anim != null && !anim.IsInTransition(0)) { seg.Slide += dt; seg.SlideAnim ??= $"{label} {speed:0.0} m/s"; }
            if ((fsmName == "ChaseState" || fsmName == "PatrolState") && speed > 0.5f && !loop && nt > 1.05f) seg.Frozen += dt;
            if (!chaseShot && fsmName == "ChaseState" && speed > 1.5f && tIn > 0.4f)
            {
                chaseShot = true;
                Capture(m, p.transform, Path.Combine(s_dir, $"{name}_move.png"), height);
            }

            // 휘두름 정점 · 피해
            float best = 0f;
            for (int i = 0; i < bones.Count; i++)
            {
                Vector3 lp = m.transform.InverseTransformPoint(bones[i].position);
                best = Mathf.Max(best, (lp - prevB[i]).magnitude / dt);
                prevB[i] = lp;
            }
            if (!IsPassive(fsmName) && tIn > 0.05f && best > seg.PeakSpeed) { seg.PeakSpeed = best; seg.Peak = tIn; seg.PeakNt = nt; }
            if (seg.Dmg < 0f && rt != null && rt.AttackHitDealt && !IsPassive(fsmName)) { seg.Dmg = tIn; seg.DmgNt = nt; }

            // 행동 한 장 — 피해 순간, 피해가 없으면 구간 0.6초
            bool action = !IsPassive(fsmName);
            string shotName = seg.Pattern ?? fsmName;
            if (action && !seg.Shot && (seg.Dmg >= 0f || tIn >= 0.6f))
            {
                seg.Shot = true;
                CollectFx(seg, m);
                shotsByName.TryGetValue(shotName, out int n);
                if (n < ShotsPerName)
                {
                    shotsByName[shotName] = n + 1;
                    Capture(m, p.transform, Path.Combine(s_dir, $"{name}_{Safe(shotName)}_{n + 1}.png"), height);
                }
            }

            // 플레이어 자리 바꾸기(무적 · HP 채움)
            if (Time.time >= nextMove && dists.Length > 1)
            {
                nextMove = Time.time + moveGap;
                float d = dists[step++ % dists.Length];
                if (FindSpot(m, p.transform.position, d, out var to)) { p.transform.position = to; if (p.TryGetComponent<Rigidbody>(out var rb)) { rb.position = to; rb.linearVelocity = Vector3.zero; } }
            }
            else if (Time.time >= nextMove && dists.Length == 1)
            {
                nextMove = Time.time + moveGap;
                if (FindSpot(m, p.transform.position, dists[0], out var to)) { p.transform.position = to; if (p.TryGetComponent<Rigidbody>(out var rb)) { rb.position = to; rb.linearVelocity = Vector3.zero; } }
            }
            var rs = p.RuntimeStats;
            if (rs != null && rs.Hp < rs.MaxHp) rs.SetHp(rs.MaxHp);
            // 휘청 확인(고친 뒤 B) — 11초부터 0.12초마다 여덟 번 쳐서 정예 포이즈를 깬다
            if (s_run == Run.FixB && Time.time - start > 11f && staggerHits > 0 && Time.time >= nextStaggerHit)
            {
                staggerHits--;
                nextStaggerHit = Time.time + 0.12f;
                MonsterPoise.PendingHitWeight = 1f;   // 평타 한 대 무게 — 기본(2차 피해 무게)으론 여덟 번에도 안 깨졌다
                m.TakeDamage(1f, p.gameObject);
                MonsterPoise.PendingHitWeight = MonsterPoise.RestingWeight;
            }
            // 비숍 — 체력 50% 아래에서 방패 막기 → 반격(로그 「[BishopShield]」)
            if (!bishopHalf && name.StartsWith("BishopKnight") && Time.time - start > 3f)
            {
                bishopHalf = true;
                m.TakeDamage(m.EffectiveMaxHp * 0.52f, p.gameObject);
            }
        }
        if (seg != null) { seg.End = Time.time; segs.Add(seg); }
        Report(name, segs);
        if (m != null && !m.IsDead) m.TakeSynergyDamage(m.CurrentHp + 10, null, 1f);
        await UniTask.Delay(1500, ignoreTimeScale: true, cancellationToken: ct);
        if (m != null && m.isActiveAndEnabled) Managers.ObjectPooler.Despawn(m.gameObject);
    }

    private static void Report(string name, List<Seg> segs)
    {
        var clipsByPattern = new Dictionary<string, HashSet<string>>();
        foreach (var s in segs)
        {
            bool action = !IsPassive(s.Fsm);
            bool stagger = s.Fsm.Contains("GetHit") && s.End - s.Start > 1f;   // 휘청(1.5초)
            if (!action && !stagger && s.Slide < 0.15f && s.Frozen < 0.2f && s.BodyMax < 0.35f) continue;
            float len = 0f;
            string id = s.Pattern != null ? $"{s.Fsm}[{s.Pattern}]" : s.Fsm;
            var sb = new StringBuilder($"  {id,-34} {s.End - s.Start:0.00}초 · 애니 {string.Join(" → ", s.Anims)}");
            if (s.Dmg >= 0f) sb.Append($" · 피해 {s.Dmg:0.00}초(애니 {s.DmgNt:0.00})");
            if (s.Peak >= 0f) sb.Append($" · 휘두름 정점 {s.Peak:0.00}초(애니 {s.PeakNt:0.00} · {s.PeakSpeed:0.0} m/s)");
            if (s.Dmg >= 0f && s.Peak >= 0f && Mathf.Abs(s.Dmg - s.Peak) > 0.15f) sb.Append($" ⚠피해-정점 {s.Dmg - s.Peak:+0.00;-0.00}초");
            if (s.MaxMove > 0.3f) sb.Append($" · 최대 이동 {s.MaxMove:0.0} m/s");
            if (s.Slide >= 0.15f) sb.Append($" ⚠미끄러짐 {s.Slide:0.00}초({s.SlideAnim})");
            if (s.Frozen >= 0.2f) sb.Append($" ⚠이동 중 애니 멈춤 {s.Frozen:0.00}초");
            if (s.BodyMax >= 0.35f) sb.Append($" ⚠몸 어긋남 최대 {s.BodyMax:0.00} m(앞 {s.BodyAt.z:0.00} · 옆 {s.BodyAt.x:0.00})");
            if (s.Fx.Count > 0) sb.Append($" · 이펙트 {string.Join(" | ", s.Fx)}");
            s_sb.AppendLine(sb.ToString());
            if (action && s.Pattern != null)
            {
                if (!clipsByPattern.TryGetValue(s.Pattern, out var set)) clipsByPattern[s.Pattern] = set = new HashSet<string>();
                foreach (var an in s.Anims) set.Add(an.Split('(')[0]);
            }
            _ = len;
        }
        // 다른 패턴이 같은 애니만 쓰면 — 두 기술이 똑같아 보인다
        var keys = clipsByPattern.Keys.ToList();
        for (int i = 0; i < keys.Count; i++)
        for (int j = i + 1; j < keys.Count; j++)
            if (clipsByPattern[keys[i]].SetEquals(clipsByPattern[keys[j]]))
                s_sb.AppendLine($"  ⚠같은 동작: 「{keys[i]}」 = 「{keys[j]}」 ({string.Join(", ", clipsByPattern[keys[i]])})");
        int actions = segs.Count(s => !IsPassive(s.Fsm));
        if (actions == 0) s_sb.AppendLine($"  행동 0회 — 상태 {string.Join(" ", segs.Select(s => s.Fsm).Distinct())}");
    }

    // ── 이펙트 — 구간 시작 뒤 새로 뜬 입자계(맨 위 입자계 묶음) · 자리(몬스터 기준 앞 · 옆 · 높이) · 크기 ──
    private static HashSet<int> Snapshot()
    {
        var set = new HashSet<int>();
        foreach (var ps in UnityEngine.Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None)) set.Add(ps.GetInstanceID());
        return set;
    }

    private static void CollectFx(Seg seg, MonsterBase m)
    {
        var roots = new Dictionary<Transform, Bounds>();
        foreach (var ps in UnityEngine.Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
        {
            if (ps == null || seg.PsBefore.Contains(ps.GetInstanceID()) || ps.particleCount == 0) continue;
            Transform r = ps.transform;
            while (r.parent != null && r.parent.GetComponent<ParticleSystem>() != null) r = r.parent;
            var parts = new ParticleSystem.Particle[ps.particleCount];
            int n = ps.GetParticles(parts);
            bool local = ps.main.simulationSpace == ParticleSystemSimulationSpace.Local;
            for (int i = 0; i < n; i++)
            {
                Vector3 wp = local ? ps.transform.TransformPoint(parts[i].position) : parts[i].position;
                if (roots.TryGetValue(r, out var b)) { b.Encapsulate(wp); roots[r] = b; }
                else roots[r] = new Bounds(wp, Vector3.zero);
            }
        }
        foreach (var kv in roots.Take(4))
        {
            Vector3 local = m.transform.InverseTransformPoint(kv.Value.center);
            seg.Fx.Add($"{kv.Key.name.Replace("(Clone)", "")} 앞 {local.z:0.0} 옆 {local.x:0.0} 높이 {local.y:0.0} · 폭 {Mathf.Max(kv.Value.size.x, kv.Value.size.z):0.0} m");
        }
    }

    // 8: 전투방 게임 카메라로 이펙트 후보 — 판정 크기 배율(BossFxReachEditor 「FX Fit」 결과) · 0.4 · 0.9 · 1.4초
    private const string FxRoot = "Assets/RelicFairy/_Imported/EffectSource/";
    private const string BhFx = "Assets/RelicFairy/Characters/Monster/Monster/Beholder/Effect/";
    private const string BkFx = "Assets/RelicFairy/Characters/Monster/Monster/BlackKnight/Effect/";
    // (태그, 경로, 판정 크기 m) — 배율은 그 자리에서 배율 1로 재서 정한다(보이는 양 90% · 정점/구간 최대 중 큰 쪽)
    private static readonly (string tag, string path, float judge)[] FxLookItems =
    {
        ("bh_electro", FxRoot + "Hovl Studio/Magic circles/Prefabs/Magic circle electro.prefab", 5f),
        ("bh_lfield",  FxRoot + "SpecialSkillsEffectsPack/AllEffects/EffectsSet_1(NotScriptBased)/Effects/Effect_52_LightningField/Effect_52_LightningField.prefab", 5f),
        ("bh_dark",    FxRoot + "Spells Pack/LWRP(URP)/Particles_LWRP/Prefabs/Auras/Aura_Dark_LWRP.prefab", 5f),
        ("bk_prick2",  BkFx + "Prick 2.prefab", 2.5f),
        ("bk_slash14", BkFx + "Sword Slash 14.prefab", 2.2f),
        ("bk_slash15", BkFx + "Sword Slash 15.prefab", 2.2f),
        ("bk_combo6",  BkFx + "Sword Slash Combo 6.prefab", 2.2f),
    };

    private static async UniTask FxLookAsync(string tag, string path, float judge, PlayerController p, CancellationToken ct)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null) { s_sb.AppendLine($"  {tag}: 없음"); return; }
        var cam = Camera.main;
        Vector3 fwd = cam != null ? Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized : p.transform.forward;
        Vector3 at = p.transform.position + fwd * 4f;
        if (NavMesh.SamplePosition(at, out var hit, 2f, NavMesh.AllAreas)) at = hit.position;
        // 배율 1로 재고 → 판정 크기 배율
        var before = EffectReachProbe.Snapshot();
        var probe = UnityEngine.Object.Instantiate(prefab, at + Vector3.up * 0.05f, Quaternion.LookRotation(fwd));
        foreach (var ps in probe.GetComponentsInChildren<ParticleSystem>(true)) { var m = ps.main; m.scalingMode = ParticleSystemScalingMode.Hierarchy; }
        var r1 = await EffectReachProbe.ObserveAsync(before, at, fwd, 1.5f, ct);
        UnityEngine.Object.Destroy(probe);
        float reach = Mathf.Max(r1.Radius, r1.RadiusMax);
        float scale = reach > 0.01f ? judge / reach : 1f;
        s_sb.AppendLine($"  {tag}: 배율 1 반경 {r1.Radius:0.00} · 최대 {r1.RadiusMax:0.00} · 앞 {r1.Forward:0.00} · 옆 {r1.Lateral:0.00} → 판정 {judge:0.0} m 배율 {scale:0.00}");
        await UniTask.Delay(500, ignoreTimeScale: true, cancellationToken: ct);
        var go = UnityEngine.Object.Instantiate(prefab, at + Vector3.up * 0.05f, Quaternion.LookRotation(fwd));
        go.transform.localScale = Vector3.one * scale;
        foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true)) { var m = ps.main; m.scalingMode = ParticleSystemScalingMode.Hierarchy; }
        float t = 0f;
        foreach (float s in new[] { 0.4f, 0.9f, 1.4f })
        {
            await UniTask.Delay(TimeSpan.FromSeconds(s - t), ignoreTimeScale: true, cancellationToken: ct);
            t = s;
            ScreenCapture.CaptureScreenshot(Path.Combine(s_dir, $"{tag}_{s * 10:00}.png"));
        }
        await UniTask.Delay(400, ignoreTimeScale: true, cancellationToken: ct);
        UnityEngine.Object.Destroy(go);
        s_sb.AppendLine($"  {tag} ×{scale:0.00} — 찍음");
        await UniTask.Delay(600, ignoreTimeScale: true, cancellationToken: ct);
    }

    // ── 도우미 ─────────────────────────────────────────────
    private static Dictionary<int, string> StateNames(Animator a)
    {
        var map = new Dictionary<int, string>();
        if (a == null) return map;
        var rac = a.runtimeAnimatorController;
        if (rac is AnimatorOverrideController oc) rac = oc.runtimeAnimatorController;
        if (rac is UnityEditor.Animations.AnimatorController ac)
            foreach (var layer in ac.layers) Walk(layer.stateMachine, map);
        return map;
    }

    private static void Walk(UnityEditor.Animations.AnimatorStateMachine sm, Dictionary<int, string> map)
    {
        foreach (var cs in sm.states) map[cs.state.nameHash] = cs.state.name;
        foreach (var sub in sm.stateMachines) Walk(sub.stateMachine, map);
    }

    private static bool Has(Animator a, string state)
        => a != null && !string.IsNullOrEmpty(state) && a.HasState(0, Animator.StringToHash(state));

    private static bool IsLoco(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        string l = s.ToLowerInvariant();
        foreach (var k in Locomotion) if (l.Contains(k)) return true;
        return false;
    }

    private static float Height(MonsterBase m)
    {
        bool any = false; Bounds b = default;
        foreach (var r in m.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
        }
        return any ? Mathf.Max(0.5f, b.size.y) : 1.8f;
    }

    private static void HoldRoomMonsters(Transform player)
    {
        var fw = player.forward; fw.y = 0f; fw.Normalize();
        foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
        {
            if (m == null || m.IsDead) continue;
            m.Status.ApplyCc(HoldCc, 9999f);
            Vector3 pos = player.position - fw * 16f;
            if (NavMesh.SamplePosition(pos, out var hit, 4f, NavMesh.AllAreas)) pos = hit.position;
            if (m.TryGetComponent<NavMeshAgent>(out var ag) && ag.isActiveAndEnabled && ag.isOnNavMesh) ag.Warp(pos);
        }
    }

    private static async UniTask<MonsterBase> Spawn(string key, Vector3 pos)
    {
        if (NavMesh.SamplePosition(pos, out var hit, 4f, NavMesh.AllAreas)) pos = hit.position;
        try { return await Managers.ObjectPooler.SpawnAsync<MonsterBase>(key, ObjectPoolerManager.PoolType.Monster, pos, Quaternion.identity); }
        catch (Exception e) { s_sb.AppendLine($"  (소환 실패 {key}: {e.Message})"); return null; }
    }

    // 몬스터에서 d m — 그 몬스터 에이전트 NavMesh 위 · 곧게 닿는 자리(벽 너머 · 방 밖 아님)
    private static bool FindSpot(MonsterBase m, Vector3 playerPos, float d, out Vector3 pos)
    {
        Vector3 from = m.transform.position;
        var filter = new NavMeshQueryFilter { agentTypeID = 0, areaMask = NavMesh.AllAreas };
        if (m.TryGetComponent<NavMeshAgent>(out var ag)) filter.agentTypeID = ag.agentTypeID;
        Vector3 away = playerPos - from; away.y = 0f;
        away = away.sqrMagnitude > 0.01f ? away.normalized : m.transform.forward;
        foreach (float scale in new[] { 1f, 0.75f, 0.5f })
            for (int i = 0; i < 9; i++)
            {
                float yaw = (i + 1) / 2 * 40f * (i % 2 == 0 ? 1f : -1f);
                Vector3 want = from + Quaternion.Euler(0f, yaw, 0f) * away * (d * scale);
                if (!NavMesh.SamplePosition(want, out var hit, 1.5f, filter)) continue;
                if (NavMesh.Raycast(from, hit.position, out _, filter)) continue;
                pos = hit.position;
                return true;
            }
        pos = playerPos;
        return false;
    }

    private static Func<Vector3, UniTask<MonsterBase>> SummonFunc()
    {
        var spawner = UnityEngine.Object.FindFirstObjectByType<MonsterSpawner>();
        var mi = typeof(MonsterSpawner).GetMethod("SpawnSummonedMinionAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        if (spawner == null || mi == null) return null;
        return (Func<Vector3, UniTask<MonsterBase>>)Delegate.CreateDelegate(typeof(Func<Vector3, UniTask<MonsterBase>>), spawner, mi);
    }

    // 몬스터 · 플레이어를 옆에서 — 벽에 막히면 반대쪽 · 그래도 막히면 위에서
    private static void Capture(MonsterBase m, Transform player, string path, float height)
    {
        if (m == null || player == null) return;
        var camGo = new GameObject("~EliteAuditCam");
        var cam = camGo.AddComponent<Camera>();
        if (Camera.main != null) cam.CopyFrom(Camera.main);
        cam.enabled = false; cam.fieldOfView = 45f;
        Vector3 mp = m.transform.position, pp = player.position;
        Vector3 line = Flat(mp - pp);
        float gap = line.magnitude;
        Vector3 dir = gap > 0.1f ? line / gap : m.transform.forward;
        Vector3 mid = (mp + pp) * 0.5f + Vector3.up * (height * 0.45f);
        Vector3 side = Vector3.Cross(Vector3.up, dir);
        float back = Mathf.Max(gap * 0.9f, height * 1.6f) + 2.5f;
        Vector3 want = mid + side * back + Vector3.up * (height * 0.4f + 0.8f);
        if (Physics.Linecast(mid, want, out _, ~0, QueryTriggerInteraction.Ignore))
        {
            Vector3 other = mid - side * back + Vector3.up * (height * 0.4f + 0.8f);
            want = !Physics.Linecast(mid, other, out _, ~0, QueryTriggerInteraction.Ignore) ? other : mid + Vector3.up * (back + 2f) - dir * 0.01f;
        }
        cam.transform.position = want;
        cam.transform.LookAt(mid);
        const int w = 960, h = 540;
        var rtex = new RenderTexture(w, h, 24);
        cam.targetTexture = rtex;
        cam.Render();
        cam.targetTexture = null;
        var prev = RenderTexture.active;
        RenderTexture.active = rtex;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        File.WriteAllBytes(path, tex.EncodeToPNG());
        UnityEngine.Object.Destroy(rtex);
        UnityEngine.Object.Destroy(tex);
        UnityEngine.Object.Destroy(camGo);
    }

    private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

    private static Transform FindDeep(Transform t, string name)
    {
        if (t.name == name) return t;
        foreach (Transform c in t) { var r = FindDeep(c, name); if (r != null) return r; }
        return null;
    }
    private static string Safe(string s) { foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_'); return s.Replace(' ', '_'); }
}
#endif
