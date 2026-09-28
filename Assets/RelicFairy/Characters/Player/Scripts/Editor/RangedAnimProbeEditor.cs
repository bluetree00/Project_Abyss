using System;
using System.Collections.Generic;
using System.Text;
using Cysharp.Threading.Tasks;
using Game.Inputs;
using UnityEditor;
using UnityEngine;

/// <summary>
/// [실측 도구 · 플레이 중] 원거리 스킬 애니메이션 확인 — 활·석궁을 <b>실제 장착 경로</b>로 들고(클립 선로드 포함)
/// 스킬을 써서 <b>매 순간 실제로 재생되는 클립 이름</b>을 찍는다.
///
/// 보려는 것 둘:
///  · 활 스킬 후반부가 카타나 발도(QSkill_Iasen)인가, 활 발사 모션인가(09-21 오버라이드 키 수정 검증).
///  · 석궁 스킬이 동작 없이 지나가는가, 짧은 시전 자세가 나오는가(09-21 시전 모션 추가 검증).
/// 결과는 콘솔 + Temp/ranged_anim_probe.txt, 화면은 Temp/ranged_*.png.
/// </summary>
public static class RangedAnimProbeEditor
{
    private const string BowPath      = "Assets/RelicFairy/Weapon/Bow/Data/T1_Bow.asset";
    private const string CrossbowPath = "Assets/RelicFairy/Weapon/Crossbow/Data/T1_Crossbow.asset";
    private static readonly string[] WatchKeys = { "QSkill_01", "QSkill_Iasen", "ESkill_01" };

    [MenuItem("RelicFairy/Debug/원거리 스킬 애니 확인 (플레이 중)")]
    private static void Run()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[RangedAnim] 플레이 모드에서만 동작한다."); return; }
        var p = GameRunBootstrapper.Instance?.Run?.Player;
        if (p == null) { Debug.LogWarning("[RangedAnim] 런 플레이어가 없다."); return; }
        RunAsync(p).Forget();
    }

    private static async UniTaskVoid RunAsync(PlayerController p)
    {
        var sb = new StringBuilder();
        ProbeOutput.Begin("Temp/ranged_anim_probe.txt", "RangedAnim");
        try
        {
            await ProbeAsync(p, BowPath,      "활",   SkillType.R, sb);
            await ProbeAsync(p, CrossbowPath, "석궁", SkillType.R, sb);
            await ProbeAsync(p, CrossbowPath, "석궁", SkillType.E, sb);
        }
        catch (OperationCanceledException) { sb.AppendLine("취소됨"); }
        catch (Exception e) { sb.AppendLine("예외: " + e); }

        string text = sb.ToString();
        ProbeOutput.Write("Temp/ranged_anim_probe.txt", "RangedAnim", text);
        Debug.Log("[RangedAnim] 결과\n" + text);
    }

    /// <summary>무기 하나를 장착하고 스킬 한 칸을 써서, 재생되는 클립이 바뀔 때마다 기록한다.</summary>
    private static async UniTask ProbeAsync(PlayerController p, string soPath, string label,
                                            SkillType slot, StringBuilder sb)
    {
        try
        {
            var so = AssetDatabase.LoadAssetAtPath<WeaponSO>(soPath);
            if (so == null) { sb.AppendLine($"⚠ {label} SO를 못 찾았다: {soPath}"); return; }

            // 이 경로만 클립을 선로드한다 — WeaponManager를 직접 부르면 선로드를 건너뛰어 오버라이드가 통째로 실패한다.
            await GameRunBootstrapper.EquipWeaponToPlayerAsync(so, p, 1);
            await UniTask.Delay(TimeSpan.FromSeconds(1.0f), ignoreTimeScale: true);

            sb.AppendLine($"── {label} · {slot} 스킬 ── 장착: {p.WeaponManager?.CurrentWeaponData?.weaponSOKey ?? "없음"}");
            AppendOverrideState(p, sb);

            if (!p.HasSkillInSlot(slot)) { sb.AppendLine($"  {slot} 칸에 스킬 없음 — 건너뜀\n"); return; }

            p.CooldownTracker.ResetCooldown(slot);
            p.InputBuffer.Clear();
            p.InputBuffer.Push(slot == SkillType.E ? Command.ESkill : Command.RSkill);

            string prev = null;
            float t0 = Time.unscaledTime;
            int shot = 0;
            while (Time.unscaledTime - t0 < 3.0f)
            {
                await UniTask.Yield(PlayerLoopTiming.Update);
                string clip = CurrentClip(p);
                if (clip == prev) continue;
                sb.AppendLine($"  {Time.unscaledTime - t0:F2}s · 재생 클립 → {clip}");
                prev = clip;
                if (shot < 3) { ScreenCapture.CaptureScreenshot($"Temp/ranged_{label}_{slot}_{shot}.png"); shot++; }
            }
            sb.AppendLine();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception e) { sb.AppendLine($"  {label} 예외: {e.Message}\n"); }
    }

    /// <summary>키(원본 클립)마다 지금 묶여 있는 클립 이름 — 무기 교체가 실제로 먹었는지 본다.</summary>
    private static void AppendOverrideState(PlayerController p, StringBuilder sb)
    {
        var aoc = p.Anim != null ? p.Anim.runtimeAnimatorController as AnimatorOverrideController : null;
        if (aoc == null) { sb.AppendLine("  ⚠ AnimatorOverrideController가 아니다 — 확인 불가"); return; }

        var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>();
        aoc.GetOverrides(pairs);
        foreach (string key in WatchKeys)
        {
            string bound = "(그 이름의 원본 클립 없음)";
            foreach (var kv in pairs)
                if (kv.Key != null && kv.Key.name == key) { bound = kv.Value != null ? kv.Value.name : "(교체 안 됨)"; break; }
            sb.AppendLine($"  키 {key,-14} → {bound}");
        }
    }

    private static string CurrentClip(PlayerController p)
    {
        var anim = p.Anim;
        if (anim == null) return "(애니메이터 없음)";
        var infos = anim.GetCurrentAnimatorClipInfo(0);
        if (infos == null || infos.Length == 0) return "(재생 클립 없음)";
        var best = infos[0];
        for (int i = 1; i < infos.Length; i++) if (infos[i].weight > best.weight) best = infos[i];
        return best.clip != null ? best.clip.name : "(null)";
    }
}
