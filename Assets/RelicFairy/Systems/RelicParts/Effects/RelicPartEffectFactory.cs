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
            // ── 유물 성장 v2(10-02) 가웨인 「해의 궤적」 — 조각 14 + 일광 반응 6(구현 계획 2) ──
            "g_dawn_sowing"     => new GDawnSowingEffect(),
            "g_daybreak_mark"   => new GDaybreakMarkEffect(),
            "g_dawn_oath"       => new GDawnOathEffect(),
            "g_morning_hunt"    => new GMorningHuntEffect(),
            "g_zenith"          => new GZenithEffect(),
            "g_second_sun"      => new GSecondSunEffect(),
            "g_sundial"         => new GSundialEffect(),
            "g_noon_bloom"      => new GNoonBloomEffect(),
            "g_ember_path"      => new GEmberPathEffect(),
            "g_setting_sun"     => new GSettingSunEffect(),
            "g_ember_carry"     => new GEmberCarryEffect(),
            "g_dusk_judgment"   => new GDuskJudgmentEffect(),
            "g_dawn_to_noon"    => new GDawnToNoonEffect(),
            "g_noon_to_dusk"    => new GNoonToDuskEffect(),
            "g_rx_thaw"         => new GRxThawEffect(),
            "g_rx_overheat"     => new GRxOverheatEffect(),
            "g_rx_wildfire"     => new GRxWildfireEffect(),
            "g_rx_twin_sun"     => new GRxTwinSunEffect(),
            "g_rx_corona"       => new GRxCoronaEffect(),
            "g_rx_eclipse"      => new GRxEclipseEffect(),

            // ── 랜슬롯 「광기의 계단」 — 조각 16 + 타락 반응 6(구현 계획 3) ──
            "l_split_oath"        => new LSplitOathEffect(),
            "l_blood_scent"       => new LBloodScentEffect(),
            "l_black_afterimage"  => new LBlackAfterimageEffect(),
            "l_betrayer_step"     => new LBetrayerStepEffect(),
            "l_torn_oath_blade"   => new LTornOathBladeEffect(),
            "l_madness_eye"       => new LMadnessEyeEffect(),
            "l_last_threshold"    => new LLastThresholdEffect(),
            "l_madness_crown"     => new LMadnessCrownEffect(),
            "l_endless_frenzy"    => new LEndlessFrenzyEffect(),
            "l_blood_frenzy"      => new LBloodFrenzyEffect(),
            "l_frenzy_step"       => new LFrenzyStepEffect(),
            "l_betrayal_feast"    => new LBetrayalFeastEffect(),
            "l_tearing_judgment"  => new LTearingJudgmentEffect(),
            "l_grudge_blade"      => new LGrudgeBladeEffect(),
            "l_betrayer_brand"    => new LBetrayerBrandEffect(),
            "l_second_judgment"   => new LSecondJudgmentEffect(),
            "l_rx_transfer"       => new LRxTransferEffect(),
            "l_rx_shatter"        => new LRxShatterEffect(),
            "l_rx_decay"          => new LRxDecayEffect(),
            "l_rx_brand_iron"     => new LRxBrandIronEffect(),
            "l_rx_expose"         => new LRxExposeEffect(),
            "l_rx_corrupt"        => new LRxCorruptEffect(),

            // ── [옛 v1 키] 옛 저장 · 옛 실측 도구 호환 ──
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
