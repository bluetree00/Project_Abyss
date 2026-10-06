#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Inputs;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// [실측 · 플레이 중] 랜슬롯 Q(심판의 일격) 전용 검 표시 — 장착 무기마다(무형검 · 카타나 T1~3 · 대검 T1~3 · 석궁 · 활)
/// Q 시전 0.9초 지점에 손 주변에 무엇이 그려지는지(활성 렌더러 · 부모 · 위치 · 크기)와 가까운 화면을 남긴다.
/// 사용자 10-03: 「무형검 이외의 검들도 랜슬롯 Q 때 검이 제대로 나와야 한다」.
/// 결과: Temp/lancelot_qsword.txt · Temp/lancelot_qsword_&lt;무기&gt;.png
/// </summary>
public static class LancelotQSwordProbeEditor
{
    private const string AutoRoot = "RelicFairy/Debug/런 구조 자동 실측/";
    private const string OutPath  = "Temp/lancelot_qsword.txt";
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static readonly string[] Weapons =
    {
        "Assets/RelicFairy/Weapon/Nameless/Data/T0_Nameless.asset",
        "Assets/RelicFairy/Weapon/Katana/Data/T1_Katana.asset",
        "Assets/RelicFairy/Weapon/Katana/Data/T2_Katana.asset",
        "Assets/RelicFairy/Weapon/Katana/Data/T3_Katana.asset",
        "Assets/RelicFairy/Weapon/Greatsword/Data/T1_Greatsword.asset",
        "Assets/RelicFairy/Weapon/Greatsword/Data/T2_Greatsword.asset",
        "Assets/RelicFairy/Weapon/Greatsword/Data/T3_Greatsword.asset",
        "Assets/RelicFairy/Weapon/Crossbow/Data/T1_Crossbow.asset",
        "Assets/RelicFairy/Weapon/Bow/Data/T1_Bow.asset",
    };
    private static bool s_armed;
    private static bool s_fxMode;   // 메뉴 11 — 트레일 · 칼날 이펙트 후보 비교
    private static bool s_dirMode;  // 메뉴 12 — Q 막타 방향(사용자 10-05 「마지막 타가 오른쪽으로」)
    private const string FinisherVfxName = "Effect_36_MadnessSlash(Clone)";   // vfx_lancelot_judgment
    private const float  FinisherVfxMinScale = 0.55f;                          // 연타 0.42 · 막타 0.63

    private const string INab1 = "Assets/RelicFairy/_Imported/EffectSource/INab Studio 1/Vfx Assets/Weapon FX Series/Weapon Trails FX/Trail Prefabs/";
    private const string INab  = "Assets/RelicFairy/_Imported/EffectSource/INab Studio/Vfx Assets/Weapon FX Series/Weapon Trails FX/Trail Prefabs/";
    private static readonly (string trail, string vfx, float scale, float glow)[] FxCandidates =
    {
        // 10-06 칼날 이펙트(INab 분노 · 저주)는 칼날을 안 따라가 버렸다 → 재질 발광 세기만 견준다(트레일 Dark1 고정)
        (INab + "Dark 1.prefab", null, 0.35f, 0f),
        (INab + "Dark 1.prefab", null, 0.35f, 1.5f),
        (INab + "Dark 1.prefab", null, 0.35f, 3.0f),
    };
    private static readonly Color GlowBase = new(0.85f, 0.08f, 0.06f);   // 랜슬롯 붉은빛(심판 참격 · 낙인과 같은 결)

    [MenuItem("RelicFairy/Debug/유물 성장 v2/11 랜슬롯 Q 검 트레일 · 이펙트 후보 비교 (테스트 허브, 플레이 중)")]
    private static void BeginFx() { s_fxMode = true; s_dirMode = false; Begin(); }

    [MenuItem("RelicFairy/Debug/유물 성장 v2/12 랜슬롯 Q 막타 방향 실측 (테스트 허브, 플레이 중)")]
    private static void BeginDir() { s_fxMode = false; s_dirMode = true; Begin(); }

    [MenuItem("RelicFairy/Debug/유물 성장 v2/9 랜슬롯 Q 전용 검 표시 실측 (테스트 허브, 플레이 중)")]
    private static void BeginShow() { s_fxMode = false; s_dirMode = false; Begin(); }

    private static void Begin()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[QSword] 플레이 모드에서만 동작한다."); return; }
        var launcher = UnityEngine.Object.FindFirstObjectByType<TestHubLauncher>();
        if (launcher == null) { Debug.LogWarning("[QSword] 테스트 허브에서 시작한다."); return; }
        var relics = typeof(TestHubLauncher).GetField("relics", Inst)?.GetValue(launcher) as RelicClassSO[];
        int idx = relics == null ? -1 : Array.FindIndex(relics, r => r != null && r.Id.ToString() == "Lancelot");
        if (idx < 0) { Debug.LogWarning("[QSword] 허브 유물 목록에 랜슬롯이 없다."); return; }
        typeof(TestHubLauncher).GetField("_relicIndex", Inst)?.SetValue(launcher, idx);
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
            var player = GameRunBootstrapper.Instance?.Run?.Player;
            if (player == null) return;
            if (s_dirMode) RunDirAsync(player, player.GetCancellationTokenOnDestroy()).Forget();
            else if (s_fxMode) RunFxAsync(player, player.GetCancellationTokenOnDestroy()).Forget();
            else RunAsync(player, player.GetCancellationTokenOnDestroy()).Forget();
        };
    }

    private static async UniTaskVoid RunAsync(PlayerController p, CancellationToken ct)
    {
        var sb = new StringBuilder("랜슬롯 Q 전용 검 표시 실측\n");
        try
        {
            typeof(PlayerController).GetField("debugInvincible", Inst)?.SetValue(p, true);
            GameRunBootstrapper.Instance?.Run?.CovenantHandler?.Cleanup();
            foreach (var m in UnityEngine.Object.FindObjectsByType<RelicFairy.Monster.MonsterBase>(FindObjectsSortMode.None))
                if (m != null && !m.IsDead) m.gameObject.SetActive(false);   // 몹이 끼어들어 피격 · 넉백으로 Q가 끊기지 않게
            await Wait(2f, ct);
            var relic = p.RelicBehavior as LancelotMadnessRelic;
            if (relic == null) { sb.AppendLine("랜슬롯 유물이 아니다"); return; }

            foreach (var path in Weapons)
            {
                string key = System.IO.Path.GetFileNameWithoutExtension(path);
                foreach (var m in UnityEngine.Object.FindObjectsByType<RelicFairy.Monster.MonsterBase>(FindObjectsSortMode.None))
                    if (m != null && !m.IsDead) m.gameObject.SetActive(false);   // 방 웨이브가 다시 나온다
                if (!await EquipAsync(p, path, ct)) { sb.AppendLine($"■ {key}: 장착 실패"); continue; }
                await Wait(1.5f, ct);
                var equipped = p.WeaponManager.CurrentWeaponInstance;
                var before = Visible(p);

                // 광란 → Q
                relic.Madness.SetStacks(relic.Madness.MaxStacks);
                if (!relic.Madness.IsFrenzy) relic.Madness.EnterFrenzy(30f);
                await Wait(0.2f, ct);
                p.CooldownTracker.ResetCooldown(SkillType.Q);
                p.InputBuffer.Clear();
                p.InputBuffer.Push(Command.QSkill);
                await Wait(0.9f, ct);

                var during = Visible(p);
                var bone = FindDeep(p.transform, "hand_r");
                sb.AppendLine($"■ {key} — 장착 무기 {(equipped != null ? equipped.name : "없음")} · Q 중 장착 무기 활성 {equipped != null && equipped.activeInHierarchy}");
                foreach (var r in during)
                {
                    bool isNew = !before.Contains(r);
                    var b = r.bounds;
                    sb.AppendLine($"   {(isNew ? "＋" : " ")} {HierPath(r.transform, p.transform)} · 부모 {r.transform.parent?.name} · 크기 {b.size.x:0.00}×{b.size.y:0.00}×{b.size.z:0.00} · 무기 소켓에서 {Vector3.Distance(b.center, Hand(p)):0.00} m · 손뼈에서 {(bone != null ? Vector3.Distance(b.center, bone.position) : -1f):0.00} m · 원점→손뼈 {(bone != null ? Vector3.Distance(r.transform.position, bone.position) : -1f):0.00} m");
                }
                foreach (var r in before)
                    if (!during.Contains(r)) sb.AppendLine($"   － {HierPath(r.transform, p.transform)} (Q 중 숨음)");
                CloseUp(p, $"Temp/lancelot_qsword_{key}_a.png");
                await Wait(1.6f, ct);   // 마무리 자세(2.4초 막타 + 포즈 홀드)
                CloseUp(p, $"Temp/lancelot_qsword_{key}_b.png");

                await Wait(2.4f, ct);   // Q 끝(3.6초 + 막타 시간 정지 0.45초 실시간)까지
                bool restored = equipped == null || equipped.activeInHierarchy;
                var left = new List<string>();
                foreach (var r in Visible(p)) if (!before.Contains(r)) left.Add(HierPath(r.transform, p.transform));
                sb.AppendLine($"   Q 끝 — 장착 무기 복구 {restored} · Q 뒤 새로 보이는 렌더러 {left.Count}{(left.Count > 0 ? " — " + string.Join(" · ", left) : "")}");
                relic.Madness.SetStacks(0);
                typeof(MadnessStack).GetField("_frenzyEnd", Inst)?.SetValue(relic.Madness, Time.time - 0.01f);
                await Wait(0.5f, ct);
            }
        }
        catch (OperationCanceledException) { sb.AppendLine("취소됨"); }
        catch (Exception e) { sb.AppendLine("예외: " + e); Debug.LogException(e); }
        finally
        {
            File.WriteAllText(OutPath, sb.ToString());
            Debug.Log($"[QSword] 끝 → {OutPath}");
        }
    }

    /// <summary>후보마다 Q를 쓰고, 전용 검이 뜨면 그 후보의 트레일 · 칼날 이펙트를 붙여 연타 중 두 장을 찍는다.</summary>
    private static async UniTaskVoid RunFxAsync(PlayerController p, CancellationToken ct)
    {
        var sb = new StringBuilder("랜슬롯 Q 검 트레일 · 이펙트 후보\n");
        try
        {
            typeof(PlayerController).GetField("debugInvincible", Inst)?.SetValue(p, true);
            GameRunBootstrapper.Instance?.Run?.CovenantHandler?.Cleanup();
            await Wait(2f, ct);
            var relic = p.RelicBehavior as LancelotMadnessRelic;
            var trailVfx = p.GetComponent<PlayerWeaponTrailVfx>();
            if (relic == null || trailVfx == null) { sb.AppendLine($"랜슬롯 {relic != null} · 트레일 구동기 {trailVfx != null}"); return; }
            // 실제 경로로 잰다 — 유물 SO에 후보 값을 잠깐 넣고 진짜 Q(AttachBladeFx)를 쓴다. 플레이 중 SO 변경은 에셋에 남으므로 끝나면 되돌린다.
            var so     = p.RelicClass;
            var fTrail = typeof(RelicClassSO).GetField("qSwordTrailPrefab", Inst);
            var fKey   = typeof(RelicClassSO).GetField("qSwordBladeVfxKey", Inst);
            var fScale = typeof(RelicClassSO).GetField("qSwordBladeVfxScale", Inst);
            var fGlow  = typeof(RelicClassSO).GetField("qSwordGlowColor", Inst);
            if (so == null || fTrail == null || fKey == null || fScale == null || fGlow == null) { sb.AppendLine("유물 SO · 칸을 못 찾음"); return; }
            var oldTrail = fTrail.GetValue(so);
            var oldKey   = fKey.GetValue(so);
            var oldScale = fScale.GetValue(so);
            var oldGlow  = fGlow.GetValue(so);
            try
            {
            for (int i = 0; i < FxCandidates.Length; i++)
            {
                var (trailPath, vfxKey, vfxScale, glow) = FxCandidates[i];
                foreach (var m in UnityEngine.Object.FindObjectsByType<RelicFairy.Monster.MonsterBase>(FindObjectsSortMode.None))
                    if (m != null && !m.IsDead) m.gameObject.SetActive(false);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(trailPath);
                fTrail.SetValue(so, prefab);
                fKey.SetValue(so, vfxKey ?? "");
                fScale.SetValue(so, vfxScale);
                fGlow.SetValue(so, glow > 0f ? GlowBase * glow : Color.black);
                relic.Madness.SetStacks(relic.Madness.MaxStacks);
                if (!relic.Madness.IsFrenzy) relic.Madness.EnterFrenzy(30f);
                await Wait(0.3f, ct);
                p.CooldownTracker.ResetCooldown(SkillType.Q);
                p.InputBuffer.Clear();
                p.InputBuffer.Push(Command.QSkill);
                await Wait(0.8f, ct);   // 검 생성 + 칼날 이펙트 비동기 로드까지
                var sword = FindDeep(p.transform, "Lancelot_QSword(Clone)");
                var tip   = sword != null ? FindDeep(sword, "QSwordTip") : null;
                var fx    = sword != null ? sword.GetComponentsInChildren<UnityEngine.VFX.VisualEffect>(true) : Array.Empty<UnityEngine.VFX.VisualEffect>();
                string fxAt = fx.Length > 0 && tip != null ? $"{Vector3.Distance(fx[0].transform.position, tip.position):0.00} m" : "—";
                string name = System.IO.Path.GetFileNameWithoutExtension(trailPath).Replace(" ", "");
                string key  = (string.IsNullOrEmpty(vfxKey) ? "없음" : vfxKey) + $"_{vfxScale:0.00}_빛{glow:0.0}";
                sb.AppendLine($"#{i} 트레일 {name}(로드 {prefab != null}) · 칼날 {key} · 검 {sword != null} · 칼날 이펙트 {fx.Length}개(칼끝에서 {fxAt})");
                CloseUp(p, $"Temp/qsword_fx_{i}_{key}_a.png");
                await Wait(0.5f, ct);
                CloseUp(p, $"Temp/qsword_fx_{i}_{key}_b.png");
                await Wait(3.0f, ct);   // Q 끝 — 실제 RestoreWeapon이 트레일 · 칼날 이펙트를 거둔다
                bool left = FindDeep(p.transform, "Lancelot_QSword(Clone)") != null;
                sb.AppendLine($"   Q 끝 — Q 검 남음 {left}");
                relic.Madness.SetStacks(0);
                typeof(MadnessStack).GetField("_frenzyEnd", Inst)?.SetValue(relic.Madness, Time.time - 0.01f);
                await Wait(0.8f, ct);
            }
            }
            finally
            {
                fTrail.SetValue(so, oldTrail);
                fKey.SetValue(so, oldKey);
                fScale.SetValue(so, oldScale);
                fGlow.SetValue(so, oldGlow);
            }
        }
        catch (OperationCanceledException) { sb.AppendLine("취소됨"); }
        catch (Exception e) { sb.AppendLine("예외: " + e); Debug.LogException(e); }
        finally
        {
            File.WriteAllText("Temp/qsword_fx.txt", sb.ToString());
            Debug.Log("[QSword] 끝 → Temp/qsword_fx.txt");
        }
    }

    /// <summary>
    /// 메뉴 12 — 카메라 기준 위 · 오른쪽으로 Q를 겨눠 쓰고, 막타 참격 이펙트가 뜬 순간의 방향 · 그려진 중심을 잰다.
    /// 오른쪽 + = 플레이어 기준 오른쪽(위에서 볼 때 시계 방향).
    /// </summary>
    private static async UniTaskVoid RunDirAsync(PlayerController p, CancellationToken ct)
    {
        var sb = new StringBuilder("랜슬롯 Q 막타 방향 실측 (각도: 위에서 볼 때 시계 방향 +)\n");
        try
        {
            typeof(PlayerController).GetField("debugInvincible", Inst)?.SetValue(p, true);
            GameRunBootstrapper.Instance?.Run?.CovenantHandler?.Cleanup();
            // 막타 다시 겨눔(10-06) 확인용 표적 하나만 남기고 나머지는 끈다 — 몹이 끼면 피격 · 넉백으로 Q가 끊긴다
            RelicFairy.Monster.MonsterBase dummy = null;
            foreach (var m in UnityEngine.Object.FindObjectsByType<RelicFairy.Monster.MonsterBase>(FindObjectsSortMode.None))
            {
                if (m == null || m.IsDead) continue;
                if (dummy == null) dummy = m;
                m.gameObject.SetActive(false);
            }
            await Wait(2f, ct);
            var relic = p.RelicBehavior as LancelotMadnessRelic;
            if (relic == null) { sb.AppendLine("랜슬롯 유물이 아니다"); return; }

            string[] names = { "위 · 적 없음", "위 · 오른쪽 5 m에 적" };
            for (int k = 0; k < names.Length; k++)
            {
                var cam = Camera.main;
                if (cam == null) { sb.AppendLine("메인 카메라 없음"); return; }
                Vector3 aimDir = Flat(cam.transform.forward);
                Vector3 aimPt  = p.transform.position + aimDir * 5f;
                Vector3 enemyAt = p.transform.position + Vector3.Cross(Vector3.up, aimDir) * 5f;
                if (k == 1)
                {
                    if (dummy == null)
                        dummy = await Managers.ObjectPooler.SpawnAsync<RelicFairy.Monster.MonsterBase>("Slime/Slime", ObjectPoolerManager.PoolType.Monster, enemyAt, Quaternion.identity);
                    if (dummy == null) { sb.AppendLine("표적으로 쓸 몬스터 없음(슬라임 부르기 실패)"); break; }
                    dummy.gameObject.SetActive(true);
                    float spawnT0 = Time.realtimeSinceStartup;   // 등장 무적이 풀릴 때까지
                    while (Time.realtimeSinceStartup - spawnT0 < 6f && dummy.IsDamageImmuneNow) await UniTask.Yield(PlayerLoopTiming.Update, ct);
                    if (dummy.TryGetComponent<UnityEngine.AI.NavMeshAgent>(out var ag) && ag.isActiveAndEnabled && ag.isOnNavMesh) ag.Warp(enemyAt);
                    else dummy.transform.position = enemyAt;
                    dummy.Status.ApplyCc("probe_hold", 9999f);
                    KeepAlive(dummy);
                    await Wait(0.3f, ct);
                }
                relic.Madness.SetStacks(relic.Madness.MaxStacks);
                if (!relic.Madness.IsFrenzy) relic.Madness.EnterFrenzy(30f);
                AimAt(aimPt);
                await Wait(0.3f, ct);
                AimAt(aimPt);
                await Wait(0.1f, ct);
                p.CooldownTracker.ResetCooldown(SkillType.Q);
                p.InputBuffer.Clear();
                p.InputBuffer.Push(Command.QSkill);
                var trace = new StringBuilder();
                SampleFacingAsync(p, trace, 3.2f, ct).Forget();   // 원인 확정용 — 0.1초마다 배율 · 물리 · 몸 · 명령 각도
                await Wait(0.3f, ct);
                Vector3 fwdStart = Flat(p.transform.forward);

                // 막타 이펙트(배율 0.63)가 뜰 때까지 — 막타는 시간 정지가 걸리므로 실시간으로 기다린다
                Transform fin = null;
                float t0 = Time.realtimeSinceStartup;
                while (fin == null && Time.realtimeSinceStartup - t0 < 6f)
                {
                    fin = FindFinisherVfx();
                    if (fin == null) await UniTask.Yield(PlayerLoopTiming.Update, ct);
                }
                Vector3 fwdFin = Flat(p.transform.forward);
                if (k == 1 && dummy != null)
                {
                    KeepAlive(dummy);
                    Vector3 toEnemy = Flat(dummy.transform.position - p.transform.position);
                    sb.AppendLine($"   막타 방향 vs 적 방향 {Vector3.SignedAngle(toEnemy, fwdFin, Vector3.up):+0;-0}° (0에 가까우면 다시 겨눔 성공) · 적 거리 {Vector3.Distance(dummy.transform.position, p.transform.position):0.0} m");
                    if (fin != null) sb.AppendLine($"   막타 참격(이펙트) 방향 vs 적 방향 {Vector3.SignedAngle(toEnemy, Flat(fin.forward), Vector3.up):+0;-0}° · 명령된 정면 vs 적 {Vector3.SignedAngle(toEnemy, Flat(p.AimForward), Vector3.up):+0;-0}°");
                }
                sb.AppendLine($"■ {names[k]}로 겨눔 — Q 시작 방향 vs 겨눈 방향 {Vector3.SignedAngle(aimDir, fwdStart, Vector3.up):+0;-0}° · Q 동안 몸 회전 {Vector3.SignedAngle(fwdStart, fwdFin, Vector3.up):+0;-0}°");
                if (fin == null) { sb.AppendLine("   막타 이펙트를 못 찾음(6초)"); continue; }
                sb.AppendLine($"   이펙트 루트 방향 vs 몸 방향 {Vector3.SignedAngle(fwdFin, Flat(fin.forward), Vector3.up):+0;-0}° · 루트 위치 정면 {Vector3.Dot(fin.position - p.transform.position, fwdFin):0.00} m · 오른쪽 {Vector3.Dot(fin.position - p.transform.position, Vector3.Cross(Vector3.up, fwdFin)):+0.00;-0.00} m");
                CaptureMain(cam, $"Temp/lancelot_q_dir_{k}_a.png");
                for (int s = 0; s < 3; s++)
                {
                    await Wait(0.12f, ct);
                    if (fin == null) break;
                    if (TryParticleCenter(fin, out var c, out int count))
                    {
                        Vector3 d = c - p.transform.position;
                        sb.AppendLine($"   +{0.12f * (s + 1):0.00}초 — 입자 {count}개 중심: 정면 {Vector3.Dot(d, fwdFin):0.00} m · 오른쪽 {Vector3.Dot(d, Vector3.Cross(Vector3.up, fwdFin)):+0.00;-0.00} m · 높이 {d.y:0.00} m");
                    }
                    if (s <= 1 && TopDownCentroid(fin, p.transform.position, fwdFin, $"Temp/lancelot_q_dir_{k}_top{s}.png",
                                                  out float cr, out float cf, out float mnR, out float mxR))
                        sb.AppendLine($"   +{0.12f * (s + 1):0.00}초 — 위에서 본 그림 무게중심: 정면 {cf:0.00} m · 오른쪽 {cr:+0.00;-0.00} m · 좌우 끝 {mnR:+0.0;-0.0} ~ {mxR:+0.0;-0.0} m");
                    if (s == 1) CaptureMain(cam, $"Temp/lancelot_q_dir_{k}_b.png");
                }
                await Wait(3.0f, ct);
                sb.AppendLine("   [회전 기록 — Q 입력부터 실시간]");
                sb.Append(trace);
                relic.Madness.SetStacks(0);
                typeof(MadnessStack).GetField("_frenzyEnd", Inst)?.SetValue(relic.Madness, Time.time - 0.01f);
                await Wait(0.8f, ct);
            }
        }
        catch (OperationCanceledException) { sb.AppendLine("취소됨"); }
        catch (Exception e) { sb.AppendLine("예외: " + e); Debug.LogException(e); }
        finally
        {
            File.WriteAllText("Temp/lancelot_q_dir.txt", sb.ToString());
            Debug.Log("[QSword] 끝 → Temp/lancelot_q_dir.txt");
        }
    }

    /// <summary>
    /// 막타 이펙트만 위에서 내려다본 정사영 한 장 — 밝기 가중 무게중심(플레이어 기준 정면 · 오른쪽 m)과 좌우 끝.
    /// 화면 위 = 플레이어 정면, 화면 오른쪽 = 플레이어 오른쪽. 이펙트 렌더러를 잠깐 빈 레이어(31)로 옮겨 그것만 찍는다.
    /// </summary>
    private static bool TopDownCentroid(Transform fin, Vector3 origin, Vector3 fwd, string path,
                                        out float right, out float forward, out float minRight, out float maxRight)
    {
        right = forward = minRight = maxRight = 0f;
        const int   Layer = 31, Px = 512;
        const float Half  = 14f;   // 위에서 28 m 폭
        var renderers = fin.GetComponentsInChildren<Renderer>(false);
        var oldLayers = new int[renderers.Length];
        for (int i = 0; i < renderers.Length; i++) { oldLayers[i] = renderers[i].gameObject.layer; renderers[i].gameObject.layer = Layer; }
        var go = new GameObject("QDirTopCam");
        try
        {
            var cam = go.AddComponent<Camera>();
            cam.orthographic     = true;
            cam.orthographicSize = Half;
            cam.clearFlags       = CameraClearFlags.SolidColor;
            cam.backgroundColor  = Color.black;
            cam.cullingMask      = 1 << Layer;
            cam.nearClipPlane    = 0.1f;
            cam.farClipPlane     = 60f;
            go.transform.position = origin + Vector3.up * 25f;
            go.transform.rotation = Quaternion.LookRotation(Vector3.down, fwd);
            var rt = new RenderTexture(Px, Px, 24);
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = null;
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(Px, Px, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, Px, Px), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            var px = tex.GetPixels();
            UnityEngine.Object.Destroy(rt);
            UnityEngine.Object.Destroy(tex);
            double sw = 0, sx = 0, sy = 0;
            int minX = Px, maxX = -1;
            for (int y = 0; y < Px; y++)
                for (int x = 0; x < Px; x++)
                {
                    var c = px[y * Px + x];
                    float w = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
                    if (w < 0.08f) continue;
                    sw += w; sx += w * x; sy += w * y;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                }
            if (sw <= 0) return false;
            float toM = 2f * Half / Px;
            right    = ((float)(sx / sw) - Px * 0.5f) * toM;
            forward  = ((float)(sy / sw) - Px * 0.5f) * toM;
            minRight = (minX - Px * 0.5f) * toM;
            maxRight = (maxX - Px * 0.5f) * toM;
            return true;
        }
        finally
        {
            for (int i = 0; i < renderers.Length; i++) if (renderers[i] != null) renderers[i].gameObject.layer = oldLayers[i];
            UnityEngine.Object.Destroy(go);
        }
    }

    /// <summary>Q 동안 0.1초(실시간)마다 시간 배율 · 물리(Rigidbody) · 몸(transform) · 명령된 정면 각도를 적는다 — 다시 겨눔이 덜 도는 원인 확정용.</summary>
    private static async UniTaskVoid SampleFacingAsync(PlayerController p, StringBuilder trace, float seconds, CancellationToken ct)
    {
        float t0 = Time.realtimeSinceStartup, next = 0f;
        try
        {
            while (Time.realtimeSinceStartup - t0 < seconds)
            {
                float t = Time.realtimeSinceStartup - t0;
                if (t >= next && p != null)
                {
                    next += 0.1f;
                    var rb = p.Rigid;
                    Vector3 aim = p.AimForward;
                    trace.AppendLine($"      {t:0.0}초 · 배율 {Time.timeScale:0.00} · 물리 {(rb != null ? rb.rotation.eulerAngles.y : 0f):0} · 몸 {p.transform.eulerAngles.y:0} · 명령 {Mathf.Repeat(Mathf.Atan2(aim.x, aim.z) * Mathf.Rad2Deg, 360f):0}");
                }
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>표적이 연타에 죽지 않게 체력을 크게 채운다(런타임 체력은 필드).</summary>
    private static void KeepAlive(RelicFairy.Monster.MonsterBase m)
    {
        var rt = typeof(RelicFairy.Monster.MonsterBase).GetField("_runtime", Inst)?.GetValue(m);
        var f  = rt?.GetType().GetField("CurrentHp", Inst);
        if (f != null) f.SetValue(rt, Convert.ChangeType(50000, f.FieldType));
    }

    /// <summary>막 뜬 막타 참격 이펙트(배율이 큰 것) — 연타 이펙트(0.42)는 건너뛴다.</summary>
    private static Transform FindFinisherVfx()
    {
        Transform best = null;
        foreach (var ps in UnityEngine.Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
        {
            var root = ps.transform.root;
            if (root.name != FinisherVfxName || root.localScale.x < FinisherVfxMinScale) continue;
            best = root;
        }
        return best;
    }

    /// <summary>이펙트 안 살아 있는 입자들의 월드 위치 평균 — 실제로 화면에 그려진 쪽.</summary>
    private static bool TryParticleCenter(Transform root, out Vector3 center, out int count)
    {
        center = Vector3.zero; count = 0;
        var buf = new ParticleSystem.Particle[256];
        foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(false))
        {
            int n = ps.GetParticles(buf);
            bool local = ps.main.simulationSpace == ParticleSystemSimulationSpace.Local;
            for (int i = 0; i < n; i++)
            {
                center += local ? ps.transform.TransformPoint(buf[i].position) : buf[i].position;
                count++;
            }
        }
        if (count == 0) return false;
        center /= count;
        return true;
    }

    private static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward;
    }

    /// <summary>예산 실측과 같은 방법 — 입력 이벤트로 마우스를 그 자리에 둔다(Q는 시작할 때 마우스 쪽으로 돈다).</summary>
    private static void AimAt(Vector3 world)
    {
        var mouse = Mouse.current;
        var cam   = Camera.main;
        if (mouse == null || cam == null) return;
        Vector3 screen = cam.WorldToScreenPoint(world + Vector3.up * 0.8f);
        if (screen.z <= 0f) return;
        InputSystem.QueueDeltaStateEvent(mouse.position, (Vector2)screen);
    }

    /// <summary>게임 카메라 화면 그대로 한 장(UI 오버레이 제외).</summary>
    private static void CaptureMain(Camera cam, string path)
    {
        int w = 960, h = 540;
        var rt = new RenderTexture(w, h, 24);
        var prevTarget = cam.targetTexture;
        cam.targetTexture = rt;
        cam.Render();
        cam.targetTexture = prevTarget;
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        File.WriteAllBytes(path, tex.EncodeToPNG());
        UnityEngine.Object.Destroy(rt);
        UnityEngine.Object.Destroy(tex);
    }

    // 손(오른손 무기 소켓) 반경 1.6 m 안에서 플레이어 계층 아래 켜진 렌더러 — 몸(SkinnedMesh)은 뺀다
    private static HashSet<Renderer> Visible(PlayerController p)
    {
        var set = new HashSet<Renderer>();
        Vector3 hand = Hand(p);
        foreach (var r in p.GetComponentsInChildren<Renderer>(false))
        {
            if (r == null || !r.enabled || r is SkinnedMeshRenderer || r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
            if (Vector3.Distance(r.bounds.center, hand) > 1.6f) continue;
            set.Add(r);
        }
        return set;
    }

    private static Vector3 Hand(PlayerController p) => p.HandTransform != null ? p.HandTransform.position : p.transform.position + Vector3.up;

    private static string HierPath(Transform t, Transform root)
    {
        var parts = new List<string>();
        for (var c = t; c != null && c != root; c = c.parent) parts.Insert(0, c.name);
        return string.Join("/", parts.Count > 4 ? parts.GetRange(parts.Count - 4, 4) : parts);
    }

    /// <summary>플레이어 오른쪽 앞 2.6 m에서 손을 보는 화면 한 장(임시 카메라 → RenderTexture → PNG).</summary>
    private static void CloseUp(PlayerController p, string path)
    {
        var go = new GameObject("QSwordProbeCam");
        try
        {
            var cam = go.AddComponent<Camera>();
            cam.fieldOfView = 40f;
            cam.nearClipPlane = 0.05f;
            Vector3 chest = p.transform.position + Vector3.up * 1.1f;
            Vector3 eye = p.transform.position + p.transform.forward * 2.6f + p.transform.right * 0.8f + Vector3.up * 1.5f;
            go.transform.position = eye;
            go.transform.LookAt(chest);
            cam.fieldOfView = 45f;
            var rt = new RenderTexture(960, 720, 24);
            cam.targetTexture = rt;
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(960, 720, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 960, 720), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            cam.targetTexture = null;
            UnityEngine.Object.Destroy(rt);
            UnityEngine.Object.Destroy(tex);
        }
        finally { UnityEngine.Object.Destroy(go); }
    }

    private static async UniTask<bool> EquipAsync(PlayerController p, string soPath, CancellationToken ct)
    {
        var wm = p.WeaponManager;
        string key = System.IO.Path.GetFileNameWithoutExtension(soPath);
        var so = AssetDatabase.LoadAssetAtPath<WeaponSO>(soPath);
        if (so == null) return false;
        var runtime = WeaponData.FromSO(so);
        // 슬롯 0에 넣고 그 슬롯으로 바꾼다 — 빈 슬롯 자동 배정은 두 번째부터 다른 슬롯을 덮어 현재 무기가 안 바뀌었다(1회차).
        // 슬롯에 인스턴스가 있으면 장착이 데이터만 바꾸고 모델은 그대로 둔다 — 먼저 해제한다(게임 내부 DestroySlotInstance와 같은 길).
        typeof(PlayerWeaponManager).GetMethod("DestroySlotInstance", BindingFlags.Static | BindingFlags.NonPublic)
            ?.Invoke(null, new object[] { wm.slots[PlayerWeaponManager.Slot0] });
        await UniTask.Yield(PlayerLoopTiming.Update, ct);
        await wm.AcquireWeaponToSlotAsync(runtime, PlayerWeaponManager.Slot0, setActive: true, playAppear: false);
        if (wm.CurrentSlotIndex != PlayerWeaponManager.Slot0) await wm.SwitchToSlotAsync(PlayerWeaponManager.Slot0);
        float t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < 8f)
        {
            var inst = wm.CurrentWeaponInstance;
            if (inst != null && inst.name.StartsWith(key) && inst.activeInHierarchy) return true;
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
        }
        return false;
    }

    private static Transform FindDeep(Transform root, string name)
    {
        if (root == null) return null;
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            var hit = FindDeep(root.GetChild(i), name);
            if (hit != null) return hit;
        }
        return null;
    }

    private static UniTask Wait(float s, CancellationToken ct)
        => UniTask.Delay(TimeSpan.FromSeconds(s), DelayType.Realtime, cancellationToken: ct);
}
#endif
