#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-13 「이펙트 = 판정」 플레이어 데이터 손질(기획 `RelicFairy_이펙트·판정일치_전수검증_20261013.md` §5 · ④ 슬롯 ejplayer 실측).
/// - 형 「무게」 내려찍기 이펙트 ×2 → ×3: 판정 3.5 m에 2.3 m(0.64배) · 여진(×1.4)은 4.9 m에 3.1 m였다.
/// - 속성 광역 폭발 공칭 반경: 불 2.2 → 4.1 · 얼음 2.2 → 6.8 · 번개 2 → 3 · 빛 2.3 → 4.53 · 어둠 1.3 → 3.6(풀 1.5 맞음). 빌더(ElementVfxRegistryBuilder) 줄도 같은 값.
/// 다시 눌러도 같은 결과.
/// </summary>
public static class PlayerJudgmentDataEditor
{
    [MenuItem("RelicFairy/Setup/Player 이펙트 데이터 손질 — 이펙트 = 판정 (10-13)")]
    public static void Apply()
    {
        int n = 0;
        n += SetFloat("Assets/RelicFairy/Weapon/Forms/Form_Weight.asset", "triggerVfxScale", 3f);
        n += SetFloat("Assets/RelicFairy/Weapon/Forms/Form_Windblade.asset", "triggerVfxScale", 1.3f);
        n += SetFloat("Assets/RelicFairy/Weapon/Greatsword/Data/GreatswordBase/FinalStrike.asset", "strikeEffectScale", 0.96f);   // 반원 3 m — 중심 0.5 m 앞(코드)과 함께(10-13 ⑥)
        // 스킬 폭발 — 스킬과 같은 길로 폭발만 따로 잰 값(10-13 ⑥): 성광 1·2단계 FrontAttackHit ×2 → 1.2 m · 3단계 MissileExplosion ×3 → 12 ~ 16 m ·
        //   환영 마무리 DoubleSlash ×3 → 10 m · 무형 연참 마무리 NamelessCut ×1 → 7.2 m(판정 3 · 3 · 3 · 2 m)
        n += SetFloat("Assets/RelicFairy/Weapon/Katana/Data/KatanaBase/HolySlash.asset", "explodeEffectScale", 4.5f);
        n += SetFloat("Assets/RelicFairy/Weapon/Katana/Data/KatanaBase/HolySlash.asset", "tier3BurstScale", 0.65f);
        n += SetFloat("Assets/RelicFairy/Weapon/Katana/Data/KatanaBase/PhantomDance.asset", "slashExplodeEffectScale", 0.9f);
        n += SetFloat("Assets/RelicFairy/Weapon/Nameless/Data/NamelessBase/NamelessIasen.asset", "tier2FinishEffectScale", 0.28f);
        // 무형 광선 착탄 폭발 — 판정 2 m에 NamelessBeamHit ×1 → 10.5 m · ×1.5 → 15.7 m(배율에 비례) → ×0.19 · 3단계 추가 착탄 ×0.22(조금 크게)
        n += SetFloat("Assets/RelicFairy/Weapon/Nameless/Data/NamelessBase/NamelessBeam.asset", "explodeEffectScale", 0.19f);
        n += SetFloat("Assets/RelicFairy/Weapon/Nameless/Data/NamelessBase/NamelessBeam.asset", "tier3ExtraHitScale", 0.22f);
        // 스킬 맞힘(한 대상 · 광역 없음 → 반경 1 m 안팎 · 10-13 ⑦): 성광 2단계 추가 맞힘 KatanaSlashHit ×0.5 → 5.2 m · 칼 맞힘 SwordHitImpact ×0.34 → 1.5 · ×0.42 → 1.9 m
        n += SetFloat("Assets/RelicFairy/Weapon/Katana/Data/KatanaBase/HolySlash.asset", "tier2ExtraHitScale", 0.1f);
        n += SetFloat("Assets/RelicFairy/Weapon/Katana/Data/KatanaBase/HolySlash.asset", "hitEffectScale", 0.23f);
        n += SetFloat("Assets/RelicFairy/Weapon/Katana/Data/KatanaBase/PhantomDance.asset", "hitEffectScale", 0.23f);
        // 무형검 지상 4번째 칸(측정 「콤보 3」) 슬래시 ×1 → 0.75 — 판정 상자는 1 ~ 3타와 같은데 앞 1.1 ~ 1.3 · 옆 1.3 ~ 1.45배(= 1 ÷ 0.7)였다(10-13 ⑧)
        n += SetFloat("Assets/RelicFairy/Weapon/Nameless/Data/NamelessBase/NamelessAbility.asset", "steps.Array.data[3].effect.scaleMultiplier", 0.75f);

        var reg = AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/RelicFairy/Systems/Combat/ElementVfxRegistry.asset");
        if (reg == null) Debug.LogWarning("[플레이어 이펙트 데이터] 없음 — ElementVfxRegistry.asset");
        else
        {
            var o = new SerializedObject(reg);
            var list = o.FindProperty("entries");
            for (int i = 0; list != null && i < list.arraySize; i++)
            {
                var e = list.GetArrayElementAtIndex(i);
                var el = (RuneElement)e.FindPropertyRelative("element").intValue;
                // 10-13 ⑤ 실측: 불 5.17은 0.74배로 넘쳐 4.1 · 얼음 10.8 · 번개 5.3 · 어둠 9.4 m(반경 3) → 도달 ∝ 배율^1.38로 추정 · 2차(얼음 5.5 → 1.32배 · 어둠 2.97 → 1.31배) · 풀은 맞음
                float want = el switch
                {
                    RuneElement.Fire => 4.1f, RuneElement.Ice => 6.8f, RuneElement.Electric => 3.0f,
                    RuneElement.Light => 4.53f, RuneElement.Dark => 3.6f, _ => -1f,
                };
                var r = e.FindPropertyRelative("areaRadius");
                if (want < 0f || r == null || Mathf.Approximately(r.floatValue, want)) continue;
                Debug.Log($"[플레이어 이펙트 데이터] ElementVfxRegistry · {el} — 광역 공칭 반경 {r.floatValue:0.##} → {want:0.##}");
                r.floatValue = want;
                n++;
            }
            o.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(reg);
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[플레이어 이펙트 데이터] 끝 — 바꾼 칸 {n}");
    }

    private static int SetFloat(string path, string field, float value)
    {
        var so = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
        var p = so != null ? new SerializedObject(so).FindProperty(field) : null;
        if (p == null) { Debug.LogWarning($"[플레이어 이펙트 데이터] 없음 — {path} · {field}"); return 0; }
        if (Mathf.Approximately(p.floatValue, value)) return 0;
        Debug.Log($"[플레이어 이펙트 데이터] {System.IO.Path.GetFileNameWithoutExtension(path)} · {field} {p.floatValue:0.##} → {value:0.##}");
        p.floatValue = value;
        p.serializedObject.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(so);
        return 1;
    }
}
#endif
