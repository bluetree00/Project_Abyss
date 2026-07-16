using System.Collections.Generic;

/// <summary>
/// 조립 서약 — 원인/효과 팔레트(정적 데이터). 플레이어가 원인×효과를 골라 조립.
/// MVP: 코드 정적 등록(즉시 동작). 후속: SO(CauseCatalogSO/EffectCatalogSO)로 데이터화 가능(디자이너 authoring).
/// 수치는 밸런싱 시작점. 계수(coefficient)=원인 리스크 비례(위험할수록 효과 큼).
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
        public float magnitude;     // 효과 크기(배율/양)
        public float radius;        // AoE 반경
        public float duration;      // 지속(초)
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

    private static readonly Dictionary<string, EffectDef> _effects = new()
    {
        ["supernova"] = new EffectDef { id="supernova", name="초신성",   desc="대상 중심 광역 폭발",        tag="공격", kind=EffectKind.AoeBurst,   magnitude=1.5f, radius=3.5f },
        ["fury"]      = new EffectDef { id="fury",      name="격노",     desc="일시적으로 피해가 증폭된다", tag="강화", kind=EffectKind.DamageBuff, magnitude=0.30f, duration=4f },
        ["bloodmark"] = new EffectDef { id="bloodmark", name="피의 보호막", desc="굳은 피가 보호막이 된다", tag="생존", kind=EffectKind.Shield,     magnitude=12f },
        ["goldrain"]  = new EffectDef { id="goldrain",  name="황금비",   desc="골드가 쏟아진다",           tag="경제", kind=EffectKind.GoldBurst,  magnitude=6f },
        ["curse"]     = new EffectDef { id="curse",     name="저주",     desc="대상이 받는 피해가 증폭된다", tag="상태이상", kind=EffectKind.Curse,   magnitude=0.20f, duration=5f },
        ["execute"]   = new EffectDef { id="execute",   name="처형",     desc="저체력 대상을 즉사시킨다",   tag="공격", kind=EffectKind.Execute,    magnitude=0.15f },
    };

    private static readonly List<string> _causeIds  = new(_causes.Keys);
    private static readonly List<string> _effectIds = new(_effects.Keys);

    public static IReadOnlyList<string> CauseIds  => _causeIds;
    public static IReadOnlyList<string> EffectIds => _effectIds;

    public static bool TryGetCause(string id, out CauseDef def)  => _causes.TryGetValue(id ?? "", out def);
    public static bool TryGetEffect(string id, out EffectDef def) => _effects.TryGetValue(id ?? "", out def);
}
