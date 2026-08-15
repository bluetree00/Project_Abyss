using UnityEngine;
using RelicFairy.Monster;

/// <summary>
/// 상태 통화 <b>환전소</b> — 서약이 "무엇을 걸고, 키우고, 먹는가"를 말할 때 지나는 단 하나의 창구.
///
/// 저장소를 하나도 갖지 않는다. 전부 기존 채널에 위임한다 —
/// 화상은 <see cref="MonsterBurnHandler"/>, 출혈은 <see cref="MonsterBleed"/>,
/// 감전은 <see cref="MonsterStatusReceiver"/>의 슬로우, 취약은 <see cref="MonsterBase"/>의 받피증폭 슬롯.
/// 통화가 하나 늘 때마다 새 저장소를 파면 "지금 무엇이 걸려 있는가"를 아는 곳이 통화 수만큼 생겨
/// 아무도 전체를 셀 수 없게 된다. 통화의 <b>어휘</b>만 여기 모으고, 실제 상태는 원래 살던 곳에 그대로 둔다.
///
/// 단위 규약(중요):
///  • <see cref="Consume"/>는 <b>그 통화 고유의 단위</b>로 걷은 양을 돌려준다(화상·출혈=피해량, 감전=스택 수).
///  • <see cref="Charge"/>는 걸려 있는 잔고를 <b>피해 환산값</b>이라는 공통 단위로 바꿔 돌려준다.
/// 둘을 한 함수로 합치지 않는 이유: 「정지」는 스택 수가 필요하고(기절 지속), 「기폭」은 피해량이 필요하다.
/// 억지로 한 단위로 통일하면 둘 중 하나는 반드시 환산을 되돌려야 한다.
/// </summary>
public static class CovenantStatus
{
    // ── 감전(C2 신설 통화) ────────────────────────────────
    // 저장소도 어휘도 새로 만들지 않는다 — 이미 있는 슬로우 채널에 id 하나를 얹은 것뿐이다.
    // 라벨("감전")·아이콘(lightning)·속성색(Electric)은 EffectDescriptionFormatter/RuneElementMap에 이미 등록돼 있다.
    public const string ShockId           = "shock";
    /// <summary>감전 1스택당 이속 감소. 감전의 본체는 둔화가 아니라 '쌓였다'는 잔고다 — 그래서 작다.</summary>
    public const float  ShockSlowPerStack = 0.05f;
    public const float  ShockDuration     = 6f;
    public const int    ShockMaxStacks    = 5;

    // ── Charge 환산계수(C5) ───────────────────────────────
    // 통화마다 단위가 다르다(초·틱·스택·배율). 지속피해 둘은 이미 피해량이라 계수가 필요 없고,
    // 나머지 둘만 "이만큼 걸려 있으면 피해 얼마어치인가"로 환산한다. 수치는 전부 여기 한곳에만 있다.
    /// <summary>감전 1스택 = 공격력 × 이 값.</summary>
    public const float ShockChargeRatio      = 0.25f;
    /// <summary>취약 = amp × 잔여초 × 공격력 × 이 값.</summary>
    public const float VulnerableChargeRatio = 0.40f;

    /// <summary>화상·출혈 틱 간격 — 다른 사용처(SolarZone/FireField/랜슬롯 파츠)와 같은 결.</summary>
    public const float DotTickInterval = 0.5f;

    /// <summary>취약 통화가 쓰는 받피증폭 슬롯 id. 다른 출처(낙인·분쇄)를 덮어쓰지 않는다.</summary>
    private const string VulnerableSlotId = "cov_curse";

    // ── 걸기 ──────────────────────────────────────────────
    /// <summary>
    /// 통화를 <b>건다</b>(같은 통화가 이미 있으면 더 강한 쪽/더 긴 쪽으로 갱신).
    /// magnitude 해석은 통화마다 다르다 — 화상=dps, 취약=받피증폭 비율, 감전=무시(스택은 1씩).
    /// </summary>
    public static void Apply(GameObject target, StatusCurrency currency, float magnitude, float duration,
                             GameObject instigator)
    {
        if (target == null || duration <= 0f) return;

        switch (currency)
        {
            case StatusCurrency.Burn:
                if (magnitude > 0f)
                    MonsterBurnHandler.Apply(target, magnitude, duration, DotTickInterval, instigator);
                break;

            case StatusCurrency.Bleed:
                MonsterBleed.Apply(target, magnitude, duration, instigator);
                break;

            case StatusCurrency.Vulnerable:
            {
                var mb = Resolve(target);
                if (mb != null && magnitude > 0f) mb.ApplyDamageTakenAmp(magnitude, duration, VulnerableSlotId);
                break;
            }

            case StatusCurrency.Shock:
                Amplify(target, StatusCurrency.Shock, 0f, 0f, instigator);
                break;
        }
    }

    /// <summary>
    /// 통화를 <b>키운다</b>(중첩 누적). 걸기와 갈라 두는 이유: 화상은 더 강한 쪽이 남는 갱신형이고
    /// 출혈·감전은 걸수록 쌓이는 누적형이라, 같은 이름으로 부르면 "계속 걸면 커지는가"가 통화마다 달라진다.
    /// </summary>
    public static void Amplify(GameObject target, StatusCurrency currency, float magnitude, float duration,
                               GameObject instigator)
    {
        if (target == null) return;

        switch (currency)
        {
            case StatusCurrency.Bleed:
                MonsterBleed.ApplyStacked(target, magnitude, duration, CovenantMath.BleedMaxStacks, instigator);
                break;

            case StatusCurrency.Shock:
            {
                var mb = Resolve(target);
                mb?.Status?.ApplySlow(ShockId, ShockSlowPerStack, ShockDuration, ShockMaxStacks);
                break;
            }

            default:
                Apply(target, currency, magnitude, duration, instigator);   // 누적 개념이 없는 통화는 갱신과 같다
                break;
        }
    }

    // ── 먹기 ──────────────────────────────────────────────
    /// <summary>
    /// 통화를 <b>먹는다</b>. 반환 = 걷어낸 양(화상·출혈은 피해량, 감전은 스택 수 — 클래스 주석의 단위 규약).
    /// asDamage=true면 걷어낸 만큼 즉시 피해로 바꾼다(기폭), false면 피해 없이 걷어내기만 한다(수확).
    /// 감전은 부분 소모가 없다 — 스택은 정수라 비율 소모가 반올림에 따라 0이 되거나 1이 된다.
    /// </summary>
    public static float Consume(GameObject target, StatusCurrency currency, float fraction,
                                GameObject instigator, bool asDamage)
    {
        var mb = Resolve(target);
        if (mb == null) return 0f;

        switch (currency)
        {
            case StatusCurrency.Burn:
                // 화상 핸들러는 터뜨리기와 걷어내기를 각자 갖고 있다(터뜨리기는 피해까지 자기가 넣는다).
                return asDamage
                    ? MonsterBurnHandler.DetonateOn(mb.gameObject, fraction)
                    : MonsterBurnHandler.DrainOn(mb.gameObject, fraction);

            case StatusCurrency.Bleed:
            {
                float value = MonsterBleed.Consume(mb.gameObject, fraction);
                if (asDamage && value > 0f)
                    mb.TakeSynergyDamage(value, instigator, 1f, false, DamageKind.Synergy);
                return value;
            }

            case StatusCurrency.Shock:
                return mb.Status != null ? mb.Status.ConsumeSlow(ShockId) : 0f;

            default:
                return 0f;
        }
    }

    // ── 잔고 읽기 ─────────────────────────────────────────
    /// <summary>한 통화의 잔고를 피해 환산값으로. attack = 유효 공격력(감전·취약 환산에만 쓰인다).</summary>
    public static float Charge(GameObject target, StatusCurrency currency, float attack)
    {
        var mb = Resolve(target);
        if (mb == null) return 0f;

        switch (currency)
        {
            // 화상 = dps × 잔여시간, 출혈 = 틱피해 × 잔여틱 — 둘 다 이미 피해량이라 계수가 필요 없다.
            case StatusCurrency.Burn:  return MonsterBurnHandler.ChargeOn(mb.gameObject);
            case StatusCurrency.Bleed: return MonsterBleed.Remaining(mb.gameObject);

            case StatusCurrency.Shock:
                return mb.Status != null ? mb.Status.GetSlowStacks(ShockId) * attack * ShockChargeRatio : 0f;

            case StatusCurrency.Vulnerable:
                return mb.DamageTakenAmpTotal * mb.DamageTakenAmpRemaining * attack * VulnerableChargeRatio;

            default: return 0f;
        }
    }

    /// <summary>걸려 있는 통화 전체의 잔고 합(피해 환산값).</summary>
    public static float Charge(GameObject target, float attack)
        => Charge(target, StatusCurrency.Burn,       attack)
         + Charge(target, StatusCurrency.Bleed,      attack)
         + Charge(target, StatusCurrency.Shock,      attack)
         + Charge(target, StatusCurrency.Vulnerable, attack);

    /// <summary>대상에게 걸린 통화 <b>종수</b>(화상·출혈·감전·취약). 「처형」 임계 배수·「초신성」 반경이 읽는다.</summary>
    public static int CountKinds(GameObject target)
    {
        var mb = Resolve(target);
        if (mb == null) return 0;

        int n = 0;
        if (MonsterBurnHandler.ChargeOn(mb.gameObject) > 0f) n++;
        if (MonsterBleed.Remaining(mb.gameObject)      > 0f) n++;
        if (mb.Status != null && mb.Status.GetSlowStacks(ShockId) > 0) n++;
        if (mb.HasDamageTakenAmp)                            n++;
        return n;
    }

    /// <summary>통화가 하나라도 걸려 있는가. 「결계」·「초신성」이 "절여진 적"을 셀 때 쓴다.</summary>
    public static bool HasAny(MonsterBase mb)
        => mb != null && !mb.IsDead && CountKinds(mb.gameObject) > 0;

    /// <summary>감전 중첩 수(「정지」 미리보기·발동 판정).</summary>
    public static int ShockStacks(GameObject target)
    {
        var mb = Resolve(target);
        return mb?.Status != null ? mb.Status.GetSlowStacks(ShockId) : 0;
    }

    // ── 내부 ──────────────────────────────────────────────
    /// <summary>살아있는 몬스터면 그 MonsterBase, 아니면 null.</summary>
    private static MonsterBase Resolve(GameObject go)
    {
        if (go == null) return null;
        var mb = go.GetComponentInParent<MonsterBase>();
        return mb != null && !mb.IsDead ? mb : null;
    }
}
