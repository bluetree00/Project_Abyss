//============================================================
// UIIds.cs
// - UI 프리팹(리소스) 식별자 모음
// - Addressables Key 매핑은 UIAddressKeys.cs에서 처리(예정)
// - HUD 내부 토글(Mode/Section)은 HUDIds.cs에서 관리
//============================================================
using System;

public static class UIIds
{
    // ---------------------------------------------------------
    // Layer (정책/라우팅 용도)
    // ---------------------------------------------------------
    public enum Layer
    {
        HUD = 0,        // 상시 표시(런 기준)
        Popup = 1,      // 잠깐 뜨는 것(스택)
        Menu = 2,       // 전체 화면 페이지
        Overlay = 3,    // 연출/차단
        WorldSpace = 4  // 월드에 붙는 UI
    }

    // ---------------------------------------------------------
    // HUD (프리셋/프리팹 단위)
    // - HUD 전체 레이아웃이 다를 때만 추가
    // ---------------------------------------------------------
    public enum Hud
    {
        None = 0,

        Default = 10,     // 기본 HUD
        Minimal = 20,     // 최소 HUD (컷씬/이동 등)
        Boss = 30,        // 보스전 특화 HUD(보스 패널 포함 등)
        Spectator = 40,   // 관전 HUD
        Debug = 90,       // 디버그 HUD
    }

    // ---------------------------------------------------------
    // Popup (스택 기반)
    // ---------------------------------------------------------
    public enum Popup
    {
        None = 0,

        // 시스템
        Pause = 10,
        Confirm = 20,
        Alert = 30,
        Toast = 40,
        Tooltip = 50,

        // 런/게임플레이
        Reward = 100,
        Shop = 110,
        Inventory = 120,
        Settings = 130,
        GameOver = 140,
    }

    // ---------------------------------------------------------
    // Menu (전체 화면 전환형)
    // ---------------------------------------------------------
    public enum Menu
    {
        None = 0,

        Title = 10,
        Lobby = 20,
        ChapterSelect = 30,
        Loadout = 40,
        Settings = 50,
        Credits = 60,

        Results = 100,
    }

    // ---------------------------------------------------------
    // Overlay (최상단, 차단/연출)
    // ---------------------------------------------------------
    public enum Overlay
    {
        None = 0,

        Fade = 10,
        LoadingBlocker = 20,
        CutsceneBars = 30,
        ScreenMessage = 40,
        TutorialBlocker = 50,
        FXLayer = 60,        // Phase 4 — HitFx UI Layer (Flash / Zoom-Line / Damage Vignette)
    }

    // ---------------------------------------------------------
    // WorldSpace UI (풀링 대상 가능성 높음)
    // ---------------------------------------------------------
    public enum World
    {
        None = 0,

        Nameplate = 10,
        DamageNumber = 20,
        InteractionHint = 30,
        QuestMarker = 40,
        LootLabel = 50
    }

    // ---------------------------------------------------------
    // Layer 분류 유틸 (UIManager 라우팅용)
    // ---------------------------------------------------------
    public static Layer GetLayer(Hud _) => Layer.HUD;
    public static Layer GetLayer(Popup _) => Layer.Popup;
    public static Layer GetLayer(Menu _) => Layer.Menu;
    public static Layer GetLayer(Overlay _) => Layer.Overlay;
    public static Layer GetLayer(World _) => Layer.WorldSpace;
}
