using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// @Popup에 항상 활성 상태로 부착. 프로젝트의 유일한 ESC 진입점.
///
/// 우선순위:
///   0) 설정 화면이 열려있으면 그것부터 닫는다 — ESC 메뉴 위에 겹쳐 열리므로 항상 가장 위다.
///   1) ESC 메뉴가 열려있으면 닫는다(토글).
///   2) 팝업 스택이 비어있지 않으면 ESC를 소비한다 — 최상단이 CloseOnEscape면 그 1개만 닫고,
///      아니면 아무것도 하지 않는다. 어느 쪽이든 메뉴는 열지 않는다.
///   3) 전부 닫혀있으면 ESC 메뉴를 연다.
///
/// 예전엔 3)에서 EscBookPopup(PAUSE/SKILL/INVENTORY 탭)을 열었는데, PAUSE 페이지의
/// 로비/옵션 버튼이 Debug.Log 스텁이라 실제로 동작하는 건 게임 종료뿐이었다.
/// 계속하기·로비로 가기·게임 종료가 전부 동작하는 <see cref="UI_EscMenu"/>로 교체했고,
/// 이제 @UIRoot에서 북 팝업 노드까지 걷어냈으므로 북을 닫는 분기도 함께 없앤다.
/// </summary>
public sealed class EscKeyListener : MonoBehaviour
{
    private UI_EscMenu _menu;

    private void Awake()
    {
        _menu = gameObject.GetOrAddComponent<UI_EscMenu>();
    }

    private void Update()
    {
        if (!Input.GetKeyDown(KeyCode.Escape)) return;
        // 리치의 추방(10-11) — 추락 · 착지 전환 · Ch1에서 일어나는 동안엔 일시정지를 열지 않는다(ESC는 건너뛰기 키도 아니다).
        if (ExileFallSequence.BlocksPause) return;
        if (RunResultView.Active != null) return;   // 결과창 동안엔 ESC 메뉴를 열지 않는다 — 아래에 열린 채 허브로 넘어갔다(10-08 리뷰)
        // 로딩 화면(900)이 덮는 동안엔 무시 — 그 아래(500)에 메뉴가 열려 시간을 멈춘 채 다음 씬으로 넘어갔다(10-09 출시 점검)
        if (UI_SceneLoading.Instance != null && UI_SceneLoading.Instance.IsCovering) return;

        // 설정은 ESC 메뉴 위에도, 로비 단독으로도 열린다 — 어느 쪽이든 가장 먼저 닫힌다.
        if (UI_Settings.CloseIfOpen()) return;

        if (_menu != null && _menu.IsOpen)
        {
            _menu.Close();
            return;
        }

        // 스택 팝업이 ESC를 소비하면 메뉴는 열지 않는다.
        if (Managers.UI != null && Managers.UI.TryCloseTopPopupOnEscape())
            return;

        // 로비엔 일시정지 메뉴를 열지 않는다 — 「계속하기」 · 「로비로 가기」가 뜻이 없다. 설정 · 팝업 닫기는 위에서 그대로 받는다(10-09 출시 점검)
        if (SceneManager.GetActiveScene().name == nameof(Define.Scene.Lobby)) return;

        _menu?.Open();
    }
}
