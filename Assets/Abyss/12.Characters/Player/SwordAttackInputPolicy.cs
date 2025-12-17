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
        // if (!Controller.isInChargingState || Controller.isAttacking) return;

        Controller.CharacterData.heavyAttackChargeTime += Time.unscaledDeltaTime;

        // if (!autoFired && Controller.CharacterData.heavyAttackChargeTime >= Controller.heavyAttackChargeThreshold)
        // {
        //     // 임계 도달 → 즉시 헤비 트리거 (버퍼로)
        //     Controller.InputBuffer.Push(Command.Heavy);
        //     autoFired = true;
        //     Controller.isInChargingState = false; // 더 이상 차지 갱신 X
        // }
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
        // Controller.isInChargingState = false;
    }
}
