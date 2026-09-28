using System;
using System.Linq;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using Game.Inputs;
using UnityEditor;
using UnityEngine;

/// <summary>
/// [실측 도구 · 플레이 중] 09-21 원거리 수정 3건을 살아 있는 화살에서 직접 확인한다.
///  ① 스킬이 쏜 화살이 자기를 <b>스킬</b>로 신고하는가(기본 공격으로 신고하면 스킬 피해 증가가 안 붙는다).
///  ② 파츠 폭발에 <b>이펙트 키</b>가 실려 있는가(예전엔 피해만 들어가고 아무것도 안 보였다).
///  ③ 파츠 레벨이 원거리 <b>스킬 단계</b>를 올렸을 때 HUD가 갱신 통지 전/후로 어떻게 달라지는가.
/// 결과: 콘솔 + Temp/ranged_fixes_probe.txt
/// </summary>
public static class RangedFixesProbeEditor
{
    private const string BowPath = "Assets/RelicFairy/Weapon/Bow/Data/T1_Bow.asset";
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic;

    [MenuItem("RelicFairy/Debug/원거리 수정 3건 확인 (플레이 중)")]
    private static void Run()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[원거리확인] 플레이 모드에서만 동작한다."); return; }
        var p = GameRunBootstrapper.Instance?.Run?.Player;
        if (p == null) { Debug.LogWarning("[원거리확인] 런 플레이어가 없다."); return; }
        RunAsync(p).Forget();
    }

    private static async UniTaskVoid RunAsync(PlayerController p)
    {
        var sb = new StringBuilder();
        ProbeOutput.Begin("Temp/ranged_fixes_probe.txt", "원거리확인");
        try
        {
            var so = AssetDatabase.LoadAssetAtPath<WeaponSO>(BowPath);
            if (so == null) { Debug.LogWarning("[원거리확인] 활 SO 없음"); return; }
            await GameRunBootstrapper.EquipWeaponToPlayerAsync(so, p, 1);
            await UniTask.Delay(TimeSpan.FromSeconds(1.0f), ignoreTimeScale: true);

            // ── ① 기본 공격 vs 스킬: 화살이 신고하는 행동 종류
            sb.AppendLine($"① 화살이 신고하는 행동 — 기본공격 {await FireAndReadAsync(p, Command.Light, "_actionType")}" +
                          $" · E 스킬 {await FireAndReadAsync(p, Command.ESkill, "_actionType")}");

            // ── ② 폭발 파츠를 켜고 화살의 폭발 이펙트 키 확인
            var explode = Managers.WeaponParts?.All?.FirstOrDefault(e => e.Kind == RangedPartKind.Explode);
            var state = RangedPartsState.Current;
            if (explode == null) sb.AppendLine("② 폭발 파츠가 차트에 없다 — 건너뜀");
            else
            {
                int before = state.LevelOf(explode.part_id);
                if (before <= 0) state.Equip(explode.part_id, 3); else state.LevelUp(explode.part_id, 3 - before);
                await UniTask.Delay(TimeSpan.FromSeconds(0.3f), ignoreTimeScale: true);
                string key = await FireAndReadAsync(p, Command.Light, "_explodeEffectKey");
                sb.AppendLine($"② 파츠 폭발 이펙트 키 — \"{key}\" (비어 있으면 안 보이는 폭발)");
                if (before <= 0) state.Unequip(explode.part_id); else state.LevelUp(explode.part_id, before - 3);
            }

            // ── ③ 파츠 총합이 원거리 단계를 올릴 때 HUD 갱신
            var split = Managers.WeaponParts?.All?.FirstOrDefault(e => e.Kind == RangedPartKind.Split);
            var wd = p.WeaponManager?.CurrentWeaponData;
            if (split == null || wd == null) sb.AppendLine("③ 분열 파츠/무기 없음 — 건너뜀");
            else
            {
                state.Unequip(split.part_id);
                await UniTask.Delay(TimeSpan.FromSeconds(0.3f), ignoreTimeScale: true);
                sb.AppendLine($"③ 파츠 0 → 단계 {wd.SkillTier} · 화면 R={ScreenRIcon()}");

                state.Equip(split.part_id, 5);                       // 원거리 임계 +4 → 2단계
                await UniTask.Delay(TimeSpan.FromSeconds(0.3f), ignoreTimeScale: true);
                sb.AppendLine($"   파츠 5(통지 전) → 단계 {wd.SkillTier} · 화면 R={ScreenRIcon()}  ← 갱신 통지가 없으면 화면이 옛 그림");

                p.WeaponManager.RaiseEquippedWeaponRefreshed();      // 재련소 파츠 강화가 지금 보내는 통지
                await UniTask.Delay(TimeSpan.FromSeconds(0.4f), ignoreTimeScale: true);
                sb.AppendLine($"   파츠 5(통지 후) → 단계 {wd.SkillTier} · 화면 R={ScreenRIcon()}");

                state.Unequip(split.part_id);
                p.WeaponManager.RaiseEquippedWeaponRefreshed();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { sb.AppendLine("예외: " + e.Message); }

        string text = sb.ToString();
        ProbeOutput.Write("Temp/ranged_fixes_probe.txt", "원거리확인", text);
        Debug.Log("[원거리확인] 결과\n" + text);
    }

    /// <summary>입력을 넣고, 그때 생긴 화살에서 비공개 필드 하나를 읽어 온다.</summary>
    private static async UniTask<string> FireAndReadAsync(PlayerController p, Command cmd, string field)
    {
        // 직전 스킬이 끝나기를 기다린다 — 스킬 중에는 입력이 먹히지 않아 "화살이 안 나옴"으로 잘못 읽힌다(09-21).
        await UniTask.Delay(TimeSpan.FromSeconds(1.5f), ignoreTimeScale: true);

        var seen = new System.Collections.Generic.HashSet<int>(
            UnityEngine.Object.FindObjectsByType<BasicArrow>(FindObjectsSortMode.None).Select(a => a.GetInstanceID()));

        p.CooldownTracker.ResetCooldown(SkillType.E);
        p.InputBuffer.Clear();
        p.InputBuffer.Push(cmd);

        var f = typeof(BasicArrow).GetField(field, Inst);
        for (int i = 0; i < 120; i++)
        {
            await UniTask.Yield(PlayerLoopTiming.Update);
            var fresh = UnityEngine.Object.FindObjectsByType<BasicArrow>(FindObjectsSortMode.None)
                                          .FirstOrDefault(a => !seen.Contains(a.GetInstanceID()) && a.gameObject.activeInHierarchy);
            if (fresh != null) return f?.GetValue(fresh)?.ToString() ?? "(필드 없음)";
        }
        return "(화살이 안 나옴)";
    }

    private static string ScreenRIcon()
    {
        var view = UnityEngine.Object.FindFirstObjectByType<CombatPanelView>();
        if (view == null) return "(패널 없음)";
        var f = typeof(CombatPanelView).GetField("_rIconImg", Inst);
        if (f?.GetValue(view) is not UnityEngine.UI.Image img) return "(_rIconImg 없음)";
        return img.sprite != null ? img.sprite.name : "(비어 있음)";
    }
}
