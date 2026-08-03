/// <summary>
/// gawain_burst_burn(불사르기) — 화상 입은 적을 직접 공격하면 남은 화상의 절반이 즉시 터진다.
///
/// 플레이어 공격 적중(OnHit) 시 대상 화상의 50%를 즉발 피해로 전환한다. 나머지 절반은 계속 탄다.
/// 폭발 피해는 TakeSynergyDamage(DoT) 경로라 OnPostDealDamage를 안 타 재귀가 없다.
/// 화상은 GawainSolarBurnPassive가 부여 — 이 파츠는 그 잔여를 앞당겨 소비할 뿐이다.
/// </summary>
public sealed class GawainBurstBurnEffect : RelicPartEffect
{
    private const float BurstFraction = 0.5f;

    public GawainBurstBurnEffect() : base("gawain_burst_burn") { }

    public override void OnHit(in HitInfo hit, PlayerController player)
    {
        if (hit.Target == null) return;
        // 화상 잔여가 있을 때만 터뜨리고, 그 자리에 화염 버스트를 낸다.
        if (!hit.Target.TryGetComponent<MonsterBurnHandler>(out var burn) || burn.Remaining <= 0f) return;

        MonsterBurnHandler.DetonateOn(hit.Target, BurstFraction);
        ElementVfxPlayer.PlayBurst(RuneElement.Fire, hit.HitPoint, 1f);
    }
}
