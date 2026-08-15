using System.Collections.Generic;

/// <summary>
/// 조립 서약 — 원인/효과 팔레트(정적 데이터). 플레이어가 원인×효과를 골라 조립.
/// MVP: 코드 정적 등록(즉시 동작). 후속: SO(CauseCatalogSO/EffectCatalogSO)로 데이터화 가능(디자이너 authoring).
/// 수치는 밸런싱 시작점. 계수(coefficient)=원인 리스크 비례(위험할수록 효과 큼).
///
/// 효과 수치를 읽을 때는 magnitude를 직접 쓰지 말고 <see cref="CovenantMath"/>를 거친다 —
/// 스케일 모드/상한/계수 반영이 전부 거기 한 곳에 있다.
/// </summary>
public static class CovenantPalette
{
    public struct CauseDef
    {
        public string id, name, desc, tag;
        public CauseTriggerKind trigger;
        public CovenantCategory category;
        public float coefficient;   // 효과 배율(원인 리스크 비례)
        public int   thresholdInt;  // 연타/연속 처치 필요 수
        public float thresholdF;    // 주기(초)/윈도우(초)

        // ── 형상(효과의 질적 변형이 읽는 축) ──────────────
        // 효과 코드가 원인 id로 if를 쓰면 원인 추가마다 효과 전부를 다시 손봐야 한다.
        // "어떤 원인이 어떤 형상인가"는 여기 데이터에만 적고, 효과는 형상만 본다.
        public CauseClass cls;
        public bool       targeted;   // 발동 순간 '이 적'이라고 가리킬 대상이 있는가
    }

    public struct EffectDef
    {
        public string id, name, desc, tag;
        public EffectKind kind;
        public float magnitude;     // 효과 크기(배율/양) — 스케일 전 기본값
        public float radius;        // AoE 반경
        public float duration;      // 지속(초)

        public CovenantScaleMode mode;    // 계수를 태우는 방식
        public float             cap;     // 유효 수치 상한(0 = 무제한). Count 모드에선 최대 횟수.
        public float             icd;     // 내부 쿨다운(초). 0 = 없음.
        public EffectAxis        axis;    // 드래프트 방어축 보장에 쓰는 분류
        public StatusCurrency    status;  // 상태 통화(카드 배지 + 시너지 힌트)
        public StatusRole        role;    // 그 통화를 거는가(Apply) 먹는가(Consume)
    }

    private static readonly Dictionary<string, CauseDef> _causes = new()
    {
        ["streak"]   = new CauseDef { id="streak",   name="연격",     desc="같은 적을 3연타할 때마다", tag="집중", trigger=CauseTriggerKind.OnHitStreakSameTarget, category=CovenantCategory.ActionConditional, coefficient=1.5f, thresholdInt=3, cls=CauseClass.Melee,    targeted=true  },
        ["slaughter"]= new CauseDef { id="slaughter",name="학살",     desc="5연속 처치 시",           tag="연쇄", trigger=CauseTriggerKind.OnKillStreak,        category=CovenantCategory.ActionConditional, coefficient=4.0f, thresholdInt=5, cls=CauseClass.Kill,     targeted=false },
        ["swap"]     = new CauseDef { id="swap",     name="전환",     desc="무기 교체 직후 3초 내 공격 시", tag="기동", trigger=CauseTriggerKind.OnWeaponSwapWindow, category=CovenantCategory.CombatRhythm,     coefficient=2.5f, thresholdF=3f,  cls=CauseClass.Mobility, targeted=true  },
        ["clear"]    = new CauseDef { id="clear",    name="개선",     desc="방을 클리어할 때",         tag="연쇄", trigger=CauseTriggerKind.OnRoomClear,         category=CovenantCategory.RunStructure,     coefficient=3.0f,                 cls=CauseClass.Boundary, targeted=false },
        ["heartbeat"]= new CauseDef { id="heartbeat",name="심장박동", desc="4초마다",                 tag="지속", trigger=CauseTriggerKind.Periodic,            category=CovenantCategory.RunStructure,     coefficient=1.0f, thresholdF=4f,  cls=CauseClass.Passive,  targeted=false },
        ["concerto"] = new CauseDef { id="concerto", name="연주",     desc="스킬을 사용할 때",         tag="스킬", trigger=CauseTriggerKind.OnSkillUse,          category=CovenantCategory.CombatRhythm,     coefficient=2.0f,                 cls=CauseClass.Skill,    targeted=false },
        ["hunt"]     = new CauseDef { id="hunt",     name="사냥 개시", desc="처치 직후 3초 내 공격 시", tag="처치", trigger=CauseTriggerKind.OnKillThenHitWindow, category=CovenantCategory.ActionConditional, coefficient=2.0f, thresholdF=3f,  cls=CauseClass.Kill,     targeted=true  },
        ["opener"]   = new CauseDef { id="opener",   name="선제",     desc="방 진입 후 첫 타격 시",     tag="개전", trigger=CauseTriggerKind.OnFirstHitInRoom,    category=CovenantCategory.RunStructure,     coefficient=1.5f,                 cls=CauseClass.Boundary, targeted=true  },
        ["besiege"]  = new CauseDef { id="besiege",  name="포위",     desc="인접한 적이 3 이상일 때",   tag="위험", trigger=CauseTriggerKind.OnProximity,         category=CovenantCategory.ActionConditional, coefficient=3.0f, thresholdInt=3, thresholdF=2f, cls=CauseClass.Danger, targeted=false },
        ["march"]    = new CauseDef { id="march",    name="행군",     desc="12m 이동할 때마다",         tag="기동", trigger=CauseTriggerKind.OnMoveDistance,      category=CovenantCategory.CombatRhythm,     coefficient=1.5f, thresholdF=12f, cls=CauseClass.Mobility, targeted=false },
    };

    // 살아 있는 효과의 id는 <b>바꾸지 않는다</b> — 이미 저장된 런의 서약 id가 이 문자열을 그대로 참조한다.
    // 수치·모드만 손본다. 없애야 할 때는 정의를 지우고 아래 _aliasEffect에 이어붙일 곳을 적는다.
    private static readonly Dictionary<string, EffectDef> _effects = new()
    {
        // ── 공격 ────────────────────────────────────────
        ["supernova"] = new EffectDef { id="supernova", name="초신성",   desc="대상 중심 광역 폭발",         tag="공격",
            kind=EffectKind.AoeBurst,   magnitude=1.5f,  radius=3.5f,
            mode=CovenantScaleMode.Damped, cap=6f,   axis=EffectAxis.Offense,  status=StatusCurrency.None },

        ["fury"]      = new EffectDef { id="fury",      name="격노",     desc="일시적으로 피해가 증폭된다",  tag="강화",
            kind=EffectKind.DamageBuff, magnitude=0.30f, duration=4f,
            mode=CovenantScaleMode.Damped, cap=1.2f, axis=EffectAxis.Offense,  status=StatusCurrency.Momentum, role=StatusRole.Apply },

        ["curse"]     = new EffectDef { id="curse",     name="저주",     desc="대상이 받는 피해가 증폭된다", tag="상태이상",
            kind=EffectKind.Curse,      magnitude=0.20f, duration=5f,
            mode=CovenantScaleMode.Damped, cap=0.8f, axis=EffectAxis.Offense,  status=StatusCurrency.Vulnerable, role=StatusRole.Apply },

        // 처형 임계는 sqrt로 완만하게 — 선형이면 고계수 원인에서 "절반 이하 즉사"가 되어 보스전이 무너진다.
        ["execute"]   = new EffectDef { id="execute",   name="처형",     desc="상태에 절인 저체력 대상을 즉사시킨다", tag="공격",
            kind=EffectKind.Execute,    magnitude=0.15f,
            mode=CovenantScaleMode.Sqrt,   cap=0.35f, axis=EffectAxis.Offense, status=StatusCurrency.None },

        ["ember"]     = new EffectDef { id="ember",     name="잔불",     desc="대상을 불태운다",             tag="상태이상",
            kind=EffectKind.Burn,       magnitude=0.20f, duration=4f,
            mode=CovenantScaleMode.Damped, cap=0.8f, axis=EffectAxis.Offense,  status=StatusCurrency.Burn, role=StatusRole.Apply },

        // 출혈은 걸수록 커진다 — 화상(더 강한 쪽 유지)과 달리 dps가 스택으로 누적된다.
        ["hemorrhage"]= new EffectDef { id="hemorrhage",name="출혈",     desc="상처가 벌어져 계속 덧난다",   tag="상태이상",
            kind=EffectKind.BleedStack, magnitude=0.15f, duration=4f,
            mode=CovenantScaleMode.Damped, cap=0.5f, axis=EffectAxis.Offense,  status=StatusCurrency.Bleed, role=StatusRole.Apply },

        // ── 소모형(상태 통화를 먹는다) ───────────────────
        // 셋 모두 <b>어떤 상태도 부여하지 않는다</b>. 먹으면서 걸면 자기 먹이를 자기가 만들어
        // 한 번의 발동이 프레임 안에서 스스로를 되먹인다(무한 기폭).
        ["detonate"]  = new EffectDef { id="detonate",  name="기폭",     desc="걸린 화상·출혈을 터뜨린다",   tag="폭발",
            kind=EffectKind.Detonate,   magnitude=0.5f,  radius=4f,
            mode=CovenantScaleMode.Damped, cap=1.2f, axis=EffectAxis.Offense,  status=StatusCurrency.Burn, role=StatusRole.Consume },

        // 수확은 상태를 '거둘' 뿐 체력을 건드리지 않는다 — 회복이 아니라 쿨감과 금이다.
        ["harvest"]   = new EffectDef { id="harvest",   name="수확",     desc="걸린 상태를 거둬 재촉과 금으로 바꾼다", tag="경제",
            kind=EffectKind.Harvest,    magnitude=0.8f,
            mode=CovenantScaleMode.Damped, cap=2f,   icd=4f, axis=EffectAxis.Utility, status=StatusCurrency.Burn, role=StatusRole.Consume },

        // ── 감전 통화(C3) ───────────────────────────────
        // 감전의 세기는 고정 상수(1스택)다. 커지는 쪽은 '몇에게 거는가' — 소모 비율을 상수로 둔 것과 같은 결이다.
        // 통화를 거는 힘까지 계수로 키우면 부여형 하나가 그물 전체를 혼자 먹여 소모형이 남아돌게 된다.
        ["arcflash"]  = new EffectDef { id="arcflash",  name="방전",     desc="대상과 인근 적에게 감전이 옮겨붙는다", tag="상태이상",
            kind=EffectKind.Arcflash,   magnitude=2f,    radius=5f,
            mode=CovenantScaleMode.Count,  cap=5f,   axis=EffectAxis.Offense,  status=StatusCurrency.Shock, role=StatusRole.Apply },

        // 스택당 지속이 크면 한 번 걸고 바로 터뜨려도 상한(StasisStunCap)에 붙어 "쌓는" 행위가 무의미해진다.
        // 정적 시뮬 기준 1회 방전 직후 0.4~2.1초 / 포화(5중첩)에서야 상한 — 쌓을수록 보상되게 잡은 값.
        ["stasis"]    = new EffectDef { id="stasis",    name="정지",     desc="쌓인 감전을 터뜨려 주변을 멈춰 세운다", tag="제어",
            kind=EffectKind.Stasis,     magnitude=0.12f, radius=4.5f,
            mode=CovenantScaleMode.Damped, cap=0.35f, icd=6f, axis=EffectAxis.Survival, status=StatusCurrency.Shock, role=StatusRole.Consume },

        // ── 방어 ────────────────────────────────────────
        // 매 타격마다 리필되면 소모보다 리필이 빨라 사실상 무적이 된다 → icd로 발동 간격을 강제.
        ["bloodmark"] = new EffectDef { id="bloodmark", name="피의 보호막", desc="굳은 피가 보호막이 된다",  tag="생존",
            kind=EffectKind.Shield,     magnitude=12f,
            mode=CovenantScaleMode.Sqrt,   cap=40f,  icd=3f, axis=EffectAxis.Survival, status=StatusCurrency.Shield, role=StatusRole.Apply },

        // 무적 시간은 계수로 늘리면 안 된다(고계수 원인 = 상시 무적) → CovenantScaleMode.None + 긴 icd.
        ["aegis"]     = new EffectDef { id="aegis",     name="성역",     desc="아주 잠시 무적이 된다",       tag="생존",
            kind=EffectKind.Invincible, magnitude=0.5f,
            mode=CovenantScaleMode.None,             icd=8f, axis=EffectAxis.Survival, status=StatusCurrency.Shield, role=StatusRole.Apply },

        // 원인 계수가 '충전 횟수'가 된다 — 위험한 조건일수록 더 여러 번 버틴다.
        // 사망 시 HP 복구는 <b>사망 방지</b>지 흡혈이 아니다 — 적에게서 빨아 오는 것이 없다.
        ["lastbreath"]= new EffectDef { id="lastbreath",name="마지막 숨결", desc="치명적인 피해를 한 번 견딘다", tag="생존",
            kind=EffectKind.DeathSave,  magnitude=1f,
            mode=CovenantScaleMode.Count,  cap=3f,   axis=EffectAxis.Survival, status=StatusCurrency.None },

        // 「흡정」(적에게 걸린 상태를 보호막으로 빨아들이던 효과)이 있던 자리.
        // 빨아들이는 결 자체를 없애는 대신, 방어축이 한 칸 비지 않도록 <b>자기 조건만으로</b> 서는 방패를 둔다 —
        // 상태는 읽기만 하고 걷어가지 않는다(먹지 않으니 소모형이 아니고, 그래서 부여형 없이도 혼자 성립한다).
        // icd(6초)보다 지속이 길면 사실상 상시 감소가 된다 — 지속을 icd보다 짧게 둬 '켜고 끄는' 창을 남긴다.
        ["ward"]      = new EffectDef { id="ward",      name="결계",     desc="잠시 받는 피해가 줄어든다(절여진 적이 많을수록 두껍게)", tag="생존",
            kind=EffectKind.Ward,       magnitude=0.10f, radius=6f, duration=4f,
            mode=CovenantScaleMode.Damped, cap=0.25f, icd=6f, axis=EffectAxis.Survival, status=StatusCurrency.None },

        // ── 보조 ────────────────────────────────────────
        ["momentum"]  = new EffectDef { id="momentum",  name="박차",     desc="이동·공격 속도가 중첩된다",   tag="가속",
            kind=EffectKind.StatBuff,   magnitude=0.04f, duration=6f,
            mode=CovenantScaleMode.Damped, cap=0.10f, axis=EffectAxis.Utility, status=StatusCurrency.Momentum, role=StatusRole.Apply },

        ["goldrain"]  = new EffectDef { id="goldrain",  name="황금비",   desc="골드가 쏟아진다",             tag="경제",
            kind=EffectKind.GoldBurst,  magnitude=6f,
            mode=CovenantScaleMode.Sqrt,   cap=40f,  axis=EffectAxis.Utility,  status=StatusCurrency.None },
    };

    // 뽑기 풀에서 제외할 효과. 정의는 남겨둔다 — 이미 저장된 서약("asm:cause@tier|effect@tier")이
    // 이 id를 참조하고 있으면 TryGetEffect로 그대로 해석돼야 하기 때문이다.
    // goldrain(황금비): 골드 보상은 서약이 아니라 상점·보상 쪽에서 다루기로 해 등장시키지 않는다.
    private static readonly HashSet<string> _draftExcluded = new() { "goldrain" };

    // ── 별칭(id 개명 대비) ────────────────────────────────
    // 세이브에는 id 문자열만 남는다. 나중에 효과·원인의 이름을 바꾸거나 통합하면 옛 세이브의 id가
    // 어디에도 없는 이름이 되어 서약이 통째로 사라진다. 개명하는 그날 여기 한 줄("옛 id" → "새 id")만
    // 적으면 되도록 조회 경로에 미리 끼워 둔다. 지금은 개명한 적이 없어 비어 있는 게 정상이다.
    //
    // sanguine(흡정) → ward(결계): 흡정은 적에게 걸린 상태를 <b>빨아들여</b> 보호막으로 바꾸는 효과였다.
    // 흡혈 계열을 통째로 걷어내기로 해 폐기하고, 같은 자리(방어)를 지키되 적에게서 무엇도 가져오지 않는
    // 결계로 잇는다. 이미 「흡정」을 벼려 둔 런은 슬롯을 잃지 않고 결계로 해석된다.
    private static readonly Dictionary<string, string> _aliasCause  = new();
    private static readonly Dictionary<string, string> _aliasEffect = new()
    {
        ["sanguine"] = "ward",
    };

    private static readonly List<string> _causeIds  = new(_causes.Keys);
    private static readonly List<string> _effectIds =
        new(System.Linq.Enumerable.Where(_effects.Keys, id => !_draftExcluded.Contains(id)));

    public static IReadOnlyList<string> CauseIds  => _causeIds;
    public static IReadOnlyList<string> EffectIds => _effectIds;

    public static bool TryGetCause(string id, out CauseDef def)
    {
        id ??= string.Empty;
        if (_causes.TryGetValue(id, out def)) return true;
        return _aliasCause.TryGetValue(id, out var real) && _causes.TryGetValue(real, out def);
    }

    public static bool TryGetEffect(string id, out EffectDef def)
    {
        id ??= string.Empty;
        if (_effects.TryGetValue(id, out def)) return true;
        return _aliasEffect.TryGetValue(id, out var real) && _effects.TryGetValue(real, out def);
    }

    /// <summary>해당 효과가 방어축(Survival)인지. 드래프트 보장/리롤 axisLock 판정용.</summary>
    public static bool IsSurvivalEffect(string id)
        => TryGetEffect(id, out var e) && e.axis == EffectAxis.Survival;

    /// <summary>
    /// "생존 카드 한 장 보장"을 실제로 채울 수 있는 효과인지 — 방어축이면서 <b>소모형이 아닌</b> 것.
    /// 소모형 방어(정지)는 다른 서약이 감전을 걸어줘야만 방패가 된다. 그것으로 보장을 채우면
    /// 부여형이 없는 판에서 "생존 카드는 있는데 아무것도 못 하는" 카드가 보장 자리에 앉는다.
    /// </summary>
    public static bool IsGuaranteedSurvivalEffect(string id)
        => TryGetEffect(id, out var e) && e.axis == EffectAxis.Survival && e.role != StatusRole.Consume;

    // ── 드래프트 페어링(C4) ───────────────────────────────
    /// <summary>
    /// 지금 뽑을 수 있는 효과 id들. <b>소모형은 그 통화를 걸어 줄 서약을 이미 가졌을 때만</b> 등장한다.
    ///
    /// 소모형 단독은 아무 일도 하지 않는다 — 먹을 게 없으면 발동조차 안 한다(Fire가 false를 돌려준다).
    /// 첫 서약이 「기폭」이면 플레이어는 벼린 서약이 한 번도 터지지 않는 런을 그대로 보게 된다.
    /// 그래서 첫 서약(보유 0)에는 부여형·중립형만 내보내고, 부여형을 쥔 뒤부터 그 통화를 먹는 카드를 푼다.
    ///
    /// 같은 <b>통화군</b>까지 본다(화상·출혈은 한 군 — 소모형이 둘을 가리지 않고 먹는다).
    /// 화상만 가진 사람에게 감전 소모형(정지)을 내보내면 "부여형은 있는데 안 물리는" 카드가 되기 때문이다.
    /// </summary>
    public static IReadOnlyList<string> DraftableEffectIds(IReadOnlyList<CovenantBase> held)
    {
        var result = new List<string>(_effectIds.Count);
        for (int i = 0; i < _effectIds.Count; i++)
        {
            var id = _effectIds[i];
            if (!TryGetEffect(id, out var e)) continue;
            if (e.role == StatusRole.Consume && !HasApplierFor(e.status, held)) continue;
            result.Add(id);
        }
        return result;
    }

    /// <summary>보유 서약 중 같은 통화군을 <b>거는</b> 것이 하나라도 있는가.</summary>
    private static bool HasApplierFor(StatusCurrency status, IReadOnlyList<CovenantBase> held)
    {
        if (held == null) return false;
        for (int i = 0; i < held.Count; i++)
            if (held[i] is AssembledCovenant a && a.Resolved && a.Role == StatusRole.Apply &&
                EffectTaxonomy.SameFamily(status, a.Status))
                return true;
        return false;
    }

    // ── 금지 조합 ─────────────────────────────────────────
    /// <summary>
    /// 벼릴 수 없는 원인×효과 짝. 카드 자체는 계속 뽑히되(원인·효과는 서로 독립으로 굴린다)
    /// 이 짝으로는 조립을 막는다 — 두 부류다:
    ///  • 발동 조건이 사실상 상시라 방어 효과가 "무적 상시화"가 되는 것(심장박동·행군·포위 × 성역/보호막)
    ///  • 처치가 원인인데 결과가 처형이라, 처형이 다시 처치를 만들어 스스로를 먹이는 것(사냥/학살 × 처형)
    ///
    /// 런타임(<see cref="AssembledCovenant"/>)에서는 <b>막지 않는다</b> — 이미 이 짝을 저장해 둔 런이
    /// 있으면 서약이 통째로 사라진다. 새로 만드는 것만 막는 게 옳다.
    /// </summary>
    private static readonly HashSet<string> _bannedPairs = new()
    {
        "heartbeat|aegis", "heartbeat|ward",
        "march|aegis", "march|bloodmark", "march|execute", "march|ward",
        "besiege|aegis",
        "hunt|execute", "slaughter|execute",
    };

    public static bool IsBannedPair(string causeId, string effectId)
        => !string.IsNullOrEmpty(causeId) && !string.IsNullOrEmpty(effectId)
           && _bannedPairs.Contains(causeId + "|" + effectId);
}
