#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using RelicFairy.Monster;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-18 사용자 「보스의 애니메이션과 이동이 부자연스러운 게 최악 — 공격 후 모션이 이상하게 돌아간다든지 제자리 걷기를 한다든지 체크하면서 나아가자」 —
/// 보스 · 중간 보스 애니 · 이동 실측. 전투 시뮬(BossFightSimEditor)과 같이 돌린다: 매 프레임 상태 · 클립 · 수평 속도 · 회전 · 몸통 뼈를 적고 다섯 가지를 찾는다.
///   · 제자리 걷기 — 이동 클립(walk · run · move · strafe …)인데 수평 속도가 InPlaceSpeed 아래로 InPlaceHold 넘게.
///   · 미끄러짐 — 서 있는 · 공격 클립인데 SlideSpeed 넘게 SlideHold 넘게 움직임(돌진 · 도약처럼 일부러 움직이는 상태는 표에서 사람이 거른다).
///   · 이상한 회전 — 초당 SpinRate 넘는 회전이 두 프레임 넘게, 또는 한 프레임 SnapYaw 넘게 튐(공격 뒤 0.6초 안이면 따로 표시).
///   · 끊김 — 크로스페이드 없이(직전 프레임 전이 아님) 상태가 바뀜 · 앞 상태가 끝까지(0.95) 안 갔을 때.
///   · 자세 튐 — 몸통 뼈(사람형 = 엉덩이 · 아니면 가장 큰 스킨드 메시 뿌리 뼈)가 몸 기준으로 한 프레임 PopDist 넘게 튐(전이 중 제외).
/// 결과: Logs/boss_fight_sim/boss_anim_*/report.txt · frames.csv · 종류마다 사진 3장. 로그 「[BossAnim] 끝」.
/// </summary>
public static class BossAnimAuditEditor
{
    private const float Duration     = 150f;
    private const float InPlaceSpeed = 0.25f;
    private const float InPlaceHold  = 0.35f;
    private const float SlideSpeed   = 1.2f;
    private const float SlideHold    = 0.2f;
    private const float SpinRate     = 360f;
    private const float SnapYaw      = 45f;
    private const float PopDist      = 0.35f;
    private const float AfterAttack  = 0.6f;
    private const int   ShotsPerKind = 3;
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static readonly string[] MoveWords = { "walk", "run", "move", "strafe", "locomotion", "chase", "jog", "trot" };
    private static readonly string[] StillWords = { "idle", "stand", "hover", "breath", "roar", "taunt", "angry" };

    private static bool s_midboss;

    [MenuItem("RelicFairy/Boss/10-18 Boss Anim Audit (Play)")]
    private static void RunBoss()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[BossAnim] 플레이 모드(보스방)에서"); return; }
        s_midboss = false;
        RunAsync().Forget();
    }

    [MenuItem("RelicFairy/Boss/10-18 Boss Anim Audit - Midboss (Play)")]
    private static void RunMid()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[BossAnim] 플레이 모드에서"); return; }
        s_midboss = true;
        RunAsync().Forget();
    }

    private sealed class Hit { public float T; public string What; public string Clip; public string State; }

    private static async UniTaskVoid RunAsync()
    {
        string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "boss_fight_sim", $"boss_anim_{DateTime.Now:MMdd_HHmmss}");
        Directory.CreateDirectory(dir);
        var sb  = new StringBuilder();
        var csv = new StringBuilder("t,fsm,clip,nt,trans,speed,yawRate,pop\n");
        var hits = new List<Hit>();
        var shots = new Dictionary<string, int>();
        string bossName = "?";
        try
        {
            MonsterBase boss = null;
            float until = Time.realtimeSinceStartup + 120f;
            while (boss == null || !boss.isActiveAndEnabled)
            {
                if (Time.realtimeSinceStartup > until) { sb.AppendLine("보스를 120초 안에 못 찾았다"); return; }
                boss = s_midboss
                    ? UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None).FirstOrDefault(m => m.name.Contains("BullTank") && !m.IsDead)
                    : UnityEngine.Object.FindFirstObjectByType<BossSpawner>()?.SpawnedBoss;
                await UniTask.Delay(250, DelayType.Realtime);
            }
            bossName = boss.name;
            var anim = boss.GetComponentInChildren<Animator>();
            Transform bone = null;
            if (anim != null && anim.isHuman) bone = anim.GetBoneTransform(HumanBodyBones.Hips);
            if (bone == null)
            {
                var smr = boss.GetComponentsInChildren<SkinnedMeshRenderer>().OrderByDescending(r => r.bounds.size.sqrMagnitude).FirstOrDefault();
                bone = smr != null ? smr.rootBone : null;
            }
            sb.AppendLine($"{bossName} — 애니메이터 {(anim != null ? anim.runtimeAnimatorController?.name : "없음")} · 몸통 뼈 {(bone != null ? bone.name : "없음")} · {Duration}초");

            Transform tr = boss.transform;
            Vector3 lastPos = tr.position; float lastYaw = tr.eulerAngles.y;
            Vector3 lastBone = bone != null ? tr.InverseTransformPoint(bone.position) : Vector3.zero;
            int lastHash = 0; bool lastTrans = false; float lastNt = 0f; string lastClip = "";
            float inPlace = 0f, slide = 0f, lastAttackEnd = -99f; int spinFrames = 0;
            float t0 = Time.time;
            await UniTask.Yield(PlayerLoopTiming.LastPostLateUpdate);
            while (boss != null && !boss.IsDead && Time.time - t0 < Duration)
            {
                await UniTask.Yield(PlayerLoopTiming.LastPostLateUpdate);
                float dt = Time.deltaTime;
                if (dt <= 0f || boss == null) continue;   // 멈춤 · 팝업
                float t = Time.time - t0;
                Vector3 p = tr.position;
                Vector3 d = p - lastPos; d.y = 0f;
                float speed = d.magnitude / dt;
                float yaw = tr.eulerAngles.y;
                float dyaw = Mathf.Abs(Mathf.DeltaAngle(lastYaw, yaw));
                float yawRate = dyaw / dt;
                string fsm = StateOf(boss);
                string clip = "-"; float nt = 0f; int hash = 0; bool trans = false;
                if (anim != null && anim.isActiveAndEnabled)
                {
                    var info = anim.GetCurrentAnimatorStateInfo(0);
                    hash = info.fullPathHash; nt = info.normalizedTime; trans = anim.IsInTransition(0);
                    var clips = anim.GetCurrentAnimatorClipInfo(0);
                    if (clips.Length > 0) clip = clips.OrderByDescending(c => c.weight).First().clip.name;
                }
                float pop = 0f;
                if (bone != null)
                {
                    Vector3 lb = tr.InverseTransformPoint(bone.position);
                    pop = (lb - lastBone).magnitude;
                    lastBone = lb;
                }
                string lc = clip.ToLowerInvariant();
                bool moving = MoveWords.Any(w => lc.Contains(w)) && !StillWords.Any(w => lc.Contains(w));
                bool attackish = fsm.Contains("Attack") || fsm.Contains("Pattern") || lc.Contains("attack") || lc.Contains("slash") || lc.Contains("strike");

                // 제자리 걷기
                inPlace = moving && speed < InPlaceSpeed ? inPlace + dt : 0f;
                if (inPlace >= InPlaceHold && inPlace - dt < InPlaceHold) Add("제자리 걷기", $"속도 {speed:0.00} m/s");
                // 미끄러짐
                slide = !moving && speed > SlideSpeed ? slide + dt : 0f;
                if (slide >= SlideHold && slide - dt < SlideHold) Add(attackish ? "미끄러짐(공격 중)" : "미끄러짐", $"속도 {speed:0.0} m/s");
                // 회전
                spinFrames = yawRate > SpinRate ? spinFrames + 1 : 0;
                bool afterAtk = t - lastAttackEnd < AfterAttack;
                if (dyaw > SnapYaw) Add(afterAtk ? "회전 튐(공격 뒤)" : "회전 튐", $"한 프레임 {dyaw:0}°");
                else if (spinFrames == 3) Add(afterAtk ? "빠른 회전(공격 뒤)" : "빠른 회전", $"{yawRate:0}°/s");
                // 끊김
                if (lastHash != 0 && hash != lastHash && !lastTrans && !trans && lastNt < 0.95f)
                    Add("끊김", $"{lastClip} {lastNt:0.00} → {clip}");
                // 자세 튐
                if (!trans && !lastTrans && hash == lastHash && pop > PopDist) Add("자세 튐", $"몸통 뼈 {pop:0.00} m/프레임");

                if (lastHash != 0 && hash != lastHash && (lastClip.ToLowerInvariant().Contains("attack") || lastClip.ToLowerInvariant().Contains("slash"))) lastAttackEnd = t;
                csv.AppendLine($"{t:0.000},{fsm},{clip},{nt:0.00},{(trans ? 1 : 0)},{speed:0.00},{yawRate:0},{pop:0.000}");
                lastPos = p; lastYaw = yaw; lastHash = hash; lastTrans = trans; lastNt = nt; lastClip = clip;

                void Add(string what, string detail)
                {
                    hits.Add(new Hit { T = t, What = $"{what} — {detail}", Clip = clip, State = fsm });
                    shots.TryGetValue(what, out int n);
                    if (n < ShotsPerKind)
                    {
                        shots[what] = n + 1;
                        ScreenCapture.CaptureScreenshot(Path.Combine(dir, $"{Safe(what)}_{n}_t{t:0.0}.png"));
                    }
                }
            }
            sb.AppendLine($"관찰 {Time.time - t0:0.0}초{(boss == null || boss.IsDead ? " · 보스 사망" : "")}");
        }
        catch (Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            sb.AppendLine();
            foreach (var g in hits.GroupBy(h => h.What.Split('—')[0].Trim()).OrderByDescending(g => g.Count()))
            {
                sb.AppendLine($"■ {g.Key} {g.Count()}회");
                foreach (var cg in g.GroupBy(h => (h.Clip, h.State)).OrderByDescending(c => c.Count()).Take(8))
                {
                    var first = cg.First();
                    sb.AppendLine($"   {cg.Count(),3}회 · 클립 {cg.Key.Clip} · 상태 {cg.Key.State} · 예 t={first.T:0.0} {first.What.Split('—').Last().Trim()}");
                }
            }
            if (hits.Count == 0) sb.AppendLine("이상 0건");
            File.WriteAllText(Path.Combine(dir, "report.txt"), sb.ToString(), Encoding.UTF8);
            File.WriteAllText(Path.Combine(dir, "frames.csv"), csv.ToString(), Encoding.UTF8);
            Debug.Log($"[BossAnim] 끝 — {bossName} · {dir}");
        }
    }

    private static string StateOf(MonsterBase boss)
    {
        var fsm = typeof(MonsterBase).GetField("_fsm", Any)?.GetValue(boss);
        return (fsm?.GetType().GetProperty("CurrentType", Any)?.GetValue(fsm) as Type)?.Name ?? "-";
    }

    private static string Safe(string s)
    {
        var sb = new StringBuilder();
        foreach (char c in s) sb.Append(char.IsLetterOrDigit(c) ? c : '_');
        return sb.ToString();
    }
}
#endif
