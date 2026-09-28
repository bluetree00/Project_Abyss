using System;
using System.Linq;
using System.Text;
using Cysharp.Threading.Tasks;
using Game.Inputs;
using UnityEditor;
using UnityEngine;

/// <summary>
/// [실측 도구 · 플레이 중] 원거리 <b>파츠 효과가 스킬에도 붙는지</b> 실제로 센다.
///
/// 코드 주석은 "기본공격과 같은 퍼널"이라고 말하지만 말이 아니라 수를 본다(09-21 사용자 요청 확인용).
/// 분열 파츠를 0 → 높은 레벨로 올리고, <b>기본 공격</b>과 <b>E 스킬</b>이 각각 만드는 화살 수를 센다.
/// 결과: 콘솔 + Temp/ranged_parts_skill_probe.txt
/// </summary>
public static class RangedPartsSkillProbeEditor
{
    private const string BowPath = "Assets/RelicFairy/Weapon/Bow/Data/T1_Bow.asset";

    [MenuItem("RelicFairy/Debug/원거리 파츠가 스킬에도 붙는지 (플레이 중)")]
    private static void Run()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[파츠스킬] 플레이 모드에서만 동작한다."); return; }
        var p = GameRunBootstrapper.Instance?.Run?.Player;
        if (p == null) { Debug.LogWarning("[파츠스킬] 런 플레이어가 없다."); return; }
        RunAsync(p).Forget();
    }

    private static async UniTaskVoid RunAsync(PlayerController p)
    {
        var sb = new StringBuilder();
        ProbeOutput.Begin("Temp/ranged_parts_skill_probe.txt", "파츠스킬");
        try
        {
            var so = AssetDatabase.LoadAssetAtPath<WeaponSO>(BowPath);
            if (so == null) { Debug.LogWarning("[파츠스킬] 활 SO 없음"); return; }
            await GameRunBootstrapper.EquipWeaponToPlayerAsync(so, p, 1);
            await UniTask.Delay(TimeSpan.FromSeconds(1.0f), ignoreTimeScale: true);
            sb.AppendLine($"장착: {p.WeaponManager?.CurrentWeaponData?.weaponSOKey} · 슬롯 {p.WeaponManager?.CurrentSlotIndex}");

            // 분열(Split) 파츠 하나를 고른다 — 차트에서 종류로 찾는다(아이디 하드코딩 금지).
            var split = Managers.WeaponParts?.All?.FirstOrDefault(e => e.Kind == RangedPartKind.Split);
            if (split == null) { sb.AppendLine("⚠ 분열 파츠가 차트에 없다 — 중단"); Finish(sb); return; }

            var state = RangedPartsState.Current;
            int before = state.LevelOf(split.part_id);
            sb.AppendLine($"분열 파츠: {split.part_id} (시작 레벨 {before})");

            foreach (int lvl in new[] { 0, 3, 6 })
            {
                SetLevel(state, split.part_id, lvl);
                await UniTask.Delay(TimeSpan.FromSeconds(0.3f), ignoreTimeScale: true);

                int basic = await CountArrowsAsync(p, Command.Light);
                await UniTask.Delay(TimeSpan.FromSeconds(0.8f), ignoreTimeScale: true);
                int skill = await CountArrowsAsync(p, Command.ESkill);
                await UniTask.Delay(TimeSpan.FromSeconds(0.8f), ignoreTimeScale: true);

                sb.AppendLine($"  분열 레벨 {lvl,-2} → 기본공격 화살 {basic,2}발 · E 스킬 화살 {skill,2}발 · 파츠 총합 {state.TotalLevel}");
            }

            SetLevel(state, split.part_id, before);   // 원래대로
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { sb.AppendLine("예외: " + e.Message); }
        Finish(sb);
    }

    private static void SetLevel(RangedPartsState state, string partId, int level)
    {
        if (level <= 0) { state.Unequip(partId); return; }
        if (state.LevelOf(partId) <= 0) state.Equip(partId, level);
        else                            state.LevelUp(partId, level - state.LevelOf(partId));
    }

    /// <summary>입력을 넣고, 그 직후 새로 생긴 화살 수를 센다.</summary>
    private static async UniTask<int> CountArrowsAsync(PlayerController p, Command cmd)
    {
        int before = UnityEngine.Object.FindObjectsByType<BasicArrow>(FindObjectsSortMode.None)
                                       .Count(a => a.gameObject.activeInHierarchy);
        p.CooldownTracker.ResetCooldown(SkillType.E);
        p.InputBuffer.Clear();
        p.InputBuffer.Push(cmd);

        int peak = 0;
        for (int f = 0; f < 110; f++)            // 스킬은 시전 뒤에 나간다(부채살 0.55초) — 넉넉히 본다
        {
            await UniTask.Yield(PlayerLoopTiming.Update);
            int now = UnityEngine.Object.FindObjectsByType<BasicArrow>(FindObjectsSortMode.None)
                                        .Count(a => a.gameObject.activeInHierarchy);
            peak = Mathf.Max(peak, now - before);
        }
        return peak;
    }

    private static void Finish(StringBuilder sb)
    {
        string text = sb.ToString();
        ProbeOutput.Write("Temp/ranged_parts_skill_probe.txt", "파츠스킬", text);
        Debug.Log("[파츠스킬] 결과\n" + text);
    }
}
