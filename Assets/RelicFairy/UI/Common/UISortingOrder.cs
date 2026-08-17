//============================================================
// UISortingOrder.cs
// - 프로젝트 전체 캔버스 그리기 순서의 <b>단일 기준</b>.
// - 값을 코드/프리팹에 흩어 두지 말고 여기서만 정한다.
//============================================================

/// <summary>
/// 캔버스 sortingOrder 표준. 밴드(100 단위)로 나누고 밴드 안에서만 세부 값을 준다.
///
/// ── 정렬 원리(업계 표준 근거) ──────────────────────────────
/// ① <b>월드 → 화면</b> : Fagerholt &amp; Lorentzon(2009) 게임 UI 4분류의 Geometry 축.
///    3D 공간에 놓인 UI(diegetic·spatial)는 화면 평면 UI(non-diegetic)보다 항상 아래.
/// ② <b>간섭 낮음 → 높음</b> : Apple HIG 모달 계층. "팝오버 위에 모달을 띄우지 말라" —
///    간섭이 강한 것일수록 위에 쌓는다. NN/g·Carbon의 긴급도 기반 그룹화와 동일.
/// ③ <b>연출 → 전환</b> : 화면 페이드는 항상 최상위(Unity 통용 관례).
/// ④ <b>밴드 + 여백</b> : Bootstrap z-index 규약(1000/1020/1030…)처럼 사이를 비워
///    나중에 끼워 넣을 수 있게 한다. 개별 값을 임의로 바꾸지 말고 밴드를 지킨다.
///
/// ⚠️ Unity 렌더 정렬 상한은 32767이다. 밴드를 1000 미만으로 유지해 여유를 크게 둔다.
/// </summary>
public static class UISortingOrder
{
    // ── 0~99 : 월드 부착(diegetic·spatial) ─────────────────
    /// <summary>월드 스페이스 캔버스 기준면.</summary>
    public const int World        = 0;
    /// <summary>월드에 펼쳐지는 연출물(인트로 책 등) — 화면 UI보다 아래.</summary>
    public const int WorldProp    = 5;
    /// <summary>상호작용 대상 위 이름표.</summary>
    public const int WorldLabel   = 10;
    /// <summary>상호작용 키 안내(라벨 위).</summary>
    public const int WorldPrompt  = 20;
    /// <summary>캐릭터에 붙는 게이지(스태미나 등).</summary>
    public const int WorldGauge   = 50;

    // ── 100~199 : HUD(non-diegetic 상시·무간섭) ────────────
    /// <summary>상시 HUD 캔버스.</summary>
    public const int Hud          = 100;
    /// <summary>HUD 보조 지시자(출구 나침반 등). <b>팝업보다 반드시 아래</b>.</summary>
    public const int HudIndicator = 120;

    // ── 200~299 : 메타 연출(상태를 화면 효과로 표현) ────────
    /// <summary>피격 비네트.</summary>
    public const int MetaVignette = 200;
    /// <summary>전체 화면 플래시.</summary>
    public const int MetaFlash    = 210;
    /// <summary>집중선 등 속도감 효과.</summary>
    public const int MetaSpeed    = 220;

    // ── 300~399 : 씬(전체 화면 페이지) ─────────────────────
    /// <summary>로비·로고 등 전체 화면 페이지. HUD를 덮는다.</summary>
    public const int Scene        = 300;

    // ── 400~499 : 팝업(중간 간섭) ──────────────────────────
    /// <summary>팝업 캔버스 기준면.</summary>
    public const int Popup        = 400;
    /// <summary>팝업 스택 시작값 — 열릴 때마다 +1.</summary>
    public const int PopupStack   = 410;

    // ── 500~599 : 시스템 모달(최고 간섭) ───────────────────
    /// <summary>일시정지·확인창 등 게임을 멈추는 모달.</summary>
    public const int SystemModal  = 500;
    /// <summary>시스템 모달 <b>위</b>에 겹치는 2단 모달(일시정지 → 설정). 밴드(500~599) 안이다.</summary>
    public const int SystemModalTop = 510;

    // ── 600~699 : 시네마틱 연출 ────────────────────────────
    /// <summary>얼티밋 연출.</summary>
    public const int Cinematic    = 600;
    /// <summary>레터박스 프레임 — 전환 와이프보다는 아래.</summary>
    public const int Letterbox    = 620;

    // ── 700~799 : 전체 화면 임팩트 ─────────────────────────
    /// <summary>보스 타격 등 화면을 때리는 순간 효과.</summary>
    public const int Impact       = 700;

    // ── 800~899 : 전환(모든 UI를 덮는다) ───────────────────
    /// <summary>카메라 페이드.</summary>
    public const int CameraFade   = 800;
    /// <summary>씬 전환 와이프.</summary>
    public const int ScreenFade   = 850;

    // ── 900~999 : 최상위(전환·로딩보다 위) ─────────────────
    /// <summary>로딩 화면·오버레이 캔버스.</summary>
    public const int Loading      = 900;
    /// <summary>부트 연출(로딩 위에 겹치는 도입 연출).</summary>
    public const int Boot         = 950;
    /// <summary>런 경계 알림(사망·클리어·부활) — 전환 와이프에 가려지면 안 된다.</summary>
    public const int RunBoundary  = 960;
    /// <summary>런 경계 선택창(심연 루프 등) — 알림 위에서 입력을 받는다.</summary>
    public const int RunChoice    = 970;
}
