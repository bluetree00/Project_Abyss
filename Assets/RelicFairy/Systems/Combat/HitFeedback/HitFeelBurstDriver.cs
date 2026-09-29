using UnityEngine;

/// <summary>
/// 합산된 타격(<see cref="HitBurst"/>) → 히트스톱·카메라 셰이크 실행.
///
/// 예전엔 <see cref="HitFeedbackService"/>가 타격 1건마다 <see cref="HitFeelService"/>를 직접 불렀다.
/// 히트스톱은 "긴 쪽 유지"로 자체 방어가 있었지만 셰이크 트라우마는 호출마다 선형 누적돼,
/// 분열 20발이 동시에 꽂히면 카메라가 통째로 요동쳤다. 그 호출을 프레임 합산본 1회로 옮긴다.
/// </summary>
public static class HitFeelBurstDriver
{
    // ── Constants ─────────────────────────────────────────────────
    // 무기 손맛 표(WeaponFeelTable)의 흔들림 값에 곱하는 배율. 표는 원래 0.03~0.22라 그대로 쓰면 대검 치명타가 최대치(100%)였다.
    private const float BasicShakeScale    = 0.1f;
    private const float SkillShakeScale    = 0.3f;
    private const float FinisherShakeScale = 1f;
    private const float FinisherSfxMinInterval = 0.2f;   // 막타가 여러 적에 연달아 나도 소리는 한 번

    private static float _lastFinisherSfx = -999f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Init()
    {
        // 도메인 리로드 OFF 2회차 중복 구독 방지 — 빼고 더한다.
        _lastFinisherSfx = -999f;
        GlobalFeelCoalescer.OnBurst -= OnBurst;
        GlobalFeelCoalescer.OnBurst += OnBurst;
    }

    // ── Event Handler ─────────────────────────────────────────────
    private static void OnBurst(HitBurst burst)
    {
        // 세기 기준은 합계가 아니라 가장 센 한 방 — 잔타가 모여 강타를 흉내내면 안 된다.
        // "여러 발 맞췄다"는 셰이크에만 로그 가산으로 얹는다.
        // 흔들림: 무기 손맛 비율(대검 > 카타나 > 활)은 남기되 단계별 배율·상한으로 「살짝」에 묶는다(09-25).
        // 막타는 배율 1이라 모든 무기에서 상한(0.022)에 닿는다 — 마무리 순간의 신호를 일정하게.
        float tierScale, cap;
        switch (burst.Tier)
        {
            case DealtHitTier.Finisher: tierScale = FinisherShakeScale; cap = HitFeelService.DealtShakeFinisher; break;
            case DealtHitTier.Skill:    tierScale = SkillShakeScale;    cap = HitFeelService.DealtShakeSkill;    break;
            default:                    tierScale = BasicShakeScale;    cap = HitFeelService.DealtShakeBasic;    break;
        }
        HitFeelService.Hit(
            burst.MaxDamage,
            burst.AnyCritical,
            WeaponFeelTable.For(burst.WeaponType),
            burst.Direction,
            GlobalFeelCoalescer.MultiHitScale(burst.Count) * tierScale,
            cap,
            longStop: burst.Tier == DealtHitTier.Finisher);

        // 막타는 흔들림 대신 화면 가장자리 금빛과 소리로 알린다(HUD 아래).
        if (burst.Tier == DealtHitTier.Finisher)
        {
            FinisherEdgeService.Pulse();
            if (Time.unscaledTime - _lastFinisherSfx >= FinisherSfxMinInterval)
            {
                _lastFinisherSfx = Time.unscaledTime;
                Managers.Sound?.PlayEvent(SoundEvent.PlayerFinisher);
            }
        }
    }
}
