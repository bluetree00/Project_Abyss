/// <summary>
/// 발동 계수(10-14 빌드 시너지 A1 · Risk of Rain 2의 proc coefficient) — 피해 한 번이 확률 효과 · 상태 지속 · 포이즈 · 강타에 주는 무게.
/// <para>연타 무기는 낮게(많이 맞혀 「같은 적 N회」 같은 횟수형으로 키운다), 한 방 무기는 높게(한 번에 확실하게).
/// 초당 기대값은 맞춘다 — 카타나 약 2.5타/초 × 0.7 ≈ 대검 약 1.1타/초 × 1.6(R2 실측). 2차 피해는 0.2라 연쇄가 번지지 않는다.</para>
/// <para>피해 공식이 「기본 피해 + 공격력」 덧셈이라(DamageFormula) 한 타 피해는 무기마다 공격력의 약 1.1배로 비슷하다 —
/// 피해량 문턱으로는 무기 성격이 갈리지 않아 계수로 가른다.</para>
/// </summary>
public static class ProcCoefficient
{
    // ── Constants ────────────────────────────────────────
    /// <summary>2차 피해(룬 · 형 · 유물 부가 피해 · 장판 · 직접 TakeDamage) — 연쇄 막기.</summary>
    public const float Secondary = 0.2f;
    /// <summary>강타 문턱(D3) — 이 계수 이상인 적중이 강타.</summary>
    public const float HeavyBase = 1.5f;
    /// <summary>필살 각인 단계마다 강타 문턱이 내려가는 몫(1.5 → 1.25 → 1.0 → 0.75).</summary>
    public const float HeavyStep = 0.25f;

    // ── Public Methods ───────────────────────────────────

    /// <summary>
    /// 무기 × 공격 종류의 기본 계수(기획 A1 표). 호출자가 <see cref="CombatDamage.Request.ProcCoef"/>를 비우면 이 값.
    /// 유물 Q는 1(한 번 쓰는 기술 — 태양 낙하 · 심판 마무리처럼 한 방인 것은 호출자가 2.5로 정한다).
    /// </summary>
    public static float Of(WeaponType weapon, WeaponActionType action, bool ranged)
    {
        if (action == WeaponActionType.QSkill) return 1f;
        if (action == WeaponActionType.GroundHeavy) return 2.5f;   // 무게 내려찍기(대검 형 발동)
        bool skill = action == WeaponActionType.ESkill || action == WeaponActionType.RSkill;
        return weapon switch
        {
            WeaponType.Greatsword => skill ? 2.5f : 1.6f,
            WeaponType.Crossbow   => skill ? 2.0f : 1.5f,
            WeaponType.Bow        => 0.6f,
            WeaponType.Katana     => skill ? 0.8f : 0.7f,   // 무형검(진화 전)도 카타나 타입
            _                     => ranged ? 0.6f : 0.7f,
        };
    }

    /// <summary>지금 강타 문턱 — 필살 각인 단계만큼 내려간다.</summary>
    public static float HeavyThreshold() => HeavyBase - HeavyStep * BuildImprint.Stage(BuildFamily.Crit);

    private static bool s_heavyNext;

    /// <summary>다음 주 피해 한 번을 강타로(구르기 첫 타 등 · 사전 보정 단계에서 건다).</summary>
    public static void ArmHeavyNext() => s_heavyNext = true;

    /// <summary>예약된 강타를 쓴다(CombatDamage가 계수를 정할 때 한 번).</summary>
    public static bool ConsumeHeavyNext()
    {
        bool b = s_heavyNext;
        s_heavyNext = false;
        return b;
    }

    [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => s_heavyNext = false;

    /// <summary>이 계수가 강타인가.</summary>
    public static bool IsHeavy(float coef) => coef >= HeavyThreshold() - 0.001f;
}
