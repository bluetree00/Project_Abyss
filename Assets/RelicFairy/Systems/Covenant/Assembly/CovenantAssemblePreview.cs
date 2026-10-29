/// <summary>
/// 조립 서약 미리보기 데이터 — UI(중앙 완성 패널·카드·툴팁)가 백엔드와 동일한 수치를 표시하기 위한 헬퍼.
/// 계산은 전부 <see cref="CovenantMath"/>에 위임한다 — 런타임(AssembledCovenant)과 <b>같은 함수</b>를 불러야
/// 카드에 적힌 숫자와 실제로 터지는 숫자가 갈라지지 않는다.
/// </summary>
public readonly struct CovenantAssemblePreview
{
    public readonly bool valid;
    public readonly CovenantCategory category;

    public readonly string causeName, causeDesc, causeTag;
    public readonly CovenantTier causeTier;
    public readonly float coefficient;        // 티어 반영 유효 계수(스케일 전 원값 — "봉인 계수" 표기)

    public readonly string effectName, effectDesc, effectTag;
    public readonly CovenantTier effectTier;
    public readonly EffectKind effectKind;
    public readonly float magnitude;          // 티어 반영 magnitude(계수 전)
    public readonly float effective;          // 스케일·상한까지 반영된 최종 수치 = 실제 동작값
    public readonly int   effectiveCount;     // 정수량(충전 횟수 등)
    public readonly float radius, duration;

    public readonly EffectAxis     axis;
    public readonly StatusCurrency status;
    public readonly StatusRole     role;      // 통화를 거는가(Apply) 먹는가(Consume) — 시너지 판정축

    /// <summary>원인 형상 — 질적 변형(기폭 단일/광역, 처형 표식 등)이 미리보기 문구에도 반영돼야 한다.</summary>
    public readonly CauseClass causeClass;
    public readonly bool       causeTargeted;
    /// <summary>원인 계기 — 학살(N마리 처치)은 쓰러진 자리에서 시작한다(10-23 줄 글 「누구에게」).</summary>
    public readonly CauseTriggerKind causeTrigger;
    /// <summary>원인 문턱 수(포위 = 5m 안 적 수) — 격노 위험 변형 안내.</summary>
    public readonly int        causeThreshold;

    private CovenantAssemblePreview(
        CovenantPalette.CauseDef c, CovenantTier ct,
        CovenantPalette.EffectDef e, CovenantTier et, float posMult = 1f)
    {
        valid       = true;
        category    = c.category;
        causeName   = c.name; causeDesc = c.desc; causeTag = c.tag; causeTier = ct;
        coefficient = CovenantMath.RawCoef(c, ct);
        effectName  = e.name; effectDesc = e.desc; effectTag = e.tag; effectTier = et;
        effectKind  = e.kind;
        magnitude   = CovenantMath.RawMag(e, et);
        // 서약서 절 위치 배율(결과 1 = 1 · 2 = 0.85 …) — 런타임 CovenantClause와 같은 식
        effective   = CovenantMath.Effective(e, et, c, ct) * posMult;
        effectiveCount = posMult >= 1f
            ? CovenantMath.EffectiveCount(e, et, c, ct)
            : UnityEngine.Mathf.Max(1, UnityEngine.Mathf.FloorToInt(CovenantMath.EffectiveCount(e, et, c, ct) * posMult));
        radius      = CovenantMath.EffectiveRadius(e);
        duration    = e.duration;
        axis        = e.axis;
        status      = e.status;
        role        = e.role;
        causeClass  = c.cls;
        causeTargeted = c.targeted;
        causeTrigger  = c.trigger;
        causeThreshold = c.thresholdInt;
    }

    /// <summary>고른 조합의 유효 수치 미리보기. 미해결 조합이면 valid=false.</summary>
    public static CovenantAssemblePreview Build(string causeId, CovenantTier causeTier,
                                                string effectId, CovenantTier effectTier)
    {
        if (CovenantPalette.TryGetCause(causeId, out var c) &&
            CovenantPalette.TryGetEffect(effectId, out var e))
            return new CovenantAssemblePreview(c, causeTier, e, effectTier);
        return default;
    }

    /// <summary>서약서 절의 유효 수치 미리보기 — 절 위치 배율(<see cref="CovenantGrammar.PositionMult"/>)을 곱한다.</summary>
    public static CovenantAssemblePreview Build(string causeId, CovenantTier causeTier,
                                                string effectId, CovenantTier effectTier, float posMult)
    {
        if (CovenantPalette.TryGetCause(causeId, out var c) &&
            CovenantPalette.TryGetEffect(effectId, out var e))
            return new CovenantAssemblePreview(c, causeTier, e, effectTier, posMult);
        return default;
    }

    /// <summary>결과 문장: "OO 할 때 → 실제 수치" (중앙 미리보기용). 서술형 설명(effectDesc)은 무슨 기능인지 안 읽혀 수치로 쓴다(09-29).</summary>
    public string ResultSentence => valid ? causeDesc + " → " + EffectAmountLabel() : "";

    /// <summary>
    /// 카드 · 좁은 칸용 한 줄 — 「누구에게」는 빼고 「무엇이 · 얼마나」만(조립 카드 설명 두 줄 ≈ 26자 안).
    /// 10-23 U8 사용자 「『화상 공격력×80%』 같은 건 감도 안 잡힌다」 — 「4초 동안 불탄다(초당 공격력 80%)」처럼 일어나는 일로 쓴다.
    /// 값은 전부 실제 동작값(effective — 런타임 <see cref="CovenantClause"/>와 같은 식)이다.
    /// </summary>
    public string EffectAmountCompact()
    {
        if (!valid) return "";
        return effectKind switch
        {
            EffectKind.AoeBurst   => $"반경 {M(radius)} 폭발(공격력 {P(effective)})",
            EffectKind.Curse      => $"적이 {Sec(duration)} 동안 받는 피해 +{P(effective)}",
            EffectKind.Execute    => $"체력 {P(ExecuteThreshold)} 이하인 적 즉사",
            EffectKind.Burn       => $"{Sec(duration)} 동안 불탄다(초당 공격력 {P(effective)})",
            EffectKind.BleedStack => $"{Sec(duration)} 동안 피 흘린다(초당 공격력 {P(effective)})",
            EffectKind.Detonate   => causeTargeted
                ? $"화상 · 출혈 {P(CovenantMath.DetonateFraction)} 터뜨림 · 둘레 {M(radius)}에 그 {P(effective)}"
                : $"{M(radius)} 안 적 모두의 화상 · 출혈 {P(CovenantMath.DetonateFraction)} 터뜨림",
            EffectKind.Harvest    => $"화상 · 출혈 걷어 스킬 대기 -{effective:0.0}초 · 골드 +{CovenantMath.HarvestGold}",
            EffectKind.Arcflash   => ArcChain
                ? $"{CovenantMath.ArcflashChainHop:0}m씩 튀며 적 {ArcCount}마리에게 감전 1개씩"
                : $"{M(radius)} 안 적 {ArcCount}마리에게 감전 1개씩",
            EffectKind.Stasis     => $"{M(radius)} 안 감전 1개당 {effective:0.00}초 기절",
            _                     => SelfText(),
        };
    }

    /// <summary>
    /// 미리보기 「효과」 줄 — 첫 결과(원인에서 곧바로)가 <b>누구에게</b> 일어나는가 + 수치 + 덧붙임(쌓임 · 상한 · 보스).
    /// 10-23 U8 — 예전 「광역 피해 ×3.2 · 반경 3.5m」는 어디서 · 누구에게가 없었다.
    /// </summary>
    public string EffectAmountLabel()
    {
        if (!valid) return "";
        SubjectAtCause(out var who, out var at, out var area);
        return Clause(who, at, area) + Detail();
    }

    /// <summary>
    /// 결과 한 문장 — 받는 쪽을 주어로(서약서 줄 · HUD · 미리보기). <paramref name="who"/> = 받는 적(「맞힌 적」 · 「8m 안에서 가장 가까운 적」 …),
    /// <paramref name="at"/> = 폭발 자리(받은 첫 적, 없으면 자리), <paramref name="area"/> = 분산 기폭 중심(자리). 이름은 <see cref="CovenantGrammar"/>가 짓는다.
    /// 「나」 쪽 결과(격노 · 박차 · 보호막 · 성역 · 결계 · 숨결 · 황금비)는 받는 쪽이 없어 카드 글과 같다.
    /// </summary>
    public string Clause(string who, string at, string area)
    {
        if (!valid) return "";
        return effectKind switch
        {
            EffectKind.Burn       => $"{who}이 {Sec(duration)} 동안 불탄다(초당 공격력 {P(effective)})",
            EffectKind.BleedStack => $"{who}이 {Sec(duration)} 동안 피 흘린다(초당 공격력 {P(effective)})",
            EffectKind.Curse      => $"{who}이 {Sec(duration)} 동안 받는 피해 +{P(effective)}",
            // 경계 원인(선제 · 개선)의 처형은 받은 적이 아니라 12m 안 체력이 가장 낮은 적을 짚고 기준이 두 배다(CovenantClause.TryExecute)
            EffectKind.Execute    => causeClass == CauseClass.Boundary
                ? $"{CovenantClause.ExecuteMarkRadius:0}m 안에서 체력이 가장 낮은 적이 체력 {P(ExecuteThreshold)} 이하면 즉사"
                : $"{who}이 체력 {P(ExecuteThreshold)} 이하면 즉사",
            EffectKind.AoeBurst   => $"{at}에서 {M(radius)} 폭발(공격력 {P(effective)})",
            EffectKind.Arcflash   => ArcChain
                ? $"{who}과 {CovenantMath.ArcflashChainHop:0}m씩 이어진 적 {ArcCount - 1}마리에게 감전 1개씩"
                : $"{who}과 그 둘레 {M(radius)} 안 적 {ArcCount - 1}마리에게 감전 1개씩",
            EffectKind.Stasis     => $"{who} 둘레 {M(radius)} 안 적의 감전을 걷어 1개당 {effective:0.00}초 기절",
            EffectKind.Detonate   => causeTargeted
                ? $"{who}의 화상 · 출혈 {P(CovenantMath.DetonateFraction)}를 터뜨리고 {M(radius)} 안 다른 적에게 그 {P(effective)}"
                : $"{area} {M(radius)} 안 적 모두의 화상 · 출혈 {P(CovenantMath.DetonateFraction)}를 터뜨린다",
            EffectKind.Harvest    => $"{who}의 화상 · 출혈 {P(CovenantMath.HarvestFraction)}를 걷어 스킬 대기 -{effective:0.0}초 · 골드 +{CovenantMath.HarvestGold}",
            _                     => SelfText(),
        };
    }

    /// <summary>
    /// 첫 결과가 받는 쪽(원인에서 곧바로) — <see cref="CovenantSentence"/>의 연쇄 시작 · <see cref="CovenantClause"/>의 대상 해석과 같은 규칙:
    /// 적을 가리키는 원인(연격 · 겨눔 · 전환 · 사냥 개시 · 선제) = 맞힌 적 · 학살 = 마지막으로 쓰러진 자리 ·
    /// 그 밖(심장박동 · 연주 · 포위 · 행군 · 개선) = 나를 중심으로 — 적 하나가 필요하면 8m 안에서 가장 가까운 적.
    /// </summary>
    public void SubjectAtCause(out string who, out string at, out string area)
    {
        if (causeTrigger == CauseTriggerKind.OnKillStreak) { who = "쓰러진 자리에서 가장 가까운 적"; at = area = "쓰러진 자리"; }
        else if (causeTargeted)                            { who = "맞힌 적"; at = area = "맞힌 적 자리"; }
        else                                               { who = NearMe; at = "내 자리"; area = "내 둘레"; }
    }

    /// <summary>「8m 안에서 가장 가까운 적」 — 받을 적이 없을 때 나를 중심으로 찾는 반경(<see cref="CovenantClause.TargetSearchRadius"/>).</summary>
    public static string NearMe => $"{CovenantClause.TargetSearchRadius:0}m 안에서 가장 가까운 적";

    /// <summary>미리보기에만 붙는 덧붙임 — 쌓임 · 상한 · 보스 · 위험 변형(카드 · 서약서 줄엔 길어서 뺀다).</summary>
    private string Detail() => effectKind switch
    {
        EffectKind.AoeBurst   => $" · 둘레에 상태 걸린 적 1마리당 반경 +{CovenantMath.SupernovaRadiusPerSteeped:0.#}m(최대 {CovenantMath.AoeRadiusCap:0.#}m)",
        EffectKind.DamageBuff => causeClass == CauseClass.Danger ? $" · 5m 안 적이 {causeThreshold}마리 아래로 줄면 바로 꺼진다" : string.Empty,
        EffectKind.BleedStack => $" · 다시 걸면 최대 {CovenantMath.BleedMaxStacks}번까지 쌓인다",
        EffectKind.Execute    => $" · 걸린 상태(화상 · 출혈 · 감전 · 저주) 1종마다 기준 ×{CovenantMath.ExecuteStatusMult:0.#}(최대 {P(CovenantMath.ExecuteThresholdCap)}) · 보스는 즉사 대신 최대 체력의 {P(effective)} 피해",
        EffectKind.Arcflash   => $" · 감전은 적마다 최대 {CovenantStatus.ShockMaxStacks}개(1개당 이동 속도 -{P(CovenantStatus.ShockSlowPerStack)})",
        EffectKind.Stasis     => $" · 기절은 최대 {CovenantMath.StasisStunCap:0.#}초",
        EffectKind.Ward       => $" · {M(radius)} 안 상태 걸린 적 1마리당 -{P(CovenantMath.WardPerSteepedEnemy)}p(최대 -{P(CovenantMath.WardReductionCap)})",
        EffectKind.StatBuff   => $" · 다시 터지면 겹쳐 쌓인다(최대 {CovenantMath.MomentumStackCap(causeClass)}번)",
        _                     => string.Empty,
    };

    /// <summary>「나」 쪽 결과 — 받는 쪽이 없는 효과.</summary>
    private string SelfText() => effectKind switch
    {
        EffectKind.DamageBuff => $"{Sec(duration)} 동안 내 피해 +{P(FuryAmp)}",
        EffectKind.Shield     => $"보호막 +{effective:0}",
        EffectKind.GoldBurst  => $"골드 +{effective:0}",
        EffectKind.Invincible => $"{effective:0.0}초 무적",
        EffectKind.DeathSave  => $"죽을 피해 1번 버티기 +1(최대 {effectiveCount}번)",
        EffectKind.StatBuff   => MomentumLabel(),
        EffectKind.Ward       => $"{Sec(duration)} 동안 받는 피해 -{P(effective)}",
        _                     => $"{effective:0.0}",
    };

    /// <summary>「박차」는 원인 형상에 따라 주축이 바뀐다(기동=이속·상한↑ / 처치=공속·지속2배). 1번 터질 때의 값.</summary>
    private string MomentumLabel()
    {
        float off = effective * CovenantMath.MomentumOffAxisRatio;
        return causeClass switch
        {
            CauseClass.Mobility => $"{Sec(duration)} 동안 이동 속도 +{P(effective)} · 공격 속도 +{P(off)}",
            CauseClass.Kill     => $"{Sec(duration * CovenantMath.MomentumKillDurationMult)} 동안 공격 속도 +{P(effective)} · 이동 속도 +{P(off)}",
            _                   => $"{Sec(duration)} 동안 이동 속도 +{P(effective)} · 공격 속도 +{P(effective * CovenantMath.MomentumAtkSpeedRatio)}",
        };
    }

    /// <summary>「격노」 실제 증폭 — 위험 원인(포위)이면 ×1.25(<see cref="CovenantClause"/> FuryAmp와 같은 식). 10-23: 예전 카드 · 줄엔 ×1.25가 빠져 있었다.</summary>
    private float FuryAmp => effective * (causeClass == CauseClass.Danger ? CovenantMath.FuryDangerBonus : 1f);

    /// <summary>처형 기준 — 경계 원인은 ×2(상한 60%). 상태 종수 배수는 그 순간 적에게 달려 있어 덧붙임으로만 말한다.</summary>
    private float ExecuteThreshold => UnityEngine.Mathf.Min(
        effective * (causeClass == CauseClass.Boundary ? CovenantMath.ExecuteBoundaryMult : 1f), CovenantMath.ExecuteThresholdCap);

    /// <summary>방전 — 감전이 걸리는 적 수(첫 적 포함). 스킬 원인은 체인이라 한 마리 더.</summary>
    private bool ArcChain => causeClass == CauseClass.Skill;
    private int  ArcCount => effectiveCount + (ArcChain ? CovenantMath.ArcflashChainBonus : 0) + 1;

    private static string P(float v)   => $"{v * 100f:0}%";
    private static string M(float r)   => $"{r:0.#}m";
    private static string Sec(float s) => $"{s:0.#}초";

    /// <summary>카드 통화 배지 문구. 예: "생존 · 보호막".</summary>
    public string Badge => valid ? EffectTaxonomy.Badge(axis, status) : "";

    // ── 시너지 힌트 ──────────────────────────────────────
    /// <summary>
    /// 이미 가진 서약과 <b>통화가 물리는지</b> 한 줄로 알려준다. 그물의 요점은 서약 하나의 세기가 아니라
    /// 걸기→터뜨리기의 순서인데, 조립 화면은 그걸 보여줄 방법이 하나도 없었다 —
    /// 플레이어가 그물을 짜려면 지금 고르는 카드가 무엇과 물리는지 <b>고르는 순간</b> 보여야 한다.
    /// 반환 null = 물리는 것 없음(UI는 줄을 감춘다).
    /// </summary>
    public string SynergyHint(System.Collections.Generic.IReadOnlyList<CovenantBase> held)
    {
        if (!valid || held == null || role == StatusRole.None) return null;

        for (int i = 0; i < held.Count; i++)
        {
            if (held[i] is not AssembledCovenant a || !a.Resolved) continue;
            if (a.Role == StatusRole.None || a.Role == role) continue;              // 같은 방향끼리는 안 물린다
            if (!EffectTaxonomy.SameFamily(status, a.Status)) continue;

            string coin = (role == StatusRole.Consume ? a.Status : status).DisplayName();
            return role == StatusRole.Consume
                ? $"시너지  「{a.EffectName}」이(가) 걸어 둔 {coin}을(를) 이 서약이 먹는다"
                : $"시너지  이 서약이 거는 {coin}을(를) 「{a.EffectName}」이(가) 먹는다";
        }
        return null;
    }

    /// <summary>효과 카드 한 장이 보유 서약과 물리는지(카드 테두리 글로우용). 원인과 무관하게 통화·역할만 본다.</summary>
    public static bool HasSynergy(string effectId, System.Collections.Generic.IReadOnlyList<CovenantBase> held)
    {
        if (held == null || !CovenantPalette.TryGetEffect(effectId, out var e) || e.role == StatusRole.None)
            return false;

        for (int i = 0; i < held.Count; i++)
            if (held[i] is AssembledCovenant a && a.Resolved &&
                a.Role != StatusRole.None && a.Role != e.role &&
                EffectTaxonomy.SameFamily(e.status, a.Status))
                return true;
        return false;
    }
}
