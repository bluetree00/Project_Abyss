using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.VisualScripting;
using UnityEngine;
using MapGeneratorManager;

public class UIManager
{
    int _order = 10;

    Stack<UI_Popup> _popupStack = new Stack<UI_Popup>();
    UI_Scene _sceneUI = null;

    // 특정 UI를 추적하기 위한 Dictionary
    Dictionary<string, GameObject> _uiObjects = new Dictionary<string, GameObject>();
    private Dictionary<int, NodeIcon> _nodeIconMap = new Dictionary<int, NodeIcon>();//WARNING:임시 작성
    private GameObject _nodeGraphPanel; //WARNING:임시 작성
    public GameObject Root
    {
        get
        {
            GameObject root = GameObject.Find("@UI_Root");
            if (root == null)
                root = new GameObject { name = "@UI_Root" };
            return root;
        }
    }

    public void SetCanvas(GameObject go, bool sort = true)
    {
        Canvas canvas = Util.GetOrAddComponent<Canvas>(go);
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;

        if (sort)
        {
            canvas.sortingOrder = _order;
            _order++;
        }
        else
        {
            canvas.sortingOrder = 0;
        }
    }

    // 월드 스페이스용 UI를 가져온후 메인 카메라를 넣어줌
    public T MakeWorldSpaceUI<T>(Transform parent = null, string name = null) where T : UI_Base
    {
        if (string.IsNullOrEmpty(name))
        {
            name = typeof(T).Name;
        }

        GameObject go = Managers.Resource.Instantiate($"UI/WorldSpace/{name}");
        if (parent != null)
        {
            go.transform.SetParent(parent);
        }

        Canvas canvas = go.GetOrAddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = Camera.main;

        return Util.GetOrAddComponent<T>(go);
    }


    public T MakeSubItem<T>(Transform parent = null, string name = null) where T : UI_Base
    {
        if (string.IsNullOrEmpty(name))
            name = typeof(T).Name;

        GameObject go = Managers.Resource.Instantiate($"UI/SubItem/{name}");
        if (parent != null)
            go.transform.SetParent(parent);

        return Util.GetOrAddComponent<T>(go);
    }

    public T MakeAugment<T>(Transform parent = null, string name = null) where T : UI_Base
    {
        if (string.IsNullOrEmpty(name))
            name = typeof(T).Name;

        GameObject go = Managers.Resource.Instantiate($"UI/Augments/{name}");
        if (parent != null)
            go.transform.SetParent(parent);

        return Util.GetOrAddComponent<T>(go);
    }

    public T ShowSceneUI<T>(string name = null) where T : UI_Scene
    {
        if (string.IsNullOrEmpty(name))
            name = typeof(T).Name;

        GameObject go = Managers.Resource.Instantiate($"UI/Scene/{name}");
        T sceneUI = Util.GetOrAddComponent<T>(go);
        _sceneUI = sceneUI;

        go.transform.SetParent(Root.transform);

        _uiObjects[name] = go;

        return sceneUI;
    }

    public T ShowPopupUI<T>(string name = null) where T : UI_Popup
    {
        if (string.IsNullOrEmpty(name))
            name = typeof(T).Name;

        GameObject go = Managers.Resource.Instantiate($"UI/Popup/{name}");
        T popup = Util.GetOrAddComponent<T>(go);
        _popupStack.Push(popup);

        go.transform.SetParent(Root.transform);


        return popup;
    }

    //TODO:다시 수정할 필요 있음
    // 팝업 UI를 표시하고 스택에 추가
    public bool HasPopup<T>() where T : UI_Popup
    {
        return _popupStack.Any(p => p is T);
    }


    public UI_Augment_Choice ShowAugmentChoiceUI(List<AugmentData> availableAugments)
    {
        UI_Augment_Choice augmentChoiceUI = ShowPopupUI<UI_Augment_Choice>();

        // 증강 UI 초기화 및 선택 항목 표시
        augmentChoiceUI.InitAugments(availableAugments);
        augmentChoiceUI.ShowAugmentChoices(); // 선택 가능한 증강 UI 표시
        return augmentChoiceUI;
    }

    // 특정 UI 제거
    public void CloseUI(string name)
    {
        if (!_uiObjects.ContainsKey(name))
        {
            Debug.LogWarning($"UI [{name}] 존재하지 않습니다.");
            return;
        }

        GameObject uiObject = _uiObjects[name];
        _uiObjects.Remove(name); // Dictionary에서 제거
        Managers.Resource.Destroy(uiObject);
        _order--;
    }


    public void ClosePopupUI(UI_Popup popup)
    {
        if (_popupStack.Count == 0)
            return;

        if (_popupStack.Peek() != popup)
        {
            Debug.Log("Close Popup Failed!");
            return;
        }

        ClosePopupUI();
    }

    public void ClosePopupUI()
    {
        if (_popupStack.Count == 0)
            return;

        UI_Popup popup = _popupStack.Pop();
        Managers.Resource.Destroy(popup.gameObject);
        popup = null;
        _order--;
    }

    public void CloseAllPopupUI()
    {
        while (_popupStack.Count > 0)
            ClosePopupUI();
    }

    public void Clear()
    {
        CloseAllPopupUI();
        _sceneUI = null;
    }

    //인벤토리 아이템 추가 방식 uiType가 핵심 추가될 UI 형태 ex) UI_EquipmentItem 만약 다인 플레이시 자신의 이벤을 플레이어가 생성 저장필요
    public void InvenPushItem(string name, string uiType)
    {
        // ScriptableObject 로드
        InvenData invenData = Managers.Resource.Load<InvenData>("Data/ItemData/Inven/InvenData");
        if (invenData == null)
        {
            Debug.LogError("인벤토리 데이터를 로드하지 못했습니다!");
            return;
        }

        // // 중복 아이템 방지 필요시 해제
        // if (invenData.ItemList.Exists(item => item.Name == name && item.UIType == uiType))
        // {
        //     Debug.LogWarning($"이미 존재하는 아이템: {name}");
        //     return;
        // }

        // UI가 이미 파괴되었는지 확인
        if (_uiObjects.ContainsKey(name) && _uiObjects[name] == null)
        {
            Debug.LogWarning($"UI [{name}]가 이미 파괴되었습니다. 아이템을 추가할 수 없습니다.");
            _uiObjects.Remove(name); // 파괴된 UI 객체 참조 제거
        }

        // 아이템 추가
        invenData.AddItem(name, uiType);

        // 인벤토리 UI 갱신
        if (_sceneUI is UI_Inven uiInven && _uiObjects.ContainsKey("UI_Inven"))
        {
            uiInven.RefreshInventory(); // UI 갱신
        }
        else
        {
            Debug.Log("닫혀있음");
        }
    }


    //WARNING:임시 작성
    public void InitializeNodeIcons(List<Node> nodes)
    {
        _nodeGraphPanel = GameObject.Find("NodeGraphPanel");
        if (_nodeGraphPanel == null)
        {
            Debug.LogError("NodeGraphPanel not found in the scene!");
            return;
        }

        // 기존 NodeIcon 맵 초기화
        _nodeIconMap.Clear();

        // NodeGraphPanel의 모든 NodeIcon 컴포넌트 찾기
        NodeIcon[] nodeIcons = _nodeGraphPanel.GetComponentsInChildren<NodeIcon>();
        foreach (var nodeIcon in nodeIcons)
        {
            if (nodeIcon.nodeId < 0)
            {
                Debug.LogWarning($"NodeIcon {nodeIcon.name} has invalid nodeId: {nodeIcon.nodeId}");
                continue;
            }

            // 노드 ID로 노드 데이터 찾기
            Node? node = nodes.Find(n => n.Id == nodeIcon.nodeId);
            if (node.HasValue)
            {
                nodeIcon.SetNodeData(node.Value);
                _nodeIconMap[nodeIcon.nodeId] = nodeIcon;

                // 클릭 이벤트 바인딩
                nodeIcon.BindClickEvent((clickedNode) =>
                {
                    Debug.Log($"Clicked Node: {clickedNode.GetLabel()} (ID: {clickedNode.Id})");
                    var connectedNodes = Managers.Stage.GetConnectedNodes();
                    if (connectedNodes.Contains(clickedNode))
                    {
                        Managers.Stage.MoveToNextStage(connectedNodes.IndexOf(clickedNode) == 0 ? -1 : 1);
                    }
                });

                Debug.Log($"NodeIcon mapped for node {nodeIcon.nodeId} ({node.Value.GetLabel()})");
            }
            else
            {
                Debug.LogWarning($"No node found for NodeIcon with ID: {nodeIcon.nodeId}");
            }
        }

        // 매핑되지 않은 노드 확인
        foreach (var node in nodes)
        {
            if (!_nodeIconMap.ContainsKey(node.Id))
            {
                Debug.LogWarning($"No NodeIcon found for node {node.Id} ({node.GetLabel()})");
            }
        }
    }
    //WARNING:임시 작성
    public void UpdateNodeIcon(int nodeId, Node nodeData)
    {
        if (_nodeIconMap.ContainsKey(nodeId))
        {
            _nodeIconMap[nodeId].SetNodeData(nodeData);
            // 현재 노드 강조 (선택적)
            HighlightNodeIcon(nodeId);
        }
        else
        {
            Debug.LogWarning($"NodeIcon for node {nodeId} not found.");
        }
    }
    //WARNING:임시 작성
    private void HighlightNodeIcon(int nodeId)
    {
        foreach (var icon in _nodeIconMap)
        {
            var image = icon.Value.GetComponent<UnityEngine.UI.Image>();
            if (image != null)
            {
                image.color = icon.Key == nodeId ? Color.yellow : Color.white; // 현재 노드 강조
            }
        }
    }


}
