using UnityEngine;

/// <summary>
/// @Popup에 항상 활성 상태로 부착. 프로젝트의 유일한 ESC 진입점.
///
/// 우선순위:
///   1) 북 팝업이 열려있으면 닫는다(기존 토글 동작 유지).
///   2) 팝업 스택이 비어있지 않으면 ESC를 소비한다 — 최상단이 CloseOnEscape면 그 1개만 닫고,
///      아니면 아무것도 하지 않는다. 어느 쪽이든 북은 열지 않는다.
///   3) 스택이 비어있고 북도 닫혀있으면 북 팝업을 연다(기존 동작).
/// </summary>
public sealed class EscKeyListener : MonoBehaviour
{
    private EscBookPopup _popup;

    private void Update()
    {
        if (!Input.GetKeyDown(KeyCode.Escape)) return;

        if (_popup == null)
            _popup = GetComponentInChildren<EscBookPopup>(true);

        // 북이 열려있으면 북 닫기가 우선(북은 스택 밖 사전배치 팝업).
        if (_popup != null && _popup.gameObject.activeSelf)
        {
            _popup.ClosePopup();
            return;
        }

        // 스택 팝업이 ESC를 소비하면 북은 열지 않는다.
        if (Managers.UI != null && Managers.UI.TryCloseTopPopupOnEscape())
            return;

        _popup?.OpenPopup();
    }
}
