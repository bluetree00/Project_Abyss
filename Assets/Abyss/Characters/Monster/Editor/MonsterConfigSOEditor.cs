using System;
using UnityEditor;
using UnityEngine;

/// <summary>
/// MonsterConfigSO 전용 Inspector.
/// 카테고리별 폴드아웃으로 항목을 구분하여 표시한다.
/// BossConfigSOEditor 가 이 클래스를 상속하므로 DrawMonsterSections() 은 protected.
/// </summary>
[CustomEditor(typeof(MonsterConfigSO), true)]
public class MonsterConfigSOEditor : Editor
{
    // ── EditorPrefs 키 공통 접두사 ────────────────────────
    protected const string PrefPrefix = "MCSOEditor_";

    // ── 폴드아웃 상태 ─────────────────────────────────────
    protected bool _foldBasicInfo    = true;
    protected bool _foldCommonStates = true;
    protected bool _foldSpecial      = true;
    protected bool _foldOverrides    = true;
    protected bool _foldElemental    = true;

    // ── SerializedProperty 캐시 ──────────────────────────
    private SerializedProperty _monsterName;
    private SerializedProperty _grade;
    private SerializedProperty _playerLayer;
    // 공용 상태 커스텀 슬롯
    private SerializedProperty _chaseState;
    private SerializedProperty _patrolState;
    private SerializedProperty _attackReadyState;
    private SerializedProperty _attackState;
    private SerializedProperty _getHitState;
    private SerializedProperty _dieState;
    private SerializedProperty _specialStates;
    private SerializedProperty _stateOverrides;
    // 원소 상태
    private SerializedProperty _elemental;

    // 파생 클래스도 쓸 수 있도록 protected
    protected string _prefId;

    protected virtual void OnEnable()
    {
        _monsterName      = serializedObject.FindProperty("monsterName");
        _grade            = serializedObject.FindProperty("grade");
        _playerLayer      = serializedObject.FindProperty("playerLayer");
        _chaseState       = serializedObject.FindProperty("chaseState");
        _patrolState      = serializedObject.FindProperty("patrolState");
        _attackReadyState = serializedObject.FindProperty("attackReadyState");
        _attackState      = serializedObject.FindProperty("attackState");
        _getHitState      = serializedObject.FindProperty("getHitState");
        _dieState         = serializedObject.FindProperty("dieState");
        _specialStates    = serializedObject.FindProperty("specialStates");
        _stateOverrides   = serializedObject.FindProperty("stateOverrides");
        _elemental        = serializedObject.FindProperty("elemental");

        _prefId = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(target));

        _foldBasicInfo    = EditorPrefs.GetBool(PrefPrefix + _prefId + "_basic",    true);
        _foldCommonStates = EditorPrefs.GetBool(PrefPrefix + _prefId + "_cmn",      true);
        _foldSpecial      = EditorPrefs.GetBool(PrefPrefix + _prefId + "_special",  true);
        _foldOverrides    = EditorPrefs.GetBool(PrefPrefix + _prefId + "_ovr",      true);
        _foldElemental    = EditorPrefs.GetBool(PrefPrefix + _prefId + "_elemental",true);
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        DrawMonsterSections();
        serializedObject.ApplyModifiedProperties();
    }

    // ── BossConfigSOEditor 에서도 호출 ────────────────────
    protected void DrawMonsterSections()
    {
        DrawSection("기본 정보", ref _foldBasicInfo, "_basic", () =>
        {
            EditorGUILayout.PropertyField(_monsterName);
            EditorGUILayout.PropertyField(_grade);
            EditorGUILayout.PropertyField(_playerLayer);
        });

        DrawSection("공용 상태 커스텀 (null = 기본 사용)", ref _foldCommonStates, "_cmn", () =>
        {
            EditorGUILayout.HelpBox(
                "null이면 기본 공용 상태 사용.\n" +
                "파생 SO를 설정하면 해당 SO가 생성하는 커스텀 상태로 교체됩니다.\n" +
                "예: SlimeRegenPatrolStateSO → RegenPatrolState",
                MessageType.Info);
            EditorGUILayout.PropertyField(_chaseState,       new GUIContent("Chase"));
            EditorGUILayout.PropertyField(_patrolState,      new GUIContent("Patrol"));
            EditorGUILayout.PropertyField(_attackReadyState, new GUIContent("AttackReady"));
            EditorGUILayout.PropertyField(_attackState,      new GUIContent("Attack"));
            EditorGUILayout.PropertyField(_getHitState,      new GUIContent("GetHit"));
            EditorGUILayout.PropertyField(_dieState,         new GUIContent("Die"));
            EditorGUILayout.Space(4);
            EditorGUILayout.PropertyField(_stateOverrides, new GUIContent(
                "복합 행동 오버라이드",
                "다중 상태가 하나의 런타임 객체를 공유해야 하는 경우에만 사용 (예: SnailShell)."), true);
        });

        DrawSection("특수 상태 (조건 + 상태 SO)", ref _foldSpecial, "_special", () =>
        {
            EditorGUILayout.HelpBox(
                "conditions 가 비어있으면 코드(PatrolState 등)에서 직접 발동.\n" +
                "conditions 가 있으면 피격 시 AND 조건 충족 여부 평가 후 발동.\n" +
                "MonsterHpConditionSO 등 MonsterConditionSO 파생 Asset 을 드래그.\nSO 슬롯 아래에 해당 SO 의 파라미터가 인라인으로 표시됩니다.",
                MessageType.Info);
            EditorGUILayout.PropertyField(_specialStates, true);
        });

        DrawSection("원소 상태 (null = 공통 원소 상태 사용)", ref _foldElemental, "_elemental", () =>
        {
            EditorGUILayout.HelpBox(
                "원소별 누적 임계값 · 저항 배율 · 오버라이드 상태 SO.\n" +
                "overrideState = null : 공통 DefaultElementalState (추후 효과 구현 예정)\n" +
                "overrideState 지정 : 해당 SO 가 생성하는 커스텀 원소 반응 상태 사용\n" +
                "예) 불 면역 몬스터 → fire.overrideState 에 FireImmuneStateSO 할당",
                MessageType.Info);
            EditorGUILayout.PropertyField(_elemental, true);
        });
    }

    // ── 공통 섹션 드로어 ─────────────────────────────────
    // BeginFoldoutHeaderGroup 대신 EditorGUI.Foldout 사용 —
    // BeginFoldoutHeaderGroup 은 중첩 불가(배열 PropertyField·ReorderableList
    // 내부에서 같은 API 호출 시 "Cannot nest" 에러)이므로 대체한다.
    protected void DrawSection(string title, ref bool foldout, string prefKeySuffix, Action drawContent)
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        Rect headerRect = GUILayoutUtility.GetRect(
            GUIContent.none,
            EditorStyles.foldoutHeader,
            GUILayout.ExpandWidth(true));

        bool newFold = EditorGUI.Foldout(headerRect, foldout, title, true, EditorStyles.foldoutHeader);
        if (newFold != foldout)
        {
            foldout = newFold;
            EditorPrefs.SetBool(PrefPrefix + _prefId + prefKeySuffix, foldout);
        }

        if (foldout)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.Space(2);
            drawContent();
            EditorGUILayout.Space(2);
            EditorGUI.indentLevel--;
        }

        EditorGUILayout.EndVertical();
        EditorGUILayout.Space(2);
    }
}
