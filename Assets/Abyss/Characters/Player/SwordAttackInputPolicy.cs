// SwordAttackInputPolicy.cs
using Game.Inputs;
using UnityEngine;

public sealed class SwordAttackInputPolicy : IAttackInputPolicy
{
    private bool autoFired;

    public void OnStarted(PlayerController Controller)
    {
        Controller.CharacterData.attackInputTime = Time.unscaledTime;
        Controller.CharacterData.heavyAttackChargeTime = 0f;
        // Controller.isInChargingState = true;
        autoFired = false;
    }

    public void Tick(PlayerController Controller, float dt)
    {
        Controller.CharacterData.heavyAttackChargeTime += Time.unscaledDeltaTime;
    }

    public void OnCanceled(PlayerController Controller)
    {
        // 이미 자동 발사 됐으면 아무 것도 안 함(버퍼에 Heavy 들어갔음)
        if (!autoFired)
        {
            float held = Time.unscaledTime - Controller.CharacterData.attackInputTime;
            // 임계 미만 → 라이트
            Controller.InputBuffer.Push(Command.Light);
        }

        // 정리
        Controller.CharacterData.attackInputTime = 0f;
        Controller.CharacterData.heavyAttackChargeTime = 0f;
    }
}
