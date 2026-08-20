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
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Init()
    {
        // 도메인 리로드 OFF 2회차 중복 구독 방지 — 빼고 더한다.
        GlobalFeelCoalescer.OnBurst -= OnBurst;
        GlobalFeelCoalescer.OnBurst += OnBurst;
    }

    // ── Event Handler ─────────────────────────────────────────────
    private static void OnBurst(HitBurst burst)
    {
        // 세기 기준은 합계가 아니라 가장 센 한 방 — 잔타가 모여 강타를 흉내내면 안 된다.
        // "여러 발 맞췄다"는 셰이크에만 로그 가산으로 얹는다.
        HitFeelService.Hit(
            burst.MaxDamage,
            burst.AnyCritical,
            WeaponFeelTable.For(burst.WeaponType),
            burst.Direction,
            GlobalFeelCoalescer.MultiHitScale(burst.Count));
    }
}
