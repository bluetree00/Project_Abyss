#if UNITY_EDITOR
using System.Reflection;
using RelicFairy.Monster;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-13 「이펙트 = 판정」 일반 · 정예 몬스터 데이터 손질(기획 `RelicFairy_이펙트·판정일치_전수검증_20261013.md` §3).
/// - 판정 모양: 전투벌 Sting = 앞 1칸(구 모양 데이터라 뒤까지 맞던 둘레 8칸 → 찌르기) · 늑대인간 HeavySwipe = 가로 3칸
///   (모양 데이터가 없어 1칸 · 보통 공격보다 작던 「휩쓸기」). 흑기사 Overhead는 세로 베기 그림이 1칸에 맞아(실측 옆 0.45배가 됨) 그대로.
/// - 시전 이펙트 배율 · 앞 위치: ejmon 실측(10-13)의 맞춤 배율. 둥근 땅 가시(게 · 골렘 Smash)는 앞 칸 가운데(1 m)로 옮겨 칸 크기로.
/// - 특수 상태 진입 불꽃: 판정 없는 것(상자 · 슬라임 회복 · 골렘 포효) 빼기 · 선인장 가시 반격 = 2.5 m에 맞춤.
/// - 투사체 맞힘 이펙트: 판정이 한 대상(0.6 m)인데 3 ~ 7 m 폭발로 보였다 → 반경 0.8 m 배율.
/// - 비홀더 직선 = 길이 1.95(0.55 m부터 → 끝 2.5 m = 판정 칸 끝).
/// - 공격 거리 > 격자 끝이면 격자 끝으로 — 1.5 ~ 2.2 m에서 휘두르면 반드시 헛쳤다(멈춤 거리도 여기서 나온다). 고정 교전 거리도 같이.
/// - 골렘 포효 진입 불꽃 · 거미 거미줄 발사 순간 몸 발밑 불꽃 빼기(전수조사 「빼기」).
/// 다시 눌러도 같은 결과. 보스 폴더는 건드리지 않는다.
/// </summary>
public static class MonsterJudgmentDataEditor
{
    private const string Mon = "Assets/RelicFairy/Characters/Monster/Monster/";
    private static readonly string[] BossFolders = { "DeathKnightBoss", "DragonBoss", "ForestGuardian", "Dragon", "DemonKing", "Boss", "TrainingDummy" };

    [MenuItem("RelicFairy/Setup/Monster 판정 데이터 손질 — 이펙트 = 판정 (10-13)")]
    public static void Apply()
    {
        int n = 0;
        n += Shape("BattleBee/SO/BattleBeePatternAttackOverride.asset", "Sting", PatternAttackOverrideSO.AttackPattern.WarningShapeType.Front1);
        n += Shape("Werewolf/SO/WerewolfPatternAttackOverride.asset", "HeavySwipe", PatternAttackOverrideSO.AttackPattern.WarningShapeType.FrontWide3);

        var beholder = AssetDatabase.LoadAssetAtPath<PatternAttackOverrideSO>(Mon + "Beholder/SO/BeholderPatternAttackOverride.asset");
        var line = beholder != null ? beholder.patterns.Find(x => x.name == "Laser1_Line3") : null;
        if (line != null && line.useProceduralBeamVfx)
        {
            float len = Mathf.Max(0.5f, 2.5f - line.castVfxForwardOffset);
            Debug.Log($"[몬스터 판정 데이터] Beholder · Laser1_Line3 — 직선 길이 {line.proceduralBeamLength:0.##} → {len:0.##}(끝 2.5 m = 판정 칸 끝)");
            line.proceduralBeamLength = len;
            EditorUtility.SetDirty(beholder);
            n++;
        }

        // 공격 거리 — 격자 끝(앞 길이)보다 길면 격자 끝으로
        var resolve = typeof(PatternAttackOverrideSO).GetMethod("ResolveWarningShape", BindingFlags.Static | BindingFlags.NonPublic);
        foreach (var guid in AssetDatabase.FindAssets("t:PatternAttackOverrideSO", new[] { Mon.TrimEnd('/') }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (IsBoss(path)) continue;
            var so = AssetDatabase.LoadAssetAtPath<PatternAttackOverrideSO>(path);
            if (so == null || so.patterns == null) continue;
            var cfgShape = ConfigShape(path);
            bool dirty = false;
            float maxRange = 0f;
            foreach (var p in so.patterns)
            {
                var shape = p.attackShapeOverride != null ? p.attackShapeOverride : cfgShape;
                if (shape is MonsterRangedAttackSO) { maxRange = Mathf.Max(maxRange, p.maxDistance); continue; }
                var grid = (MonsterGroundWarning.GridShape)resolve.Invoke(null, new object[] { p, shape });
                MonsterGroundWarning.GridBounds(grid, out _, out _, out _, out float maxY);
                if (p.maxDistance > maxY + 0.05f)
                {
                    Debug.Log($"[몬스터 판정 데이터] {Short(path)} · {p.name} — 공격 거리 {p.maxDistance:0.##} → {maxY:0.##}(격자 {grid} 끝)");
                    p.maxDistance = maxY;
                    dirty = true; n++;
                }
                maxRange = Mathf.Max(maxRange, p.maxDistance);
            }
            // 칼 베기 오른쪽 위치(10-13 ⑩) — 앞 1 ~ 2칸 판정은 몸 가운데 줄인데 베기를 오른손 쪽으로 0.25 ~ 0.4 m 옮겨 옆이 1.3 ~ 1.6배로 보였다
            //   (베기 후보 비교: 그림 자체는 앞 1.5 m에 옆 0.42 m = 0.84배로 맞음) → 오른쪽 위치 0.1 m 이하로
            foreach (var p in so.patterns)
            {
                if (p.castVfxPrefab == null || !p.castVfxPrefab.name.StartsWith("Sword Slash") || p.castVfxRightOffset <= 0.1f) continue;
                var sh = p.attackShapeOverride != null ? p.attackShapeOverride : cfgShape;
                if (sh is MonsterRangedAttackSO) continue;
                var gr = (MonsterGroundWarning.GridShape)resolve.Invoke(null, new object[] { p, sh });
                if (gr != MonsterGroundWarning.GridShape.Front1 && gr != MonsterGroundWarning.GridShape.Front2) continue;
                Debug.Log($"[몬스터 판정 데이터] {Short(path)} · {p.name} — 베기 오른쪽 위치 {p.castVfxRightOffset:0.##} → 0.1(격자 {gr} 가운데 줄)");
                p.castVfxRightOffset = 0.1f;
                dirty = true; n++;
            }
            if (so.engageDistance > 0f && so.engageDistance > maxRange + 0.05f)
            {
                Debug.Log($"[몬스터 판정 데이터] {Short(path)} — 고정 교전 거리 {so.engageDistance:0.##} → {maxRange:0.##}");
                so.engageDistance = maxRange;
                dirty = true; n++;
            }
            if (dirty) EditorUtility.SetDirty(so);
        }

        n += ClearVfx(Mon + "Golem/SO/GolemRoarData.asset");
        n += ClearVfx(Mon + "Spider/SO/SpiderWebRangedAttack.asset");
        n += MuteLayer(Mon + "BattleBee/Effect/Prick 2_Reduced.prefab", "SparksCore");
        n += CastPrefab("Cactus", "BasicAttack", Mon + "BattleBee/Effect/Prick 2_Reduced.prefab");   // 원본 Prick 2는 불티로 측정마다 흔들렸다(⑨)   // 찌르기 불티 — 칸 밖 3 m(83 사진 · 10-13 ⑦)

        // 시전 이펙트 — (폴더, 패턴, 배율, 앞 위치 · NaN이면 그대로). 실측 = 바탕화면 기획 이펙트·판정일치 §4 결과표
        foreach (var (folder, pattern, scale, fwd) in new (string, string, float, float)[]
        {
            // 2차(10-13): 1차 실측이 배율에 정비례하지 않아 1차 값에서 앞 · 옆이 둘 다 0.8 ~ 1.25배에 들도록 다시 맞춤
            ("BishopKnight", "Slash", 0.34f, float.NaN), ("BishopKnight", "Thrust", 0.45f, float.NaN),
            ("BlackKnight", "Slash", 0.3f, float.NaN), ("BlackKnight", "Thrust", 0.32f, float.NaN), ("BlackKnight", "Overhead", 0.36f, float.NaN),
            ("LizardWarrior", "Slash", 0.31f, float.NaN), ("LizardWarrior", "Thrust", 0.45f, float.NaN),
            ("Skeleton", "Slash", 0.34f, float.NaN), ("Skeleton", "Thrust", 0.37f, float.NaN),
            // 3차(10-13 ⑤): 누운 베기 3칸은 아래 회전을 세로(0, -90)로 바꾸고 같은 격자의 세로 베기 값으로 · 벌 Sting은 첫 줄 데우기 오차를 걷어낸 값(×1.0 → 3.0 m)으로
            ("Orc", "Slash", 0.3f, float.NaN), ("RatAssassin", "Slash", 0.3f, float.NaN),
            ("Werewolf", "Claw", 0.3f, 1.25f), ("Werewolf", "HeavySwipe", 0.44f, float.NaN),
            // 4차(10-13 ⑤ 배율 훑기): 찌르기(Prick)는 배율을 키워도 앞으로 안 늘고(×0.3 → 1.4에 0.6 → 1.2 m) 옆만 넓어진다
            // → 옆이 맞는 배율로 두고 그림 끝이 판정 칸 끝의 0.9배에 오게 앞으로 옮긴다(이펙트는 몬스터에 붙어 있고 돌진 없음)
            // 5차(10-13 ⑦ · 83 사진 ②f): 앞으로 옮기면 몸통(약 1 m)이 칸 안에 든다 · 도달 3 ~ 4.7 m는 멀리 튀는 불티(SparksCore) 몫이라 그 층을 끈다(아래)
            ("BattleBee", "Sting", 0.9f, 0.85f), ("StingRay", "BasicAttack", 0.9f, 0.85f),
            ("BattleBee", "DashSting", 0.9f, 1.75f), ("Fishman", "SpearJab", 0.9f, 1.75f), ("RatAssassin", "Lunge", 0.6f, 1.9f),
            ("Cactus", "BasicAttack", 0.9f, 0.85f),   // ⑨ 찌르기 사본으로(아래) — 가오리와 같은 설정
            ("Beholder", "Laser2_Burst8", 0.16f, float.NaN),
            ("CrabMonster", "Claw", 0.21f, 1f), ("CrabMonster", "Stab", 0.21f, 1f), ("CrabMonster", "Spin", 0.53f, float.NaN),
            ("Golem", "Smash", 0.28f, 1f),
        })
            n += CastVfx(folder, pattern, scale, fwd);

        // 시전 이펙트 회전 — 앞 1 ~ 2칸 판정인데 베기가 옆으로 누워(쥐 0 · 0 / 오크 · 늑대 할퀴기 90 · -90) 옆 2 ~ 4배로 번졌다.
        // 잘 맞는 세로 베기(해골 · 흑기사 · 도마뱀 = 0 · -90)와 같게. 가로 3칸 휩쓸기(HeavySwipe)는 누운 그대로가 맞다.
        foreach (var (folder, pattern, rx, ry) in new (string, string, float, float)[]
        {
            ("RatAssassin", "Slash", 0f, -90f), ("Orc", "Slash", 0f, -90f), ("Werewolf", "Claw", 0f, -90f),
        })
            n += CastRot(folder, pattern, rx, ry);

        // 특수 상태 진입 이펙트 — 판정 없는 불꽃은 빼고, 선인장 가시 반격(2.5 m 즉시)은 그 크기로
        n += ClearVfx(Mon + "ChestMonster/SO/ChestDecoyData.asset");
        n += ClearVfx(Mon + "Slime/SO/SlimeRegenData.asset");
        n += FloatField(Mon + "Cactus/SO/CactusThornData.asset", "_vfxScale", 0.69f);

        // 투사체 맞힘 — 반경 0.8 m
        foreach (var (name, scale) in new[] { ("CyclopsRangedAttack", 0.14f), ("EvilMageBurstRangedAttack", 0.16f), ("EvilMageRangedAttack", 0.16f),
                                              ("FishmanRangedAttack", 0.14f), ("NagaWizardBurstRangedAttack", 0.11f), ("NagaWizardRangedAttack", 0.12f),
                                              ("SpiderWebRangedAttack", 0.12f) })
        {
            var g = AssetDatabase.FindAssets(name + " t:MonsterRangedAttackSO", new[] { Mon.TrimEnd('/') });
            var so = g.Length > 0 ? AssetDatabase.LoadAssetAtPath<MonsterRangedAttackSO>(AssetDatabase.GUIDToAssetPath(g[0])) : null;
            if (so == null || so.name != name) { Debug.LogWarning($"[몬스터 판정 데이터] 없음 — {name}"); continue; }
            if (Mathf.Approximately(so.vfxHitEffectScale, scale)) continue;
            Debug.Log($"[몬스터 판정 데이터] {name} — 맞힘 이펙트 배율 {so.vfxHitEffectScale:0.###} → {scale:0.###}(반경 0.8 m)");
            so.vfxHitEffectScale = scale;
            EditorUtility.SetDirty(so);
            n++;
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[몬스터 판정 데이터] 끝 — 바꾼 칸 {n}");
    }

    private static int Shape(string rel, string pattern, PatternAttackOverrideSO.AttackPattern.WarningShapeType shape)
    {
        var so = AssetDatabase.LoadAssetAtPath<PatternAttackOverrideSO>(Mon + rel);
        var p = so != null ? so.patterns.Find(x => x.name == pattern) : null;
        if (p == null) { Debug.LogWarning($"[몬스터 판정 데이터] 없음 — {rel} · {pattern}"); return 0; }
        if (p.warningShape == shape) return 0;
        Debug.Log($"[몬스터 판정 데이터] {rel} · {pattern} — 판정 모양 {p.warningShape} → {shape}");
        p.warningShape = shape;
        EditorUtility.SetDirty(so);
        return 1;
    }

    private static int CastVfx(string folder, string pattern, float scale, float forward)
    {
        PatternAttackOverrideSO so = null;
        foreach (var g in AssetDatabase.FindAssets("t:PatternAttackOverrideSO", new[] { Mon + folder }))
            so = AssetDatabase.LoadAssetAtPath<PatternAttackOverrideSO>(AssetDatabase.GUIDToAssetPath(g));
        var p = so != null ? so.patterns.Find(x => x.name == pattern) : null;
        if (p == null) { Debug.LogWarning($"[몬스터 판정 데이터] 없음 — {folder} · {pattern}"); return 0; }
        bool fwd = !float.IsNaN(forward) && !Mathf.Approximately(p.castVfxForwardOffset, forward);
        if (Mathf.Approximately(p.castVfxScale, scale) && !fwd) return 0;
        Debug.Log($"[몬스터 판정 데이터] {folder} · {pattern} — 시전 이펙트 배율 {p.castVfxScale:0.###} → {scale:0.###}" +
                  (fwd ? $" · 앞 위치 {p.castVfxForwardOffset:0.##} → {forward:0.##}" : ""));
        p.castVfxScale = scale;
        if (fwd) p.castVfxForwardOffset = forward;
        EditorUtility.SetDirty(so);
        return 1;
    }

    /// <summary>프리팹 사본의 층 하나를 끈다(렌더러만 — 시전 길 ForcePlayVfx가 SetActive를 되살린다 · 83 확인).</summary>
    private static int MuteLayer(string prefabPath, string layer)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null) { Debug.LogWarning($"[몬스터 판정 데이터] 없음 — {prefabPath}"); return 0; }
        var root = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            int off = 0;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == layer && t.TryGetComponent<ParticleSystemRenderer>(out var r) && r.enabled) { r.enabled = false; off++; }
            if (off == 0) return 0;
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Debug.Log($"[몬스터 판정 데이터] {Short(prefabPath)} — {layer} 층 끔 {off}개");
            return off;
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    private static int CastPrefab(string folder, string pattern, string prefabPath)
    {
        PatternAttackOverrideSO so = null;
        foreach (var g in AssetDatabase.FindAssets("t:PatternAttackOverrideSO", new[] { Mon + folder }))
            so = AssetDatabase.LoadAssetAtPath<PatternAttackOverrideSO>(AssetDatabase.GUIDToAssetPath(g));
        var p = so != null ? so.patterns.Find(x => x.name == pattern) : null;
        var fx = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (p == null || fx == null) { Debug.LogWarning($"[몬스터 판정 데이터] 없음 — {folder} · {pattern} · {prefabPath}"); return 0; }
        if (p.castVfxPrefab == fx) return 0;
        Debug.Log($"[몬스터 판정 데이터] {folder} · {pattern} — 시전 이펙트 {(p.castVfxPrefab != null ? p.castVfxPrefab.name : "없음")} → {fx.name}");
        p.castVfxPrefab = fx;
        EditorUtility.SetDirty(so);
        return 1;
    }

    private static int CastRot(string folder, string pattern, float rx, float ry)
    {
        PatternAttackOverrideSO so = null;
        foreach (var g in AssetDatabase.FindAssets("t:PatternAttackOverrideSO", new[] { Mon + folder }))
            so = AssetDatabase.LoadAssetAtPath<PatternAttackOverrideSO>(AssetDatabase.GUIDToAssetPath(g));
        var p = so != null ? so.patterns.Find(x => x.name == pattern) : null;
        if (p == null) { Debug.LogWarning($"[몬스터 판정 데이터] 없음 — {folder} · {pattern}"); return 0; }
        if (Mathf.Approximately(p.castVfxXRotationOffset, rx) && Mathf.Approximately(p.castVfxYRotationOffset, ry)) return 0;
        Debug.Log($"[몬스터 판정 데이터] {folder} · {pattern} — 시전 이펙트 회전 ({p.castVfxXRotationOffset:0} · {p.castVfxYRotationOffset:0}) → ({rx:0} · {ry:0})");
        p.castVfxXRotationOffset = rx;
        p.castVfxYRotationOffset = ry;
        EditorUtility.SetDirty(so);
        return 1;
    }

    private static int FloatField(string path, string field, float value)
    {
        var so = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
        if (so == null) { Debug.LogWarning($"[몬스터 판정 데이터] 없음 — {path}"); return 0; }
        var o = new SerializedObject(so);
        var p = o.FindProperty(field);
        if (p == null || Mathf.Approximately(p.floatValue, value)) return 0;
        Debug.Log($"[몬스터 판정 데이터] {Short(path)} — {field} {p.floatValue:0.###} → {value:0.###}");
        p.floatValue = value;
        o.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(so);
        return 1;
    }

    private static int ClearVfx(string path)
    {
        var so = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
        if (so == null) { Debug.LogWarning($"[몬스터 판정 데이터] 없음 — {path}"); return 0; }
        var o = new SerializedObject(so);
        var p = o.FindProperty("_vfxPrefab");
        if (p == null || p.objectReferenceValue == null) return 0;
        Debug.Log($"[몬스터 판정 데이터] {Short(path)} — {p.objectReferenceValue.name} 뺌");
        p.objectReferenceValue = null;
        o.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(so);
        return 1;
    }

    private static MonsterAttackShapeSO ConfigShape(string patternPath)
    {
        string rel = patternPath.Replace(Mon, "");
        string folder = Mon + rel.Substring(0, rel.IndexOf('/'));
        foreach (var g in AssetDatabase.FindAssets("t:MonsterConfigSO", new[] { folder }))
        {
            var cfg = AssetDatabase.LoadAssetAtPath<MonsterConfigSO>(AssetDatabase.GUIDToAssetPath(g));
            if (cfg != null && cfg.stat != null && cfg.stat.attackShape != null) return cfg.stat.attackShape;
        }
        return null;
    }

    private static bool IsBoss(string path)
    {
        foreach (var b in BossFolders) if (path.Contains("/" + b + "/")) return true;
        return path.Contains("/Boss_Monster/");
    }

    private static string Short(string path) => path.Replace(Mon, "");
}
#endif
