using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.UI;

/// <summary>
/// 어드레서블 Grid SO 그룹을 로드해 선택 버튼을 동적으로 생성한다.
///
/// buttonContainer에 LayoutGroup(Vertical/Horizontal/Grid)이 있으면 자동 배치.
/// LayoutGroup이 없으면 Inspector의 buttonSize / spacing / vertical 값으로 수동 배치.
/// </summary>
public class GridLoader : MonoBehaviour
{
    [Header("Addressables")]
    [Tooltip("어드레서블 Grid SO 그룹 키 (레이블 또는 그룹명).")]
    public string gridGroupKey = "SO Grids";

    [Header("UI")]
    [Tooltip("GridSelectButton 컴포넌트가 부착된 버튼 프리팹.")]
    public GameObject selectButtonPrefab;
    [Tooltip("버튼들이 생성될 부모 Transform.")]
    public RectTransform buttonContainer;

    [Header("수동 배치 (LayoutGroup 없을 때)")]
    [Tooltip("버튼 하나의 크기 (Width, Height).")]
    public Vector2 buttonSize = new Vector2(200f, 60f);
    [Tooltip("버튼 간 간격.")]
    public float spacing = 10f;
    [Tooltip("true = 세로 배치, false = 가로 배치.")]
    public bool vertical = true;

    private AsyncOperationHandle<IList<GridAssetSO>> _handle;
    private bool _handleValid;

    void Start() => StartCoroutine(LoadGridSOs());

    void OnDestroy()
    {
        if (_handleValid && _handle.IsValid())
            Addressables.Release(_handle);
    }

    private IEnumerator LoadGridSOs()
    {
        if (selectButtonPrefab == null)
        {
            Debug.LogError("[GridLoader] selectButtonPrefab이 null입니다. Inspector에서 할당하세요.");
            yield break;
        }
        if (buttonContainer == null)
        {
            Debug.LogError("[GridLoader] buttonContainer가 null입니다. Inspector에서 할당하세요.");
            yield break;
        }

        Debug.Log($"[GridLoader] '{gridGroupKey}' 레이블로 GridAssetSO 로드 시작...");

        _handle = Addressables.LoadAssetsAsync<GridAssetSO>(gridGroupKey, null);
        _handleValid = true;
        yield return _handle;

        if (_handle.Status != AsyncOperationStatus.Succeeded)
        {
            Debug.LogError(
                $"[GridLoader] '{gridGroupKey}' 로드 실패!\n" +
                $"원인: {_handle.OperationException?.Message}\n" +
                "해결: Addressables Groups 창 → Play Mode Script → Use Asset Database (faster) 로 변경");
            yield break;
        }

        if (_handle.Result == null || _handle.Result.Count == 0)
        {
            Debug.LogError(
                $"[GridLoader] '{gridGroupKey}' 레이블로 등록된 GridAssetSO가 없습니다.\n" +
                "Addressables Groups 창에서 각 GridAsset SO의 Labels 컬럼에 'SO Grids'가 있는지 확인하세요.");
            yield break;
        }

        Debug.Log($"[GridLoader] {_handle.Result.Count}개 GridAssetSO 로드 완료. 버튼 생성 시작.");

        bool hasLayoutGroup = buttonContainer.GetComponent<LayoutGroup>() != null;
        int index = 0;

        foreach (var gridSO in _handle.Result)
        {
            if (gridSO == null) continue;

            var go = Instantiate(selectButtonPrefab, buttonContainer);

            // Grid SO 주입 + 버튼 이미지 즉시 반영
            var btn = go.GetComponent<GridSelectButton>();
            if (btn != null)
            {
                btn.gridAsset = gridSO;
                btn.ApplyButtonSprite(); // Start()보다 먼저 호출해 동적 생성 시에도 적용
            }

            // LayoutGroup 없을 때 수동으로 위치 지정
            if (!hasLayoutGroup)
            {
                var rt = go.GetComponent<RectTransform>();
                if (rt != null)
                {
                    rt.sizeDelta = buttonSize;
                    float step = (vertical ? buttonSize.y : buttonSize.x) + spacing;
                    rt.anchoredPosition = vertical
                        ? new Vector2(0f, -step * index)
                        : new Vector2(step * index, 0f);
                }
            }

            index++;
        }

        // 수동 배치 시 컨테이너 높이/폭 자동 조정
        if (!hasLayoutGroup && index > 0)
            ResizeContainer(index);
    }

    private void ResizeContainer(int count)
    {
        float step = (vertical ? buttonSize.y : buttonSize.x) + spacing;
        float totalSize = step * count - spacing;
        buttonContainer.sizeDelta = vertical
            ? new Vector2(buttonContainer.sizeDelta.x, totalSize)
            : new Vector2(totalSize, buttonContainer.sizeDelta.y);
    }
}
