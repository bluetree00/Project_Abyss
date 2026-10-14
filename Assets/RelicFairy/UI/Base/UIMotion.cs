using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 톤 v2 움직임(10-07) — 상태가 바뀌는 순간을 스프라이트 교체 대신 실시간으로 보여 준다(사용자 「활성화 연출도 제대로」).
/// 원칙: 0.12~0.35초 · 감속 · 튕김 없음 · 흔들림 없음. 시간정지(팝업 · 결과창) 중에도 돈다(unscaled).
/// </summary>
public static class UIMotion
{
    public const float Quick  = 0.14f;   // 눌림
    public const float Swap   = 0.18f;   // 상태 바꿈(교차 페이드)
    public const float Trace  = 0.25f;   // 테가 그려짐
    public const float Settle = 0.35f;   // 판이 자리 잡음

    /// <summary>감속(세제곱) — 0 → 0, 0.5 → 0.875, 1 → 1.</summary>
    public static float EaseOut(float t)
    {
        t = Mathf.Clamp01(t);
        float k = 1f - t;
        return 1f - k * k * k;
    }

    /// <summary>seconds 동안 step(0→1, 감속)을 매 프레임 부른다. 마지막엔 반드시 step(1). 취소되면 그 자리에서 멈춘다.</summary>
    public static async UniTask Tween(float seconds, Action<float> step, CancellationToken ct, Func<float, float> ease = null)
    {
        ease ??= EaseOut;
        float t0 = Time.unscaledTime;
        while (true)
        {
            float t = seconds <= 0f ? 1f : (Time.unscaledTime - t0) / seconds;
            step(ease(Mathf.Clamp01(t)));
            if (t >= 1f) return;
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
        }
    }

    /// <summary>주기 period로 0 ↔ 1을 오가며 step을 부른다 — ct가 취소될 때까지(숨쉬기). 취소는 OperationCanceledException.</summary>
    public static async UniTask Breathe(float period, Action<float> step, CancellationToken ct)
    {
        float t0 = Time.unscaledTime;
        float p  = Mathf.Max(0.01f, period);
        while (true)
        {
            float k = (Time.unscaledTime - t0) / p;
            step(0.5f - 0.5f * Mathf.Cos(k * Mathf.PI * 2f));
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
        }
    }
}
