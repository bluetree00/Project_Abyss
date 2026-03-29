using System.Collections.Generic;
using Abyss.Monster;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

/// <summary>
/// BossConfigSO 전용 Inspector.
/// MonsterConfigSOEditor 를 상속해 몬스터 공통 섹션을 재사용하고,
/// 보스 전용 섹션(패턴 브레이크 / 연속 패턴 페널티 / 패턴 엔트리)을 추가한다.
///
/// 패턴 엔트리의 조건은 더 이상 문자열 키가 아닌
/// BossConditionSO 파생 Asset 을 드래그해 넣는 방식으로 조립한다.
/// </summary>
[CustomEditor(typeof(BossConfigSO), true)]
public class BossConfigSOEditor : MonsterConfigSOEditor
{
    // ── 폴드아웃 ─────────────────────────────────────────────────
    private bool _foldBreak   = true;
    private bool _foldPenalty = true;
    private bool _foldEntries = true;

    // ── 보스 전용 프로퍼티 ────────────────────────────────────────
    private SerializedProperty _breakMin;
    private SerializedProperty _breakMax;
    private SerializedProperty _penaltyDur;
    private SerializedProperty _penaltyMult;
    private SerializedProperty _patternEntries;

    // ── 패턴 엔트리 UI ────────────────────────────────────────────
    private ReorderableList _entriesList;
    private readonly List<bool> _entryFolds = new();

    // ─────────────────────────────────────────────────────────────
    protected override void OnEnable()
    {
        base.OnEnable();

        _breakMin       = serializedObject.FindProperty("patternBreakDurationMin");
        _breakMax       = serializedObject.FindProperty("patternBreakDurationMax");
        _penaltyDur     = serializedObject.FindProperty("patternRepeatPenaltyDuration");
        _penaltyMult    = serializedObject.FindProperty("patternRepeatPenaltyMult");
        _patternEntries = serializedObject.FindProperty("patternEntries");

        _foldBreak   = EditorPrefs.GetBool(PrefPrefix + _prefId + "_bk_break",   true);
        _foldPenalty = EditorPrefs.GetBool(PrefPrefix + _prefId + "_bk_penalty", true);
        _foldEntries = EditorPrefs.GetBool(PrefPrefix + _prefId + "_bk_entries", true);

        BuildEntriesList();
    }

    // ─────────────────────────────────────────────────────────────
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        DrawMonsterSections();
        DrawBossSections();
        serializedObject.ApplyModifiedProperties();
    }

    // ─────────────────────────────────────────────────────────────
    private void DrawBossSections()
    {
        DrawSection("패턴 브레이크", ref _foldBreak, "_bk_break", () =>
        {
            using var h = new EditorGUILayout.HorizontalScope();
            EditorGUILayout.PropertyField(_breakMin, new GUIContent("최소 대기", "패턴 완료 후 최소 대기 (초)"));
            EditorGUILayout.PropertyField(_breakMax, new GUIContent("최대 대기", "패턴 완료 후 최대 대기 (초)"));
        });

        DrawSection("연속 패턴 페널티", ref _foldPenalty, "_bk_penalty", () =>
        {
            EditorGUILayout.PropertyField(_penaltyDur,  new GUIContent("페널티 지속 (초)", "직전 패턴에 페널티를 적용하는 시간"));
            EditorGUILayout.PropertyField(_penaltyMult, new GUIContent("가중치 배율",       "페널티 구간 동안 적용할 배율. 0 = 완전 차단"));
        });

        DrawSection("패턴 엔트리 (위 → 아래 = 우선순위 높음)", ref _foldEntries, "_bk_entries", () =>
        {
            EditorGUILayout.HelpBox(
                "conditions: 조건 키 문자열 목록. AND 로 평가됩니다. 비어있으면 항상 참 (fallback 엔트리).\n" +
                "키: Phase2 / Dist_Close / Dist_Far / AfterBackstep / AfterSidestep / TimePressure",
                MessageType.Info);
            SyncFoldList();
            _entriesList.DoLayoutList();
        });
    }

    // ── ReorderableList 구성 ─────────────────────────────────────
    private void BuildEntriesList()
    {
        _entriesList = new ReorderableList(serializedObject, _patternEntries,
            draggable: true, displayHeader: true, displayAddButton: true, displayRemoveButton: true);

        _entriesList.drawHeaderCallback = rect =>
            EditorGUI.LabelField(rect, $"패턴 엔트리  ({_patternEntries.arraySize}개)", EditorStyles.boldLabel);

        _entriesList.elementHeightCallback = index =>
        {
            if (index >= _entryFolds.Count) return EditorGUIUtility.singleLineHeight + 4;

            float lh = EditorGUIUtility.singleLineHeight;
            float sv = EditorGUIUtility.standardVerticalSpacing;

            if (!_entryFolds[index]) return lh + sv + 4;

            var ep   = _patternEntries.GetArrayElementAtIndex(index);
            var cond = ep.FindPropertyRelative("conditions");
            var pat  = ep.FindPropertyRelative("patterns");

            float h = lh + sv;                                       // 헤더 foldout
            h += EditorGUI.GetPropertyHeight(cond, true) + sv;      // conditions 리스트
            h += lh + sv;                                            // selectionMode
            h += lh + sv;                                            // forceExecute
            h += EditorGUI.GetPropertyHeight(pat, true) + sv;       // patterns 리스트
            h += 8;                                                  // 하단 여백
            return h;
        };

        _entriesList.drawElementCallback = DrawEntry;

        _entriesList.onAddCallback = list =>
        {
            _patternEntries.arraySize++;
            _entryFolds.Add(true);
            serializedObject.ApplyModifiedProperties();
        };

        _entriesList.onRemoveCallback = list =>
        {
            int idx = list.index;
            if (idx >= 0 && idx < _entryFolds.Count) _entryFolds.RemoveAt(idx);
            ReorderableList.defaultBehaviours.DoRemoveButton(list);
        };
    }

    private void DrawEntry(Rect rect, int index, bool isActive, bool isFocused)
    {
        while (_entryFolds.Count <= index) _entryFolds.Add(true);

        var ep       = _patternEntries.GetArrayElementAtIndex(index);
        var condProp = ep.FindPropertyRelative("conditions");
        var forceProp= ep.FindPropertyRelative("forceExecute");
        var modeProp = ep.FindPropertyRelative("selectionMode");
        var patProp  = ep.FindPropertyRelative("patterns");

        float lh = EditorGUIUtility.singleLineHeight;
        float sv = EditorGUIUtility.standardVerticalSpacing;
        var   r  = new Rect(rect.x, rect.y + 2, rect.width, lh);

        // ── 헤더 Foldout ──────────────────────────────────────────
        int    condCount = condProp.arraySize;
        bool   isForce   = forceProp.boolValue;
        string modeLabel = modeProp.enumDisplayNames.Length > modeProp.enumValueIndex
                           ? modeProp.enumDisplayNames[modeProp.enumValueIndex] : "";
        string condLabel = condCount == 0 ? "항상 참" : $"조건 {condCount}개";
        string header    = $"[{index}]  {condLabel}   ({patProp.arraySize}개 패턴, {modeLabel})"
                         + (isForce ? "  ★ FORCE" : "");

        var prevColor = GUI.color;
        if (isForce) GUI.color = new Color(1f, 0.85f, 0.4f);
        _entryFolds[index] = EditorGUI.Foldout(r, _entryFolds[index], header, true, EditorStyles.foldoutHeader);
        GUI.color = prevColor;

        if (!_entryFolds[index]) return;
        r.y += lh + sv;

        int prevIndent = EditorGUI.indentLevel;
        EditorGUI.indentLevel++;

        // ── 조건 리스트 ────────────────────────────────────────────
        float condH    = EditorGUI.GetPropertyHeight(condProp, true);
        var   condRect = new Rect(r.x, r.y, r.width, condH);
        EditorGUI.PropertyField(condRect, condProp,
            new GUIContent("조건 키 (AND)", "비어있으면 항상 참.\nPhase2 / Dist_Close / Dist_Far / AfterBackstep / AfterSidestep / TimePressure"), true);
        r.y += condH + sv;

        // ── 선택 방식 ──────────────────────────────────────────────
        EditorGUI.PropertyField(r, modeProp, new GUIContent("선택 방식",
            "WeightedRandom: 가중치 랜덤 | Sequential: 순서 | Random: 균등 랜덤"));
        r.y += lh + sv;

        // ── 강제 실행 ──────────────────────────────────────────────
        EditorGUI.PropertyField(r, forceProp, new GUIContent("강제 실행 (Force)",
            "패턴 브레이크를 무시하고 즉시 실행. 페이즈 인터럽트용."));
        r.y += lh + sv;

        // ── 패턴 목록 ──────────────────────────────────────────────
        float patH    = EditorGUI.GetPropertyHeight(patProp, true);
        var   patRect = new Rect(r.x, r.y, r.width, patH);
        EditorGUI.PropertyField(patRect, patProp, new GUIContent("패턴 목록"), true);

        EditorGUI.indentLevel = prevIndent;
    }

    private void SyncFoldList()
    {
        while (_entryFolds.Count < _patternEntries.arraySize) _entryFolds.Add(true);
        while (_entryFolds.Count > _patternEntries.arraySize) _entryFolds.RemoveAt(_entryFolds.Count - 1);
    }
}
