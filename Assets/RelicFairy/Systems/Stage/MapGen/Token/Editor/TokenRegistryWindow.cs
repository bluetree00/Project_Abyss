#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools > RelicFairy > Token Registry — 등록된 모든 토큰 핸들러를 카테고리별로 표시.
/// 새 핸들러는 [TokenHandler] Attribute만 붙이면 자동으로 이 목록에 나타난다.
/// </summary>
public class TokenRegistryWindow : EditorWindow
{
    private Vector2 _scroll;
    private TokenCategory? _filter;

    [MenuItem("Tools/RelicFairy/Token Registry")]
    public static void Open() => GetWindow<TokenRegistryWindow>("Token Registry");

    private void OnEnable()
    {
        TokenRegistry.Reset();
        TokenRegistry.EnsureInitialized();
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("등록된 토큰 핸들러", EditorStyles.boldLabel);
        EditorGUILayout.Space(4);

        // 카테고리 필터
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("전체", GUILayout.Width(50)))
                _filter = null;

            foreach (TokenCategory cat in System.Enum.GetValues(typeof(TokenCategory)))
                if (GUILayout.Button(cat.ToString(), GUILayout.Width(80)))
                    _filter = cat;

            GUILayout.FlexibleSpace();
            if (GUILayout.Button("새로고침", GUILayout.Width(70)))
            {
                TokenRegistry.Reset();
                TokenRegistry.EnsureInitialized();
                Repaint();
            }
        }

        EditorGUILayout.Space(6);

        var all = TokenRegistry.ExactHandlers.Values
            .Concat(TokenRegistry.PrefixHandlers)
            .OrderBy(e => e.Category)
            .ThenBy(e => e.Code)
            .ToList();

        int total   = all.Count;
        int visible = _filter.HasValue ? all.Count(e => e.Category == _filter.Value) : total;
        EditorGUILayout.LabelField($"총 {total}개 / 표시 {visible}개", EditorStyles.miniLabel);
        EditorGUILayout.Space(2);

        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        TokenCategory? lastCat = null;
        foreach (var entry in all)
        {
            if (_filter.HasValue && entry.Category != _filter.Value) continue;

            if (lastCat != entry.Category)
            {
                if (lastCat.HasValue) EditorGUILayout.Space(4);
                lastCat = entry.Category;
                EditorGUILayout.LabelField($"── {entry.Category} ──", EditorStyles.boldLabel);
            }

            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                // 코드 (IsPrefix면 * 접미사)
                string label = entry.IsPrefix ? $"{entry.Code}*" : entry.Code;
                EditorGUILayout.LabelField(label, EditorStyles.boldLabel, GUILayout.Width(70));

                // 설명
                EditorGUILayout.LabelField(entry.Description, GUILayout.ExpandWidth(true));

                // 핸들러 클래스명
                EditorGUILayout.LabelField(
                    entry.Handler.GetType().Name,
                    EditorStyles.miniLabel,
                    GUILayout.Width(180));
            }
        }

        EditorGUILayout.EndScrollView();
    }
}
#endif
