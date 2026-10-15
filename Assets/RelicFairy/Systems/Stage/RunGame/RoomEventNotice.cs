using System;

/// <summary>
/// 서비스 방 돌발 이벤트 알림 신호(10-15 · 기획 「재련소·강화·돌발이벤트 개선안」 §6 「들어가는 순간 안다」).
/// 재련소 · 정제소 · 상점 컨트롤러가 방이 화면에 드러나는 순간(<see cref="RunFlowController.RoomRevealed"/>) 이벤트 이름 + 한 줄을 쏘고,
/// 화면 쪽(<c>ZoneTitleBanner</c> — 구역 이름과 같은 화면 위 가운데 배너)이 그린다. 시스템이 UI를 직접 알지 않게 신호만 둔다(ZoneSign과 같은 결).
/// </summary>
public static class RoomEventNotice
{
    public static event Action<string, string> Requested;

    public static void Raise(string title, string description)
    {
        if (!string.IsNullOrEmpty(title)) Requested?.Invoke(title, description);
    }
}
