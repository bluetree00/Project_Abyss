using System;
using System.Reflection;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// [실측 도구 · 플레이 중] 강화 단계에 따라 <b>HUD 스킬 아이콘이 실제로 바뀌는지</b> 본다.
///
/// 근접(카타나)은 무기 강화 수치가 단계를 정한다(+5·+10) → 수치를 올리고 장착 무기 갱신을 통지한 뒤,
/// 데이터가 고른 그림 이름과 <b>화면에 실제로 붙은 그림 이름</b>을 같이 찍는다(둘이 다르면 HUD 갱신 문제다).
/// 결과: 콘솔 + Temp/skill_tier_icon_probe.txt
/// </summary>
public static class SkillTierIconProbeEditor
{
    private const string KatanaPath = "Assets/RelicFairy/Weapon/Katana/Data/T1_Katana.asset";

    [MenuItem("RelicFairy/Debug/스킬 단계 아이콘 확인 (플레이 중)")]
    private static void Run()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[단계아이콘] 플레이 모드에서만 동작한다."); return; }
        var p = GameRunBootstrapper.Instance?.Run?.Player;
        if (p == null) { Debug.LogWarning("[단계아이콘] 런 플레이어가 없다."); return; }
        RunAsync(p).Forget();
    }

    private static async UniTaskVoid RunAsync(PlayerController p)
    {
        var sb = new System.Text.StringBuilder();
        ProbeOutput.Begin("Temp/skill_tier_icon_probe.txt", "단계아이콘");
        try
        {
            var so = AssetDatabase.LoadAssetAtPath<WeaponSO>(KatanaPath);
            if (so == null) { Debug.LogWarning("[단계아이콘] 카타나 SO 없음"); return; }
            await GameRunBootstrapper.EquipWeaponToPlayerAsync(so, p, 0);
            await UniTask.Delay(TimeSpan.FromSeconds(1.0f), ignoreTimeScale: true);

            var wm = p.WeaponManager;
            var wd = wm?.CurrentWeaponData;
            if (wd == null) { Debug.LogWarning("[단계아이콘] 장착 무기 없음"); return; }

            foreach (int lvl in new[] { 0, 5, 10 })
            {
                wd.enhanceLevel = lvl;
                wm.RaiseEquippedWeaponRefreshed();          // 재련소 강화 직후와 같은 통지
                await UniTask.Delay(TimeSpan.FromSeconds(0.4f), ignoreTimeScale: true);

                string dataE = wd.skillEIcon != null ? wd.skillEIcon.name : "(없음)";
                string dataR = wd.skillQIcon != null ? wd.skillQIcon.name : "(없음)";
                sb.AppendLine($"강화 +{lvl,-2} · 단계 {wd.SkillTier} · 데이터 E={dataE} R={dataR} · 화면 R={ScreenRIcon()}");
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { sb.AppendLine("예외: " + e.Message); }

        string text = sb.ToString();
        ProbeOutput.Write("Temp/skill_tier_icon_probe.txt", "단계아이콘", text);
        Debug.Log("[단계아이콘] 결과\n" + text);
    }

    /// <summary>HUD R 칸에 실제로 붙어 있는 그림 이름(비공개 필드라 리플렉션으로 읽는다).</summary>
    private static string ScreenRIcon()
    {
        var view = UnityEngine.Object.FindFirstObjectByType<CombatPanelView>();
        if (view == null) return "(CombatPanelView 없음)";
        var f = typeof(CombatPanelView).GetField("_rIconImg", BindingFlags.Instance | BindingFlags.NonPublic);
        if (f?.GetValue(view) is not Image img) return "(_rIconImg 없음)";
        return img.sprite != null ? img.sprite.name : "(비어 있음)";
    }
}
