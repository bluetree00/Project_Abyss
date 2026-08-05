using UnityEngine;

/// <summary>
/// lancelot_blood_price(피의 대가) — 적을 처치하면 광기 스택을 추가로 얻어 광란에 더 빨리 도달한다.
///
/// OnKill에서 광기 스택 +1. (광란 중에는 MadnessStack.AddStack이 no-op이라 자연히 무시된다.)
/// </summary>
public sealed class LancelotBloodPriceEffect : RelicPartEffect
{
    private const int BonusStacks = 1;

    public LancelotBloodPriceEffect() : base("lancelot_blood_price") { }

    public override void OnKill(GameObject deadEnemy, PlayerController player)
    {
        (player.RelicBehavior as LancelotMadnessRelic)?.AddStack(BonusStacks);
    }
}
