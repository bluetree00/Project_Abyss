using System;
using UnityEngine;

/// <summary>
/// 히트 이벤트 허브. 공격자/피격자 양측 연출 구독자(카메라 쉐이크, Flash 쉐이더, Volume 펄스 등)가
/// OnHit 이벤트를 구독하여 반응한다.
///
/// 실제 Hit-Stop / Camera Shake 실행은 HitFeelService에 위임 (하위 호환).
/// Phase 2~3 에서 VictimHitFeedback / VolumePulseService 등이 OnHit 구독 예정.
/// </summary>
public static class HitFeedbackService
{
    // ── Static ────────────────────────────────────────────────────
    /// <summary>히트 발생 시 발행. 구독자는 OnEnable에서 += / OnDisable에서 -= 필수.</summary>
    public static event Action<HitInfo> OnHit;

    // ── Public Methods ────────────────────────────────────────────
    /// <summary>
    /// 이펙트-타겟 콜라이더 충돌 시점에서 호출.
    /// 1) 기본 HitStop + CameraShake (⑨ + ① 기존 HitFeel)
    /// 2) target의 IHitReceiver 직접 호출 (🔵 피격자 로컬 피드백 — Flash/PointLight)
    /// 3) OnHit 이벤트 발행 (🔴 글로벌 구독자 — 카메라/PostFX 등 Phase 3+)
    /// </summary>
    public static void RaiseHit(in HitInfo info)
    {
        PlayDefaultFeel(info);
        DispatchToTargetReceiver(info);
        DispatchToGlobalSubscribers(info);
    }

    // ── Private Methods ───────────────────────────────────────────
    /// <summary>
    /// 크리티컬/일반 기본 피드백. 기존 HitFeelService API에 포워딩하여 기존 체감을 그대로 유지한다.
    /// Phase 3에서 WeaponHitProfileSO 도입 시 프로필 우선으로 전환.
    /// </summary>
    private static void PlayDefaultFeel(in HitInfo info)
    {
        HitFeelService.Hit(info.Damage, info.IsCritical);
    }

    /// <summary>피격자 로컬 피드백 — IHitReceiver 구현체에게 직접 전달.</summary>
    private static void DispatchToTargetReceiver(in HitInfo info)
    {
        if (info.Target == null) return;
        if (!info.Target.TryGetComponent<IHitReceiver>(out var receiver)) return;

        try { receiver.OnReceiveHit(info); }
        catch (Exception e) { Debug.LogException(e); }
    }

    /// <summary>글로벌 구독자에게 OnHit 이벤트 발행 — 예외 격리.</summary>
    private static void DispatchToGlobalSubscribers(in HitInfo info)
    {
        var handlers = OnHit;
        if (handlers == null) return;

        foreach (var handler in handlers.GetInvocationList())
        {
            try { ((Action<HitInfo>)handler).Invoke(info); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }
}
