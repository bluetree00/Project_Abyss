using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Attach this to a small UI button/image in the Selection UI.
/// Clicking it opens the gameplay view for the assigned grid asset.
/// (Requires a raycastable Graphic like Image on the same object.)
///
/// 두 가지 사용 방식:
/// (A) Inspector에 gridAsset SO 할당 → 기존 방식 그대로.
/// (B) SetGridData()로 런타임 데이터 주입 → SO 없이 사용 가능.
///     런타임 데이터가 있으면 SO보다 우선 적용된다.
/// </summary>
public class GridSelectButton : MonoBehaviour, IPointerClickHandler
{
    public GridAssetSO gridAsset;

    private GridAssetData _runtimeData;

    void Start()
    {
        ApplyButtonSprite();
    }

    /// <summary>gridAsset.buttonSprite 가 있으면 오브젝트의 Image 에 적용한다.</summary>
    public void ApplyButtonSprite()
    {
        if (gridAsset == null || gridAsset.buttonSprite == null) return;
        var img = GetComponent<Image>();
        if (img != null)
            img.sprite = gridAsset.buttonSprite;
    }

    /// <summary>
    /// 런타임에 SO 없이 퍼즐 데이터를 직접 주입한다.
    /// 주입 후 버튼 클릭 시 이 데이터로 BoardManager.EnterGrid가 호출된다.
    /// </summary>
    public void SetGridData(GridAssetData data) => _runtimeData = data;

    public void OnPointerClick(PointerEventData eventData)
    {
        if (BoardManager.Instance == null) return;

        if (_runtimeData != null)
            BoardManager.Instance.EnterGrid(_runtimeData);
        else
            BoardManager.Instance.EnterGrid(gridAsset);
    }
}
