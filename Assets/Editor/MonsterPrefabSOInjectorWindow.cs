using UnityEditor;
using UnityEngine;
using UnityEditor.SceneManagement;
using System.IO;

public class MonsterPrefabSOInjectorWindow : EditorWindow
{
    private GameObject prefab;
    private MonsterAbilitySetSO abilitySetSO;
    private MonsterEffectProfileSO effectProfileSO;

    private Define.MonsterType selectedMonsterType;

    [MenuItem("Tools/몬스터 프리팹 SO 할당기")]
    public static void ShowWindow()
    {
        GetWindow<MonsterPrefabSOInjectorWindow>("몬스터 프리팹 SO 할당기");
    }

    private void OnGUI()
    {
        GUILayout.Label("프리팹과 SO를 지정하면 해당 프리팹에 SO가 할당됩니다.", EditorStyles.wordWrappedLabel);

        EditorGUILayout.Space();

        prefab = (GameObject)EditorGUILayout.ObjectField("몬스터 프리팹", prefab, typeof(GameObject), false);
        abilitySetSO = (MonsterAbilitySetSO)EditorGUILayout.ObjectField("AbilitySet SO", abilitySetSO, typeof(MonsterAbilitySetSO), false);
        effectProfileSO = (MonsterEffectProfileSO)EditorGUILayout.ObjectField("EffectProfile SO", effectProfileSO, typeof(MonsterEffectProfileSO), false);

        selectedMonsterType = (Define.MonsterType)EditorGUILayout.EnumPopup("몬스터 타입 선택", selectedMonsterType);

        EditorGUILayout.Space();

        if (GUILayout.Button("SO 할당 및 프리팹 저장"))
        {
            if (prefab == null)
            {
                Debug.LogError("프리팹을 먼저 지정하세요.");
                return;
            }

            string prefabPath = AssetDatabase.GetAssetPath(prefab);
            if (string.IsNullOrEmpty(prefabPath))
            {
                Debug.LogError("프리팹 경로를 찾을 수 없습니다.");
                return;
            }

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);

            if (instance == null)
            {
                Debug.LogError("프리팹 인스턴스 생성 실패");
                return;
            }

            // 몬스터 타입에 맞는 컴포넌트 탐색 (예: MonsterController 상속 컴포넌트)
            var monsterController = instance.GetComponent<MonsterController>();

            if (monsterController == null)
            {
                Debug.LogError("몬스터 컨트롤러 컴포넌트를 찾을 수 없습니다.");
                DestroyImmediate(instance);
                return;
            }

            // 타입 체크
            if (monsterController.Type != selectedMonsterType)
            {
                Debug.LogWarning($"프리팹 몬스터 타입({monsterController.Type})과 선택한 타입({selectedMonsterType})이 다릅니다.");
            }

            // SO 할당
            if (abilitySetSO != null)
            {
                var abilitySetField = monsterController.GetType().GetProperty("AbilitySet");
                if (abilitySetField != null && abilitySetField.CanWrite)
                {
                    abilitySetField.SetValue(monsterController, abilitySetSO);
                }
                else
                {
                    // 필드나 프로퍼티가 없으면 직접 컴포넌트에 public 변수로 접근하거나 별도 setter 만들어야 함
                    // 예를 들어 monsterController.AbilitySet = abilitySetSO; 직접 접근 가능하면 이 부분 수정
                    // 없으면 직접 monsterController에 public 함수 만들어서 할당하는 방법 추천
                    Debug.LogWarning("AbilitySet 프로퍼티가 없거나 쓰기 불가능합니다.");
                }
            }

            if (effectProfileSO != null)
            {
                var effectProfileField = monsterController.GetType().GetProperty("EffectProfile");
                if (effectProfileField != null && effectProfileField.CanWrite)
                {
                    effectProfileField.SetValue(monsterController, effectProfileSO);
                }
                else
                {
                    Debug.LogWarning("EffectProfile 프로퍼티가 없거나 쓰기 불가능합니다.");
                }
            }

            // 변경사항 프리팹에 적용
            PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);

            DestroyImmediate(instance);

            Debug.Log($"프리팹에 SO 할당 및 저장 완료: {prefabPath}");
        }
    }
}
