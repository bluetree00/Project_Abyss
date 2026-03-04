using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Attach this to a small UI button/image in the Selection UI.
/// Clicking it opens the gameplay view for the assigned grid asset.
/// (Requires a raycastable Graphic like Image on the same object.)
/// </summary>
public class LeeGridSelectButton : MonoBehaviour, IPointerClickHandler
{
    public LeeGridAssetSO gridAsset;

    public void OnPointerClick(PointerEventData eventData)
    {
        if (LeeBoardManager.Instance == null) return;
        LeeBoardManager.Instance.EnterGrid(gridAsset);
    }
}
