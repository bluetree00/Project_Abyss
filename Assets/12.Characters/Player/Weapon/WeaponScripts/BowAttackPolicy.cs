using System;
using UnityEngine;
/// <summary>
/// 활: 짧게 누르면 QuickShot(Light), 길게 당기면 Charged(Heavy) — 실전: started에서 charge start, canceled에서 판단
/// - started: 시작 시간 저장
/// - canceled: release 시점에서 Light/Heavy Push
/// - Tick: (옵션) charge 진행도에 따라 이펙트/사운드
/// </summary>
public class BowAttackPolicy : IAttackInputPolicy
{
    private float _startTime;
    private bool _holding;
    private readonly float _chargeThreshold = 0.5f; // 0.5초 이상이면 강한 샷(Heavy)

    public void OnStarted(PlayerController c)
    {
        _holding = true;
        _startTime = Time.unscaledTime;
        // 예: charge effect 시작
    }

    public void OnCanceled(PlayerController c)
    {
        if (!_holding) return;
        _holding = false;

        float held = Time.unscaledTime - _startTime;
        if (held >= _chargeThreshold)
        {
            // Charged shot => Heavy
            c.InputBuffer.Push(Game.Inputs.Command.Heavy);
            Debug.Log("[BowPolicy] Charged -> Heavy (held=" + held.ToString("F2") + ")");
        }
        else
        {
            // Quick shot => Light
            c.InputBuffer.Push(Game.Inputs.Command.Light);
            Debug.Log("[BowPolicy] Quick -> Light (held=" + held.ToString("F2") + ")");
        }
    }

    public void Tick(PlayerController c, float dt)
    {
        if (!_holding) return;
        float held = Time.unscaledTime - _startTime;
        // (옵션) show charge progress, cap, play sound etc.
    }
}
