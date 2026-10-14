#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-13 유물 고르기 팝업 한눈 줄 — 가웨인 · 랜슬롯 능력마다 한 줄 요약 · 큰 수치 1~2개를 채운다(사용자 「스크롤 불편 · 상세할 필요 없이 한눈에」).
/// 수치는 각 능력의 수치 표(stats)와 같은 값을 짧게 줄인 것 — 표는 그대로 둔다(되찾은 기억 쪽 등 상세가 필요한 곳이 쓴다).
/// 여러 번 눌러도 같다. 로그 「[RelicGlance] 끝」.
/// </summary>
public static class RelicGlanceFillEditor
{
    private const string Dir = "Assets/RelicFairy/Systems/Relic/Data/";

    // 능력 이름 → (한 줄 요약, 큰 수치 "라벨|값")
    private static readonly (string relic, string ability, string glance, string[] keys)[] Rows =
    {
        ("RelicClass_Gawain",   "하루의 순환", "여명 → 정오 → 황혼이 저절로 돈다",   new[] { "화상|초당 10%", "처치 충전|+20%" }),
        ("RelicClass_Gawain",   "정오",       "심어 둔 화상이 한꺼번에 터진다",     new[] { "공격 속도|+30%", "모든 피해|+20%" }),
        ("RelicClass_Gawain",   "낙일",       "전방에 태양을 떨어뜨린다",           new[] { "착탄 피해|350%", "작열 지대|4초" }),
        ("RelicClass_Lancelot", "광기",       "맞힐수록 세지고, 그만큼 약해진다", new[] { "공격력 최대|+40%", "받는 피해 최대|+30%" }),
        ("RelicClass_Lancelot", "광란",       "40스택에 4초 폭주 — 끝나면 0으로",   new[] { "이동 속도|+20%" }),
        ("RelicClass_Lancelot", "심판의 일격", "부채꼴 연타 뒤 마무리 강타",         new[] { "피해|520%", "낙인 받는 피해|+20%" }),
    };

    [MenuItem("RelicFairy/Tools/10-13 유물 고르기 팝업 — 한눈 줄 채우기(가웨인 · 랜슬롯)")]
    private static void Run()
    {
        int set = 0, missing = 0;
        foreach (var (relic, ability, glance, keys) in Rows)
        {
            var so = AssetDatabase.LoadAssetAtPath<RelicClassSO>(Dir + relic + ".asset");
            if (so == null) { Debug.LogWarning($"[RelicGlance] 없음 — {relic}"); missing++; continue; }
            var s   = new SerializedObject(so);
            var arr = s.FindProperty("abilities");
            bool found = false;
            for (int i = 0; arr != null && i < arr.arraySize; i++)
            {
                var a = arr.GetArrayElementAtIndex(i);
                if (a.FindPropertyRelative("name").stringValue != ability) continue;
                a.FindPropertyRelative("glance").stringValue = glance;
                var k = a.FindPropertyRelative("glanceStats");
                k.arraySize = keys.Length;
                for (int j = 0; j < keys.Length; j++) k.GetArrayElementAtIndex(j).stringValue = keys[j];
                found = true;
                set++;
            }
            if (!found) { Debug.LogWarning($"[RelicGlance] 능력 없음 — {relic} · {ability}"); missing++; }
            s.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(so);
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[RelicGlance] 끝 — 채움 {set} · 없음 {missing}");
    }
}
#endif
