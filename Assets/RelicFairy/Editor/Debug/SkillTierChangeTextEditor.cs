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
        // 10-08 강화 점검: 실제 동작 · 에셋 문구(10-06~08 손질 반영)와 맞췄다 — 예전 표로 돌리면 문구가 낡은 값으로 되돌아갔다
        (W + "Nameless/Data/Nameless_ESkill.asset",     "관통 5",       "착탄 폭발 반경 2m · 피해 50%"),           // FanArrow: 2 관통 · 3 폭발
        (W + "Nameless/Data/Nameless_RSkill.asset",     "연참 +2",      "마지막 일섬 = 지나온 길 전체(폭 6m · 연참 3배)"),  // IasenSlash: 2 연참 +2 · 3 길 가르기(C1)
        (W + "Katana/Data/Katana_ESkill.asset",         "적중 시 추가 베기 3회(각 8+공격력)", "적중 시 마무리 폭발 반경 3m(30+공격력)"),  // HolySlash: 적중해야 이어진다
        (W + "Katana/Data/Katana_QSkill.asset",         "각인 택1(추격·응축)", "검기 폭발 반경 3m(25+공격력)"),  // PhantomDance: 2 각인 선택(시범, SkillEngravingPilotEditor) · 3 검기 폭발
        (W + "Greatsword/Data/Greatsword_ESkill.asset", "2연속 시전",    "3연속 · 확산 검기 반경 6.5m(3파)"),       // PerimeterGuard: 시전 1→2→3 · 3 확산(C3)
        (W + "Greatsword/Data/Greatsword_QSkill.asset", "추가타 30+공격력", "추가타 45+공격력"),                 // FinalStrike: 2·3 추가 강타
        (W + "Bow/Data/Bow_ESkill.asset",               "관통 5",       "착탄 폭발 반경 2m · 피해 50%"),           // FanArrow
        (W + "Bow/Data/Bow_QSkill.asset",               "2발 · 발당 50+공격력×1.5", "1발 · 100+공격력×3 · 착탄 폭발 2m(30%)"),  // FocusShot: 공격력 몫을 모은다
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
