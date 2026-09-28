using System;

namespace RelicFairy.Monster
{
/// <summary>
/// 보스 HP바가 더 보여줄 것 — 페이즈 바 · 무적 · 무방비(반격 기회).
/// 구현한 보스만 HUD가 읽는다(<see cref="HudPresenter.BindBoss"/>). 나머지 보스는 지금과 같다.
///
/// 페이즈 바(09-19 사용자 지시): 총 체력을 <see cref="HudPageMarkers"/> 경계로 나눠 <b>페이즈마다 한 줄</b>로 보여 준다.
/// 한 줄을 다 깎으면 전환 연출이 <see cref="HudPageRefill"/>을 부르고, 바가 차오르며 다음 페이즈가 시작된다.
/// </summary>
public interface IBossHudSource
{
    /// <summary>페이즈 경계(HP 비율 0~1, 내림차순). 페이즈 n의 바 = [경계 n−1 → 경계 n]. 없으면 null(한 줄).</summary>
    float[] HudPageMarkers { get; }

    /// <summary>지금 HP바가 보여 주는 페이즈(1부터). 전환 연출이 바를 채우기 시작하는 순간 다음 페이즈로 바뀐다.</summary>
    int HudPage { get; }

    /// <summary>다음 페이즈 바가 차오르기 시작한다 — (새 페이즈, 차오르는 시간 초).</summary>
    event Action<int, float> HudPageRefill;

    /// <summary>지금 무적인가(전환 · 결계 · 사라짐).</summary>
    bool HudInvulnerable { get; }

    /// <summary>무적이 켜지거나 꺼졌다.</summary>
    event Action<bool> HudInvulnerableChanged;

    /// <summary>무방비 창이 열렸다 — 인자 = 초.</summary>
    event Action<float> HudVulnerableWindow;

    /// <summary>페이지 눈금이 바뀌었다(모드 확정 등).</summary>
    event Action HudPageMarkersChanged;
}
}
