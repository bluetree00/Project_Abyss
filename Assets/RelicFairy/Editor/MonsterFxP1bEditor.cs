#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-13 몬스터 전수조사 P1 교체 3칸 — 식충식물 독 분사 · 성난 버섯 포자 · 살라만더 브레스(83 새 후보 렌더 비교 N4 · N7 · N6 3차, 4b가 사진 확인).
/// - 독 분사 = Spells Pack `Spell_Nature_4_Green` 구름만(알갱이까지 끔) ×0.934 — 판정 3.5 m 원을 고르게 덮는다.
/// - 포자 = `Spell_Nature_4_Yellow` 구름 + 포자 알갱이 ×1.143 — 알갱이가 보여야 「포자」로 읽힌다. 판정 4 m.
/// 두 프리팹은 게임 수명이 15.3 · 10.5초다. 피해는 한 번인데 구름이 오래 남으면 「계속 위험한 장판」으로 읽혀서,
/// 사본에서 반복을 끄고 입자 수명을 2.5초 · 길이를 1초 이하로 맞춘다(정점 2초 무렵 모양은 그대로).
/// - 브레스 = Luke Peek 화염방사 Style 3(불 혀가 앞으로 뻗어 앞 60° 부채꼴 안에 든다 · Style 1은 둥글게 옆으로 번졌다) ×5.6 · 몬스터 정면.
///   83 실측(×5): 0.97초 3.2 m · 1.8초 3.7 m · 3초 안 최대 4.3 m → ×5.6이면 3.6 · 4.1 · 4.8 m = 판정 4 m의 0.9 ~ 1.2배.
///   ⚠ 게임 길(SpawnVFX) 실측(10-13 ejmon)은 ×5.6 → 9.6 m · ×2.3 → 7.1 m로 배율에 거의 안 따라온다(배율을 안 따르는 층 · 연기로 추정).
///     83 사진(Hierarchy · ×4.96, 부채꼴 안)이 직접 근거라 ×5로 둔다 — 원인은 다음 슬롯에 게임 길 사진으로 확인.
///   반복 10.3초짜리라 반복 끄고 뿜는 길이 = 2.2초(예고 0.6 + 피해 2초 끝 무렵까지 불 혀가 닿는다) · 입자 수명 2.5초 이하.
/// 층은 렌더러 · 광원만 끈다(SetActive는 시전 경로가 되살린다 · 83 확인). 다시 눌러도 같은 결과.
/// </summary>
public static class MonsterFxP1bEditor
{
    private const string Mon = "Assets/RelicFairy/Characters/Monster/Monster/";
    private const string Spells = "Assets/RelicFairy/_Imported/EffectSource/Spells Pack/LWRP(URP)/Particles_LWRP/Prefabs/Variations/Spells/Nature/";
    private const float MaxLife = 2.5f, MaxDuration = 1f;

    private const string SprayR = Mon + "MonsterPlant/Effect/Spell_Nature_4_Green_Spray.prefab";
    private const string SporeR = Mon + "MushroomAngry/Effect/Spell_Nature_4_Yellow_Spore.prefab";
    private const string BreathR = Mon + "Salamander/Effect/Style 3 - Flamethrower_Breath.prefab";
    private const string Fire = "Assets/RelicFairy/_Imported/EffectSource/Luke Peek/Realistic Fire/Prefabs/";
    private const float BreathDuration = 2.2f;

    [MenuItem("RelicFairy/Setup/Monster Fx P1b — 독 분사 · 포자 (10-13)")]
    public static void Apply()
    {
        Make(Spells + "Spell_Nature_4_Green_LWRP Variant Variant.prefab",  SprayR, "@root,Point Light,Glow,Expansive Wave 2,Trails,Spore");
        Make(Spells + "Spell_Nature_4_Yellow_LWRP Variant Variant.prefab", SporeR, "@root,Point Light,Glow,Expansive Wave 2,Trails");
        Make(Fire + "Style 3/Style 3 - Flamethrower.prefab", BreathR, "", BreathDuration);
        // 특수 상태 길(SpawnVFX)은 입자 크기 모드를 안 바꾼다 — 83 비교 도구(Hierarchy 강제)와 같게 사본에 굽는다(10-13 첫 실측: 브레스 10.3 m)
        foreach (var dst in new[] { SprayR, SporeR, BreathR }) BakeHierarchy(dst);

        Set(Mon + "MonsterPlant/SO/MonsterPlantSprayData.asset", "_vfxPrefab", SprayR, "_vfxScale", 1.12f);   // 10-13 ⑩: 0.934는 게임 길 실측 0.75 ~ 0.81배
        Set(Mon + "MushroomAngry/SO/MushroomAngrySporeData.asset", "sporeEffectPrefab", SporeR, "sporeEffectScale", 1.143f);
        Set(Mon + "Salamander/SO/SalamanderBreathData.asset", "_vfxPrefab", BreathR, "_vfxScale", 5f, "_vfxFaceForward");
        AssetDatabase.SaveAssets();
        Debug.Log("[몬스터 이펙트 P1b] 끝");
    }

    private static void Make(string src, string dst, string mute, float maxDuration = MaxDuration)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(dst) != null) { Debug.Log($"[몬스터 이펙트 P1b] 줄인 판 있음 — {dst}"); return; }
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(src);
        if (prefab == null) { Debug.LogWarning($"[몬스터 이펙트 P1b] 원본 없음 — {src}"); return; }
        // 변형(Variant) 프리팹은 CopyAsset이 원본 연결을 그대로 둔다 — 풀어서(unpacked) 새 프리팹으로 저장한다
        var root = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        try
        {
            PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            var names = new HashSet<string>(mute.Split(','));
            int off = 0, trimmed = 0;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                bool hit = names.Contains(t.name) || (t == root.transform && names.Contains("@root"));
                if (!hit) continue;
                foreach (var r in t.GetComponents<Renderer>()) { r.enabled = false; off++; }
                foreach (var l in t.GetComponents<Light>()) { l.enabled = false; off++; }
            }
            foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                bool changed = false;
                if (main.loop) { main.loop = false; changed = true; }
                if (main.duration > maxDuration) { main.duration = maxDuration; changed = true; }
                var life = main.startLifetime;
                if (life.mode == ParticleSystemCurveMode.Constant && life.constant > MaxLife) { main.startLifetime = MaxLife; changed = true; }
                else if (life.mode == ParticleSystemCurveMode.TwoConstants && life.constantMax > MaxLife)
                {
                    main.startLifetime = new ParticleSystem.MinMaxCurve(Mathf.Min(life.constantMin, MaxLife), MaxLife);
                    changed = true;
                }
                if (changed) trimmed++;
            }
            root.name = System.IO.Path.GetFileNameWithoutExtension(dst);
            PrefabUtility.SaveAsPrefabAsset(root, dst);
            Debug.Log($"[몬스터 이펙트 P1b] 줄인 판 — {dst.Replace(Mon, "")} · 끈 렌더러 · 광원 {off}({mute}) · 수명 · 길이 다듬은 입자계 {trimmed}");
        }
        finally { Object.DestroyImmediate(root); }
    }

    private static void BakeHierarchy(string dst)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(dst) == null) return;
        var root = PrefabUtility.LoadPrefabContents(dst);
        try
        {
            int n = 0;
            foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                if (main.scalingMode == ParticleSystemScalingMode.Hierarchy) continue;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                n++;
            }
            if (n > 0) PrefabUtility.SaveAsPrefabAsset(root, dst);
            Debug.Log($"[몬스터 이펙트 P1b] 입자 크기 모드 Hierarchy — {dst.Replace(Mon, "")} · {n}개");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    private static void Set(string asset, string prefabField, string prefabPath, string scaleField, float scale, string faceField = null)
    {
        var so = AssetDatabase.LoadAssetAtPath<ScriptableObject>(asset);
        var fx = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (so == null || fx == null) { Debug.LogWarning($"[몬스터 이펙트 P1b] 없음 — {asset} · {prefabPath}"); return; }
        var o = new SerializedObject(so);
        var p = o.FindProperty(prefabField);
        var s = o.FindProperty(scaleField);
        if (p == null || s == null) { Debug.LogWarning($"[몬스터 이펙트 P1b] 칸 없음 — {asset} · {prefabField}/{scaleField}"); return; }
        Debug.Log($"[몬스터 이펙트 P1b] {asset.Replace(Mon, "")} — {(p.objectReferenceValue != null ? p.objectReferenceValue.name : "없음")} ×{s.floatValue:0.###} → {fx.name} ×{scale:0.###}");
        p.objectReferenceValue = fx;
        s.floatValue = scale;
        if (faceField != null)
        {
            var f = o.FindProperty(faceField);
            if (f == null) Debug.LogWarning($"[몬스터 이펙트 P1b] 칸 없음 — {asset} · {faceField}(patch_p3_vfx_path 반입 전?)");
            else f.boolValue = true;
        }
        o.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(so);
    }
}
#endif
