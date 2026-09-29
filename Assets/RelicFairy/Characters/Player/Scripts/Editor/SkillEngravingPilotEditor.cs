using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Inputs;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using RelicFairy.Monster;

/// <summary>
/// 스킬 각인 시범(환영베기, 09-25 — 기획 스킬구성_재련소연결 A안).
///  · 편집 모드: 카타나 R(환영베기) SO에 2단계 각인 둘(잔상 추격 · 응축)을 채운다.
///  · 런 중: 카타나를 +3(2단계)으로 들고 각인 없음 / 잔상 추격 / 응축으로 환영베기를 한 번씩 써서
///    맞은 적 수·피해 합·정면 적 피해를 비교하고, 고를 거리 제안(TryGetPendingOffer)·기록(Choose)을 확인한다.
/// 결과: Temp/skill_engraving_probe.txt
/// </summary>
public static class SkillEngravingPilotEditor
{
    private const string KatanaR  = "Assets/RelicFairy/Weapon/Katana/Data/Katana_QSkill.asset";
    private const string KatanaSO = "Assets/RelicFairy/Weapon/Katana/Data/T1_Katana.asset";
    private const string OutPath  = "Temp/skill_engraving_probe.txt";
    private const int    TankHp   = 50000;
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    [MenuItem("RelicFairy/Debug/스킬 각인 시범 — 환영베기 각인 정의 채우기 (편집 모드)")]
    private static void FillDefs()
    {
        var so = AssetDatabase.LoadAssetAtPath<SkillSO>(KatanaR);
        if (so == null) { Debug.LogWarning("[각인] 환영베기 SO 없음: " + KatanaR); return; }
        var ser  = new SerializedObject(so);
        var list = ser.FindProperty("_engravings");
        list.arraySize = 2;
        Set(list.GetArrayElementAtIndex(0), PhantomDanceBehaviorSO.EngravePursuit, 2, "잔상 추격",
            "검기가 앞자리 대신 가까운 적들을 하나씩 쫓아가 벤다. 흩어진 적에게.");
        Set(list.GetArrayElementAtIndex(1), PhantomDanceBehaviorSO.EngraveCondense, 2, "응축",
            "검기 3발을 한 번에 모아 크게 벤다(피해 합 ×1.2 · 범위 ×1.6). 단단한 한 놈에게.");
        // 2단계는 이제 자동 「검기 3연발」이 아니라 각인 선택이다 — 재련소 미리보기 문구도 맞춘다.
        ser.FindProperty("_tier2Change").stringValue = "각인 택1(추격·응축)";
        ser.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(so);
        AssetDatabase.SaveAssets();
        Debug.Log("[각인] 환영베기 각인 2종 채움");
    }

    private static void Set(SerializedProperty e, string id, int tier, string name, string desc)
    {
        e.FindPropertyRelative("id").stringValue          = id;
        e.FindPropertyRelative("tier").intValue           = tier;
        e.FindPropertyRelative("displayName").stringValue = name;
        e.FindPropertyRelative("description").stringValue = desc;
    }

    [MenuItem("RelicFairy/Debug/스킬 각인 시범 실측 — 환영베기 (런 중)")]
    private static void Probe()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[각인] 플레이 모드에서만 동작한다."); return; }
        var p = GameRunBootstrapper.Instance?.Run?.Player;
        if (p == null) { Debug.LogWarning("[각인] 런 플레이어가 없다."); return; }
        ProbeAsync(p, p.GetCancellationTokenOnDestroy()).Forget();
    }

    private static async UniTaskVoid ProbeAsync(PlayerController p, CancellationToken ct)
    {
        var sb = new StringBuilder();
        ProbeOutput.Begin(OutPath, "각인");
        int pass = 0, fail = 0;
        void Line(bool ok, string t) { sb.AppendLine($"  {(ok ? "✅" : "❌")} {t}"); if (ok) pass++; else fail++; }
        try
        {
            var so = AssetDatabase.LoadAssetAtPath<WeaponSO>(KatanaSO);
            await GameRunBootstrapper.EquipWeaponToPlayerAsync(so, p, 1);
            await Wait(1f, ct);
            var wd = p.WeaponManager?.CurrentWeaponData;
            if (wd == null) { Line(false, "카타나 장착 실패"); return; }
            wd.enhanceLevel = 3;      // 2단계(임계 +3)
            wd.engravings   = "";
            Line(wd.SkillTier == 2, $"카타나 +3 → 스킬 {wd.SkillTier}단계");

            // 제안·기록
            var opts = new List<SkillSO.EngravingDef>();
            bool offered = SkillEngravingService.TryGetPendingOffer(wd, opts, out var skill, out int tier);
            Line(offered && opts.Count == 2 && skill == wd.skillQ && tier == 2,
                 $"고를 거리 제안 — {(offered ? $"{skill.skillName} {tier}단계 · " + string.Join(" / ", opts.ConvertAll(o => o.displayName)) : "없음")}");

            var monsters = await PrepareMonstersAsync(p, ct);
            if (monsters.Count < 3) { Line(false, $"실측 몬스터 부족({monsters.Count})"); return; }

            var results = new List<(string label, int struck, int total, int front)>();
            foreach (var (label, engr) in new[] { ("각인 없음(검기 3연발)", ""), ("잔상 추격", PhantomDanceBehaviorSO.EngravePursuit), ("응축", PhantomDanceBehaviorSO.EngraveCondense) })
            {
                wd.engravings = engr;
                Arrange(p, monsters);
                await Wait(0.4f, ct);
                var hp0 = new Dictionary<MonsterBase, int>();
                foreach (var m in monsters) { SetHp(m, TankHp); hp0[m] = TankHp; }
                p.CooldownTracker.ResetCooldown(SkillType.R);
                p.InputBuffer.Clear();
                p.InputBuffer.Push(Command.RSkill);
                await Wait(2.6f, ct);
                int struck = 0, total = 0;
                foreach (var kv in hp0)
                {
                    int d = kv.Value - (kv.Key != null ? kv.Key.CurrentHp : kv.Value);
                    if (d > 0) { struck++; total += d; }
                }
                int front = TankHp - monsters[0].CurrentHp;
                results.Add((label, struck, total, front));
                sb.AppendLine($"  · {label}: 맞은 적 {struck}/{monsters.Count} · 피해 합 {total} · 정면 적 {front}");
            }
            // 기대: 추격은 흩어진 적을 더 많이 맞힌다 · 응축은 정면 적에게 더 세다
            Line(results[1].struck > results[0].struck, $"잔상 추격 — 흩어진 적을 더 맞힘 ({results[0].struck} → {results[1].struck})");
            Line(results[2].front > results[0].front,   $"응축 — 정면 적에게 더 셈 ({results[0].front} → {results[2].front})");

            wd.engravings = "";
            bool chose = SkillEngravingService.Choose(wd, wd.skillQ, PhantomDanceBehaviorSO.EngraveCondense);
            bool again = SkillEngravingService.TryGetPendingOffer(wd, opts, out _, out _);
            Line(chose && wd.HasEngraving(PhantomDanceBehaviorSO.EngraveCondense) && !again, $"기록 — 응축 선택 후 제안 사라짐 (기록값 「{wd.engravings}」)");
            wd.engravings = "";
        }
        catch (OperationCanceledException) { sb.AppendLine("취소됨"); }
        catch (Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            sb.Insert(0, $"통과 {pass} · 실패 {fail}\n");
            ProbeOutput.Write(OutPath, "각인", sb.ToString());
            Debug.Log("[각인] 결과\n" + sb);
        }
    }

    /// <summary>정면 3m에 하나(응축 비교용), 나머지는 좌우·뒤로 4~7m 흩는다(추격 비교용 — 앞자리 검기엔 안 닿는 거리).</summary>
    private static void Arrange(PlayerController p, List<MonsterBase> ms)
    {
        var t = p.transform;
        Warp(ms[0], t.position + t.forward * 3f);
        for (int i = 1; i < ms.Count; i++)
        {
            float ang = 60f + (i - 1) * (240f / Mathf.Max(1, ms.Count - 1));
            Vector3 dir = Quaternion.AngleAxis(ang, Vector3.up) * t.forward;
            Warp(ms[i], t.position + dir * (4f + i % 3));
        }
    }

    private static async UniTask<List<MonsterBase>> PrepareMonstersAsync(PlayerController p, CancellationToken ct)
    {
        var list = new List<MonsterBase>();
        float t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < 10f)
        {
            list.Clear();
            foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
                if (m != null && !m.IsDead && m.isActiveAndEnabled && !m.IsDamageImmuneNow && m.Grade != MonsterGrade.Boss) list.Add(m);
            if (list.Count >= 5) break;
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
        }
        if (list.Count > 5) list.RemoveRange(5, list.Count - 5);
        foreach (var m in list) SetHp(m, TankHp);
        return list;
    }

    private static void SetHp(MonsterBase m, int hp)
    {
        var rt = typeof(MonsterBase).GetField("_runtime", Inst)?.GetValue(m) as MonsterRuntimeData;
        if (rt != null) rt.CurrentHp = hp;
    }

    private static void Warp(MonsterBase m, Vector3 pos)
    {
        if (NavMesh.SamplePosition(pos, out var hit, 3f, NavMesh.AllAreas)) pos = hit.position;
        if (m.TryGetComponent<NavMeshAgent>(out var ag) && ag.isActiveAndEnabled && ag.isOnNavMesh) { ag.Warp(pos); ag.isStopped = true; }   // 배치가 흐트러지지 않게
        else m.transform.position = pos;
    }

    private static UniTask Wait(float s, CancellationToken ct)
        => UniTask.Delay(TimeSpan.FromSeconds(s), DelayType.Realtime, cancellationToken: ct);
}
