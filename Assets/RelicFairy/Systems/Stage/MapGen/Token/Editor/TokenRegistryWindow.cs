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
    private TokenCategory? _filterCategory;
    private TokenPhase?    _filterPhase;

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
            EditorGUILayout.LabelField("카테고리:", GUILayout.Width(55));
            if (GUILayout.Button("전체", GUILayout.Width(44)))
                _filterCategory = null;
            foreach (TokenCategory cat in System.Enum.GetValues(typeof(TokenCategory)))
                if (GUILayout.Button(cat.ToString(), GUILayout.Width(80)))
                    _filterCategory = cat;
        }

        // 페이즈 필터
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("페이즈:", GUILayout.Width(55));
            if (GUILayout.Button("전체", GUILayout.Width(44)))
                _filterPhase = null;
            foreach (TokenPhase phase in System.Enum.GetValues(typeof(TokenPhase)))
                if (GUILayout.Button(phase.ToString(), GUILayout.Width(80)))
                    _filterPhase = phase;

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
            .OrderBy(e => e.Phase)
            .ThenBy(e => e.Category)
            .ThenBy(e => e.Code)
            .ToList();

        var visible = all
            .Where(e => (!_filterCategory.HasValue || e.Category == _filterCategory.Value)
                     && (!_filterPhase.HasValue    || e.Phase    == _filterPhase.Value))
            .ToList();

        EditorGUILayout.LabelField($"총 {all.Count}개 / 표시 {visible.Count}개", EditorStyles.miniLabel);

        // 헤더 행
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("코드",     EditorStyles.miniLabel, GUILayout.Width(70));
            EditorGUILayout.LabelField("페이즈",   EditorStyles.miniLabel, GUILayout.Width(72));
            EditorGUILayout.LabelField("카테고리", EditorStyles.miniLabel, GUILayout.Width(72));
            EditorGUILayout.LabelField("설명",     EditorStyles.miniLabel, GUILayout.ExpandWidth(true));
            EditorGUILayout.LabelField("핸들러",   EditorStyles.miniLabel, GUILayout.Width(200));
        }

        EditorGUILayout.Space(2);
        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        TokenPhase? lastPhase = null;
        foreach (var entry in visible)
        {
            if (lastPhase != entry.Phase)
            {
                if (lastPhase.HasValue) EditorGUILayout.Space(4);
                lastPhase = entry.Phase;

                var phaseColor = entry.Phase == TokenPhase.PreBuild
                    ? new Color(0.3f, 0.7f, 1f)
                    : new Color(0.5f, 1f, 0.5f);
                var prev = GUI.color;
                GUI.color = phaseColor;
                EditorGUILayout.LabelField($"── {entry.Phase} ──", EditorStyles.boldLabel);
                GUI.color = prev;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    string codeLabel = entry.IsPrefix ? $"{entry.Code}*" : entry.Code;
                    EditorGUILayout.LabelField(codeLabel,              EditorStyles.boldLabel,  GUILayout.Width(70));
                    EditorGUILayout.LabelField(entry.Phase.ToString(),  EditorStyles.miniLabel,  GUILayout.Width(72));
                    EditorGUILayout.LabelField(entry.Category.ToString(),                        GUILayout.Width(72));
                    EditorGUILayout.LabelField(entry.Description,                                GUILayout.ExpandWidth(true));
                    EditorGUILayout.LabelField(entry.Handler.GetType().Name, EditorStyles.miniLabel, GUILayout.Width(200));
                }

                if (!string.IsNullOrEmpty(entry.CsvExample))
                {
                    var prev = GUI.color;
                    GUI.color = new Color(0.9f, 0.85f, 0.5f);
                    EditorGUILayout.LabelField("CSV: " + entry.CsvExample, EditorStyles.miniLabel);
                    GUI.color = prev;
                }
            }
        }

        EditorGUILayout.EndScrollView();
    }
}
#endif
