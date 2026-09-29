using UnityEditor;
using UnityEngine;

/// <summary>
/// [데이터 적용 · 편집 모드] 스킬 SO에 「그 단계가 되면 바뀌는 것」 짧은 문구를 채운다(09-25).
/// 재련소 「다음 목표」 행이 다음 단계 미리보기로 쓴다(<see cref="SkillSO.ChangeAtTier"/>).
/// 문구는 행동 코드의 단계 분기에서 옮겼다 — 행동을 바꾸면 여기도 고칠 것. 몇 번 돌려도 같은 결과.
/// 행 폭(360px)에 16px로 들어가게 짧게 쓴다(E·R 키가 앞에 붙는다).
/// </summary>
public static class SkillTierChangeTextEditor
{
    private const string W = "Assets/RelicFairy/Weapon/";

    private static readonly (string asset, string tier2, string tier3)[] Rows =
    {
        (W + "Nameless/Data/Nameless_ESkill.asset",     "관통 5",       "착탄 폭발"),        // FanArrow: 2 관통 · 3 폭발
        (W + "Nameless/Data/Nameless_RSkill.asset",     "연참 +2",      ""),                 // IasenSlash: 2 연참 +2(3단계 분기 없음)
        (W + "Katana/Data/Katana_ESkill.asset",         "추가 베기 3회", "마무리 폭발"),      // HolySlash: 2 적중 시 추가 타격 · 3 폭발
        (W + "Katana/Data/Katana_QSkill.asset",         "각인 택1(추격·응축)", "검기 폭발"),  // PhantomDance: 2 각인 선택(시범, SkillEngravingPilotEditor) · 3 검기 폭발
        (W + "Greatsword/Data/Greatsword_ESkill.asset", "2연속 시전",    "3연속·검기 확장"),  // PerimeterGuard: 시전 1→2→3 · 3 확장 파동
        (W + "Greatsword/Data/Greatsword_QSkill.asset", "충격파 추가타", "폭발 추가타"),      // FinalStrike: 2 추가 타격 · 3 폭발
        (W + "Bow/Data/Bow_ESkill.asset",               "관통 5",       "착탄 폭발"),        // FanArrow
        (W + "Bow/Data/Bow_QSkill.asset",               "화살 강화",     "화살 최대"),        // FocusShot: 피해·크기 단계
        (W + "Crossbow/Data/Crossbow_ESkill.asset",     "공속 +50%",    "공속 +70%"),        // RapidFire
        (W + "Crossbow/Data/Crossbow_QSkill.asset",     "+2발",         "+3발"),             // MultiShot
    };

    [MenuItem("RelicFairy/Debug/스킬 단계 변화 문구 채우기 (편집 모드)")]
    private static void Apply()
    {
        int n = 0;
        foreach (var (asset, t2, t3) in Rows)
        {
            var so = AssetDatabase.LoadAssetAtPath<SkillSO>(asset);
            if (so == null) { Debug.LogWarning("[단계문구] 스킬 없음: " + asset); continue; }
            var ser = new SerializedObject(so);
            ser.FindProperty("_tier2Change").stringValue = t2;
            ser.FindProperty("_tier3Change").stringValue = t3;
            ser.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(so);
            n++;
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[단계문구] 완료 — 스킬 {n}개");
    }
}
