// SwordAttackInputPolicy.cs
using Game.Inputs;
using UnityEngine;

public sealed class SwordAttackInputPolicy : IAttackInputPolicy
{
    private bool _autoFired;
    private float _attackInputTime;
    private float _heavyAttackChargeTime;

    public void OnStarted(PlayerController controller)
    {
        _attackInputTime = Time.unscaledTime;
        _heavyAttackChargeTime = 0f;
        _autoFired = false;
    }

    public void Tick(PlayerController controller, float dt)
    {
        _heavyAttackChargeTime += dt;
    }

    public void OnCanceled(PlayerController controller)
    {
        if (!_autoFired)
        {
            float held = Time.unscaledTime - _attackInputTime;
            float threshold = controller.RuntimeStats.HeavyChargeThreshold;
            controller.InputBuffer.Push(held >= threshold ? Command.Heavy : Command.Light);
        }

        _attackInputTime = 0f;
        _heavyAttackChargeTime = 0f;
    }
}
