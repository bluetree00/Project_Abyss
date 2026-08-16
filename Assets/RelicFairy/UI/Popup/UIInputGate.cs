using UnityEngine;

/// <summary>
/// 월드 상호작용 키(F 등 <c>Input.GetKeyDown</c> 원시 폴링)의 공용 UI 게이트.
///
/// 차단형 팝업이 열리면 <see cref="UIManager"/>가 timeScale=0 + 플레이어 InputAction 비활성을 건다.
/// 하지만 월드 상호작용은 New Input System을 거치지 않는 raw 폴링이라 그 차단을 통째로 무시한다 —
/// 팝업 위에서 F를 누르면 뒤에 서 있던 제단/픽업이 같이 먹혔다(이중 발동).
/// 호출처마다 제각각 <c>_opening</c>/<c>HasPopup&lt;T&gt;()</c>로 막아오던 걸 단일 신호로 수렴한다.
///
/// 판정 기준은 <see cref="UIManager.IsGameplayBlocked"/> — 즉 <c>BlocksGameplay</c> 팝업 유무다.
/// 비차단 팝업(토스트·HUD 위젯)은 게이트하지 않는다.
/// </summary>
public static class UIInputGate
{
    /// <summary>차단형 팝업이 열려 있으면 true — 월드 상호작용 원시 입력을 무시할 것.</summary>
    public static bool Blocked => Managers.UI?.IsGameplayBlocked ?? false;
}
