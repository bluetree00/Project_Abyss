// BowAttackInputPolicy.cs
using Game.Inputs;
using UnityEngine;

public sealed class BowAttackInputPolicy : IAttackInputPolicy
{
    public void OnStarted(PlayerController Controller)
    {
        Controller.CharacterData.attackInputTime = Time.unscaledTime;
        Controller.CharacterData.heavyAttackChargeTime = 0f;
        Controller.isInChargingState = true;
        Controller.HeavyAttackAbility?.HeavyAttackStartCharging(Controller);
    }

    public void Tick(PlayerController Controller, float dt)
    {
        if (!Controller.isInChargingState) return;
        Controller.CharacterData.heavyAttackChargeTime += Time.unscaledDeltaTime;
        Controller.HeavyAttackAbility?.HeavyAttackUpdateCharging(Controller, Controller.CharacterData.heavyAttackChargeTime);
        // 활은 자동 발사 없음 (릴리즈에서만 발사)
    }

    public void OnCanceled(PlayerController Controller)
    {
        float held = Time.unscaledTime - Controller.CharacterData.attackInputTime;

        // 릴리즈 순간 Heavy 발사(파워 = held로 내부에서 처리 가능)
        Controller.InputBuffer.Push(Command.Heavy);

        // 정리
        Controller.HeavyAttackAbility?.HeavyAttackUpdateCharging(Controller, held);
        Controller.HeavyAttackAbility?.HeavyAttackCancelCharging(Controller);
        Controller.CharacterData.attackInputTime = 0f;
        Controller.CharacterData.heavyAttackChargeTime = 0f;
        Controller.isInChargingState = false;
    }
}
