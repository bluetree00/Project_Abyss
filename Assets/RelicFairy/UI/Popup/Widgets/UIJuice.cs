using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 팝업 안에서 쓰는 UI 로컬 저스(등장·펀치·플래시) 공용 헬퍼.
///
/// <b>왜 unscaled인가</b> — BlocksGameplay 팝업이 열리면 <c>TimeScaleArbiter</c>가 Pause(0f)를 잡는다.
/// 그 상태에선 카메라 셰이크·히트스톱·슬로우모가 전부 무효라(구현설계_보상공개연출 §A-5),
/// 팝업 안에서 살아있는 연출은 <b>unscaledDeltaTime 기반 UI 트윈</b>과 <c>VolumePulseService</c>뿐이다.
///
/// 모든 메서드는 취소 토큰을 받아 팝업 파괴 시 안전하게 빠진다.
/// </summary>
public static class UIJuice
{
    /// <summary>알파 0·스케일·y오프셋에서 제자리로 팝인. <paramref name="rotZ"/>는 시작 회전각(도).</summary>
    public static async UniTask PopInAsync(RectTransform rt, CanvasGroup cg, float duration,
                                           float fromScale, float fromYOffset, float rotZ,
                                           CancellationToken ct)
    {
        if (rt == null) return;

        Vector2 basePos = rt.anchoredPosition;
        float t = 0f;
        while (t < 1f)
        {
            t = Mathf.Min(t + Time.unscaledDeltaTime / Mathf.Max(0.01f, duration), 1f);
            float e = EaseOutBack(t);

            rt.localScale       = Vector3.one * Mathf.LerpUnclamped(fromScale, 1f, e);
            rt.anchoredPosition = basePos + new Vector2(0f, Mathf.LerpUnclamped(fromYOffset, 0f, e));
            if (rotZ != 0f)
                rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.LerpUnclamped(rotZ, 0f, e));
            if (cg != null) cg.alpha = Mathf.Clamp01(t * 2f);

            await UniTask.Yield(PlayerLoopTiming.Update, ct);
        }

        SnapPopIn(rt, cg, basePos);
    }

    /// <summary>팝인 트윈을 최종 상태로 즉시 확정(스킵 입력용).</summary>
    public static void SnapPopIn(RectTransform rt, CanvasGroup cg, Vector2 basePos)
    {
        if (rt != null)
        {
            rt.localScale       = Vector3.one;
            rt.localRotation    = Quaternion.identity;
            rt.anchoredPosition = basePos;
        }
        if (cg != null) cg.alpha = 1f;
    }

    /// <summary>스케일 펀치(sin 1회). 원 스케일로 복귀.</summary>
    public static async UniTask PunchAsync(RectTransform rt, float amp, float duration, CancellationToken ct)
    {
        if (rt == null) return;

        float t = 0f;
        while (t < 1f)
        {
            t = Mathf.Min(t + Time.unscaledDeltaTime / Mathf.Max(0.01f, duration), 1f);
            rt.localScale = Vector3.one * (1f + amp * Mathf.Sin(t * Mathf.PI));
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
        }
        rt.localScale = Vector3.one;
    }

    /// <summary>색 플래시 → 기준색 복귀(선형 감쇠). <paramref name="pulses"/>회 반복.</summary>
    public static async UniTask FlashAsync(UnityEngine.UI.Graphic target, Color flash, float duration,
                                           int pulses, CancellationToken ct)
    {
        if (target == null) return;

        Color baseCol = target.color;
        for (int p = 0; p < Mathf.Max(1, pulses); p++)
        {
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                target.color = Color.Lerp(flash, baseCol, t / duration);
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
        }
        target.color = baseCol;
    }

    /// <summary>알파 <paramref name="from"/> → 0 페이드아웃(전체화면 플래시 오버레이용).</summary>
    public static async UniTask FadeOutAsync(UnityEngine.UI.Graphic target, float from, float duration,
                                             CancellationToken ct)
    {
        if (target == null) return;

        Color c = target.color;
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            c.a = Mathf.Lerp(from, 0f, t / duration);
            target.color = c;
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
        }
        c.a = 0f;
        target.color = c;
    }

    /// <summary>timeScale과 무관한 대기. 팝업(timeScale=0) 안에서 쓰는 딜레이.</summary>
    public static async UniTask HoldAsync(float seconds, CancellationToken ct)
    {
        if (seconds <= 0f) return;
        float t = 0f;
        while (t < seconds)
        {
            t += Time.unscaledDeltaTime;
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
        }
    }

    private static float EaseOutBack(float t)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        float p = t - 1f;
        return 1f + c3 * p * p * p + c1 * p * p;
    }
}
