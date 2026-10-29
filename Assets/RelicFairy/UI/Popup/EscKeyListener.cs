using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// @Popup에 항상 활성 상태로 부착. 프로젝트의 유일한 ESC 진입점.
///
/// 우선순위(10-25 사용자 「모든 UI를 ESC로 닫을 수 있게 · 없으면 설정 창」 — 빈 화면은 지금의 일시정지 메뉴 그대로,
/// 보상 고르기 창은 그대로 안 닫힘):
///   0) 설정 화면이 열려있으면 그것부터 닫는다 — ESC 메뉴 위에 겹쳐 열리므로 항상 가장 위다.
///   1) ESC 메뉴가 열려있으면 — 「되찾은 기억」 · 「장비」 쪽이면 그것만, 아니면 메뉴를 닫는다.
///   2) 팝업 스택이 비어있지 않으면 ESC를 소비한다 — 최상단이 CloseOnEscape면 그 1개만 닫고,
///      아니면 아무것도 하지 않는다. 어느 쪽이든 메뉴는 열지 않는다. 룬판은 팝업 위로 올려 열었으면 팝업보다 먼저, 아니면 다음.
///   3) 로비 세이브 슬롯 창.
///   4) 전부 닫혀있으면 ESC 메뉴를 연다 — 단 런 경계 · 갈림길(960~) · 엔딩 카드가 떠 있으면 그 밑에 열지 않는다
///      (밑에 열린 메뉴가 다음 씬까지 시간을 멈춘 채 남았다 — 10-25 ESC 전수 조사).
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
        if (Input.GetKeyDown(KeyCode.Escape)) Handle();
    }

    /// <summary>ESC 한 번 — 위 우선순위대로 하나만 처리한다(실측 도구가 키 없이 부른다).</summary>
    private void Handle()
    {
        // 리치의 추방(10-11) — 추락 · 착지 전환 · Ch1에서 일어나는 동안엔 일시정지를 열지 않는다(ESC는 건너뛰기 키도 아니다).
        if (ExileFallSequence.BlocksPause) return;
        // 결과창 — ESC = [계속](1초 뒤부터 받는다). 메뉴는 열지 않는다 — 아래에 열린 채 허브로 넘어갔다(10-08 리뷰)
        if (RunResultView.Active != null) { RunResultView.Active.Continue(); return; }
        // 로딩 화면(900)이 덮는 동안엔 무시 — 그 아래(500)에 메뉴가 열려 시간을 멈춘 채 다음 씬으로 넘어갔다(10-09 출시 점검)
        if (UI_SceneLoading.Instance != null && UI_SceneLoading.Instance.IsCovering) return;
        // 악몽 갈림길 창은 ESC를 직접 받아 닫는다(그 밑 500에 메뉴가 숨어 열렸다)
        if (BaseCampNightmareToggle.IsChoiceOpen || NightmareGateChoice.IsOpen || RankingBoardOverlay.IsOpen || StartGift.IsOpen) return;   // 10-29 던전 입구 「해방기 / 악몽」 창 · 랭킹 비석도 같다

        // 확인 창(종료 · 요정의 샘 등)은 무엇보다 위에 뜬다 — 가장 먼저 취소로 닫는다(10-16).
        if (UI_ConfirmDialog.CloseIfOpen()) return;

        // 설정은 ESC 메뉴 위에도, 로비 단독으로도 열린다 — 어느 쪽이든 가장 먼저 닫힌다.
        if (UI_Settings.CloseIfOpen()) return;

        if (_menu != null && _menu.IsOpen)
        {
            _menu.HandleEscape();
            return;
        }

        // 룬판 — 상점 위로 올려 열었으면 팝업보다 위라 먼저 닫는다. 아니면 그 위 팝업(판에서 연 정제소 등)이 먼저.
        var grid = UI_GridPanel.Instance;
        if (grid != null && grid.IsRaisedAbovePopups && grid.TryHandleEscape()) return;

        // 스택 팝업이 ESC를 소비하면 메뉴는 열지 않는다.
        if (Managers.UI != null && Managers.UI.TryCloseTopPopupOnEscape())
            return;

        if (grid != null && grid.TryHandleEscape()) return;

        // 로비 세이브 슬롯 창 — 로비는 아래에서 멈추므로 그 전에 본다.
        if (UI_SaveSlotPanel.CloseIfOpen()) return;

        // 로비엔 일시정지 메뉴를 열지 않는다 — 「계속하기」 · 「로비로 가기」가 뜻이 없다. 설정 · 팝업 닫기는 위에서 그대로 받는다(10-09 출시 점검)
        if (SceneManager.GetActiveScene().name == nameof(Define.Scene.Lobby)) return;

        // 런 경계 · 갈림길(심연 깊이 · 런 끝 문구 · 멀린 부활) · 엔딩 카드 동안엔 그 밑에 일시정지를 열지 않는다
        if (EndingCardOverlay.IsPlaying || TopOverlayShowing()) return;

        _menu?.Open();
    }

    /// <summary>런 경계(960) 이상 오버레이 캔버스가 떠 있나 — ESC를 눌렀을 때만 훑는다.</summary>
    private static bool TopOverlayShowing()
    {
        foreach (var c in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            if (c.isActiveAndEnabled && c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay
                && c.sortingOrder >= UISortingOrder.RunBoundary)
                return true;
        return false;
    }
}
