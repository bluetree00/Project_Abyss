/// <summary>
/// effect_key 문자열 → <see cref="IRelicPartEffect"/> 인스턴스 생성.
///
/// RELIC_PARTS_DATA의 effect_key를 고유키로 분기한다. 아직 본문을 채우지 않은 key는
/// default 빈 <see cref="RelicPartEffect"/>로 폴백하므로, 드래프트·획득·저장 배관은
/// 효과 구현 여부와 무관하게 동작한다. 본문은 key마다 하나씩 단계적으로 채운다.
/// 키는 RELIC_PARTS_DATA.json의 effect_key와 정확히 일치해야 한다.
/// </summary>
public static class RelicPartEffectFactory
{
    public static IRelicPartEffect Create(string effectKey)
    {
        return effectKey switch
        {
            // ── 구현 완료 ──
            // 가웨인(화염)
            "gawain_burst_burn"    => new GawainBurstBurnEffect(),     // 화상 적 공격 시 절반 즉발
            "gawain_burn_spread"   => new GawainBurnSpreadEffect(),    // 화상 적 사망 시 1체 전염
            "gawain_solar_calamity"=> new GawainSolarCalamityEffect(), // (코어) 사망 시 광역 전염
            "gawain_judgment_brand"=> new GawainJudgmentBrandEffect(), // (코어) 화상 처형/보스 연장
            "gawain_eternal_noon"  => new GawainEternalNoonEffect(),   // (코어) 정오 상시 유지
            "gawain_noon_bloom"    => new GawainNoonBloomEffect(),     // 정오 진입 시 발밑 불씨 지대
            "gawain_ember_trail"   => new GawainEmberTrailEffect(),    // 근접 타격 자리 불씨 지대
            // 랜슬롯(광기)
            "lancelot_endless_frenzy"=> new LancelotEndlessFrenzyEffect(), // (코어) 광란 1회 자동 재점화
            "lancelot_blood_price" => new LancelotBloodPriceEffect(),  // 처치 시 광기 +1
            "lancelot_blood_thirst"=> new LancelotBloodThirstEffect(), // 광란 중 처치 시 연장
            "lancelot_lasting_madness"=> new LancelotLastingMadnessEffect(), // 광란 종료 후 광기 절반 유지
            "lancelot_bleed_brand" => new LancelotBleedBrandEffect(),  // 심판타 → 출혈 부여
            "lancelot_betrayer_brand"=> new LancelotBetrayerBrandEffect(), // (코어) 심판타 처형/보스 광기가득
            "lancelot_blood_feast" => new LancelotBloodFeastEffect(),  // (코어) 광란 처치 시 출혈 전염

            // 미구현 key는 빈 스켈레톤으로 폴백(연결만 보장, 효과 없음)
            _ => new RelicPartEffect(effectKey),
        };
    }
}
