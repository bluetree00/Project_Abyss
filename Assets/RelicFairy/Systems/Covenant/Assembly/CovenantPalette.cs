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
        public StatusCurrency    status;  // 카드 배지 표시용 상태 통화
    }

    private static readonly Dictionary<string, CauseDef> _causes = new()
    {
        ["streak"]   = new CauseDef { id="streak",   name="연격",     desc="같은 적을 3연타할 때마다", tag="집중", trigger=CauseTriggerKind.OnHitStreakSameTarget, category=CovenantCategory.ActionConditional, coefficient=1.5f, thresholdInt=3 },
        ["slaughter"]= new CauseDef { id="slaughter",name="학살",     desc="5연속 처치 시",           tag="연쇄", trigger=CauseTriggerKind.OnKillStreak,        category=CovenantCategory.ActionConditional, coefficient=4.0f, thresholdInt=5 },
        ["swap"]     = new CauseDef { id="swap",     name="전환",     desc="무기 교체 직후 3초 내 공격 시", tag="기동", trigger=CauseTriggerKind.OnWeaponSwapWindow, category=CovenantCategory.CombatRhythm,     coefficient=2.5f, thresholdF=3f },
        ["clear"]    = new CauseDef { id="clear",    name="개선",     desc="방을 클리어할 때",         tag="연쇄", trigger=CauseTriggerKind.OnRoomClear,         category=CovenantCategory.RunStructure,     coefficient=3.0f },
        ["heartbeat"]= new CauseDef { id="heartbeat",name="심장박동", desc="4초마다",                 tag="지속", trigger=CauseTriggerKind.Periodic,            category=CovenantCategory.RunStructure,     coefficient=1.0f, thresholdF=4f },
        ["concerto"] = new CauseDef { id="concerto", name="연주",     desc="스킬을 사용할 때",         tag="스킬", trigger=CauseTriggerKind.OnSkillUse,          category=CovenantCategory.CombatRhythm,     coefficient=2.0f },
        ["hunt"]     = new CauseDef { id="hunt",     name="사냥 개시", desc="처치 직후 3초 내 공격 시", tag="처치", trigger=CauseTriggerKind.OnKillThenHitWindow, category=CovenantCategory.ActionConditional, coefficient=2.0f, thresholdF=3f },
        ["opener"]   = new CauseDef { id="opener",   name="선제",     desc="방 진입 후 첫 타격 시",     tag="개전", trigger=CauseTriggerKind.OnFirstHitInRoom,    category=CovenantCategory.RunStructure,     coefficient=1.5f },
        ["besiege"]  = new CauseDef { id="besiege",  name="포위",     desc="인접한 적이 3 이상일 때",   tag="위험", trigger=CauseTriggerKind.OnProximity,         category=CovenantCategory.ActionConditional, coefficient=3.0f, thresholdInt=3, thresholdF=2f },
        ["march"]    = new CauseDef { id="march",    name="행군",     desc="12m 이동할 때마다",         tag="기동", trigger=CauseTriggerKind.OnMoveDistance,      category=CovenantCategory.CombatRhythm,     coefficient=1.5f, thresholdF=12f },
    };

    // 기존 5종(supernova/fury/bloodmark/curse/execute)은 <b>id를 바꾸지 않는다</b> —
    // 이미 저장된 런의 서약 id가 이 문자열을 그대로 참조한다. 수치·모드만 손본다.
    private static readonly Dictionary<string, EffectDef> _effects = new()
    {
        // ── 공격 ────────────────────────────────────────
        ["supernova"] = new EffectDef { id="supernova", name="초신성",   desc="대상 중심 광역 폭발",         tag="공격",
            kind=EffectKind.AoeBurst,   magnitude=1.5f,  radius=3.5f,
            mode=CovenantScaleMode.Damped, cap=6f,   axis=EffectAxis.Offense,  status=StatusCurrency.None },

        ["fury"]      = new EffectDef { id="fury",      name="격노",     desc="일시적으로 피해가 증폭된다",  tag="강화",
            kind=EffectKind.DamageBuff, magnitude=0.30f, duration=4f,
            mode=CovenantScaleMode.Damped, cap=1.2f, axis=EffectAxis.Offense,  status=StatusCurrency.Momentum },

        ["curse"]     = new EffectDef { id="curse",     name="저주",     desc="대상이 받는 피해가 증폭된다", tag="상태이상",
            kind=EffectKind.Curse,      magnitude=0.20f, duration=5f,
            mode=CovenantScaleMode.Damped, cap=0.8f, axis=EffectAxis.Offense,  status=StatusCurrency.Vulnerable },

        // 처형 임계는 sqrt로 완만하게 — 선형이면 고계수 원인에서 "절반 이하 즉사"가 되어 보스전이 무너진다.
        ["execute"]   = new EffectDef { id="execute",   name="처형",     desc="저체력 대상을 즉사시킨다",    tag="공격",
            kind=EffectKind.Execute,    magnitude=0.15f,
            mode=CovenantScaleMode.Sqrt,   cap=0.35f, axis=EffectAxis.Offense, status=StatusCurrency.None },

        ["ember"]     = new EffectDef { id="ember",     name="잔불",     desc="대상을 불태운다",             tag="상태이상",
            kind=EffectKind.Burn,       magnitude=0.20f, duration=4f,
            mode=CovenantScaleMode.Damped, cap=0.8f, axis=EffectAxis.Offense,  status=StatusCurrency.Burn },

        // ── 방어 ────────────────────────────────────────
        // 매 타격마다 리필되면 소모보다 리필이 빨라 사실상 무적이 된다 → icd로 발동 간격을 강제.
        ["bloodmark"] = new EffectDef { id="bloodmark", name="피의 보호막", desc="굳은 피가 보호막이 된다",  tag="생존",
            kind=EffectKind.Shield,     magnitude=12f,
            mode=CovenantScaleMode.Sqrt,   cap=40f,  icd=3f, axis=EffectAxis.Survival, status=StatusCurrency.Shield },

        // 무적 시간은 계수로 늘리면 안 된다(고계수 원인 = 상시 무적) → CovenantScaleMode.None + 긴 icd.
        ["aegis"]     = new EffectDef { id="aegis",     name="성역",     desc="아주 잠시 무적이 된다",       tag="생존",
            kind=EffectKind.Invincible, magnitude=0.5f,
            mode=CovenantScaleMode.None,             icd=8f, axis=EffectAxis.Survival, status=StatusCurrency.Shield },

        // 원인 계수가 '충전 횟수'가 된다 — 위험한 조건일수록 더 여러 번 버틴다.
        ["lastbreath"]= new EffectDef { id="lastbreath",name="마지막 숨결", desc="치명적인 피해를 한 번 견딘다", tag="생존",
            kind=EffectKind.DeathSave,  magnitude=1f,
            mode=CovenantScaleMode.Count,  cap=3f,   axis=EffectAxis.Survival, status=StatusCurrency.None },

        // ── 보조 ────────────────────────────────────────
        ["momentum"]  = new EffectDef { id="momentum",  name="박차",     desc="이동·공격 속도가 중첩된다",   tag="가속",
            kind=EffectKind.StatBuff,   magnitude=0.04f, duration=6f,
            mode=CovenantScaleMode.Damped, cap=0.10f, axis=EffectAxis.Utility, status=StatusCurrency.Momentum },

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
    private static readonly Dictionary<string, string> _aliasCause  = new();
    private static readonly Dictionary<string, string> _aliasEffect = new();

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
}
