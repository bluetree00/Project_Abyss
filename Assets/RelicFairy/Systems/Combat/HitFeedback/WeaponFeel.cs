/// <summary>
/// 장비(무기) 타입별 타격 "손맛" 프로필 — 히트스톱 깊이/길이배수 + 카메라 셰이크 강도/길이.
/// HitFeedbackService.RaiseHit 경로에서 HitInfo.WeaponType 으로 조회되어 HitFeelService.Hit 에 전달된다.
/// (데미지 비례 정지길이/깊이 앵커는 HitFeelService가 보유, 여기선 무기별 "무게감"만 변조)
///
/// 차별화 의도:
///  · 카타나   = 빠르고 경쾌 (짧은 정지·작은 셰이크)
///  · 대검     = 묵직 (긴 정지·큰 셰이크)
///  · 활       = 정밀 (최소 정지·작은 셰이크, 크리만 강조)
///  · 석궁     = 날카로움 (활보다 살짝 묵직)
///
/// 정밀 튜닝이 필요한 개별 무기는 후속에서 WeaponSO에 옵셔널 프로필 SO를 추가해 오버라이드 가능(현재는 타입 테이블).
/// </summary>
public readonly struct WeaponFeel
{
    public readonly float StopScale;         // 비크리 히트스톱 timeScale(깊이, 작을수록 깊음)
    public readonly float CritStopScale;     // 크리 히트스톱 깊이
    public readonly float StopDurationMult;  // 데미지 비례 정지길이 배수(무게감)
    public readonly float ShakeAmp;          // 비크리 셰이크 진폭
    public readonly float ShakeDur;          // 비크리 셰이크 길이
    public readonly float CritShakeAmp;      // 크리 셰이크 진폭
    public readonly float CritShakeDur;      // 크리 셰이크 길이

    public WeaponFeel(
        float stopScale, float critStopScale, float stopDurationMult,
        float shakeAmp, float shakeDur, float critShakeAmp, float critShakeDur)
    {
        StopScale        = stopScale;
        CritStopScale    = critStopScale;
        StopDurationMult = stopDurationMult;
        ShakeAmp         = shakeAmp;
        ShakeDur         = shakeDur;
        CritShakeAmp     = critShakeAmp;
        CritShakeDur     = critShakeDur;
    }
}

/// <summary>무기 타입 → WeaponFeel 기본 테이블. 미등록 타입은 Default(기존 HitFeel 앵커와 동일).</summary>
public static class WeaponFeelTable
{
    // 기존 HitFeelService.Hit 의 비크리(Light) / 크리 앵커와 동일 — 폴백 시 체감 무변경.
    public static readonly WeaponFeel Default =
        new WeaponFeel(0.10f, 0.02f, 1.00f, 0.04f, 0.06f, 0.18f, 0.18f);

    private static readonly WeaponFeel Katana =
        new WeaponFeel(0.10f, 0.02f, 0.85f, 0.045f, 0.06f, 0.15f, 0.16f);

    private static readonly WeaponFeel Greatsword =
        new WeaponFeel(0.06f, 0.015f, 1.60f, 0.11f, 0.13f, 0.22f, 0.20f);

    private static readonly WeaponFeel Bow =
        new WeaponFeel(0.13f, 0.03f, 0.70f, 0.03f, 0.05f, 0.13f, 0.14f);

    private static readonly WeaponFeel Crossbow =
        new WeaponFeel(0.10f, 0.025f, 0.95f, 0.055f, 0.07f, 0.16f, 0.15f);

    public static WeaponFeel For(WeaponType type) => type switch
    {
        WeaponType.Katana     => Katana,
        WeaponType.Greatsword => Greatsword,
        WeaponType.Bow        => Bow,
        WeaponType.Crossbow   => Crossbow,
        _                     => Default,
    };
}
