using System;
using UnityEngine;
/// <summary>
/// 검: 누르면 즉시 Light, 길게 누르면 Heavy (Hold threshold)
/// - started: 기록만
/// - canceled: 실제로 Light/Heavy를 결정하여 InputBuffer.Push
/// - Tick: 미사용 (보류)
/// </summary>
public class SwordAttackPolicy : IAttackInputPolicy
{
    private float _startTime;
    private bool _holding;

    // 홀드 임계값 (초) - 길게 누르면 Heavy
    private readonly float _holdThreshold = 0.25f;

    public void OnStarted(PlayerController c)
    {
        _holding = true;
        _startTime = Time.unscaledTime;
        // (옵션) 바로 비주얼/사운드: charge start 등
    }

    public void OnCanceled(PlayerController c)
    {
        if (!_holding) return;
        _holding = false;

        float held = Time.unscaledTime - _startTime;
        if (held >= _holdThreshold)
        {
            // Heavy
            c.InputBuffer.Push(Game.Inputs.Command.Heavy);
            Debug.Log("[SwordPolicy] Released -> Heavy (held=" + held.ToString("F2") + ")");
        }
        else
        {
            // Light
            c.InputBuffer.Push(Game.Inputs.Command.Light);
            Debug.Log("[SwordPolicy] Released -> Light (held=" + held.ToString("F2") + ")");
        }
    }

    public void Tick(PlayerController c, float dt)
    {
        // optionally, show charge VFX when held long enough
    }
}
