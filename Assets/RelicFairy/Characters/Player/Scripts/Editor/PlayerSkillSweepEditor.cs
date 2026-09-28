using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using Game.Inputs;
using UnityEditor;
using UnityEngine;

/// <summary>
/// [실측 도구 · 플레이 중] 무기별 스킬 점검 — 무기 5종(T1 카타나·대검·활·석궁, T0 무형검)을 차례로 장착하고
/// E·R을 <b>실제 입력 경로</b>(입력 버퍼 → CanUseSkillNow → 스킬 상태)로 한 번씩 쓴다.
/// 각 스킬이 ① 스킬 상태에 들어가는가 ② 제한 시간 안에 평소(None)로 돌아오는가 ③ 쿨다운이 걸리는가
/// ④ 도중 에러·예외가 없는가를 본다. 결과 Temp/skill_sweep.txt.
/// 런 플레이어의 장착 무기가 바뀐다 — 검증용 런에서만 쓴다.
/// </summary>
public static class PlayerSkillSweepEditor
{
    private const string OutPath      = "Temp/skill_sweep.txt";
    private const float  EquipTimeout = 5f;
    private const float  EnterTimeout = 0.6f;
    private const float  ExitTimeout  = 8f;
    private const BindingFlags Inst   = BindingFlags.Instance | BindingFlags.NonPublic;

    private static readonly string[] Weapons =
    {
        "Assets/RelicFairy/Weapon/Katana/Data/T1_Katana.asset",
        "Assets/RelicFairy/Weapon/Greatsword/Data/T1_Greatsword.asset",
        "Assets/RelicFairy/Weapon/Bow/Data/T1_Bow.asset",
        "Assets/RelicFairy/Weapon/Crossbow/Data/T1_Crossbow.asset",
        "Assets/RelicFairy/Weapon/Nameless/Data/T0_Nameless.asset",
    };

    private static readonly List<string> s_errors = new();
    private static bool s_running;

    [MenuItem("RelicFairy/Debug/무기별 스킬 점검 (플레이 중)")]
    private static void Run()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[SkillSweep] 플레이 모드에서만 동작한다."); return; }
        if (s_running) { Debug.LogWarning("[SkillSweep] 이미 실행 중이다."); return; }
        var p = GameRunBootstrapper.Instance?.Run?.Player;
        if (p == null) { Debug.LogWarning("[SkillSweep] 런 플레이어가 없다 — 런을 시작한 뒤 실행한다."); return; }
        SweepAsync(p).Forget();
    }

    private static async UniTaskVoid SweepAsync(PlayerController p)
    {
        s_running = true;
        s_errors.Clear();
        Application.logMessageReceived += OnLog;
        var sb = new StringBuilder();
        int pass = 0, fail = 0;
        try
        {
            var act = GetField<LayerStateMachine<ActState>>(p, "_actSM");

            // 유물 Q — 가웨인은 정오에만 열린다. 정오 고정(파츠 「영원한 정오」와 같은 API)으로 열고, 끝나면 원래대로.
            string qLine = await TryRelicQAsync(p, act);
            if (qLine.StartsWith("PASS", StringComparison.Ordinal) || qLine.StartsWith("SKIP", StringComparison.Ordinal)) pass++; else fail++;
            sb.AppendLine($"== 유물 {p.RelicClass?.name ?? "없음"} Q");
            sb.AppendLine("  " + qLine);
            Debug.Log($"[SkillSweep] 유물 Q: {qLine}");

            foreach (string path in Weapons)
            {
                string key = Path.GetFileNameWithoutExtension(path);
                if (!await EquipAsync(p, path, key)) { sb.AppendLine($"FAIL {key} 장착 안 됨"); fail++; continue; }
                var wd = p.WeaponManager.CurrentWeaponData;
                sb.AppendLine($"== {key} ({wd.weaponType}) · E {(wd.skillE != null ? wd.skillE.name : "없음")} · R {(wd.skillQ != null ? wd.skillQ.name : "없음")}");

                foreach (var slot in new[] { SkillType.E, SkillType.R })
                {
                    string line = await TrySkillAsync(p, act, slot);
                    bool ok = line.StartsWith("PASS", StringComparison.Ordinal) || line.StartsWith("SKIP", StringComparison.Ordinal);
                    if (ok) pass++; else fail++;
                    sb.AppendLine("  " + line);
                    Debug.Log($"[SkillSweep] {key} {slot}: {line}");
                }
            }
        }
        catch (OperationCanceledException) { sb.AppendLine("취소됨"); }
        catch (Exception e) { sb.AppendLine("예외: " + e); fail++; }
        finally
        {
            Application.logMessageReceived -= OnLog;
            s_running = false;
        }
        sb.Insert(0, $"무기별 스킬 점검 — PASS {pass} / FAIL {fail}\n");
        File.WriteAllText(OutPath, sb.ToString());
        Debug.Log($"[SkillSweep] 완료 — PASS {pass} / FAIL {fail} → {OutPath}");
    }

    /// <summary>유물 Q를 게이트를 열어 한 번 쓴다. 게이트를 열 방법을 모르는 유물이면 SKIP.</summary>
    private static async UniTask<string> TryRelicQAsync(PlayerController p, LayerStateMachine<ActState> act)
    {
        if (!p.HasSkillInSlot(SkillType.Q)) return "SKIP 유물 Q 없음";

        ZenithGauge gauge = (p.RelicBehavior as GawainZenithRelic)?.Gauge;
        bool heldBefore = gauge != null && GetField<bool>(gauge, "_holdNoon");
        if (gauge != null) gauge.SetHoldNoon(true);
        else if (!(p.RelicBehavior?.CanUseSkill(SkillType.Q) ?? true)) return "SKIP 게이트를 열 방법 없음(가웨인 외 유물)";

        try { return await TrySkillAsync(p, act, SkillType.Q); }
        finally { if (gauge != null && !heldBefore) gauge.SetHoldNoon(false); }
    }

    /// <summary>입력 버퍼로 스킬 하나를 쓰고 결과 한 줄을 돌려준다.</summary>
    private static async UniTask<string> TrySkillAsync(PlayerController p, LayerStateMachine<ActState> act, SkillType slot)
    {
        if (!p.HasSkillInSlot(slot)) return "SKIP 스킬 없음(잠금 칸)";

        // 이전 스킬이 끝나 평소 상태로 돌아올 때까지 — 스킬 중 입력은 무시된다.
        float t0 = Time.unscaledTime;
        while (act.CurrentId != ActState.None && Time.unscaledTime - t0 < ExitTimeout)
            await UniTask.Yield(PlayerLoopTiming.Update);

        p.CooldownTracker.ResetCooldown(slot);
        int errorsBefore = s_errors.Count;
        var state = slot switch { SkillType.Q => ActState.QSkill, SkillType.E => ActState.ESkill, _ => ActState.RSkill };
        var cmd   = slot switch { SkillType.Q => Command.QSkill,  SkillType.E => Command.ESkill,  _ => Command.RSkill };

        p.InputBuffer.Clear();
        p.InputBuffer.Push(cmd);
        // 즉시형(석궁 버프 등)은 스킬 상태에 들어갔다가 같은 프레임에 나온다 — 쿨다운 시작도 발동으로 본다.
        float t1 = Time.unscaledTime;
        while (act.CurrentId != state && p.CooldownTracker.IsReady(slot) && Time.unscaledTime - t1 < EnterTimeout)
            await UniTask.Yield(PlayerLoopTiming.Update);
        if (act.CurrentId != state && p.CooldownTracker.IsReady(slot)) return $"FAIL 발동 안 함(act {act.CurrentId} · 쿨다운 없음)";
        if (EditorApplication.isPaused) return "FAIL 에디터 일시정지 — 무효";
        if (act.CurrentId != state)
        {
            int instantErrs = s_errors.Count - errorsBefore;
            return instantErrs > 0 ? $"FAIL 에러 {instantErrs}건 — {s_errors[errorsBefore]}"
                                   : $"PASS 즉시형 · 쿨다운 {p.CooldownTracker.GetRemaining(slot):F1}s · 끝난 뒤 act {act.CurrentId}";
        }

        float t2 = Time.unscaledTime;
        while (act.CurrentId == state && Time.unscaledTime - t2 < ExitTimeout)
            await UniTask.Yield(PlayerLoopTiming.Update);
        float dur = Time.unscaledTime - t2;
        if (act.CurrentId == state) return $"FAIL {ExitTimeout:0}초 안에 안 끝남(고착)";

        bool cooling = !p.CooldownTracker.IsReady(slot);
        // 유물 Q는 쿨다운 대신 게이트로 막을 수 있다(가웨인: 쿨다운 0 · 정오 구간당 1회) — 닫혔으면 사용 제한으로 본다.
        bool gated   = slot == SkillType.Q && !(p.RelicBehavior?.CanUseSkill(slot) ?? true);
        int  errs    = s_errors.Count - errorsBefore;
        if (errs > 0)   return $"FAIL 에러 {errs}건 — {s_errors[errorsBefore]}";
        if (gated && !cooling) return $"PASS {dur:F2}s · 게이트 닫힘(쿨다운 대신 유물 규칙) · 끝난 뒤 act {act.CurrentId}";
        if (!cooling)   return $"FAIL 쿨다운이 안 걸림 · {dur:F2}s";
        return $"PASS {dur:F2}s · 쿨다운 {p.CooldownTracker.GetRemaining(slot):F1}s · 끝난 뒤 act {act.CurrentId}";
    }

    private static async UniTask<bool> EquipAsync(PlayerController p, string path, string key)
    {
        var wm = p.WeaponManager;
        if (wm == null) return false;
        if (wm.CurrentWeaponData != null && wm.CurrentWeaponData.weaponSOKey == key) return true;

        var so = AssetDatabase.LoadAssetAtPath<WeaponSO>(path);
        if (so == null) return false;
        var runtime = WeaponData.FromSO(so);
        typeof(PlayerWeaponManager)
            .GetMethod("ApplyServerOverrideIfAvailable", BindingFlags.Static | BindingFlags.NonPublic)
            ?.Invoke(null, new object[] { runtime, key });   // 차트(EQUIPMENT_DATA) 값이 정본이다
        wm.AcquireWeaponAsync(runtime, autoEquip: true).Forget();

        float t0 = Time.unscaledTime;
        while (Time.unscaledTime - t0 < EquipTimeout)
        {
            if (wm.CurrentWeaponData != null && wm.CurrentWeaponData.weaponSOKey == key) break;
            await UniTask.Yield(PlayerLoopTiming.Update);
        }
        // 장착 직후 무기 교체 연출·애니 오버라이드가 끝날 여유
        await UniTask.Delay(TimeSpan.FromSeconds(0.5f), ignoreTimeScale: true);
        return wm.CurrentWeaponData != null && wm.CurrentWeaponData.weaponSOKey == key;
    }

    private static void OnLog(string msg, string stack, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            s_errors.Add(msg.Length > 160 ? msg.Substring(0, 160) : msg);
    }

    private static T GetField<T>(object target, string name)
    {
        var f = target.GetType().GetField(name, Inst);
        if (f == null) throw new MissingFieldException(target.GetType().Name, name);
        return (T)f.GetValue(target);
    }
}
