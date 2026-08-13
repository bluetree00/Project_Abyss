using UnityEngine;

[CreateAssetMenu(menuName = "RelicFairy/Legendary/VFX Catalog", fileName = "LegendaryVfxCatalog")]
public class LegendaryVfxCatalog : ScriptableObject
{
    [Header("Fire")]
    public GameObject fireAoeVfx;       // Effect_45_BlastFlame
    public GameObject fireSingleVfx;    // Effect_51_FlameTsunami
    public GameObject fireOrbVfx;       // PixPlays Fireball projectile
    public GameObject fireHitVfx;       // Hit 16 fire / FireBallHit (F-2 final)
    public GameObject fireAoeHitVfx;    // Flash 16 fire (F-1 per-enemy)
    public GameObject fireSingleHitVfx; // Flash 25 orange explosion (F-2 per-dive)
    public GameObject fireOrbHitVfx;    // Flash 18 nova orange (F-3)

    [Header("Ice")]
    public GameObject iceAoeVfx;        // Effect_49_IceAge
    public GameObject iceSingleVfx;     // Effect_26_IceFatalWheel (I-2 dive attack)
    public GameObject iceFieldVfx;      // Effect_25_IceField
    public GameObject iceHitVfx;        // Flash 26 blue crystal (I-2 hit, I-3 shard)
    public GameObject iceAoeHitVfx;     // Flash 26 blue crystal (I-1 per-enemy)
    public GameObject iceFieldHitVfx;   // Flash 14 blue rapid (I-3 zone per-enemy)

    [Header("Electric")]
    public GameObject elecAoeVfx;       // Effect_24_LightningStrike_2
    public GameObject elecSingleVfx;    // Effect_16_ElectricField
    public GameObject elecBallVfx;      // Particle Electric Ball
    public GameObject elecHitVfx;       // Hit 2 electro (T-2 explosion)
    public GameObject elecAoeHitVfx;    // Flash 2 electro (T-1 per-target)
    public GameObject elecSingleHitVfx; // Flash 4 yellow arrow (T-2 per-tick)
    public GameObject elecOrbHitVfx;    // Flash 2 electro (T-3)

    [Header("Grass")]
    public GameObject grassAoeVfx;      // Effect_46_PoisonSmoke
    public GameObject grassSingleVfx;   // Effect_49_EyeOfTheStorm
    public GameObject grassStormVfx;    // Spell_Storm_3_Green Variant (P-2 binding)
    public GameObject grassArrowVfx;    // Projectile 1 nature arrow (transform)
    public GameObject grassHitVfx;      // Hit 1 nature arrow (P-3)
    public GameObject grassAoeHitVfx;   // Flash 24 green explosion (P-1 per-enemy)
    public GameObject grassSingleHitVfx;// Flash 12 slime (P-2 per-hit)

    [Header("Light")]
    public GameObject lightAoeVfx;      // Effect_11_LightInFullBloom
    public GameObject lightJudgeVfx;    // Effect_31_LumenJudgement
    public GameObject lightBeamVfx;     // Effect_28_PurifierBeam
    public GameObject lightSwordVfx;    // SmallLightBullet
    public GameObject lightHitVfx;      // SmallLightBulletHit (L-3)
    public GameObject lightAoeHitVfx;   // Flash 22 cute star (L-1 per-enemy)
    public GameObject lightSingleHitVfx;// Flash 4 yellow arrow (L-2 per-beam)

    [Header("Dark")]
    public GameObject darkAoeVfx;       // Effect_27_ExposeOfDarkness
    public GameObject darkDeathWaveVfx; // Effect_50_DeathWave
    public GameObject darkSingleVfx;    // Effect_43_DarkDimensionAttack
    public GameObject darkCloneVfx;     // 분신 칼 휘두르기 VFX (Inspector에서 지정)
    public GameObject darkScytheVfx;    // Effect_43_DarkChainSwamp
    public GameObject darkHitVfx;       // Hit 3 black fire / Flash 3 black fire (D-3)
    public GameObject darkAoeHitVfx;    // Flash 3 black fire (D-1 per-enemy)
    public GameObject darkSingleHitVfx; // Flash 17 nova violet (D-2 per-clone)
}
