/// <summary>
/// effect_type 문자열 → IRuneEffect 인스턴스 생성.
///
/// 24효과(6속성×4단계)를 effect_type 고유키로 분기한다. <b>24종 전부 실구현</b>돼 있으며,
/// default의 빈 RuneEffect는 데이터에 없는 키가 들어왔을 때의 안전 폴백이다.
/// 키는 MERLIN_RUNE_SYNERGY_DATA의 effect_type과 정확히 일치해야 한다.
/// </summary>
public static class RuneEffectFactory
{
    public static IRuneEffect Create(RuneSynergyEntry entry)
    {
        if (entry == null || string.IsNullOrEmpty(entry.effect_type))
            return null;

        RuneEffect fx = entry.effect_type switch
        {
            // ── ⚡ 전기 (자기강화 루프, 단독완결) — 실구현 ──
            "ElecStatic"    => new ElecStaticEffect(),
            "ElecDischarge" => new ElecDischargeEffect(),
            "ElecShock"     => new ElecShockEffect(),
            "ElecOverload"  => new ElecOverloadEffect(),

            // ── 🔥 불 (화염 증폭, 적 상태=점화) — 실구현 ──
            "FireEmber"     => new FireEmberEffect(),
            "FireIgnite"    => new FireIgniteEffect(),
            "FireBlaze"     => new FireBlazeEffect(),
            "FireScorch"    => new FireScorchEffect(),

            // ── ✦ 빛 (치명타 루프, 리소스+장판) — 실구현 ──
            "LightRadiance"  => new LightRadianceEffect(),
            "LightBurst"     => new LightBurstEffect(),
            "LightSanctuary" => new LightSanctuaryEffect(),
            "LightField"     => new LightFieldEffect(),

            // ── ☘ 풀 (독안개 장판, 필드 중심) — 실구현 ──
            "GrassMist"         => new GrassMistEffect(),
            "GrassMistPlus"     => new GrassMistPlusEffect(),
            "GrassMistInsight"  => new GrassMistInsightEffect(),
            "GrassMistDominion" => new GrassMistDominionEffect(),

            // ── ❄ 얼음 (제어, 적 상태=서리/빙결) — 실구현 ──
            "IceFrost"   => new IceFrostEffect(),
            "IceFreeze"  => new IceFreezeEffect(),
            "IceShatter" => new IceShatterEffect(),
            "IceGlacier" => new IceGlacierEffect(),

            // ── 🌑 어둠 (피격 강화, 게이지) — 실구현 ──
            "DarkErosion"    => new DarkErosionEffect(),
            "DarkRelease"    => new DarkReleaseEffect(),
            "DarkAfterimage" => new DarkAfterimageEffect(),
            "DarkAbyss"      => new DarkAbyssEffect(),

            // 미등록 effect_type만 빈 효과 폴백(현재 24종 전부 실구현 → 정상 데이터에선 미사용)
            _ => new RuneEffect(),
        };

        fx.Bind(entry);
        return fx;
    }
}
