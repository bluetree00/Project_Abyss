#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// StageUI 프리팹의 노드를 11개 6층 구조로 재구성하는 에디터 유틸.
/// 메뉴: Abyss/Stage/Rebuild Stage Nodes (11 nodes, 6 layers)
/// </summary>
public static class RebuildStageNodes
{
    // 아이콘맵 SO 경로
    private const string ICON_MAP_PATH = "Assets/Abyss/UI/Stage/StageNodeIconMap.asset";
    private const string PREFAB_PATH = "Assets/Abyss/UI/Stage/StageUI.prefab";

    private struct NodeDef
    {
        public int pointId;
        public string name;
        public StageCategory stageCategory;
        public NormalRoomCategory normalRoom;
        public Vector2 position; // anchoredPosition (center anchor)
        public List<int> nextPointIds;
        public float scale;
    }

    [MenuItem("Abyss/Stage/Rebuild Stage Nodes (11 nodes, 6 layers)")]
    public static void Execute()
    {
        // 6층 Y좌표 (위에서 아래로)
        float y1 = 1600f;  // 층1: Start
        float y2 = 960f;   // 층2
        float y3 = 320f;   // 층3
        float y4 = -320f;  // 층4
        float y5 = -960f;  // 층5
        float y6 = -1600f; // 층6: Boss

        var nodes = new List<NodeDef>
        {
            // 층1: Start
            new NodeDef { pointId = 0, name = "Node_Start", stageCategory = StageCategory.Start,
                normalRoom = NormalRoomCategory.Random, position = new Vector2(0, y1),
                nextPointIds = new List<int>{1, 2}, scale = 2f },

            // 층2: Normal x2 (Resolve 시 랜덤 카테고리 배정)
            new NodeDef { pointId = 1, name = "Node_01", stageCategory = StageCategory.Normal,
                normalRoom = NormalRoomCategory.Random, position = new Vector2(-250, y2),
                nextPointIds = new List<int>{3, 4}, scale = 1.5f },
            new NodeDef { pointId = 2, name = "Node_02", stageCategory = StageCategory.Normal,
                normalRoom = NormalRoomCategory.Random, position = new Vector2(250, y2),
                nextPointIds = new List<int>{4, 5}, scale = 1.5f },

            // 층3: Normal x3
            new NodeDef { pointId = 3, name = "Node_03", stageCategory = StageCategory.Normal,
                normalRoom = NormalRoomCategory.Random, position = new Vector2(-350, y3),
                nextPointIds = new List<int>{6}, scale = 1.5f },
            new NodeDef { pointId = 4, name = "Node_04", stageCategory = StageCategory.Normal,
                normalRoom = NormalRoomCategory.Random, position = new Vector2(0, y3),
                nextPointIds = new List<int>{6, 7}, scale = 1.5f },
            new NodeDef { pointId = 5, name = "Node_05", stageCategory = StageCategory.Normal,
                normalRoom = NormalRoomCategory.Random, position = new Vector2(350, y3),
                nextPointIds = new List<int>{7}, scale = 1.5f },

            // 층4: Normal x2
            new NodeDef { pointId = 6, name = "Node_06", stageCategory = StageCategory.Normal,
                normalRoom = NormalRoomCategory.Random, position = new Vector2(-200, y4),
                nextPointIds = new List<int>{8}, scale = 1.5f },
            new NodeDef { pointId = 7, name = "Node_07", stageCategory = StageCategory.Normal,
                normalRoom = NormalRoomCategory.Random, position = new Vector2(200, y4),
                nextPointIds = new List<int>{8, 9}, scale = 1.5f },

            // 층5: Normal x2
            new NodeDef { pointId = 8, name = "Node_08", stageCategory = StageCategory.Normal,
                normalRoom = NormalRoomCategory.Random, position = new Vector2(-200, y5),
                nextPointIds = new List<int>{100}, scale = 1.5f },
            new NodeDef { pointId = 9, name = "Node_09", stageCategory = StageCategory.Normal,
                normalRoom = NormalRoomCategory.Random, position = new Vector2(200, y5),
                nextPointIds = new List<int>{100}, scale = 1.5f },

            // 층6: Boss
            new NodeDef { pointId = 100, name = "Node_Boss", stageCategory = StageCategory.Boss,
                normalRoom = NormalRoomCategory.Random, position = new Vector2(0, y6),
                nextPointIds = new List<int>(), scale = 2.5f },
        };

        // 아이콘맵 SO 로드
        var iconMap = AssetDatabase.LoadAssetAtPath<StageNodeIconMap>(ICON_MAP_PATH);

        // 프리팹 열기
        var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_PATH);
        var prefabRoot = PrefabUtility.LoadPrefabContents(PREFAB_PATH);

        // 기존 StagePointUI 노드 삭제
        var existingPoints = prefabRoot.GetComponentsInChildren<StagePointUI>(true);
        foreach (var p in existingPoints)
            Object.DestroyImmediate(p.gameObject);

        // 새 노드 생성
        foreach (var def in nodes)
        {
            var go = new GameObject(def.name);
            go.layer = 5; // UI layer
            go.transform.SetParent(prefabRoot.transform, false);

            // RectTransform
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = def.position;
            rt.sizeDelta = new Vector2(100, 100);
            rt.localScale = Vector3.one * def.scale;

            // Image
            var img = go.AddComponent<Image>();
            img.raycastTarget = true;

            // Button
            var btn = go.AddComponent<Button>();

            // StagePointUI (SerializedObject로 private 필드 설정)
            var pointUI = go.AddComponent<StagePointUI>();
            var so = new SerializedObject(pointUI);
            so.FindProperty("pointId").intValue = def.pointId;
            so.FindProperty("stageCategory").enumValueIndex = (int)def.stageCategory;
            so.FindProperty("normalRoomCategory").enumValueIndex = (int)def.normalRoom;

            // nextPointIds
            var nextProp = so.FindProperty("nextPointIds");
            nextProp.ClearArray();
            for (int i = 0; i < def.nextPointIds.Count; i++)
            {
                nextProp.InsertArrayElementAtIndex(i);
                nextProp.GetArrayElementAtIndex(i).intValue = def.nextPointIds[i];
            }

            // iconMap
            if (iconMap != null)
                so.FindProperty("iconMap").objectReferenceValue = iconMap;

            // Button OnClick → OnPointClicked
            var onClickProp = so.FindProperty("m_OnClick"); // Button의 것이 아닌 별도 설정 필요

            so.ApplyModifiedPropertiesWithoutUndo();

            // Button.onClick → StagePointUI.OnPointClicked 연결
            UnityEditor.Events.UnityEventTools.AddPersistentListener(
                btn.onClick,
                new UnityEngine.Events.UnityAction(pointUI.OnPointClicked));
        }

        // DebugRunPanel을 마지막 sibling으로 이동 (맨 앞에 렌더링)
        var debugPanel = prefabRoot.transform.Find("DebugRunPanel ");
        if (debugPanel != null)
            debugPanel.SetAsLastSibling();

        // 프리팹 저장
        PrefabUtility.SaveAsPrefabAsset(prefabRoot, PREFAB_PATH);
        PrefabUtility.UnloadPrefabContents(prefabRoot);

        Debug.Log($"[RebuildStageNodes] 완료. {nodes.Count}개 노드 생성.");
    }
}
#endif
