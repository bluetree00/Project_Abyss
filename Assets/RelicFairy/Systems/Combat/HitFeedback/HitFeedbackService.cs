using System;
using UnityEngine;

/// <summary>
/// 히트 이벤트 허브. 연출을 <b>두 계층으로 갈라</b> 내보낸다.
///
///  · <b>국소</b>(착탄 지점마다 나야 옳다) — 피격자 플래시·히트 VFX·데미지 숫자.
///    타격 1건마다 즉시 발행한다: IHitReceiver 직접 호출 + <see cref="OnHit"/>.
///  · <b>글로벌</b>(공격 1회당 한 번이어야 옳다) — 히트스톱·카메라 셰이크·풀스크린 플래시·PostFX 펄스.
///    <see cref="GlobalFeelCoalescer"/>에 적립만 하고, 프레임 끝에 합산본이 1회 발행된다.
///
/// 갈라놓지 않으면 분열·관통·폭발이 붙은 원거리 한 발이 화면 연출을 수십 겹으로 터뜨린다.
/// 새 화면 연출을 붙일 때는 <see cref="GlobalFeelCoalescer.OnBurst"/>를 구독할 것 —
/// <see cref="OnHit"/>을 구독하면 다시 같은 문제로 돌아간다.
/// </summary>
public static class HitFeedbackService
{
    // ── Static ────────────────────────────────────────────────────
    /// <summary>히트 발생 시 발행. 구독자는 OnEnable에서 += / OnDisable에서 -= 필수.</summary>
    public static event Action<HitInfo> OnHit;

    // ── Public Methods ────────────────────────────────────────────
    /// <summary>
    /// 피해가 실제로 들어간 시점에서 호출.
    /// 1) target의 IHitReceiver 직접 호출 (🔵 국소 — 피격자 Flash/PointLight)
    /// 2) OnHit 이벤트 발행 (🔵 국소 — 타격 1건 단위로 알아야 하는 구독자)
    /// 3) 글로벌 화면 연출은 프레임 합산기에 적립 (🔴 프레임 끝에 1회 발행)
    /// </summary>
    public static void RaiseHit(in HitInfo info)
    {
        DispatchToTargetReceiver(info);
        DispatchToGlobalSubscribers(info);
        GlobalFeelCoalescer.Accumulate(info);
    }

    // ── Private Methods ───────────────────────────────────────────
    /// <summary>피격자 로컬 피드백 — IHitReceiver 구현체에게 직접 전달.</summary>
    private static void DispatchToTargetReceiver(in HitInfo info)
    {
        if (info.Target == null) return;
        if (!info.Target.TryGetComponent<IHitReceiver>(out var receiver)) return;

        try { receiver.OnReceiveHit(info); }
        catch (Exception e) { Debug.LogException(e); }
    }

    /// <summary>국소 구독자에게 OnHit 이벤트 발행(타격 1건 단위) — 예외 격리.</summary>
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
