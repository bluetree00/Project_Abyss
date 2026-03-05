using UnityEngine;
using UnityEngine.EventSystems;

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
public class LeeGridSelectButton : MonoBehaviour, IPointerClickHandler
{
    public LeeGridAssetSO gridAsset;

    private LeeGridAssetData _runtimeData;

    /// <summary>
    /// 런타임에 SO 없이 퍼즐 데이터를 직접 주입한다.
    /// 주입 후 버튼 클릭 시 이 데이터로 LeeBoardManager.EnterGrid가 호출된다.
    /// </summary>
    public void SetGridData(LeeGridAssetData data) => _runtimeData = data;

    public void OnPointerClick(PointerEventData eventData)
    {
        if (LeeBoardManager.Instance == null) return;

        if (_runtimeData != null)
            LeeBoardManager.Instance.EnterGrid(_runtimeData);
        else
            LeeBoardManager.Instance.EnterGrid(gridAsset);
    }
}
