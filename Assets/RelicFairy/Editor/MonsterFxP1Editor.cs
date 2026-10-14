#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-12 몬스터 전수조사 P1 — 이펙트 데이터(기획 `RelicFairy_몬스터전수조사_이펙트·판정예고_20261012.md` §2 · 83 렌더 비교 MONFX_1~7).
/// 사용자(a0 전달): 「버섯 폭발 이펙트 부적절 · 공유 몬스터 체크」.
/// - 줄인 판 3개: 사본에서 무거운 층의 <b>렌더러만</b> 끈다(층을 지우면 아래 자식까지 사라지고, SetActive로 끄면 시전 경로 ForcePlayVfx가 되살린다 · 83 확인).
/// - 축소: 찌르기 · 침 5칸 → Prick 2 줄인 판 · 검 · 할퀴기 12칸 → Sword Slash 12 줄인 판(배율은 그대로).
/// - 교체(렌더 비교로 고름): 쥐 Slash = 베기 줄인 판 ×0.6 · 골렘 Smash/Slam = Spikes 줄인 판 ×0.6 · 비홀더 직선 = 코드 내장 보라 직선 2.5 m · 가오리 방전 = 전기 고리.
///   독 분사 · 브레스 · 포자는 코드 고친 뒤 다시 찍는다(이 메뉴는 건드리지 않는다).
/// - 빼기: 플레이어 자리 맞힘 불꽃 6칸(같은 순간 플레이어 피격 연출이 알린다) · 특수 상태 진입 불꽃 5칸(몸 색 · 자세 · 움직임이 알린다).
/// 몸통 · 물기 시전 이펙트 빼기는 MonsterHitFxSetup의 10-12 메뉴가 맡는다. 다시 눌러도 같은 결과(줄인 판이 있으면 다시 만들지 않는다).
/// </summary>
public static class MonsterFxP1Editor
{
    private const string Mon = "Assets/RelicFairy/Characters/Monster/Monster/";
    private const string Mp  = "Assets/RelicFairy/_Imported/EffectSource/Magic Pig Games (Infinity PBR)/Shared Files/Magic Spells & Particles/";
    private const string Keep = null;

    private const string PrickR  = Mon + "BattleBee/Effect/Prick 2_Reduced.prefab";
    private const string SlashR  = Mon + "Skeleton/Effect/Sword Slash 12_Reduced.prefab";
    private const string SpikesR = Mon + "Golem/Effect/Spikes attack_Reduced.prefab";
    private const string ElecField = Mp + "Particle Components/PComponent Electric Field Circular.prefab";
    private static readonly Color BeamPurple = new(0.70f, 0.40f, 1.00f, 0.95f);   // 83 비교 도구 칸 3a(예고 색 흰 금 ~ 주홍과 갈린다)

    private static readonly (string src, string dst, string mute)[] Reduced =
    {
        (Mon + "BattleBee/Effect/Prick 2.prefab",       PrickR,  "Flash,FlashCore,Distortion,Smoke,StartSmoke,Derbis,LightCone1,SparksCore"),   // 9층 — 흰 찌름 줄 · 주황 궤적만(불티는 10-13 끔 — 칸 밖으로 3 m 튀어 「더 길게 찌른다」로 보였다)
        (Mon + "Skeleton/Effect/Sword Slash 12.prefab", SlashR,  "Distortion(Delete for Mobile),Hit,Flash,Flash1,Flash3,InkSparks,Smoke"),
        (Mon + "Golem/Effect/Spikes attack.prefab",     SpikesR, "Fire,Distortion,ShockWave2,SmokeDark"),
    };

    // (패턴 에셋, 패턴 이름, 새 시전 이펙트 · Keep = 그대로, 배율 · NaN = 그대로, 앞으로 m · NaN = 그대로, 맞힘 빼기)
    private static readonly (string asset, string pattern, string fx, float scale, float fwd, bool dropHit)[] Patterns =
    {
        (Mon + "BattleBee/SO/BattleBeePatternAttackOverride.asset", "Sting",     PrickR, float.NaN, float.NaN, false),
        (Mon + "BattleBee/SO/BattleBeePatternAttackOverride.asset", "DashSting", PrickR, float.NaN, float.NaN, false),
        (Mon + "StingRay/SO/StingRayPatternAttack.asset",           "BasicAttack", PrickR, float.NaN, 0.4f,   false),   // 몸 가운데 → 앞 0.4 m
        (Mon + "Fishman/SO/FishmanPatternAttackOverride.asset",     "SpearJab",  PrickR, float.NaN, float.NaN, false),
        (Mon + "RatAssassin/SO/RatAssassinPatternAttackOverride.asset", "Lunge", PrickR, float.NaN, float.NaN, true),
        (Mon + "RatAssassin/SO/RatAssassinPatternAttackOverride.asset", "Slash", SlashR, 0.6f,      float.NaN, false),   // 교체 1a
        (Mon + "Skeleton/SO/SkeletonPatternAttackOverride.asset",   "Slash",     SlashR, float.NaN, float.NaN, false),
        (Mon + "Skeleton/SO/SkeletonPatternAttackOverride.asset",   "Thrust",    SlashR, float.NaN, float.NaN, false),
        (Mon + "Orc/SO/OrcPatternAttackOverride.asset",             "Slash",     SlashR, float.NaN, float.NaN, true),
        (Mon + "Orc/SO/OrcPatternAttackOverride.asset",             "Spin",      Keep,   float.NaN, float.NaN, true),    // 시전 빼기는 MonsterHitFxSetup 10-12
        (Mon + "BishopKnight/SO/BishopKnightPatternAttackOverride.asset", "Slash",  SlashR, float.NaN, float.NaN, false),
        (Mon + "BishopKnight/SO/BishopKnightPatternAttackOverride.asset", "Thrust", SlashR, float.NaN, float.NaN, false),
        (Mon + "BlackKnight/SO/BlackKnightPatternAttackOverride.asset", "Slash",    SlashR, float.NaN, float.NaN, false),
        (Mon + "BlackKnight/SO/BlackKnightPatternAttackOverride.asset", "Thrust",   SlashR, float.NaN, float.NaN, false),
        (Mon + "BlackKnight/SO/BlackKnightPatternAttackOverride.asset", "Overhead", SlashR, float.NaN, float.NaN, false),
        (Mon + "LizardWarrior/SO/LizardWarriorPatternAttackOverride.asset", "Slash",  SlashR, float.NaN, float.NaN, true),
        (Mon + "LizardWarrior/SO/LizardWarriorPatternAttackOverride.asset", "Thrust", SlashR, float.NaN, float.NaN, true),
        (Mon + "Werewolf/SO/WerewolfPatternAttackOverride.asset",   "Claw",       SlashR, float.NaN, float.NaN, false),
        (Mon + "Werewolf/SO/WerewolfPatternAttackOverride.asset",   "HeavySwipe", SlashR, float.NaN, float.NaN, false),
        (Mon + "Golem/SO/GolemPatternAttackOverride.asset",         "Smash",     SpikesR, 0.6f,     float.NaN, false),   // 교체 2b
        (Mon + "Golem/SO/GolemPatternAttackOverride.asset",         "Slam",      SpikesR, 0.6f,     float.NaN, false),
        (Mon + "FlyingDemon/SO/FlyingDemonPatternAttack.asset",     "BasicAttack", Keep, float.NaN, float.NaN, true),
    };

    // 특수 상태 진입 — 몸 색 · 자세 · 움직임이 알린다(빼기)
    private static readonly string[] DropSpecialFx =
    {
        Mon + "Skeleton/SO/SkeletonEnrageData.asset", Mon + "Werewolf/SO/WerewolfEnrageData.asset", Mon + "Specter/SO/SpecterPhaseData.asset",
        Mon + "FairyBat/SO/FairyBatFleeData.asset",   Mon + "BishopKnight/SO/BishopShieldData.asset",
    };

    [MenuItem("RelicFairy/Setup/Monster Fx P1 — 축소 · 교체 · 맞힘 · 특수 진입 (10-12)")]
    public static void Apply()
    {
        int made = 0, changed = 0, missing = 0;
        foreach (var (src, dst, mute) in Reduced) made += MakeReduced(src, dst, mute) ? 1 : 0;

        var prefabs = new Dictionary<string, GameObject>();
        GameObject Load(string path)
        {
            if (path == null) return null;
            if (!prefabs.TryGetValue(path, out var go)) prefabs[path] = go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            return go;
        }

        foreach (var (asset, pattern, fx, scale, fwd, dropHit) in Patterns)
        {
            var so = AssetDatabase.LoadAssetAtPath<ScriptableObject>(asset);
            var o = so != null ? new SerializedObject(so) : null;
            var pat = o != null ? FindPattern(o, pattern) : null;
            if (pat == null) { Debug.LogWarning($"[몬스터 이펙트 P1] 없음 — {Short(asset)} · {pattern}"); missing++; continue; }
            var sb = new System.Text.StringBuilder($"[몬스터 이펙트 P1] {Short(asset)} · {pattern}");
            if (fx != null)
            {
                var cast = pat.FindPropertyRelative("castVfxPrefab");
                var go = Load(fx);
                if (go == null) { Debug.LogWarning($"[몬스터 이펙트 P1] 프리팹 없음 — {fx}"); missing++; continue; }
                sb.Append($" — 시전 {Name(cast.objectReferenceValue)} → {go.name}");
                cast.objectReferenceValue = go;
            }
            if (!float.IsNaN(scale))
            {
                var p = pat.FindPropertyRelative("castVfxScale");
                sb.Append($" · 배율 {p.floatValue:0.##} → {scale:0.##}");
                p.floatValue = scale;
            }
            if (!float.IsNaN(fwd))
            {
                var p = pat.FindPropertyRelative("castVfxForwardOffset");
                sb.Append($" · 앞 {p.floatValue:0.##} → {fwd:0.##} m");
                p.floatValue = fwd;
            }
            if (dropHit)
            {
                var hit = pat.FindPropertyRelative("hitVfxPrefab");
                sb.Append($" · 맞힘 {Name(hit.objectReferenceValue)} → 없음");
                hit.objectReferenceValue = null;
            }
            o.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(so);
            Debug.Log(sb.ToString());
            changed++;
        }

        // 교체 3a — 비홀더 직선: 플레이어 자리 번개 폭발 → 판정 칸과 같은 코드 내장 직선
        {
            string asset = Mon + "Beholder/SO/BeholderPatternAttackOverride.asset";
            var so = AssetDatabase.LoadAssetAtPath<ScriptableObject>(asset);
            var o = so != null ? new SerializedObject(so) : null;
            var pat = o != null ? FindPattern(o, "Laser1_Line3") : null;
            if (pat == null) { Debug.LogWarning("[몬스터 이펙트 P1] 없음 — Beholder · Laser1_Line3"); missing++; }
            else
            {
                Debug.Log($"[몬스터 이펙트 P1] Beholder · Laser1_Line3 — 시전 {Name(pat.FindPropertyRelative("castVfxPrefab").objectReferenceValue)} → 코드 내장 직선 2.5 m · 굵기 0.16 · 보라");
                pat.FindPropertyRelative("castVfxPrefab").objectReferenceValue = null;
                pat.FindPropertyRelative("castVfxAtPlayerPosition").boolValue = false;
                pat.FindPropertyRelative("useProceduralBeamVfx").boolValue = true;
                pat.FindPropertyRelative("proceduralBeamLength").floatValue = 2.5f;
                pat.FindPropertyRelative("proceduralBeamWidth").floatValue = 0.16f;
                pat.FindPropertyRelative("proceduralBeamColor").colorValue = BeamPurple;
                o.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(so);
                changed++;
            }
        }

        // 교체 5a — 가오리 방전: 불꽃 탄 맞힘 → 전기 고리(판정 3.5 m에 맞춘 배율 · 83 비교 도구 값)
        changed += SetSpecialFx(Mon + "StingRay/SO/StingRayDischargeData.asset", Load(ElecField), 7.181f) ? 1 : 0;
        foreach (var asset in DropSpecialFx) changed += SetSpecialFx(asset, null, float.NaN) ? 1 : 0;

        AssetDatabase.SaveAssets();
        Debug.Log($"[몬스터 이펙트 P1] 끝 — 줄인 판 새로 {made} · 바꾼 칸 {changed} · 못 찾음 {missing}");
    }

    private static bool MakeReduced(string src, string dst, string mute)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(dst) != null) { Debug.Log($"[몬스터 이펙트 P1] 줄인 판 있음 — {Short(dst)}"); return false; }
        if (!AssetDatabase.CopyAsset(src, dst)) { Debug.LogWarning($"[몬스터 이펙트 P1] 사본 실패 — {src}"); return false; }
        var names = new HashSet<string>(mute.Split(','));
        var root = PrefabUtility.LoadPrefabContents(dst);
        int off = 0;
        try
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (!names.Contains(t.name)) continue;
                foreach (var r in t.GetComponents<Renderer>()) { r.enabled = false; off++; }
            }
            PrefabUtility.SaveAsPrefabAsset(root, dst);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        Debug.Log($"[몬스터 이펙트 P1] 줄인 판 — {Short(dst)} · 렌더러 끔 {off}({mute})");
        return true;
    }

    private static bool SetSpecialFx(string asset, GameObject fx, float scale)
    {
        var so = AssetDatabase.LoadAssetAtPath<ScriptableObject>(asset);
        if (so == null) { Debug.LogWarning($"[몬스터 이펙트 P1] 없음 — {Short(asset)}"); return false; }
        var o = new SerializedObject(so);
        var p = o.FindProperty("_vfxPrefab");
        if (p == null) { Debug.LogWarning($"[몬스터 이펙트 P1] _vfxPrefab 칸 없음 — {Short(asset)}"); return false; }
        string before = Name(p.objectReferenceValue);
        p.objectReferenceValue = fx;
        string scaleNote = "";
        if (!float.IsNaN(scale))
        {
            var s = o.FindProperty("_vfxScale");
            scaleNote = $" · 배율 {s.floatValue:0.##} → {scale:0.###}";
            s.floatValue = scale;
        }
        o.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(so);
        Debug.Log($"[몬스터 이펙트 P1] {Short(asset)} — 특수 진입 {before} → {(fx != null ? fx.name : "없음")}{scaleNote}");
        return true;
    }

    private static SerializedProperty FindPattern(SerializedObject o, string name)
    {
        var arr = o.FindProperty("patterns");
        if (arr == null) return null;
        for (int i = 0; i < arr.arraySize; i++)
        {
            var e = arr.GetArrayElementAtIndex(i);
            if (e.FindPropertyRelative("name")?.stringValue == name) return e;
        }
        return null;
    }

    private static string Name(Object o) => o != null ? o.name : "없음";
    private static string Short(string path) => path.Replace(Mon, "");
}
#endif
