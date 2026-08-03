using UnityEngine;

/// <summary>
/// lancelot_blood_thirst(피의 갈증) — 광란 중 적을 처치할 때마다 광란 지속시간이 조금 갱신된다.
///
/// OnKill에서 광란(Frenzy) 상태일 때만 잔여 지속시간을 소량 연장한다.
/// </summary>
public sealed class LancelotBloodThirstEffect : RelicPartEffect
{
    private const float FrenzyRefresh = 1.2f;   // 처치당 광란 연장(초)

    public LancelotBloodThirstEffect() : base("lancelot_blood_thirst") { }

    public override void OnKill(GameObject deadEnemy, PlayerController player)
    {
        var madness = (player.RelicBehavior as LancelotMadnessRelic)?.Madness;
        if (madness != null && madness.IsFrenzy)
            madness.ExtendFrenzy(FrenzyRefresh);
    }
}
