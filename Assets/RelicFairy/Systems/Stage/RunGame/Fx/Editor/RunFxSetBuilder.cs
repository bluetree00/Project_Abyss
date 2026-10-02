using System.Collections.Generic;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

/// <summary>
/// 런 공용 이펙트 목록을 만든다 — _Imported 이펙트 팩에서 고른 프리팹을 칸에 적고 Addressables에 올린다.
/// 메뉴: RelicFairy/Setup/Build Run Fx Set (다시 눌러도 같은 결과).
///
/// 고른 기준(10-01 후보 비교, Logs/reward_fx/cand_*): 빛깔을 곱해 입히므로 <b>원래 색이 흰빛에 가까운</b> 것만 받는다
/// (보라 · 청록 원본에 금색을 곱하면 초록 · 검보라가 된다). 표지 기둥만 예외 — 전설 전용이라 원래 금빛을 그대로 쓴다.
/// </summary>
public static class RunFxSetBuilder
{
    private const string SetPath   = "Assets/RelicFairy/Systems/Stage/RunGame/Fx/RunFxSet.asset";
    private const string GroupName = "Effects";
    private const string Fx        = "Assets/RelicFairy/_Imported/EffectSource/";
    private const string Ssep      = Fx + "SpecialSkillsEffectsPack/AllEffects/EffectsSet_1(NotScriptBased)/Effects/";
    // 예고 장판 — 리치와 같은 데칼(원 · 화살표). 놀이가 도는 동안만 PatternGuideHelper에 넣는다
    private const string GuideCircle = "Assets/RelicFairy/Characters/Monster/Boss_Monster/Lich/LichGuide_Circle.mat";
    private const string GuideArrow  = "Assets/RelicFairy/Characters/Monster/Boss_Monster/Lich/LichGuide_Arrow.mat";

    // 칸 · 프리팹 경로 · 기준 배율 · 한 번 재생 수명(0 = 파티클 길이) · 메모
    private static readonly (RunFxSlot slot, string path, float scale, float lifetime, string note)[] Map =
    {
        (RunFxSlot.Pillar, Ssep + "Effect_31_LumenJudgement/Effect_31_LumenJudgement.prefab", 0.1f, 0f,
            "등장 — 빛기둥 + 바닥 고리(SSEP 31, 0.4초에 정점)"),
        (RunFxSlot.Ring,   Fx + "Hovl Studio/RPG VFX Bundle/Prefabs/Magic buffs and hits/Soft blue buff.prefab", 1f, 2f,
            "등장 박자 — 빛줄기 + 바닥 불꽃(Hovl RPG, 점광원 포함)"),
        (RunFxSlot.Glyph,  Ssep + "Effect_18_TimeField/Effect_18_TimeField.prefab", 0.2f, 0f,
            "바닥 문양(SSEP 18, 반복) — 원본 청록: 파랑 · 보라에만 쓴다"),
        (RunFxSlot.Swirl,  Fx + "Hovl Studio/Auras pack 3/Prefabs/Star aura.prefab", 0.7f, 0f,
            "예고 — 빛 호가 모이는 오라(Hovl Auras 3, 반복)"),
        (RunFxSlot.Beacon, Ssep + "Effect_38_GloryBoundary/Effect_38_GloryBoundary.prefab", 0.2f, 0f,
            "표지 기둥 — 금빛 기둥 + 고리(SSEP 38, 반복, 전설 전용)"),
        (RunFxSlot.Portal, Ssep + "Effect_16_SpaceWarpPortal/Effect_16_SpaceWarpPortal.prefab", 0.08f, 0f,
            "챕터 게이트 — 서 있는 소용돌이 문(SSEP 16, 반복, 원래 빛깔 · 거점 심연 입구와 같은 것)"),
    };

    [MenuItem("RelicFairy/Setup/Build Run Fx Set")]
    public static void Build()
    {
        var set = AssetDatabase.LoadAssetAtPath<RunFxSetSO>(SetPath);
        if (set == null)
        {
            set = ScriptableObject.CreateInstance<RunFxSetSO>();
            AssetDatabase.CreateAsset(set, SetPath);
        }

        var list = new List<RunFxEntry>();
        foreach (var m in Map)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(m.path);
            if (prefab == null)
            {
                Debug.LogWarning($"[RunFx 만들기] 프리팹 없음 — {m.path} ({m.slot} 건너뜀)");
                continue;
            }
            list.Add(new RunFxEntry { slot = m.slot, prefab = prefab, scale = m.scale, lifetime = m.lifetime, note = m.note });
        }
        set.EditorSetEntries(list);
        set.EditorSetGuides(AssetDatabase.LoadAssetAtPath<Material>(GuideCircle), AssetDatabase.LoadAssetAtPath<Material>(GuideArrow));
        if (set.GuideCircle == null) Debug.LogWarning($"[RunFx 만들기] 예고 장판 재질 없음 — {GuideCircle}");
        EditorUtility.SetDirty(set);
        AssetDatabase.SaveAssets();

        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null) { Debug.LogError("[RunFx 만들기] AddressableAssetSettings 없음"); return; }
        var group = settings.FindGroup(GroupName);
        if (group == null) group = settings.DefaultGroup;

        var entry = settings.CreateOrMoveEntry(AssetDatabase.AssetPathToGUID(SetPath), group, false, false);
        entry.address = RunFx.SetAddress;
        settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryMoved, entry, true);
        AssetDatabase.SaveAssets();

        Debug.Log($"[RunFx 만들기] 목록 {list.Count}/{Map.Length}칸 · Addressables {group.Name} / {RunFx.SetAddress}");
    }
}
