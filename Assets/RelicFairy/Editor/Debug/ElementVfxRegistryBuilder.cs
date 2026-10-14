using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// [데이터 도구] 속성 이펙트 목록(ElementVfxRegistry.asset)을 아래 표로 채운다.
///
/// 프리팹은 전부 같은 묶음(Spells Pack LWRP)에서 골랐다 — 미리보기(RelicFairy/Debug/VFX 후보 미리보기)로
/// 후보를 렌더링해 비교한 결과다(10-01). 공칭 반경은 그 렌더링에서 눈금(1m · 3m)으로 읽은 값.
/// 표를 고치면 이 메뉴를 다시 돌린다(에셋을 손으로 고치지 않는다).
/// 결과: Temp/element_vfx_registry.txt
/// </summary>
public static class ElementVfxRegistryBuilder
{
    private const string AssetPath = "Assets/RelicFairy/Systems/Combat/ElementVfxRegistry.asset";
    private const string Pack  = "Assets/RelicFairy/_Imported/EffectSource/Spells Pack/LWRP(URP)/Particles_LWRP/Prefabs/";
    private const string Pig   = "Assets/RelicFairy/_Imported/EffectSource/Magic Pig Games (Infinity PBR)/Shared Files/Magic Spells & Particles/Particles/";
    private const float  CircleRadius = 2.3f;   // Aura_* 마법진이 배율 1에서 보이는 반경

    private struct Row
    {
        public RuneElement element;
        public string aura;  public float auraRadius;
        public string body;  public float bodyScale; public Color bodyTint;   // 몸 배율(0 = 1) · 색 곱(알파 0 = 원래 색) — 10-11 톤 2차
        public string burst;
        public string impact; public float impactHeight;
        public string area;   public float areaRadius; public float areaLife;
    }

    private static readonly Row[] Rows =
    {
        new Row { element = RuneElement.Fire,
                  aura = Pack + "Auras/Aura_Fire_LWRP.prefab", auraRadius = CircleRadius,
                  // 10-11 톤 2차: Magic Pig 불꽃은 시작 색이 흰 기본 — 불빛을 곱한다
                  body = Pig + "Particle Flame.prefab", bodyTint = new Color(1f, 0.6f, 0.3f, 1f),
                  burst = Pack + "Buffs/Buff_Fire_LWRP.prefab",
                  impact = Pack + "Projectiles/Explosion/Explosion _Fire_2_LWRP.prefab", impactHeight = 1f,
                  area = Pack + "Spells/Spell_Fire_5_LWRP.prefab", areaRadius = 4.1f /* 10-13 실측: 공칭 2.2 → 반경 4에 9.4 m · 5.17 → 2.9 m */, areaLife = 1.6f },
        new Row { element = RuneElement.Ice,
                  aura = Pack + "Auras/Aura_Ice_LWRP.prefab", auraRadius = CircleRadius,
                  // 10-11 톤 2차: 얼음 방패 거품은 빙결 · 분쇄에만(서리는 몸 이펙트 없음) — 그마저 옅게(알파 0.5)
                  body = Pack + "Shields/Shield_Ice_LWRP.prefab", bodyTint = new Color(1f, 1f, 1f, 0.5f),
                  burst = Pack + "Buffs/Buff_Ice_LWRP.prefab",
                  impact = Pack + "Projectiles/Explosion/Explosion_Ice_2_LWRP.prefab", impactHeight = 1f,
                  area = Pack + "Spells/Spell_Ice_5_LWRP.prefab", areaRadius = 6.8f /* 10-13 실측: 공칭 2.2 → 반경 3에 10.8 m · 5.5 → 4.0 m */, areaLife = 1.5f },
        new Row { element = RuneElement.Electric,
                  aura = Pack + "Auras/Aura_Storm_LWRP.prefab", auraRadius = CircleRadius,
                  // 10-11 톤 2차: 구슬 원래 크기 2가 몸을 덮었다 — 반
                  body = Pig + "Particle Electric Ball.prefab", bodyScale = 0.5f,
                  burst = Pack + "Buffs/Buff_Storm_LWRP.prefab",
                  impact = Pig + "Particle Electric Explosion.prefab", impactHeight = 1f,
                  area = Pack + "Spells/Spell_Storm_4_LWRP.prefab", areaRadius = 3.0f /* 10-13 실측: 반경 3 → 5.3 m */, areaLife = 1.1f },
        new Row { element = RuneElement.Grass,
                  // 10-11 톤 2차: 독안개 장판 — 옛 Spell_Nature_10은 한 번 터지는 주문(길이 7초)이라 반복하면 7초마다 다시 터졌다 →
                  // 다른 속성 장판과 같은 바닥 루프(Aura_Nature)
                  aura = Pack + "Auras/Aura_Nature_LWRP.prefab", auraRadius = CircleRadius,
                  // 중독 몸 = 몸을 도는 푸른 잎 소용돌이(은은함). 방패 거품(Shield_Nature)은 장판 안 몬스터마다 밝은 공이 떠 「막았다」로 읽혔다(10-01 실측).
                  body = "Assets/RelicFairy/_Imported/EffectSource/Hovl Studio/Auras pack 3/Prefabs/Forest aura.prefab",
                  burst = Pack + "Buffs/Buff_Nature_LWRP.prefab",
                  impact = Pack + "Projectiles/Explosion/Explosion_Nature_2_LWRP.prefab", impactHeight = 1f,
                  area = Pack + "Projectiles/Explosion/Explosion_Nature_2_LWRP.prefab", areaRadius = 1.5f, areaLife = 1.5f },
        new Row { element = RuneElement.Light,
                  aura = Pack + "Auras/Aura_Light_LWRP.prefab", auraRadius = CircleRadius,
                  body = Pack + "Buffs/Buff_Light_LWRP.prefab",
                  burst = Pack + "Buffs/Buff_Light_LWRP.prefab",
                  impact = Pack + "Projectiles/Explosion/Explosion_Light_2_LWRP.prefab", impactHeight = 1f,
                  area = Pack + "Spells/Spell_Light_2_LWRP.prefab", areaRadius = 4.53f /* 10-13 실측: 반경 3.5 → 6.9 m */, areaLife = 1.5f },
        // 이 묶음의 「Dark」는 초록-검정이라 독으로 읽힌다 — 어둠은 보라(Arcane)를 쓴다.
        new Row { element = RuneElement.Dark,
                  aura = Pack + "Auras/Aura_Arcane_LWRP.prefab", auraRadius = CircleRadius,
                  body = "Assets/RelicFairy/_Imported/EffectSource/Hovl Studio/RPG VFX Bundle/Prefabs/Magic buffs and hits/Debuff 1.prefab",
                  burst = Pack + "Buffs/Buff_Arcane_LWRP.prefab",
                  impact = Pack + "Projectiles/Explosion/Explosion_Arcane_2_LWRP.prefab", impactHeight = 1f,
                  area = Pack + "Projectiles/Explosion/Explosion_Arcane_2_LWRP.prefab", areaRadius = 3.6f /* 10-13 실측: 공칭 1.3 → 반경 3에 9.4 m · 2.97 → 3.9 m */, areaLife = 2f },
    };

    [MenuItem("RelicFairy/Debug/속성 이펙트 목록 채우기")]
    private static void Build()
    {
        var registry = AssetDatabase.LoadAssetAtPath<ElementVfxRegistry>(AssetPath);
        var sb = new StringBuilder();
        if (registry == null) { Debug.LogError("[ElementVfxRegistryBuilder] 목록 에셋이 없다: " + AssetPath); return; }

        var so = new SerializedObject(registry);
        var entries = so.FindProperty("entries");
        entries.arraySize = Rows.Length;
        int missing = 0;
        for (int i = 0; i < Rows.Length; i++)
        {
            var r = Rows[i];
            var e = entries.GetArrayElementAtIndex(i);
            e.FindPropertyRelative("element").enumValueIndex = (int)r.element;
            missing += Set(e, "statusAura", r.aura, sb, r.element);
            e.FindPropertyRelative("auraRadius").floatValue = r.auraRadius;
            missing += Set(e, "statusBody", r.body, sb, r.element);
            e.FindPropertyRelative("bodyScale").floatValue = r.bodyScale;
            e.FindPropertyRelative("bodyTint").colorValue  = r.bodyTint;
            missing += Set(e, "burst", r.burst, sb, r.element);
            missing += Set(e, "impact", r.impact, sb, r.element);
            e.FindPropertyRelative("impactHeight").floatValue = r.impactHeight;
            missing += Set(e, "area", r.area, sb, r.element);
            e.FindPropertyRelative("areaRadius").floatValue = r.areaRadius;
            e.FindPropertyRelative("areaLife").floatValue = r.areaLife;
        }
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(registry);
        AssetDatabase.SaveAssets();

        sb.Insert(0, $"속성 이펙트 목록 채움 — {Rows.Length}속성 · 못 찾은 프리팹 {missing}\n");
        File.WriteAllText(Path.Combine("Temp", "element_vfx_registry.txt"), sb.ToString());
        Debug.Log("[ElementVfxRegistryBuilder] " + sb);
    }

    private static int Set(SerializedProperty entry, string field, string path, StringBuilder sb, RuneElement element)
    {
        var prefab = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(path);
        entry.FindPropertyRelative(field).objectReferenceValue = prefab;
        sb.AppendLine($"{element} {field}: {(prefab != null ? prefab.name : "✗ 없음 — " + path)}");
        return prefab == null ? 1 : 0;
    }
}
