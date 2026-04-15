using Abyss.Monster;
using UnityEditor;
using UnityEngine;

/// <summary>
/// MonsterConditionSO 오브젝트 필드 전용 PropertyDrawer.
///
/// SO 슬롯 아래에 해당 SO 의 직렬화 필드를 인라인으로 표시·편집할 수 있다.
/// SO 종류가 달라져도 자동으로 해당 SO 의 파라미터를 노출한다.
/// </summary>
[CustomPropertyDrawer(typeof(MonsterConditionSO), useForChildren: true)]
public class MonsterConditionSODrawer : PropertyDrawer
{
    private const float Indent  = 12f;
    private const float Spacing = 2f;

    // ── 높이 계산 ─────────────────────────────────────────────────
    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        float h = EditorGUIUtility.singleLineHeight; // ObjectField 한 줄

        if (property.objectReferenceValue != null)
        {
            var so   = new SerializedObject(property.objectReferenceValue);
            var iter = so.GetIterator();
            if (iter.NextVisible(true)) // m_Script 건너뜀
            {
                while (iter.NextVisible(false))
                    h += EditorGUI.GetPropertyHeight(iter, true) + Spacing;
            }
            h += Spacing * 2; // 상하 여백
        }

        return h;
    }

    // ── 그리기 ────────────────────────────────────────────────────
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);

        // ── ObjectField (SO 드래그) ────────────────────────────────
        var fieldRect = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
        EditorGUI.PropertyField(fieldRect, property, label);

        // ── 인라인 파라미터 ───────────────────────────────────────
        if (property.objectReferenceValue != null)
        {
            var so   = new SerializedObject(property.objectReferenceValue);
            so.Update();

            var iter = so.GetIterator();
            if (iter.NextVisible(true)) // m_Script 건너뜀
            {
                float y      = fieldRect.yMax + Spacing;
                float xOff   = position.x + Indent;
                float wOff   = position.width - Indent;
                int   prevIL = EditorGUI.indentLevel;
                EditorGUI.indentLevel = 0;

                while (iter.NextVisible(false))
                {
                    float ph   = EditorGUI.GetPropertyHeight(iter, true);
                    var   rect = new Rect(xOff, y, wOff, ph);

                    // 배경 박스 (첫 번째 필드 위)
                    if (Mathf.Approximately(y, fieldRect.yMax + Spacing))
                    {
                        var bgRect = new Rect(xOff - 2, y - 1,
                                              wOff + 2,
                                              GetPropertyHeight(property, label)
                                              - EditorGUIUtility.singleLineHeight - Spacing + 1);
                        EditorGUI.DrawRect(bgRect, new Color(0f, 0f, 0f, 0.1f));
                    }

                    EditorGUI.PropertyField(rect, iter, true);
                    y += ph + Spacing;
                }

                EditorGUI.indentLevel = prevIL;
            }

            if (so.hasModifiedProperties) so.ApplyModifiedProperties();
        }

        EditorGUI.EndProperty();
    }
}
