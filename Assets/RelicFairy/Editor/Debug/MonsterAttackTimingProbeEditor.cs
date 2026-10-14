#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using RelicFairy.Monster;
using UnityEditor;
using UnityEngine;

/// <summary>
/// [실측 · 플레이 중] 일반 몬스터 공격 타이밍 — 피해가 들어가는 순간과 몸이 실제로 휘두르는 순간(뼈 속도 정점)을 잰다.
/// 사용자(10-07): 「일반 몬스터가 언제 공격하는지 전혀 알 수 없다 — 공격 전에 작은 반짝임」.
/// 패턴 공격은 시전 이펙트가 나오는 순간 피해를 함께 넣는다(PatternAttackOverrideSO) — castVfxDelay 0.1인 7종은 damageDelay와 무관하게
/// 시작 0.1초에 맞는다. 테스트 허브에서 런을 열고 첫 방에 들어서면 플레이어(무적) 앞 1.6 m에 한 종씩 불러 8초 동안 공격을 받는다.
/// 결과: Temp/monster_attack_timing.txt + 피해 순간 화면 Temp/ui_shots/MT_*.png
/// </summary>
public static class MonsterAttackTimingProbeEditor
{
    private const string Root     = "RelicFairy/Debug/몬스터 공격 예고/";
    private const string AutoRoot = "RelicFairy/Debug/런 구조 자동 실측/";
    private const string OutPath  = "Temp/monster_attack_timing.txt";
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private const float WatchSeconds = 7f;

    // 한 번에 7종까지(실측은 바뀐 칸만 · 7칸 이하)
    // 1: castVfxDelay 0.1 — 피해가 시작 0.1초에 들어가던 7종(10-07 고침: 피해 = 휘두름 정점)
    private static readonly string[] Changed7 =
    {
        "Skeleton/Skeleton", "Orc/Orc", "Werewolf/Werewolf", "LizardWarrior/LizardWarrior",
        "RatAssassin/RatAssassin", "BishopKnight/BishopKnight", "BlackKnight/BlackKnight",
    };
    // 2 · 3: 나머지 — 이펙트 앞당김(castVfxLeadTime 0.15)에 묶여 피해가 damageDelay보다 0.15초 일렀다
    private static readonly string[] GroupA =
    {
        "BattleBee/BattleBee", "MushroomAngry/MushroomAngry", "Slime/Slime", "Spider/Spider",
        "Fishman/Fishman", "MonsterPlant/MonsterPlant", "StingRay/StingRay",
    };
    private static readonly string[] GroupB =
    {
        "Specter/Specter", "Golem/Golem", "FlyingDemon/FlyingDemon", "Salamander/Salamander",
        "Beholder/Beholder", "FairyBat/FairyBat", "LichSkeleton/LichSkeleton",
    };

    // 4: 공격 예고 반짝임 후보(_Imported) — 해골 머리에서 게임 카메라로 0.03 · 0.08 · 0.15 · 0.3초
    private const string Fx = "Assets/RelicFairy/_Imported/EffectSource/";
    private const string MagicPig = Fx + "Magic Pig Games (Infinity PBR)/Shared Files/Magic Spells & Particles/Particle Components/";
    private static readonly (string path, float scale)[] GlintCandidates =
    {
        (Fx + "Hovl Studio/RPG VFX Bundle/Prefabs/Magic buffs and hits/Yellow Flash.prefab", 1f),
        (MagicPig + "PComponent Spiked Star.prefab", 1f),
        (MagicPig + "PComponent Star Burst.prefab", 1f),
        (MagicPig + "PComponent Slow Flash.prefab", 1f),
        (Fx + "Hovl Studio/AAA Projectiles Vol 1/Prefabs/Flash 22 cute star.prefab", 1f),
        (Fx + "MasterStylizedProjectiles/Projectiles/Star/Prefabs/StarMuzzle.prefab", 1f),
        (Fx + "Hovl Studio/AAA Projectiles Vol 1/Prefabs/Flash 5 red.prefab", 1f),
    };

    // 5: 두 번째 고침(10-07) — 1챕터 · 나머지 실측에서 휘두름과 어긋난 5종
    private static readonly string[] Fixed2 =
    {
        "Fishman/Fishman", "Specter/Specter", "Golem/Golem", "FlyingDemon/FlyingDemon", "Salamander/Salamander",
    };

    // 6: 사망 연출 — 작은 · 중간 · 큰 몸 한 마리씩 처치하고 실시간 0.03~2.3초를 찍는다(사용자 10-06: 「사망 이펙트를 더 간단하면서 손맛 있게」)
    private static readonly string[] DeathKeys = { "Slime/Slime", "Skeleton/Skeleton", "Orc/Orc" };
    // 정지(0.09초) 동안은 PNG 한 장이 약 50 ms라 찍을수록 프레임이 멈췄다(10-08 진단: 0.03 · 0.08초가 같은 프레임) →
    // 정지 구간은 프레임마다 숫자만 남기고, 섬광 정점(0.06초) 한 장만 찍는다.
    private static readonly float[]  DeathShotTimes = { 0.06f, 0.3f, 0.6f, 1.2f, 1.8f };

    // 7: 피격 이펙트 후보(10-08 사용자 「피격 이펙트를 공격 종류에 어느 정도 맞춘다」) — 대검 = 무거운 타격, 가웨인 Q = 태양
    private const string Travis = Fx + "Travis Game Assets/Hit Impact Effects/Prefabs/Hits/";
    private const string Hovl1  = Fx + "Hovl Studio/AAA Projectiles Vol 1/Prefabs/";
    private static readonly (string path, float scale)[] HitCandidates =
    {
        ("Assets/RelicFairy/Prefabs/UseEffect/PlayerEffect/SwordHitImpact.prefab", 0.84f),   // 지금 대검 3타
        (Travis + "Hit_01.prefab", 1f),
        (Travis + "Hit_02.prefab", 1f),
        (Travis + "Hit_03.prefab", 1f),
        (Travis + "Hit_04.prefab", 1f),
        (Hovl1 + "Hit 16 fire.prefab", 1f),
        ("Assets/RelicFairy/Prefabs/Effects/HitEffect_02.prefab", 1f),                    // 지금 스킬 · 유물 Q 공용
    };

    // 8: 사망 이펙트 후보(사용자 10-06 「더 간단하면서 손맛 있게」) — 지금 DieEffect_01(초록 줄기 Smoke Flash)과 비교
    private static readonly (string path, float scale)[] DeathCandidates =
    {
        ("Assets/RelicFairy/Prefabs/Effects/DieEffect_01.prefab", 1f),
        (Hovl1 + "Hit 17 nova violet.prefab", 1f),
        (Hovl1 + "Hit 19 circle bomb.prefab", 1f),
        (Hovl1 + "Hit 26 blue crystal.prefab", 1.5f),
        (Hovl1 + "Hit 3 black fire.prefab", 1.2f),
        (Hovl1 + "Hit 22 cute star.prefab", 1.5f),
        (MagicPig + "PComponent Smoke Puff.prefab", 1f),
    };

    private static bool     s_armed;
    private static (string path, float scale)[] s_cand;
    private static string   s_candPrefix;
    private static bool     s_candAtHead;
    private static string[] s_keys = Changed7;
    private static bool     s_glintCand;
    private static bool     s_deathCap;
    private static bool     s_outlineCap;   // 9: 범위 예고(얇은 외곽선) — 공격 준비 중간을 가까이서

    // 9: 범위 예고 확인(10-11) — 작은 근접 4종 + 넓은 칸(Around8 · FrontWide3 쪽) 2종
    private static readonly string[] OutlineKeys =
    {
        "Slime/Slime", "MushroomAngry/MushroomAngry", "MonsterPlant/MonsterPlant", "StingRay/StingRay", "Snail/Snail", "Cactus/Cactus",
    };

    [MenuItem(Root + "1 공격 타이밍 — 바뀐 7종 (테스트 허브, 플레이 중)")]
    private static void BeginTiming() => Begin(Changed7);

    [MenuItem(Root + "2 공격 타이밍 — 1챕터 7종 (테스트 허브, 플레이 중)")]
    private static void BeginTimingA() => Begin(GroupA);

    [MenuItem(Root + "3 공격 타이밍 — 나머지 7종 (테스트 허브, 플레이 중)")]
    private static void BeginTimingB() => Begin(GroupB);

    [MenuItem(Root + "5 공격 타이밍 — 두 번째 고친 5종 (테스트 허브, 플레이 중)")]
    private static void BeginTimingFixed2() => Begin(Fixed2);

    [MenuItem(Root + "9 범위 예고 근접 촬영 (테스트 허브, 플레이 중)")]
    private static void BeginOutlineCap() { Begin(OutlineKeys); s_outlineCap = true; }

    [MenuItem(Root + "6 사망 연출 촬영 (테스트 허브, 플레이 중)")]
    private static void BeginDeathCap() { Begin(DeathKeys); s_deathCap = true; }

    [MenuItem(Root + "4 반짝임 후보 비교 (테스트 허브, 플레이 중)")]
    private static void BeginGlintCand() { Begin(Changed7); s_glintCand = true; s_cand = GlintCandidates; s_candPrefix = "MG_"; s_candAtHead = true; }

    [MenuItem(Root + "8 사망 이펙트 후보 비교 (테스트 허브, 플레이 중)")]
    private static void BeginDeathCand() { Begin(Changed7); s_glintCand = true; s_cand = DeathCandidates; s_candPrefix = "MX_"; s_candAtHead = false; }

    [MenuItem(Root + "7 피격 이펙트 후보 비교 (테스트 허브, 플레이 중)")]
    private static void BeginHitCand() { Begin(Changed7); s_glintCand = true; s_cand = HitCandidates; s_candPrefix = "MH_"; s_candAtHead = false; }

    private static void Begin(string[] keys)
    {
        s_keys = keys;
        s_glintCand = false;
        s_deathCap = false;
        s_outlineCap = false;
        if (!Application.isPlaying) { Debug.LogWarning("[MonsterTiming] 플레이 모드에서만 동작한다."); return; }
        var launcher = UnityEngine.Object.FindFirstObjectByType<TestHubLauncher>();
        if (launcher == null) { Debug.LogWarning("[MonsterTiming] 테스트 허브에서 시작한다."); return; }
        // 유물 없이 — 가웨인 「정오」 글자 · 이펙트가 화면을 가렸다(10-07 반짝임 후보 화면)
        typeof(TestHubLauncher).GetField("relics", Inst)?.SetValue(launcher, new RelicClassSO[] { null });
        s_armed = true;
        Application.logMessageReceived -= OnLog;
        Application.logMessageReceived += OnLog;
        EditorApplication.ExecuteMenuItem(AutoRoot + "시작 (테스트 허브에서, 플레이 중)");
    }

    private static void OnLog(string msg, string stack, LogType type)
    {
        if (!s_armed || !msg.StartsWith("[RunAuto]") || !msg.Contains("방 진입")) return;
        s_armed = false;
        Application.logMessageReceived -= OnLog;
        EditorApplication.delayCall += () =>
        {
            EditorApplication.ExecuteMenuItem(AutoRoot + "중지·기록");
            var p = GameRunBootstrapper.Instance?.Run?.Player;
            if (p == null) { Debug.LogWarning("[MonsterTiming] 런 플레이어가 없다"); return; }
            RunAsync(p, p.GetCancellationTokenOnDestroy()).Forget();
        };
    }

    private static async UniTaskVoid RunAsync(PlayerController p, CancellationToken ct)
    {
        var sb = new StringBuilder("몬스터 공격 타이밍 — 공격 상태 시작부터 초(피해 · 휘두름 정점 = 뼈 속도 최고) · 애니 진행 · 반짝임 = 피해보다 몇 초 앞");
        sb.AppendLine();
        Directory.CreateDirectory("Temp/ui_shots");
        try
        {
            typeof(PlayerController).GetField("debugInvincible", Inst)?.SetValue(p, true);
            GameRunBootstrapper.Instance?.Run?.CovenantHandler?.Cleanup();
            HideAll();
            await Wait(4f, ct);   // 첫 전투방 입장 부감 카메라가 끝날 때까지
            Vector3 home = p.transform.position, fwd = Flat(p.transform.forward);
            if (s_glintCand) { await GlintCandAsync(home, fwd, sb, ct); return; }
            if (s_deathCap) { await DeathCapAsync(p, home, fwd, sb, ct); return; }
            foreach (var key in s_keys)
            {
                HideAll();
                p.transform.position = home;
                var m = await Managers.ObjectPooler.SpawnAsync<MonsterBase>(key, ObjectPoolerManager.PoolType.Monster, home + fwd * 1.6f, Quaternion.LookRotation(-fwd));
                if (m == null) { sb.AppendLine($"{key}: 부르기 실패"); continue; }
                float tw = Time.realtimeSinceStartup;
                var fsmF = typeof(MonsterBase).GetField("_fsm", Inst);
                while (Time.realtimeSinceStartup - tw < 8f && (m.IsDamageImmuneNow || fsmF?.GetValue(m) == null)) await UniTask.Yield(PlayerLoopTiming.Update, ct);   // InitAsync가 FSM을 만든 뒤
                await WatchAsync(m, key, sb, ct);
                if (m != null) Managers.ObjectPooler.Despawn(m.gameObject);
                await Wait(0.5f, ct);
            }
        }
        catch (OperationCanceledException) { sb.AppendLine("취소됨"); }
        catch (Exception e) { sb.AppendLine("예외: " + e); Debug.LogException(e); }
        finally
        {
            File.WriteAllText(OutPath, sb.ToString());
            Debug.Log($"[MonsterTiming] 끝 → {OutPath}");
        }
    }

    private static async UniTask WatchAsync(MonsterBase m, string key, StringBuilder sb, CancellationToken ct)
    {
        string name = key.Split('/')[0];
        var fsmField = typeof(MonsterBase).GetField("_fsm", Inst);
        var curField = typeof(MonsterFSM).GetField("_current", Inst);
        var states = new StringBuilder();
        string lastState = null;
        var rt = typeof(MonsterBase).GetField("_runtime", Inst)?.GetValue(m) as MonsterRuntimeData;
        var anim = m.GetComponentInChildren<Animator>();
        var glintField = typeof(MonsterBase).GetField("_attackGlintShown", Inst);
        bool glintPrev = false, glintShot = false;
        float glintAt = -1f;
        var bones = new List<Transform>();
        foreach (var smr in m.GetComponentsInChildren<SkinnedMeshRenderer>())
            foreach (var b in smr.bones) if (b != null && !bones.Contains(b)) bones.Add(b);
        var prev = new Vector3[bones.Count];

        bool inAtk = false, shot = false, midShot = false;
        float t0 = 0f, tDmg = -1f, tPeak = -1f, peak = 0f, ntDmg = -1f, ntPeak = -1f;
        string pattern = "?";
        int attacks = 0;
        float start = Time.time;
        while (Time.time - start < WatchSeconds && m != null && !m.IsDead)
        {
            await UniTask.Yield(PlayerLoopTiming.PostLateUpdate, ct);
            if (m == null) break;
            var fsmNow = fsmField?.GetValue(m);
            var cur = fsmNow != null ? curField?.GetValue(fsmNow) : null;
            string sn = cur?.GetType().Name ?? "없음";
            if (sn != lastState) { if (states.Length < 400) states.Append($" {sn}@{Time.time - start:0.0}"); lastState = sn; }
            string tn = cur?.GetType().Name ?? "";
            bool atk = tn.EndsWith("AttackState") && !tn.Contains("Ready");
            bool glintNow = glintField != null && (bool)glintField.GetValue(m);
            if (glintNow && !glintPrev)
            {
                glintAt = Time.time;   // 준비 상태에서 켜질 수 있다(공격 시작보다 앞)
            }
            // 반짝임 정점(약 0.04초 뒤)에 한 장 — 몬스터마다 처음 한 번
            if (!glintShot && glintAt > 0f && Time.time - glintAt >= 0.04f && Camera.main != null)
            {
                glintShot = true;
                Capture(Camera.main, $"Temp/ui_shots/MT_{name}_glint.png");
            }
            glintPrev = glintNow;
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            if (atk && !inAtk)
            {
                t0 = Time.time; tDmg = tPeak = ntDmg = ntPeak = -1f; peak = 0f; shot = false; midShot = false;
                var pat = cur.GetType().GetField("_pattern", Inst)?.GetValue(cur);
                pattern = pat?.GetType().GetField("name")?.GetValue(pat) as string ?? tn;
                for (int i = 0; i < bones.Count; i++) prev[i] = m.transform.InverseTransformPoint(bones[i].position);
            }
            else if (atk)
            {
                float best = 0f;
                for (int i = 0; i < bones.Count; i++)
                {
                    Vector3 lp = m.transform.InverseTransformPoint(bones[i].position);
                    best = Mathf.Max(best, (lp - prev[i]).magnitude / dt);
                    prev[i] = lp;
                }
                if (best > peak) { peak = best; tPeak = Time.time - t0; ntPeak = Nt(anim); }
                // 9: 준비 0.2초 지점 — 외곽선이 떠 있는지 세고 가까이서 한 장
                if (s_outlineCap && !midShot && Time.time - t0 >= 0.2f)
                {
                    midShot = true;
                    int outlines = 0;
                    foreach (var w in UnityEngine.Object.FindObjectsByType<MonsterGroundWarning>(FindObjectsSortMode.None))
                        if (w != null && w.name.Contains("Outline")) outlines++;
                    int tele = 0;   // 10-12 통일 예고(MonsterTelegraph) — 판정과 같은 모양 · 앞줄이 피해 순간 끝에 닿는다
                    string shapes = "";
                    foreach (var t in UnityEngine.Object.FindObjectsByType<MonsterTelegraph>(FindObjectsSortMode.None))
                        if (t != null) { tele++; shapes += t.name.Replace("[MonsterTelegraph.", "").Replace("]", "") + " "; }
                    sb.AppendLine($"   {name} 준비 0.2초 — 옛 외곽선 {outlines}개 · 통일 예고 {tele}개({shapes.Trim()})");
                    CaptureClose(m, $"Temp/ui_shots/MO_{name}_{pattern}_{attacks}.png");
                }
                if (tDmg < 0f && rt != null && rt.AttackHitDealt)
                {
                    tDmg = Time.time - t0; ntDmg = Nt(anim);
                    if (!shot && Camera.main != null) { shot = true; Capture(Camera.main, $"Temp/ui_shots/MT_{name}_{pattern}_{attacks}.png"); }
                    if (s_outlineCap) CaptureClose(m, $"Temp/ui_shots/MI_{name}_{pattern}_{attacks}.png");   // 9: 맞는 순간 — 새 공격 이펙트(10-11)
                }
            }
            if (!atk && inAtk)
            {
                attacks++;
                string lead = tDmg >= 0f && glintAt > 0f ? $"{t0 + tDmg - glintAt:0.00}" : "없음";
                sb.AppendLine($"{name,-13} {pattern,-12} 피해 {Fmt(tDmg)} (애니 {ntDmg:0.00}) · 휘두름 정점 {Fmt(tPeak)} (애니 {ntPeak:0.00} · {peak:0.0} m/s) · 반짝임 {lead} · 길이 {Time.time - t0:0.00}");
                glintAt = -1f;
            }
            inAtk = atk;
        }
        if (attacks == 0) sb.AppendLine($"{name,-13} 공격 0회(8초) — 상태:{states}");
    }

    /// <summary>9: 플레이어와 몬스터 사이를 옆 위에서 가까이 — 범위 외곽선 · 공격 이펙트 확인용(게임 카메라는 이름표에 가린다).</summary>
    private static void CaptureClose(MonsterBase m, string path)
    {
        var player = GameRunBootstrapper.Instance?.Run?.Player;
        if (player == null || m == null) return;
        var camGo = new GameObject("~OutlineCam");
        var cam = camGo.AddComponent<Camera>();
        if (Camera.main != null) cam.CopyFrom(Camera.main);
        cam.enabled = false; cam.fieldOfView = 45f;
        Vector3 mid = (player.transform.position + m.transform.position) * 0.5f;
        Vector3 side = Vector3.Cross(Vector3.up, Flat(m.transform.position - player.transform.position));
        cam.transform.position = mid - side * 3.2f + Vector3.up * 3.6f;
        cam.transform.LookAt(mid);
        Capture(cam, path);
        UnityEngine.Object.Destroy(camGo);
    }

    private static async UniTask GlintCandAsync(Vector3 home, Vector3 fwd, StringBuilder sb, CancellationToken ct)
    {
        var m = await Managers.ObjectPooler.SpawnAsync<MonsterBase>("Skeleton/Skeleton", ObjectPoolerManager.PoolType.Monster, home + fwd * 3f, Quaternion.LookRotation(-fwd));
        if (m == null) { sb.AppendLine("해골 부르기 실패"); return; }
        var fsmF = typeof(MonsterBase).GetField("_fsm", Inst);
        float tw = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - tw < 8f && (m.IsDamageImmuneNow || fsmF?.GetValue(m) == null)) await UniTask.Yield(PlayerLoopTiming.Update, ct);
        m.Status.ApplyCc("probe_hold", 9999f);   // 걸어오거나 때리지 않게
        await Wait(1f, ct);
        var head = typeof(MonsterBase).GetField("_headBone", Inst)?.GetValue(m) as Transform;
        sb.AppendLine($"머리 뼈 {(head != null ? head.name : "없음")}");
        float[] times = { 0.03f, 0.08f, 0.15f, 0.3f };
        for (int i = 0; i < s_cand.Length; i++)
        {
            var (path, scale) = s_cand[i];
            HideOthers(m);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) { sb.AppendLine($"#{i} 로드 실패 {path}"); continue; }
            // MonsterAttackGlint와 같은 자리 — 머리에서 카메라 쪽으로 0.5 m
            // 반짝임: 머리 · 카메라 쪽 0.5 m(MonsterAttackGlint) / 피격: 몸통 1 m(CombatDamage 기본 HitPoint와 같은 높이)
            Vector3 at = s_candAtHead ? (head != null ? head.position : m.transform.position + Vector3.up * 1.2f)
                                      : m.transform.position + Vector3.up * 1f;
            var cam = Camera.main;
            if (cam != null) at += (cam.transform.position - at).normalized * (s_candAtHead ? 0.5f : 0.2f);
            var go = UnityEngine.Object.Instantiate(prefab, at, Quaternion.identity);
            go.transform.localScale *= scale;
            float prev = 0f;
            for (int k = 0; k < times.Length; k++)
            {
                await Wait(times[k] - prev, ct);
                prev = times[k];
                if (cam != null) Capture(cam, $"Temp/ui_shots/{s_candPrefix}{i}_{(char)('a' + k)}.png");
            }
            UnityEngine.Object.Destroy(go);
            sb.AppendLine($"#{i} {Path.GetFileNameWithoutExtension(path)} ×{scale} · 화면 {(cam != null ? (Vector2)cam.WorldToScreenPoint(at) : Vector2.zero)}");
            await Wait(0.8f, ct);
        }
        Managers.ObjectPooler.Despawn(m.gameObject);
    }

    private static async UniTask DeathCapAsync(PlayerController p, Vector3 home, Vector3 fwd, StringBuilder sb, CancellationToken ct)
    {
        var fsmF = typeof(MonsterBase).GetField("_fsm", Inst);
        foreach (var key in DeathKeys)
        {
            string name = key.Split('/')[0];
            HideAll();
            var m = await Managers.ObjectPooler.SpawnAsync<MonsterBase>(key, ObjectPoolerManager.PoolType.Monster, home + fwd * 3f, Quaternion.LookRotation(-fwd));
            if (m == null) { sb.AppendLine($"{name}: 부르기 실패"); continue; }
            float tw = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - tw < 8f && (m.IsDamageImmuneNow || fsmF?.GetValue(m) == null)) await UniTask.Yield(PlayerLoopTiming.Update, ct);
            m.Status.ApplyCc("probe_hold", 9999f);
            await Wait(0.8f, ct);
            HideOthers(m);
            float t0 = Time.realtimeSinceStartup;
            m.TakeDamage(1e7f, p.gameObject);   // 플레이어 직접 타격 처치 = 막타 히트스톱 포함
            float minScale = 1f;
            string burstNote = "처치 섬광 못 봄";
            bool burstSeen = false;
            for (int k = 0; k < DeathShotTimes.Length; k++)
            {
                while (Time.realtimeSinceStartup - t0 < DeathShotTimes[k])
                {
                    minScale = Mathf.Min(minScale, Time.timeScale);
                    if (k == 0 && burstSeen)
                    {
                        float bt = 0f; int bn = 0;
                        foreach (var ps in UnityEngine.Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
                            if (ps.gameObject.activeInHierarchy && ps.GetComponentInParent<PooledOneShotVfx>()?.name == "MonsterDeathBurst")
                            { bt = Mathf.Max(bt, ps.time); bn += ps.particleCount; }
                        burstNote += $" [{Time.realtimeSinceStartup - t0:0.000}s f{Time.frameCount} ×{Time.timeScale:0.00} 진행 {bt:0.000} 입자 {bn}]";
                    }
                    if (!burstSeen)   // 처치 섬광 인스턴스가 처음 보인 순간(실시간) · 파티클 진행 · 실시간 재생 여부
                    {
                        foreach (var ps in UnityEngine.Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
                        {
                            if (!ps.gameObject.activeInHierarchy || ps.GetComponentInParent<PooledOneShotVfx>()?.name != "MonsterDeathBurst") continue;   // 풀 인스턴스 이름 = 주소
                            burstSeen = true;
                            burstNote = $"처치 섬광 {Time.realtimeSinceStartup - t0:0.000}초에 보임 · 입자 {ps.particleCount} · 진행 {ps.time:0.000} · 실시간 {ps.main.useUnscaledTime} · 배속 {Time.timeScale:0.00}";
                            break;
                        }
                    }
                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                }
                if (Camera.main != null) Capture(Camera.main, $"Temp/ui_shots/MD_{name}_{k}.png");
                if (k < 2)   // 처치 섬광 진행 — 정지 중에도 실시간으로 도는가(가장 입자가 많은 자식)
                {
                    ParticleSystem best = null;
                    foreach (var ps in UnityEngine.Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
                        if (ps.gameObject.activeInHierarchy && ps.GetComponentInParent<PooledOneShotVfx>()?.name == "MonsterDeathBurst"
                            && (best == null || ps.particleCount > best.particleCount)) best = ps;
                    burstNote += best == null ? $" | {DeathShotTimes[k]}초: 없음"
                        : $" | {DeathShotTimes[k]}초: {best.name} 입자 {best.particleCount} 진행 {best.time:0.000} 재생 {best.isPlaying} 실시간 {best.main.useUnscaledTime} 배속 {Time.timeScale:0.00} 프레임 {Time.frameCount} 실시간Δ {Time.unscaledDeltaTime:0.000}";
                }
            }
            sb.AppendLine($"{name}: {burstNote}");
            sb.AppendLine($"{name}: 처치 · 최저 배속 {minScale:0.00} · 화면 {(Camera.main != null ? (Vector2)Camera.main.WorldToScreenPoint(m.transform.position) : Vector2.zero)}");
            await UniTask.Delay(TimeSpan.FromSeconds(1.0f), DelayType.Realtime, cancellationToken: ct);
        }
    }

    private static void HideOthers(MonsterBase keep)
    {
        foreach (var mb in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
            if (mb != null && mb != keep && !mb.IsDead) mb.gameObject.SetActive(false);
    }

    private static string Fmt(float t) => t < 0f ? "없음" : $"{t:0.00}";

    private static float Nt(Animator a)
    {
        if (a == null) return -1f;
        return a.IsInTransition(0) ? a.GetNextAnimatorStateInfo(0).normalizedTime : a.GetCurrentAnimatorStateInfo(0).normalizedTime;
    }

    private static void HideAll()
    {
        foreach (var mb in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
            if (mb != null && !mb.IsDead) mb.gameObject.SetActive(false);
    }

    private static async UniTask Wait(float s, CancellationToken ct)
        => await UniTask.Delay(TimeSpan.FromSeconds(s), DelayType.DeltaTime, cancellationToken: ct);

    private static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 0.0001f ? v.normalized : Vector3.forward; }

    private static void Capture(Camera cam, string path)
    {
        int w = 960, h = 540;
        var rtex = new RenderTexture(w, h, 24);
        var prevTarget = cam.targetTexture;
        cam.targetTexture = rtex;
        cam.Render();
        cam.targetTexture = prevTarget;
        var prev = RenderTexture.active;
        RenderTexture.active = rtex;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        File.WriteAllBytes(path, tex.EncodeToPNG());
        UnityEngine.Object.Destroy(rtex);
        UnityEngine.Object.Destroy(tex);
    }
}
#endif
