using UnityEngine;

/// <summary>
/// @Popup에 항상 활성 상태로 부착.
/// ESC 키 → EscBookPopup 토글.
/// </summary>
public sealed class EscKeyListener : MonoBehaviour
{
    private EscBookPopup _popup;

    private void Update()
    {
        if (!Input.GetKeyDown(KeyCode.Escape)) return;

        if (_popup == null)
            _popup = GetComponentInChildren<EscBookPopup>(true);

        if (_popup == null) return;

        if (_popup.gameObject.activeSelf)
            _popup.ClosePopup();
        else
            _popup.OpenPopup();
    }
}
