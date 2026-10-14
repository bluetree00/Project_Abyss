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
/// (보라 · 청록 원본에 금색을 곱하면 초록 · 검보라가 된다). 보상 등급 기둥(Loot*)은 예외 — 등급색이 원래 들어 있어 색을 입히지 않는다.
/// </summary>
public static class RunFxSetBuilder
{
    private const string SetPath   = "Assets/RelicFairy/Systems/Stage/RunGame/Fx/RunFxSet.asset";
    private const string GroupName = "Effects";
    private const string Fx        = "Assets/RelicFairy/_Imported/EffectSource/";
    private const string Ssep      = Fx + "SpecialSkillsEffectsPack/AllEffects/EffectsSet_1(NotScriptBased)/Effects/";
    private const string Vefects   = Fx + "Vefects/Item Pickup VFX URP/VFX/Particles/";
    // 예고 장판 — 리치와 같은 데칼(원 · 화살표). 놀이가 도는 동안만 PatternGuideHelper에 넣는다
    private const string GuideCircle = "Assets/RelicFairy/Characters/Monster/Boss_Monster/Lich/LichGuide_Circle.mat";
    private const string GuideArrow  = "Assets/RelicFairy/Characters/Monster/Boss_Monster/Lich/LichGuide_Arrow.mat";

    // 칸 · 프리팹 경로 · 기준 배율 · 한 번 재생 수명(0 = 파티클 길이) · 메모 · 재채색(빛깔 갈아 끼우기) · 끌 자식(쉼표)
    private static readonly (RunFxSlot slot, string path, float scale, float lifetime, string note, bool recolor, string mute)[] Map =
    {
        (RunFxSlot.Pillar, Ssep + "Effect_31_LumenJudgement/Effect_31_LumenJudgement.prefab", 0.1f, 0f,
            "등장 — 빛기둥 + 바닥 고리(SSEP 31, 0.4초에 정점)", false, null),
        // 10-11 톤 2차 V-1: 옛 Soft blue buff는 원래 파랑(b=1.0 · r 0.16~0.51)이라 금 · 주황을 곱하면 탁한 회갈색이 됐다.
        // 같은 팩(Hovl RPG)에서 입자 시작 색 채도를 재어 흰빛 원본만 골랐다 — Cute buff Yorai(채도 0, 바닥 별 모양 = 톤이 귀엽다) ·
        // Buff 6 / Buff white twist(채도 0.13 · 흰 고리 + 비틀린 빛줄기 · 입자계 3 · 스크립트 · 콜라이더 0). 「고리」 뜻에 맞는 white twist.
        (RunFxSlot.Ring,   Fx + "Hovl Studio/RPG VFX Bundle/Random effect prefabs/Buff white twist.prefab", 1f, 2f,
            "박자 고리 — 흰 고리 + 비틀린 빛줄기(Hovl RPG Buff white twist, 흰빛 원본 · 색 곱 OK)", false, null),
        // V-2: 원래 청보라라 등급색을 곱하면 금(전설)이 탁했다 → 밝기는 두고 빛깔만 갈아 끼운다
        (RunFxSlot.Swirl,  Fx + "Hovl Studio/Auras pack 3/Prefabs/Star aura.prefab", 0.7f, 0f,
            "예고 — 빛 호가 모이는 오라(Hovl Auras 3, 반복, 재채색)", true, null),
        // 10-09 출시 점검 — Burst · Orb가 처음부터 비어(10-01 「나중에 고른다」) 유물 기억 빛 · 찬란 분출 · 기수 · 변덕 특성 · 쉼터 대장간 분출이
        // 화면에 안 나왔다. 금빛 틴트 후보 비교(장비 형/23): 탈락 — 노바(보라×금 = 탁한 분홍) · Magic Sparks(흰 네모) · Lvl up(검은 얼룩 남음) ·
        // SmallLightBullet(위로 뻗는 꼬리) · INab Holy(안 보임)
        // V-15: 입자는 0초에 한 번 터지고 최대 3초 산다 — 수명 0(길이 5 + 3)이면 풀에서 8초를 붙잡았다
        (RunFxSlot.Burst,  Fx + "Spells Pack/LWRP(URP)/Particles_LWRP/Prefabs/Projectiles/Explosion/Explosion_Light_2_LWRP.prefab", 1f, 3f,
            "분출 — 빛 구 + 불꽃(Spells Pack Explosion_Light_2)", false, null),
        // V-2: 원래 금갈색이라 유물 계열색(파랑 쪽)을 곱하면 탁한 올리브가 됐다 → 재채색
        // 10-11 2차 촬영: 금덩이(Gold dot · Gold1.png)와 바닥 · 둘레 고리(Gold2.png)는 텍스처 자체가 금빛이라 재채색이 안 먹었고
        // 「노란 조각 덩어리」로 보였다 → 끄고 흰 텍스처 빛(Flares · Flash)만 유물 빛깔로
        (RunFxSlot.Orb,    Fx + "Hovl Studio/RPG VFX Bundle/Random effect prefabs/Gold dot.prefab", 1f, 0f,
            "떠 있는 빛(반복) — 유물 기억 빛(Hovl RPG Gold dot의 흰 빛만 · 재채색)", true, "Gold dot,Ground,ShockWave"),
        // 보상 등급 기둥(10-05) — 문양(SSEP 18) · 표지 기둥(SSEP 38)은 리치 봉인진 · 봉인 완성과 같은 프리팹이라 봉인으로 읽혀 걷었다.
        // V-3(10-11): 기둥 줄기 · 빛 · 반짝이는 두고 큰 빛 번짐 하위(Lens 6~8 · Flare 6~15 × 1.8 = 최대 27 m)만 끈다
        (RunFxSlot.LootCommon,    Vefects + "VFX_Item_Common.prefab",    1f, 0f, "보상 등급 기둥 — 일반(Vefects Item Pickup, 반복, 원래 빛깔)", false, null),
        (RunFxSlot.LootRare,      Vefects + "VFX_Item_Rare.prefab",      1f, 0f, "보상 등급 기둥 — 레어(원래 청빛 · 번짐 끔)", false, "Lens,Flare_03"),
        (RunFxSlot.LootEpic,      Vefects + "VFX_Item_Epic.prefab",      1f, 0f, "보상 등급 기둥 — 에픽(원래 보랏빛 · 번짐 끔)", false, "Lens,Flare_03"),
        (RunFxSlot.LootLegendary, Vefects + "VFX_Item_Legendary.prefab", 1f, 0f, "보상 등급 기둥 — 전설(원래 금빛 · 번짐 끔)", false, "Lens,Flare_04,Flare_05"),
        // V-6: 입자 장 두 개(각 3500/s) 중 하나를 끈다
        (RunFxSlot.Portal, Ssep + "Effect_16_SpaceWarpPortal/Effect_16_SpaceWarpPortal.prefab", 0.08f, 0f,
            "챕터 게이트 — 서 있는 소용돌이 문(SSEP 16, 반복, 원래 빛깔 · 입자 장 하나 끔)", false, "Effect_16_ParticleField"),
        // 10-07 후보 비교(몬스터 공격 예고/4 — 해골 머리 · 게임 카메라): Yellow Flash만 「작은 반짝임」으로 읽혔다(흰 핵 + 노란 살 0.03초 → 0.3초에 사라짐).
        // 탈락: Magic Pig 별 3종 · Hovl Flash 5 red(안 보임) · cute star(흐린 연기) · StarMuzzle(몸을 덮는 분홍 폭발)
        (RunFxSlot.AttackGlint, Fx + "Hovl Studio/RPG VFX Bundle/Prefabs/Magic buffs and hits/Yellow Flash.prefab", 1.4f, 0f,
            "일반 몬스터 공격 예고 반짝임 — 맞히기 0.45초 전 머리에서(Hovl RPG Yellow Flash, 몸 높이로 크기)", false, null),
    };

    // 진한 빛깔 칸 — Hovl 「…cg」 재질(_Emission 2~8)에 옅은 색을 곱하면 흰빛으로 바랬다(10-11 2차 촬영 VFX2_01 · 05)
    private static readonly RunFxSlot[] VividSlots = { RunFxSlot.Ring, RunFxSlot.Orb };

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
            list.Add(new RunFxEntry { slot = m.slot, prefab = prefab, scale = m.scale, lifetime = m.lifetime, note = m.note,
                                      recolor = m.recolor, mute = m.mute, vividTint = System.Array.IndexOf(VividSlots, m.slot) >= 0 });
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
