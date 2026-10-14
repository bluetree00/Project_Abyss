#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using RelicFairy.Monster;
using UnityEditor;
using UnityEngine;

/// <summary>
/// [실측 · 플레이 중] 10-13 「이펙트 = 판정」 — 사용자 「이펙트에 맞는 판정이 나오는지 모든 플레이어 · 몬스터 검증」.
/// 1 몬스터(일반 · 정예 — 보스는 84): 패턴 시전 이펙트를 게임 재생 길(SpawnPatternVfx)과 같은 자리 · 회전 · 배율 · Hierarchy로 띄워
///   격자 판정(앞 길이 · 옆 폭)과 비교하고, 판정에 맞는 배율 추정을 적는다. 특수 상태 진입 · 투사체 맞힘 · 특성 폭발도 같은 방식.
/// 재는 셈은 <see cref="EffectReachProbe"/>(보이는 양 90% 도달 · 0.8 ~ 1.25배). 런 없이 플레이 중이면 된다(테스트 허브).
/// 결과: Temp/effect_judgment_monster.txt
/// </summary>
public static class EffectJudgmentAuditEditor
{
    private const string Mon = "Assets/RelicFairy/Characters/Monster/Monster/";
    private const string OutMonster = "Temp/effect_judgment_monster.txt";
    private const BindingFlags SAll = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
    private static readonly string[] BossFolders = { "DeathKnightBoss", "DragonBoss", "ForestGuardian", "Dragon", "DemonKing", "Boss", "TrainingDummy" };

    // 특수 상태 판정 반경(코드 상수 — 10-13 에이전트 전수 · 4b 대조). 없으면 반경만 적는다.
    private static readonly Dictionary<string, (float r, string note)> SpecialHit = new()
    {
        { "StingRayDischargeData", (3.5f, "즉시 원") }, { "MonsterPlantSprayData", (3.5f, "즉시 원") },
        { "SalamanderBreathData", (4f, "부채꼴 ±30° · 2초") }, { "GolemRoarData", (6f, "넉백만") },
        { "CactusThornData", (2.5f, "맞을 때마다") }, { "BeholderEyeBeamData", (5f, "0.3초마다 1.5초") },
    };

    [MenuItem("RelicFairy/Debug/10-13 이펙트 = 판정/1 몬스터 — 패턴 · 특수 상태 · 원거리 · 특성 (플레이 중)")]
    private static void RunMonsters()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[이펙트=판정] 플레이 모드에서만 동작한다."); return; }
        RunMonstersAsync(Application.exitCancellationToken).Forget();
    }

    private static async UniTaskVoid RunMonstersAsync(CancellationToken ct)
    {
        var sb = new StringBuilder("10-13 이펙트 = 판정 — 일반 · 정예 몬스터(보스는 84)\n");
        sb.AppendLine("셈: 보이는 양(Σ 알파 × 크기²) 90% 도달 · 굴절층 뺌 · 판정 대비 0.8 ~ 1.25배면 맞음 · 맞춤 배율 = 지금 배율 × 판정 ÷ 측정(Hierarchy라 비례)");
        int ok = 0, bad = 0, none = 0;
        var station = MakeStation();
        try
        {
            // ── 패턴 시전 이펙트 vs 격자 ──
            sb.AppendLine("\n■ 패턴 시전 이펙트 vs 격자 판정(앞 길이 · 옆 반폭)");
            var resolve = typeof(PatternAttackOverrideSO).GetMethod("ResolveWarningShape", SAll);
            var hier    = typeof(PatternAttackOverrideSO).GetMethod("ApplyVfxHierarchyScaling", SAll);
            var force   = typeof(PatternAttackOverrideSO).GetMethod("ForcePlayVfx", SAll);
            bool warmed = false;   // 첫 재생은 셰이더 준비로 어긋난다(⑦ 첫 줄 벌 Sting 3.2 m · 같은 설정 가오리 1.4 m) — 한 번 버린다
            foreach (var guid in AssetDatabase.FindAssets("t:PatternAttackOverrideSO", new[] { Mon.TrimEnd('/') }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (IsBoss(path)) continue;
                var so = AssetDatabase.LoadAssetAtPath<PatternAttackOverrideSO>(path);
                if (so == null || so.patterns == null) continue;
                var cfgShape = ConfigShape(path);   // 게임은 패턴 모양이 없으면 몬스터 설정의 공격 모양을 쓴다(ctx.Stat.attackShape)
                foreach (var p in so.patterns)
                {
                    var shape = p.attackShapeOverride != null ? p.attackShapeOverride : cfgShape;
                    if (shape is MonsterRangedAttackSO) continue;
                    var grid = (MonsterGroundWarning.GridShape)resolve.Invoke(null, new object[] { p, shape });
                    MonsterGroundWarning.GridBounds(grid, out _, out float maxX, out _, out float maxY);
                    string head = $"  {Folder(path),-14} {p.name,-14} 격자 {grid}(앞 {maxY:0.#} · 옆 ±{maxX:0.#})";
                    if (p.useProceduralBeamVfx)
                    {
                        float end = p.castVfxForwardOffset + p.proceduralBeamLength;
                        bool fine = EffectReachProbe.Ok(end, maxY);
                        sb.AppendLine($"{head} · 코드 직선 {p.castVfxForwardOffset:0.##} ~ {end:0.##} m — 앞 {EffectReachProbe.Ratio(end, maxY)} {(fine ? "맞음" : "어긋남")}");
                        if (fine) ok++; else bad++;
                        continue;
                    }
                    if (p.castVfxPrefab == null) { sb.AppendLine($"{head} · 이펙트 없음(예고 · 반짝임만)"); none++; continue; }
                    float scale = p.castVfxScale > 0.001f ? p.castVfxScale : 1f;
                    if (!warmed) { await MeasureCastAsync(station, p, scale, ct); warmed = true; }
                    var before = EffectReachProbe.Snapshot();
                    Vector3 origin = station.position + station.forward * p.castVfxForwardOffset + station.right * p.castVfxRightOffset;
                    var rot = station.rotation * Quaternion.Euler(p.castVfxXRotationOffset, p.castVfxYRotationOffset, p.castVfxZRotationOffset);
                    var inst = UnityEngine.Object.Instantiate(p.castVfxPrefab, origin + Vector3.up * p.castVfxVerticalOffset, rot,
                                                              p.castVfxAttachToMonster ? station : null);
                    inst.transform.localScale *= scale;
                    hier?.Invoke(null, new object[] { inst });
                    foreach (var ps in inst.GetComponentsInChildren<ParticleSystem>(true)) { var m = ps.main; m.loop = false; m.cullingMode = ParticleSystemCullingMode.AlwaysSimulate; }
                    force?.Invoke(null, new object[] { inst });
                    var r = await EffectReachProbe.ObserveAsync(before, station.position, station.forward, p.castVfxDuration > 0f ? p.castVfxDuration : 1.2f, ct);
                    if (inst != null) UnityEngine.Object.Destroy(inst);   // 입자 stopAction이 먼저 지울 수 있다
                    bool fOk = EffectReachProbe.Ok(r.Forward, maxY), lOk = r.Lateral <= maxX * EffectReachProbe.OkHigh;
                    float fit = r.Forward > 0.05f ? scale * maxY / r.Forward : 0f;
                    sb.AppendLine($"{head} · {p.castVfxPrefab.name} ×{scale:0.##} · 앞 {r.Forward:0.0} m({EffectReachProbe.Ratio(r.Forward, maxY)}) · 옆 ±{r.Lateral:0.0} m({EffectReachProbe.Ratio(r.Lateral, maxX)}) · 반경 {r.Radius:0.0} · " +
                                  $"{(fOk && lOk ? "맞음" : "어긋남")} · 앞 맞춤 배율 ≈ ×{fit:0.##}");
                    if (fOk && lOk) ok++; else bad++;
                    await UniTask.Delay(150, DelayType.Realtime, cancellationToken: ct);
                }
            }

            // ── 특수 상태 진입 이펙트 ──
            sb.AppendLine("\n■ 특수 상태 진입 이펙트(SpecialStateDataBase.SpawnVFX) vs 판정 반경");
            foreach (var guid in AssetDatabase.FindAssets("t:SpecialStateDataBase", new[] { Mon.TrimEnd('/') }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (IsBoss(path)) continue;
                var data = AssetDatabase.LoadAssetAtPath<SpecialStateDataBase>(path);
                if (data == null) continue;
                var before = EffectReachProbe.Snapshot();
                var go = data.SpawnVFX(station);
                if (go == null) { sb.AppendLine($"  {Folder(path),-14} {data.name,-28} 이펙트 없음"); none++; continue; }
                string goName = go.name;   // 관찰 중에 SpawnVFX 자동 삭제가 먼저 지울 수 있다
                foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true)) { var m = ps.main; m.cullingMode = ParticleSystemCullingMode.AlwaysSimulate; }
                var r = await EffectReachProbe.ObserveAsync(before, station.position, station.forward, 2.5f, ct);
                if (go != null) UnityEngine.Object.Destroy(go);
                AppendRadius(sb, $"  {Folder(path),-14} {data.name,-28} {goName}", r, SpecialHit.TryGetValue(data.name, out var h) ? h : (0f, ""), ref ok, ref bad);
                await UniTask.Delay(150, DelayType.Realtime, cancellationToken: ct);
            }

            // ── 투사체 맞힘 이펙트 — 판정은 수평 0.6 m · 광역 없음 → 1 m를 넘으면 「광역처럼 보임」 ──
            sb.AppendLine("\n■ 투사체 맞힘 이펙트(MonsterVfxProjectile 길) — 판정 0.6 m · 광역 없음 · 반경 1 m 이하면 맞음");
            foreach (var guid in AssetDatabase.FindAssets("t:MonsterRangedAttackSO", new[] { Mon.TrimEnd('/') }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (IsBoss(path)) continue;
                var so = AssetDatabase.LoadAssetAtPath<MonsterRangedAttackSO>(path);
                if (so == null || so.vfxHitEffectPrefab == null) continue;
                var before = EffectReachProbe.Snapshot();
                var go = UnityEngine.Object.Instantiate(so.vfxHitEffectPrefab, station.position + Vector3.up, Quaternion.identity);
                go.transform.localScale = Vector3.one * Mathf.Max(0.001f, so.vfxHitEffectScale);
                foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true)) { var m = ps.main; m.scalingMode = ParticleSystemScalingMode.Hierarchy; m.cullingMode = ParticleSystemCullingMode.AlwaysSimulate; }
                var r = await EffectReachProbe.ObserveAsync(before, station.position, station.forward, 1.5f, ct);
                if (go != null) UnityEngine.Object.Destroy(go);
                bool fine = r.Radius <= 1f;
                float fit = r.Radius > 0.05f ? so.vfxHitEffectScale * 0.8f / r.Radius : 0f;
                sb.AppendLine($"  {Folder(path),-14} {so.name,-28} {so.vfxHitEffectPrefab.name} ×{so.vfxHitEffectScale:0.##} · 반경 {r.Radius:0.0} m(구간 최대 {r.RadiusMax:0.0}) · {(fine ? "맞음" : "광역처럼 보임")} · 0.8 m 맞춤 배율 ≈ ×{fit:0.##}");
                if (fine) ok++; else bad++;
                await UniTask.Delay(150, DelayType.Realtime, cancellationToken: ct);
            }

            // ── 특성 · 정예 접두어 폭발(RunFx Burst) ──
            sb.AppendLine("\n■ 특성 · 정예 접두어 폭발(RunFx.Play Burst)");
            await RunFx.LoadAsync();   // 런 밖(허브)에선 아직 안 불러 있어 Play가 아무것도 안 띄웠다(10-13 첫 실측 0 m)
            foreach (var (label, scale, hit) in new[] { ("폭발 유해(특성)", 3.2f, 3f), ("박격(접두어 · 84)", 1.65f, 1.5f), ("가시(접두어 · 84)", 2.75f, 2.5f) })
            {
                var before = EffectReachProbe.Snapshot();
                RunFx.Play(RunFxSlot.Burst, station.position, scale, Color.white);
                var r = await EffectReachProbe.ObserveAsync(before, station.position, station.forward, 1.5f, ct);
                AppendRadius(sb, $"  {label,-20} Burst ×{scale:0.##}", r, (hit, "원"), ref ok, ref bad, scale);
                await UniTask.Delay(150, DelayType.Realtime, cancellationToken: ct);
            }
        }
        catch (OperationCanceledException) { sb.AppendLine("취소됨"); }
        catch (Exception e) { sb.AppendLine("예외: " + e); Debug.LogException(e); }
        finally
        {
            if (station != null) UnityEngine.Object.Destroy(station.gameObject);
            sb.Insert(0, $"맞음 {ok} · 어긋남 {bad} · 이펙트 없음 {none}\n");
            System.IO.File.WriteAllText(OutMonster, sb.ToString());
            Debug.Log($"[이펙트=판정] 끝 — 맞음 {ok} · 어긋남 {bad} · 없음 {none} → {OutMonster}");
        }
    }

    // ── 배율 훑기(10-13 ⑤) — 배율에 정비례하지 않는 칸(찌르기 · 옆으로 누운 베기)을 여러 배율로 재서 고른다 ──
    private const string OutSweep = "Temp/effect_judgment_sweep.txt";
    private static readonly (string folder, string pattern)[] SweepRows =
    {
        ("BattleBee", "Sting"), ("BattleBee", "DashSting"), ("Cactus", "BasicAttack"), ("StingRay", "BasicAttack"),
        ("Fishman", "SpearJab"), ("RatAssassin", "Lunge"), ("RatAssassin", "Slash"), ("Orc", "Slash"), ("Werewolf", "Claw"),
    };
    private static readonly float[] SweepScales = { 0.3f, 0.5f, 0.7f, 0.9f, 1.1f, 1.4f };

    [MenuItem("RelicFairy/Debug/10-13 이펙트 = 판정/2 몬스터 배율 훑기 (플레이 중)")]
    private static void RunSweep()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[이펙트=판정] 플레이 모드에서만 동작한다."); return; }
        RunSweepAsync(Application.exitCancellationToken).Forget();
    }

    private static async UniTaskVoid RunSweepAsync(CancellationToken ct)
    {
        var sb = new StringBuilder("10-13 이펙트 = 판정 — 몬스터 배율 훑기(배율 → 앞 · 옆, 괄호 = 판정 대비)\n");
        var station = MakeStation();
        var resolve = typeof(PatternAttackOverrideSO).GetMethod("ResolveWarningShape", SAll);
        try
        {
            bool warmed = false;
            foreach (var (folder, name) in SweepRows)
            {
                PatternAttackOverrideSO so = null; string path = null;
                foreach (var g in AssetDatabase.FindAssets("t:PatternAttackOverrideSO", new[] { Mon + folder }))
                { path = AssetDatabase.GUIDToAssetPath(g); so = AssetDatabase.LoadAssetAtPath<PatternAttackOverrideSO>(path); }
                var p = so != null ? so.patterns.Find(x => x.name == name) : null;
                if (p == null || p.castVfxPrefab == null) { sb.AppendLine($"  {folder,-14} {name,-12} 없음"); continue; }
                var shape = p.attackShapeOverride != null ? p.attackShapeOverride : ConfigShape(path);
                var grid = (MonsterGroundWarning.GridShape)resolve.Invoke(null, new object[] { p, shape });
                MonsterGroundWarning.GridBounds(grid, out _, out float maxX, out _, out float maxY);
                // 첫 재생은 셰이더 준비로 프레임이 비어 작게 잡혔다(④ 첫 줄 벌 Sting 0.8 m → 다시 재면 3.0 m) — 한 번 버린다
                if (!warmed) { await MeasureCastAsync(station, p, p.castVfxScale, ct); warmed = true; }
                sb.Append($"  {folder,-14} {name,-12} 격자 {grid}(앞 {maxY:0.#} · 옆 ±{maxX:0.#}) · 지금 ×{p.castVfxScale:0.##}\n   ");
                foreach (var sc in SweepScales)
                {
                    var r = await MeasureCastAsync(station, p, sc, ct);
                    sb.Append($" ×{sc:0.#} 앞 {r.Forward:0.0}({EffectReachProbe.Ratio(r.Forward, maxY)}) 옆 {r.Lateral:0.0}({EffectReachProbe.Ratio(r.Lateral, maxX)}) |");
                }
                sb.AppendLine();
            }
        }
        catch (OperationCanceledException) { sb.AppendLine("취소됨"); }
        catch (Exception e) { sb.AppendLine("예외: " + e); Debug.LogException(e); }
        finally
        {
            if (station != null) UnityEngine.Object.Destroy(station.gameObject);
            System.IO.File.WriteAllText(OutSweep, sb.ToString());
            Debug.Log($"[이펙트=판정] 훑기 끝 → {OutSweep}");
        }
    }

    // ── 칼 베기 후보 비교(10-13 ⑩) ──
    private const string OutSlash = "Temp/effect_judgment_slash_candidates.txt";
    private const string HovlSlash = "Assets/RelicFairy/_Imported/EffectSource/Hovl Studio 2/Sword slash VFX/Prefabs/";

    [MenuItem("RelicFairy/Debug/10-13 이펙트 = 판정/3 몬스터 칼 베기 후보 — 1칸 줄에 맞는 가는 그림 (플레이 중)")]
    private static void RunSlashCandidates()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[이펙트=판정] 플레이 모드에서만 동작한다."); return; }
        RunSlashCandidatesAsync(Application.exitCancellationToken).Forget();
    }

    private static async UniTaskVoid RunSlashCandidatesAsync(CancellationToken ct)
    {
        var sb = new StringBuilder("10-13 몬스터 칼 베기 후보 — 앞 0.4 · 위 0.8 · Hierarchy · 배율 0.3 · 0.5에서 잼 → 앞 1.5 m 배율의 옆(판정 ±0.5 · 1.25배 = 0.62 이하면 맞음)\n");
        var station = MakeStation();
        var hier  = typeof(PatternAttackOverrideSO).GetMethod("ApplyVfxHierarchyScaling", SAll);
        var force = typeof(PatternAttackOverrideSO).GetMethod("ForcePlayVfx", SAll);
        var paths = new List<string>();
        for (int i = 1; i <= 17; i++) paths.Add(HovlSlash + $"Sword Slash {i}.prefab");
        paths.Add(HovlSlash + "Sword Slash mirror.prefab");
        paths.Add(Mon + "Skeleton/Effect/Sword Slash 12_Reduced.prefab");
        paths.Add("Assets/RelicFairy/_Imported/EffectSource/MasterStylizedProjectiles/Projectiles/Common/Prefabs/SlashLine.prefab");
        try
        {
            bool warmed = false;
            foreach (var path in paths)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) { sb.AppendLine($"  없음 — {path}"); continue; }
                foreach (var (rx, ry) in new[] { (0f, -90f), (0f, 0f) })
                {
                    if (!warmed) { await MeasurePrefabAsync(station, prefab, rx, ry, 0.4f, hier, force, ct); warmed = true; }
                    var a3 = await MeasurePrefabAsync(station, prefab, rx, ry, 0.3f, hier, force, ct);
                    var a5 = await MeasurePrefabAsync(station, prefab, rx, ry, 0.5f, hier, force, ct);
                    // 앞이 1.5 m가 되는 배율을 두 점 사이 직선으로 · 그 배율의 옆
                    float k = Mathf.Abs(a5.Forward - a3.Forward) > 0.01f ? (1.5f - a3.Forward) / (a5.Forward - a3.Forward) : 0f;
                    float sc = 0.3f + 0.2f * k, lat = a3.Lateral + (a5.Lateral - a3.Lateral) * k;
                    bool fine = sc > 0.05f && lat <= 0.62f && lat > 0.2f;
                    sb.AppendLine($"  {(fine ? "맞음" : "    ")} {prefab.name,-22} 회전 ({rx:0} · {ry:0}) · ×0.3 앞 {a3.Forward:0.00} 옆 {a3.Lateral:0.00} · ×0.5 앞 {a5.Forward:0.00} 옆 {a5.Lateral:0.00} → 앞 1.5 m ≈ ×{sc:0.00} · 옆 ≈ {lat:0.00} m({lat / 0.5f:0.00}배) · {a5.Names}");
                }
            }
        }
        catch (OperationCanceledException) { sb.AppendLine("취소됨"); }
        catch (Exception e) { sb.AppendLine("예외: " + e); Debug.LogException(e); }
        finally
        {
            if (station != null) UnityEngine.Object.Destroy(station.gameObject);
            System.IO.File.WriteAllText(OutSlash, sb.ToString());
            Debug.Log($"[이펙트=판정] 베기 후보 끝 → {OutSlash}");
        }
    }

    private static async UniTask<EffectReachProbe.Result> MeasurePrefabAsync(Transform station, GameObject prefab, float rx, float ry, float scale,
                                                                            MethodInfo hier, MethodInfo force, CancellationToken ct)
    {
        var before = EffectReachProbe.Snapshot();
        Vector3 origin = station.position + station.forward * 0.4f + Vector3.up * 0.8f;
        var inst = UnityEngine.Object.Instantiate(prefab, origin, station.rotation * Quaternion.Euler(rx, ry, 0f), station);
        inst.transform.localScale *= scale;
        hier?.Invoke(null, new object[] { inst });
        foreach (var ps in inst.GetComponentsInChildren<ParticleSystem>(true)) { var m = ps.main; m.loop = false; m.cullingMode = ParticleSystemCullingMode.AlwaysSimulate; }
        force?.Invoke(null, new object[] { inst });
        var r = await EffectReachProbe.ObserveAsync(before, station.position, station.forward, 1.2f, ct);
        if (inst != null) UnityEngine.Object.Destroy(inst);
        await UniTask.Delay(150, DelayType.Realtime, cancellationToken: ct);
        return r;
    }

    /// <summary>패턴 시전 이펙트를 게임 길(SpawnPatternVfx)과 같은 자리 · 회전 · Hierarchy로 배율만 바꿔 띄워 잰다.</summary>
    private static async UniTask<EffectReachProbe.Result> MeasureCastAsync(Transform station, PatternAttackOverrideSO.AttackPattern p, float scale, CancellationToken ct)
    {
        var hier  = typeof(PatternAttackOverrideSO).GetMethod("ApplyVfxHierarchyScaling", SAll);
        var force = typeof(PatternAttackOverrideSO).GetMethod("ForcePlayVfx", SAll);
        var before = EffectReachProbe.Snapshot();
        Vector3 origin = station.position + station.forward * p.castVfxForwardOffset + station.right * p.castVfxRightOffset;
        var rot = station.rotation * Quaternion.Euler(p.castVfxXRotationOffset, p.castVfxYRotationOffset, p.castVfxZRotationOffset);
        var inst = UnityEngine.Object.Instantiate(p.castVfxPrefab, origin + Vector3.up * p.castVfxVerticalOffset, rot,
                                                  p.castVfxAttachToMonster ? station : null);
        inst.transform.localScale *= scale;
        hier?.Invoke(null, new object[] { inst });
        foreach (var ps in inst.GetComponentsInChildren<ParticleSystem>(true)) { var m = ps.main; m.loop = false; m.cullingMode = ParticleSystemCullingMode.AlwaysSimulate; }
        force?.Invoke(null, new object[] { inst });
        var r = await EffectReachProbe.ObserveAsync(before, station.position, station.forward, p.castVfxDuration > 0f ? p.castVfxDuration : 1.2f, ct);
        if (inst != null) UnityEngine.Object.Destroy(inst);
        await UniTask.Delay(150, DelayType.Realtime, cancellationToken: ct);
        return r;
    }

    private static void AppendRadius(StringBuilder sb, string head, EffectReachProbe.Result r, (float r, string note) hit, ref int ok, ref int bad, float scale = 0f)
    {
        if (hit.r <= 0f) { sb.AppendLine($"{head} · 반경 {r.Radius:0.0} m(판정 표 없음)"); return; }
        bool fine = EffectReachProbe.Ok(r.Radius, hit.r);
        string fit = scale > 0f && r.Radius > 0.05f ? $" · 맞춤 배율 ≈ ×{scale * hit.r / r.Radius:0.##}" : "";
        sb.AppendLine($"{head} · 반경 {r.Radius:0.0} m(구간 최대 {r.RadiusMax:0.0}) vs 판정 {hit.r:0.#} m {hit.note} · {EffectReachProbe.Ratio(r.Radius, hit.r)} {(fine ? "맞음" : "어긋남")}{fit}");
        if (fine) ok++; else bad++;
    }

    /// <summary>재는 자리 — 카메라 앞 바닥(화면 밖 입자가 멈추지 않게 · 렌더와 무관하게 잴 수 있지만 같은 조건으로).</summary>
    private static Transform MakeStation()
    {
        var cam = Camera.main;
        Vector3 at = Vector3.zero, fwd = Vector3.forward;
        if (cam != null)
        {
            fwd = cam.transform.forward; fwd.y = 0f; fwd = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : Vector3.forward;
            at = cam.transform.position + fwd * 8f;
            if (Physics.Raycast(at + Vector3.up * 20f, Vector3.down, out var hit, 60f)) at = hit.point;
        }
        var go = new GameObject("[이펙트=판정 자리]");
        go.transform.SetPositionAndRotation(at, Quaternion.LookRotation(fwd));
        return go.transform;
    }

    /// <summary>같은 몬스터 폴더의 설정(MonsterConfigSO) 공격 모양 — 없으면 null(게임도 Front1).</summary>
    private static MonsterAttackShapeSO ConfigShape(string patternPath)
    {
        string folder = Mon + Folder(patternPath);
        foreach (var g in AssetDatabase.FindAssets("t:MonsterConfigSO", new[] { folder }))
        {
            var cfg = AssetDatabase.LoadAssetAtPath<MonsterConfigSO>(AssetDatabase.GUIDToAssetPath(g));
            if (cfg != null && cfg.stat != null && cfg.stat.attackShape != null) return cfg.stat.attackShape;
        }
        return null;
    }

    private static bool IsBoss(string path)
    {
        foreach (var b in BossFolders) if (path.Contains("/" + b + "/")) return true;
        return path.Contains("/Boss_Monster/");
    }

    private static string Folder(string path)
    {
        string rel = path.Replace(Mon, "");
        int i = rel.IndexOf('/');
        return i > 0 ? rel.Substring(0, i) : rel;
    }
}
#endif
